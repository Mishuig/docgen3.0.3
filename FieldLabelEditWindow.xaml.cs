using System.Windows;

namespace DocumentGenerator;

public partial class FieldLabelEditWindow : Window
{
    public string Label => LabelBox.Text.Trim();

    public FieldLabelEditWindow(string currentLabel, string variableKey)
    {
        InitializeComponent();
        LabelBox.Text = currentLabel;
        KeyHint.Text = $"Змінна в шаблоні: {{{{{variableKey}}}}}";
        LabelBox.SelectAll();
        LabelBox.Focus();
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(LabelBox.Text))
        {
            MessageBox.Show("Вкажіть назву поля.", "Увага", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        DialogResult = true;
    }
}
