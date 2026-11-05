using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.Win32;
using DocumentGenerator.Domain;
using DocumentGenerator.Infrastructure;
using DocumentGenerator.Services;

namespace DocumentGenerator;

public partial class MainWindow : Window
{
    private readonly TemplateService _templates = new();
    private readonly DocxTemplateEngine _engine = new();
    private readonly VariableStore _variableStore = new();
    private readonly Dictionary<string, TextBox> _inputs = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> _labels = new(StringComparer.OrdinalIgnoreCase);
    private List<string> _fieldOrder = new();
    private TemplateRecord? _selected;
    private bool _loading;

    public MainWindow()
    {
        InitializeComponent();
        Loaded += async (_, _) => await LoadTemplatesAsync();
    }

    private async Task LoadTemplatesAsync(int? selectId = null)
    {
        try
        {
            _loading = true;
            var items = await _templates.GetAllAsync(SearchBox.Text);
            TemplatesList.ItemsSource = items;
            var selected = items.FirstOrDefault(x => x.Id == (selectId ?? _selected?.Id));
            TemplatesList.SelectedItem = selected ?? (items.Count > 0 ? items[0] : null);
            if (TemplatesList.SelectedItem is null) ShowNoSelection();
        }
        catch (Exception ex) { ShowError("Не вдалося завантажити шаблони", ex); }
        finally { _loading = false; }
    }

    private async void ImportTemplate_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog { Title = "Оберіть Word-шаблон", Filter = "Word documents (*.docx)|*.docx", CheckFileExists = true };
        if (dialog.ShowDialog() != true) return;
        try
        {
            var record = await _templates.ImportAsync(dialog.FileName);
            await LoadTemplatesAsync(record.Id);
            MessageBox.Show($"Шаблон «{record.Name}» імпортовано.", "Готово", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex) { ShowError("Не вдалося імпортувати DOCX", ex); }
    }

    private async void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
    { if (!_loading && IsLoaded) await LoadTemplatesAsync(); }

    private async void Refresh_Click(object sender, RoutedEventArgs e) => await LoadTemplatesAsync();

    private void TemplatesList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        // Зберігаємо значення попереднього шаблону перед перемиканням
        if (_selected is not null && _inputs.Count > 0)
            SaveCurrentState();

