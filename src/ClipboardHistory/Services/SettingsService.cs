using System.IO;
using System.Text.Json;
using ClipboardHistory.Models;

namespace ClipboardHistory.Services;

public sealed class SettingsService
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    private readonly string _filePath;
    private AppSettings _settings;

    public SettingsService()
    {
        var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ClipboardHistory");
        Directory.CreateDirectory(dir);
        _filePath = Path.Combine(dir, "settings.json");
        _settings = LoadOrDefault();
    }

    public AppSettings Current => _settings;

    public void Reload() => _settings = LoadOrDefault();

    public void Save()
    {
        var json = JsonSerializer.Serialize(_settings, JsonOptions);
        File.WriteAllText(_filePath, json);
    }

    private AppSettings LoadOrDefault()
    {
        try
        {
            if (File.Exists(_filePath))
            {
                var json = File.ReadAllText(_filePath);
                var loaded = JsonSerializer.Deserialize<AppSettings>(json);
                if (loaded != null)
                    return loaded;
            }
        }
        catch
        {
            // ignore corrupt settings
        }

        return new AppSettings();
    }
}
