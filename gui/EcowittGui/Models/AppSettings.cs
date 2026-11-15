using System.Text.Json;

namespace EcowittGui.Models;

/// <summary>How download_sd.py is started; each value maps to one command line mode.</summary>
public enum RunMode
{
    /// <summary>No mode flag: SD sync and REST polling in one endless loop.</summary>
    Loop,
    /// <summary>--sd: sync the SD card once and exit.</summary>
    Sd,
    /// <summary>--all: download every SD file once regardless of the sync state.</summary>
    All,
    /// <summary>--rest: only poll the REST API.</summary>
    Rest,
}

/// <summary>GUI settings, persisted as JSON in %APPDATA%\EcowittGui.</summary>
public sealed class AppSettings
{
    public string PythonExe { get; set; } = "python";
    public string ScriptPath { get; set; } = "";
    public RunMode Mode { get; set; } = RunMode.Loop;
    public bool UseInterval { get; set; }
    public double Interval { get; set; } = 60;
    /// <summary>--config; empty = the script's default (config.yaml next to the script).</summary>
    public string ConfigPath { get; set; } = "";
    /// <summary>--data-dir; empty = base_path from the config.</summary>
    public string DataDir { get; set; } = "";

    private static readonly string FilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "EcowittGui", "settings.json");

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public static AppSettings Load()
    {
        AppSettings settings;
        try
        {
            settings = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath)) ?? new AppSettings();
        }
        catch (Exception)
        {
            settings = new AppSettings();
        }
        if (string.IsNullOrWhiteSpace(settings.ScriptPath) || !File.Exists(settings.ScriptPath))
        {
            settings.ScriptPath = FindScript() ?? settings.ScriptPath;
        }
        return settings;
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(this, JsonOptions));
        }
        catch (Exception)
        {
            // settings are a convenience; failing to save them must not break the app
        }
    }

    /// <summary>Looks for download_sd.py in the folders above the executable (gui/EcowittGui/bin/... -> repo root).</summary>
    private static string? FindScript()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir != null; dir = dir.Parent)
        {
            var candidate = Path.Combine(dir.FullName, "download_sd.py");
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }
        return null;
    }
}
