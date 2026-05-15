using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace HltbDatasetPlugin;

public enum TimeFormat { HoursMinutes, HoursOnly, MinutesOnly, Decimal }

public class HltbSettings
{
    public TimeFormat TimeFormat { get; set; } = TimeFormat.HoursMinutes;
    public bool ShowPolledCounts { get; set; }
    public bool AutoPopulateOnSelect { get; set; }
    public bool ShowBadge { get; set; } = true;
    public bool ShowInBigBox { get; set; } = true;

    /// <summary>If true, fall back to live HLTB scraping when dataset misses.</summary>
    public bool EnableScraping { get; set; } = true;

    /// <summary>If true, write structured log to hltb_plugin.log in plugin dir.</summary>
    public bool EnableLogging { get; set; } = true;

    /// <summary>Set by plugin after first auto-populate completes.</summary>
    public bool FirstRunPopulated { get; set; }

    /// <summary>Custom user aliases: lookup name (normalized) -> canonical HLTB name.</summary>
    public Dictionary<string, string> UserAliases { get; set; } = new();

    /// <summary>Override or supplement built-in LaunchBox->HLTB platform mappings.</summary>
    public Dictionary<string, string> PlatformMappings { get; set; } = new();

    [JsonIgnore]
    public string PluginDir => Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location) ?? ".";

    public static HltbSettings Load(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                var json = File.ReadAllText(path);
                return JsonSerializer.Deserialize<HltbSettings>(json) ?? new HltbSettings();
            }
        }
        catch (Exception ex)
        {
            HltbLogger.Warn($"Settings load failed from {path}: {ex.Message}");
        }
        return new HltbSettings();
    }

    /// <summary>Atomically load settings or create defaults if file missing/corrupt.</summary>
    public static HltbSettings LoadOrCreate(string path)
    {
        var settings = Load(path);
        if (!File.Exists(path))
        {
            try
            {
                settings.Save(path);
                HltbLogger.Info($"Created default settings at {path}");
            }
            catch (Exception ex)
            {
                HltbLogger.Warn($"Failed to create settings file: {ex.Message}");
            }
        }
        return settings;
    }

    public void Save()
    {
        Save(Path.Combine(PluginDir, "hltb_settings.json"));
    }

    public void Save(string path)
    {
        var dir = Path.GetDirectoryName(path);
        if (dir != null && !Directory.Exists(dir))
            Directory.CreateDirectory(dir);

        var json = JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(path, json);
    }

    /// <summary>Override or supplement built-in platform mappings.</summary>
    public string MapPlatform(string launchboxPlatform)
    {
        if (string.IsNullOrEmpty(launchboxPlatform)) return "";
        if (PlatformMappings.TryGetValue(launchboxPlatform, out var mapped))
            return mapped;
        return HltbDatasetPlugin.PlatformMappings.Map(launchboxPlatform);
    }
}
