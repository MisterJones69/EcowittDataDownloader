using System.Globalization;
using YamlDotNet.Serialization;

namespace EcowittGui.Models;

/// <summary>One REST endpoint from config.yaml (rest.endpoints).</summary>
public sealed record RestEndpoint(string Url, string File, double Interval, bool Paged);

/// <summary>
/// The parts of config.yaml the GUI needs, resolved the same way download_sd.py's
/// load_config() does: data_dir (from --data-dir) replaces base_path, relative paths are
/// relative to the script's folder (the GUI starts the script with that folder as working
/// directory, so relative --data-dir values resolve identically).
/// </summary>
public sealed class DownloaderConfig
{
    public string Host { get; private init; } = "";
    public string BasePath { get; private init; } = "";
    public string SdOutDir { get; private init; } = "";
    public string SdStateFile { get; private init; } = "";
    public double SdInterval { get; private init; }
    public string RestOutDir { get; private init; } = "";
    public IReadOnlyList<RestEndpoint> Endpoints { get; private init; } = [];

    public static DownloaderConfig Load(string scriptDir, string? configPath, string? dataDir)
    {
        var cfgFile = string.IsNullOrWhiteSpace(configPath)
            ? Path.Combine(scriptDir, "config.yaml")
            : Path.GetFullPath(configPath, scriptDir);
        var yaml = new DeserializerBuilder().Build()
            .Deserialize<Dictionary<string, object?>>(File.ReadAllText(cfgFile)) ?? [];

        var gateway = Map(yaml, "gateway");
        var sd = Map(yaml, "sd");
        var rest = Map(yaml, "rest");
        var host = Str(gateway, "host");

        string basePath = !string.IsNullOrWhiteSpace(dataDir)
            ? Path.GetFullPath(dataDir, scriptDir)
            : Path.GetFullPath(Str(yaml, "base_path", "."), scriptDir);
        var sdOut = Path.Combine(basePath, Str(sd, "out_dir", "download"));

        var endpoints = new List<RestEndpoint>();
        if (rest.TryGetValue("endpoints", out var list) && list is List<object> items)
        {
            foreach (var item in items.OfType<Dictionary<object, object>>())
            {
                var e = item.ToDictionary(kv => kv.Key.ToString()!, kv => (object?)kv.Value);
                endpoints.Add(new RestEndpoint(
                    Str(e, "url").Replace("{host}", host),
                    Str(e, "file"),
                    Num(e, "interval", 0),
                    Str(e, "paged", "false").Equals("true", StringComparison.OrdinalIgnoreCase)));
            }
        }

        return new DownloaderConfig
        {
            Host = host,
            BasePath = basePath,
            SdOutDir = sdOut,
            SdStateFile = Path.Combine(sdOut, Str(sd, "state_file", ".sync_state.json")),
            SdInterval = Num(sd, "interval", 600),
            RestOutDir = Path.Combine(basePath, Str(rest, "out_dir", "download_rest")),
            Endpoints = endpoints,
        };
    }

    private static Dictionary<string, object?> Map(Dictionary<string, object?> d, string key)
    {
        if (d.TryGetValue(key, out var v) && v is Dictionary<object, object> m)
        {
            return m.ToDictionary(kv => kv.Key.ToString()!, kv => (object?)kv.Value);
        }
        return [];
    }

    private static string Str(Dictionary<string, object?> d, string key, string fallback = "")
    {
        return d.TryGetValue(key, out var v) && v != null ? v.ToString()! : fallback;
    }

    private static double Num(Dictionary<string, object?> d, string key, double fallback)
    {
        return double.TryParse(Str(d, key), NumberStyles.Float, CultureInfo.InvariantCulture, out var n) ? n : fallback;
    }
}
