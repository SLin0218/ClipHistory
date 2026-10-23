using System;
using System.IO;
using System.Text.Json;

namespace SlinClip.Services;

public class AppSettings
{
    /// <summary>
    /// 最大保存条目数（平衡性能与历史留存，默认 500 条）
    /// </summary>
    public int MaxHistoryCount { get; set; } = 500;

    /// <summary>
    /// 单条内容大小上限（KB），平衡内存占用与高吞吐，默认 1024 KB (1 MB)
    /// </summary>
    public int MaxContentSizeKB { get; set; } = 1024;
}

public class SettingsService
{
    private readonly string _settingsFilePath;
    private readonly object _lock = new();
    private AppSettings _settings;

    public event Action<AppSettings>? OnSettingsChanged;

    public AppSettings Settings
    {
        get
        {
            lock (_lock)
            {
                return _settings;
            }
        }
    }

    public SettingsService(string baseDataDir)
    {
        if (!Directory.Exists(baseDataDir))
        {
            Directory.CreateDirectory(baseDataDir);
        }
        _settingsFilePath = Path.Combine(baseDataDir, "clip_history_settings.json");
        var legacyPath = Path.Combine(baseDataDir, "slin_clip_settings.json");
        if (!File.Exists(_settingsFilePath) && File.Exists(legacyPath))
        {
            try { File.Move(legacyPath, _settingsFilePath); } catch { }
        }
        _settings = Load();
    }

    private AppSettings Load()
    {
        try
        {
            if (File.Exists(_settingsFilePath))
            {
                var json = File.ReadAllText(_settingsFilePath);
                var loaded = JsonSerializer.Deserialize<AppSettings>(json);
                if (loaded != null)
                {
                    // 边界保护
                    if (loaded.MaxHistoryCount < 50) loaded.MaxHistoryCount = 50;
                    if (loaded.MaxHistoryCount > 10000) loaded.MaxHistoryCount = 10000;
                    if (loaded.MaxContentSizeKB < 64) loaded.MaxContentSizeKB = 64;
                    if (loaded.MaxContentSizeKB > 51200) loaded.MaxContentSizeKB = 51200;
                    return loaded;
                }
            }
        }
        catch { }

        var defaults = new AppSettings();
        Save(defaults);
        return defaults;
    }

    public void Update(Action<AppSettings> updateAction)
    {
        AppSettings updated;
        lock (_lock)
        {
            updateAction(_settings);
            updated = _settings;
            Save(_settings);
        }
        OnSettingsChanged?.Invoke(updated);
    }

    private void Save(AppSettings settings)
    {
        try
        {
            var json = JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(_settingsFilePath, json);
        }
        catch { }
    }
}
