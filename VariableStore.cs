using System.IO;
using System.Text.Json;

namespace DocumentGenerator.Services;

/// <summary>
/// Зберігає останні введені значення змінних, кастомні назви полів та порядок
/// для кожного шаблону (JSON у data/variables/).
/// </summary>
public sealed class VariableStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private static string StoreDir => Path.Combine(Infrastructure.AppPaths.Data, "variables");

    public sealed class FieldConfig
    {
        public string Key { get; set; } = "";
        public string? Label { get; set; }
        public int Order { get; set; }
    }

    public sealed class TemplateVariables
    {
        public Dictionary<string, string> Values { get; set; } = new(StringComparer.OrdinalIgnoreCase);
        public List<FieldConfig> Fields { get; set; } = new();
    }

    public TemplateVariables Load(int templateId)
    {
        try
        {
            var path = GetPath(templateId);
            if (!File.Exists(path)) return new TemplateVariables();

            var json = File.ReadAllText(path);

            // Backward compatibility: old format was just Dictionary<string,string>
            if (json.TrimStart().StartsWith('{') && !json.Contains("\"values\"", StringComparison.OrdinalIgnoreCase)
                && !json.Contains("\"fields\"", StringComparison.OrdinalIgnoreCase))
            {
                var oldValues = JsonSerializer.Deserialize<Dictionary<string, string>>(json, JsonOptions);
                return new TemplateVariables
                {
                    Values = oldValues is null
                        ? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                        : new Dictionary<string, string>(oldValues, StringComparer.OrdinalIgnoreCase)
                };
            }

            var data = JsonSerializer.Deserialize<TemplateVariables>(json, JsonOptions);
            if (data is null) return new TemplateVariables();

            data.Values = new Dictionary<string, string>(data.Values ?? new(), StringComparer.OrdinalIgnoreCase);
            data.Fields ??= new List<FieldConfig>();
            return data;
        }
        catch
        {
            return new TemplateVariables();
        }
    }

    public void Save(int templateId, TemplateVariables data)
    {
        try
        {
            Directory.CreateDirectory(StoreDir);
            var path = GetPath(templateId);
            var json = JsonSerializer.Serialize(data, JsonOptions);
            File.WriteAllText(path, json);
        }
        catch
        {
            // Не блокуємо роботу програми, якщо не вдалося зберегти
        }
    }

    /// <summary>Зручний метод для збереження лише значень (зберігає існуючі labels/order).</summary>
    public void SaveValues(int templateId, IReadOnlyDictionary<string, string> values)
    {
        var data = Load(templateId);
        data.Values = new Dictionary<string, string>(values, StringComparer.OrdinalIgnoreCase);
        Save(templateId, data);
    }

    private static string GetPath(int templateId) =>
        Path.Combine(StoreDir, $"template_{templateId}.json");
}