        _selected = TemplatesList.SelectedItem as TemplateRecord;
        _inputs.Clear();
        _labels.Clear();
        _fieldOrder.Clear();
        FieldsPanel.Children.Clear();
        if (_selected is null) { ShowNoSelection(); return; }
        try
        {
            SelectedTemplateTitle.Text = _selected.Name;
            var path = AppPaths.ResolveTemplate(_selected.RelativePath);
            var keys = _engine.FindPlaceholders(path);
            var saved = _variableStore.Load(_selected.Id);

            TemplateInfo.Text = $"Категорія: {_selected.Category} • Змінних: {keys.Count}. Заповніть поля нижче (значення та назви запам'ятовуються).";
            if (keys.Count == 0)
            {
                FieldsPanel.Children.Add(new TextBlock
                {
                    Text = "У шаблоні не знайдено маркерів {{variable}}. Додайте їх у DOCX та повторно імпортуйте файл.",
                    TextWrapping = TextWrapping.Wrap,
                    Foreground = Brushes.DarkOrange,
                    Margin = new Thickness(0, 4, 0, 12)
                });
                return;
            }

            // Merge saved order/labels with current keys from template
            var keySet = new HashSet<string>(keys, StringComparer.OrdinalIgnoreCase);
            var ordered = new List<string>();

            foreach (var fc in saved.Fields.OrderBy(f => f.Order))
            {
                if (keySet.Contains(fc.Key) && !ordered.Contains(fc.Key, StringComparer.OrdinalIgnoreCase))
                {
                    ordered.Add(fc.Key);
                    if (!string.IsNullOrWhiteSpace(fc.Label))
                        _labels[fc.Key] = fc.Label!;
                }
            }

            foreach (var key in keys)
            {
                if (!ordered.Contains(key, StringComparer.OrdinalIgnoreCase))
                    ordered.Add(key);
            }

            _fieldOrder = ordered;

            foreach (var key in _fieldOrder)
            {
                var value = saved.Values.TryGetValue(key, out var v) ? v : "";
                AddFieldRow(key, value);
            }
        }
        catch (Exception ex) { ShowError("Не вдалося прочитати шаблон", ex); }
    }

    private void AddFieldRow(string key, string value)
    {
        var row = new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(0xF8, 0xFA, 0xFC)),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(10, 8, 10, 10),
            Margin = new Thickness(0, 0, 0, 10),
            Tag = key
        };

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        // Header: label + buttons
        var header = new DockPanel { Margin = new Thickness(0, 0, 0, 4) };

        var labelBlock = new TextBlock
        {
            Text = GetDisplayLabel(key),
            FontWeight = FontWeights.SemiBold,
            Foreground = Brushes.DarkSlateGray,
            VerticalAlignment = VerticalAlignment.Center,
            TextWrapping = TextWrapping.Wrap
        };
        labelBlock.Tag = "label";
        DockPanel.SetDock(labelBlock, Dock.Left);
        header.Children.Add(labelBlock);

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            VerticalAlignment = VerticalAlignment.Center
        };
        DockPanel.SetDock(buttons, Dock.Right);

        var editBtn = CreateIconButton("✎", "Змінити назву поля у формі", () => EditFieldLabel(key, labelBlock));
        var upBtn = CreateIconButton("↑", "Перемістити вгору", () => MoveField(key, -1));
        var downBtn = CreateIconButton("↓", "Перемістити вниз", () => MoveField(key, 1));

        buttons.Children.Add(editBtn);
        buttons.Children.Add(upBtn);
        buttons.Children.Add(downBtn);
        header.Children.Add(buttons);

        Grid.SetRow(header, 0);
        Grid.SetColumnSpan(header, 2);
        grid.Children.Add(header);

        var input = new TextBox
        {
            Tag = key,
            MinWidth = 260,
            AcceptsReturn = IsMultilineKey(key),
            TextWrapping = TextWrapping.Wrap,
            Text = value,
            Margin = new Thickness(0, 2, 0, 0)
        };
        if (IsMultilineKey(key))
            input.MinHeight = 64;

        Grid.SetRow(input, 1);
        Grid.SetColumnSpan(input, 2);
        grid.Children.Add(input);

        row.Child = grid;
        FieldsPanel.Children.Add(row);
        _inputs[key] = input;
    }

    private Button CreateIconButton(string content, string tooltip, Action onClick)
    {
        var btn = new Button
        {
            Content = content,
            ToolTip = tooltip,
            Width = 28,
            Height = 28,
            Padding = new Thickness(0),
            Margin = new Thickness(3, 0, 0, 0),
            FontSize = 13,
            Background = new SolidColorBrush(Color.FromRgb(0xE8, 0xED, 0xF5)),
            Foreground = new SolidColorBrush(Color.FromRgb(0x24, 0x34, 0x4D)),
            BorderThickness = new Thickness(0),
            Cursor = System.Windows.Input.Cursors.Hand
        };
        btn.Click += (_, _) => onClick();
        return btn;
    }

    private string GetDisplayLabel(string key)
    {
        if (_labels.TryGetValue(key, out var custom) && !string.IsNullOrWhiteSpace(custom))
            return custom;
        return Humanize(key);
    }

    private void EditFieldLabel(string key, TextBlock labelBlock)
    {
        var current = GetDisplayLabel(key);
        var dialog = new FieldLabelEditWindow(current, key) { Owner = this };
        if (dialog.ShowDialog() != true) return;

        _labels[key] = dialog.Label;
        labelBlock.Text = dialog.Label;
        SaveCurrentState();
    }

    private void MoveField(string key, int direction)
    {
        var idx = _fieldOrder.FindIndex(k => string.Equals(k, key, StringComparison.OrdinalIgnoreCase));
        if (idx < 0) return;
        var newIdx = idx + direction;
        if (newIdx < 0 || newIdx >= _fieldOrder.Count) return;

        (_fieldOrder[idx], _fieldOrder[newIdx]) = (_fieldOrder[newIdx], _fieldOrder[idx]);
        RebuildFieldsPanel();
        SaveCurrentState();
    }

    private void RebuildFieldsPanel()
    {
        // Preserve current values from inputs
        var currentValues = _inputs.ToDictionary(
            x => x.Key,
            x => x.Value.Text ?? "",
            StringComparer.OrdinalIgnoreCase);

        FieldsPanel.Children.Clear();
        _inputs.Clear();

        foreach (var key in _fieldOrder)
        {
            currentValues.TryGetValue(key, out var val);
            AddFieldRow(key, val ?? "");
        }
    }

    private void ShowNoSelection()
    {
        _selected = null;
        _inputs.Clear();
        _labels.Clear();
        _fieldOrder.Clear();
        FieldsPanel.Children.Clear();
        SelectedTemplateTitle.Text = "Оберіть шаблон";
        TemplateInfo.Text = "Імпортуйте DOCX або оберіть шаблон зі списку.";
        FieldsPanel.Children.Add(new TextBlock { Text = "Поля шаблону з'являться тут.", Foreground = Brushes.Gray });
    }

    private static string Humanize(string key)
    {
        if (string.IsNullOrEmpty(key)) return "(порожня змінна)";
        var spaced = System.Text.RegularExpressions.Regex.Replace(key, "([a-zа-яіїєґ])([A-ZА-ЯІЇЄҐ])", "$1 $2");
        spaced = spaced.Replace('_', ' ').Replace('-', ' ').Replace('.', ' ').Replace(',', ' ');
        return $"{spaced}  ·  {key}";
    }

    private static bool IsMultilineKey(string key) =>
        key.Contains("address", StringComparison.OrdinalIgnoreCase) ||
        key.Contains("comment", StringComparison.OrdinalIgnoreCase) ||
        key.Contains("note", StringComparison.OrdinalIgnoreCase) ||
        key.Contains("text", StringComparison.OrdinalIgnoreCase) ||
        key.Contains("description", StringComparison.OrdinalIgnoreCase) ||
        key.Contains("адреса", StringComparison.OrdinalIgnoreCase) ||
        key.Contains("коментар", StringComparison.OrdinalIgnoreCase) ||
        key.Contains("опис", StringComparison.OrdinalIgnoreCase);

    private void OpenTemplate_Click(object sender, RoutedEventArgs e)
    {
        if (_selected is null) return;
        try { Process.Start(new ProcessStartInfo(AppPaths.ResolveTemplate(_selected.RelativePath)) { UseShellExecute = true }); }
        catch (Exception ex) { ShowError("Не вдалося відкрити файл", ex); }
    }

    private async void Rename_Click(object sender, RoutedEventArgs e)
    {
        if (_selected is null) return;
        var dialog = new TemplateEditWindow(_selected.Name, _selected.Category) { Owner = this };
        if (dialog.ShowDialog() != true) return;
        try { await _templates.RenameAsync(_selected.Id, dialog.TemplateName, dialog.Category); await LoadTemplatesAsync(_selected.Id); }
        catch (Exception ex) { ShowError("Не вдалося зберегти налаштування шаблону", ex); }
    }

    private async void Delete_Click(object sender, RoutedEventArgs e)
    {
        if (_selected is null) return;
        if (MessageBox.Show($"Видалити шаблон «{_selected.Name}» з бібліотеки? Файл буде переміщено до backups.", "Підтвердження", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
        try { await _templates.DeleteAsync(_selected.Id); await LoadTemplatesAsync(); }
        catch (Exception ex) { ShowError("Не вдалося видалити шаблон", ex); }
    }

    private async void Backup_Click(object sender, RoutedEventArgs e)
    {
        try { await _templates.BackupAllAsync(); MessageBox.Show($"Резервну копію створено у папці:\n{AppPaths.Backups}", "Готово", MessageBoxButton.OK, MessageBoxImage.Information); }
        catch (Exception ex) { ShowError("Не вдалося створити резервну копію", ex); }
    }

    private void ClearFields_Click(object sender, RoutedEventArgs e)
    {
        foreach (var input in _inputs.Values) input.Clear();
        if (_selected is not null) SaveCurrentState();
    }

    private void Preview_Click(object sender, RoutedEventArgs e)
    {
        if (!TryGetValues(out var values)) return;

        try
        {
            SaveCurrentState();
            var templatePath = AppPaths.ResolveTemplate(_selected!.RelativePath);
            var previewDir = Path.Combine(AppPaths.Exports, "preview");
            Directory.CreateDirectory(previewDir);

            var previewName = $"preview_{SafeFileName(_selected.Name)}_{DateTime.Now:yyyyMMdd_HHmmss}.docx";
            var previewPath = Path.Combine(previewDir, previewName);

            _engine.Generate(templatePath, previewPath, values);

            Process.Start(new ProcessStartInfo(previewPath) { UseShellExecute = true });

            MessageBox.Show(
                "Документ відкрито для перегляду/редагування.\n\n" +
                "Можете внести правки у Word/LibreOffice і зберегти їх там.\n" +
                "Щоб зберегти фінальну версію з новою назвою — натисніть «Зберегти документ».",
                "Перегляд",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
        catch (Exception ex) { ShowError("Помилка перегляду", ex); }
    }

    private async void Generate_Click(object sender, RoutedEventArgs e)
    {
        if (!TryGetValues(out var values)) return;

        var suggested = $"{SafeFileName(_selected!.Name)}_{DateTime.Now:yyyyMMdd_HHmm}.docx";
        var choice = new SaveChoiceWindow(suggested) { Owner = this };
        if (choice.ShowDialog() != true || choice.Result == SaveChoiceWindow.SaveMode.Cancel) return;

        string targetPath;
        if (choice.Result == SaveChoiceWindow.SaveMode.AutoName)
        {
            Directory.CreateDirectory(AppPaths.Exports);
            targetPath = Path.Combine(AppPaths.Exports, suggested);
        }
        else
        {
            var dialog = new SaveFileDialog
            {
                Title = "Зберегти сформований документ",
                Filter = "Word document (*.docx)|*.docx",
                FileName = suggested,
                DefaultExt = ".docx",
                InitialDirectory = AppPaths.Exports
            };
            if (dialog.ShowDialog() != true) return;
            targetPath = dialog.FileName;
        }

        try
        {
            SaveCurrentState();
            var templatePath = AppPaths.ResolveTemplate(_selected.RelativePath);

            if (string.Equals(Path.GetFullPath(templatePath), Path.GetFullPath(targetPath), StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Не можна зберігати результат поверх оригінального шаблону. Оберіть інше ім’я або папку exports.");

            await Task.Run(() => _engine.Generate(templatePath, targetPath, values));

            var open = MessageBox.Show(
                $"Документ збережено:\n{targetPath}\n\nВідкрити файл?",
                "Готово",
                MessageBoxButton.YesNo,
                MessageBoxImage.Information);

            if (open == MessageBoxResult.Yes)
                Process.Start(new ProcessStartInfo(targetPath) { UseShellExecute = true });
        }
        catch (Exception ex) { ShowError("Помилка генерації документа", ex); }
    }

    private bool TryGetValues(out Dictionary<string, string> values)
    {
        values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (_selected is null)
        {
            MessageBox.Show("Спочатку оберіть шаблон.", "Увага", MessageBoxButton.OK, MessageBoxImage.Warning);
            return false;
        }
        if (_inputs.Count == 0)
        {
            MessageBox.Show("У шаблоні немає змінних для заповнення.", "Увага", MessageBoxButton.OK, MessageBoxImage.Warning);
            return false;
        }

        values = _inputs.ToDictionary(x => x.Key, x => x.Value.Text ?? "", StringComparer.OrdinalIgnoreCase);
        var missing = values.Where(x => string.IsNullOrWhiteSpace(x.Value)).Select(x => GetDisplayLabel(x.Key)).ToList();
        if (missing.Count > 0)
        {
            var result = MessageBox.Show(
                $"Не заповнені поля:\n{string.Join("\n", missing)}\n\nПродовжити з порожніми значеннями?",
                "Перевірка даних",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);
            if (result != MessageBoxResult.Yes) return false;
        }
        return true;
    }

    private void SaveCurrentState()
    {
        if (_selected is null || _inputs.Count == 0) return;

        var data = new VariableStore.TemplateVariables
        {
            Values = _inputs.ToDictionary(x => x.Key, x => x.Value.Text ?? "", StringComparer.OrdinalIgnoreCase),
            Fields = _fieldOrder.Select((key, i) => new VariableStore.FieldConfig
            {
                Key = key,
                Label = _labels.TryGetValue(key, out var l) ? l : null,
                Order = i
            }).ToList()
        };
        _variableStore.Save(_selected.Id, data);
    }

    private static string SafeFileName(string name)
    {
        foreach (var c in Path.GetInvalidFileNameChars())
            name = name.Replace(c, '_');
        return name.Trim();
    }

    private static void ShowError(string title, Exception ex)
    {
        try { File.AppendAllText(Path.Combine(AppPaths.Logs, "application.log"), $"{DateTime.UtcNow:O} | {title}\n{ex}\n\n"); } catch { }
        MessageBox.Show($"{title}.\n{ex.Message}\n\nДеталі записано в logs/application.log.", "Помилка", MessageBoxButton.OK, MessageBoxImage.Error);
    }
}
