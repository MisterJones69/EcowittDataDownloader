using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using EcowittGui.Models;

namespace EcowittGui.ViewModels;

/// <summary>One JSON file in download_rest.</summary>
public sealed partial class RestFileItem : ObservableObject
{
    public required string Path { get; init; }
    public required string Key { get; init; }
    public required string Title { get; init; }
    public required string Description { get; init; }
    /// <summary>Endpoint from config.yaml that writes this file, or null for other files (copies etc.).</summary>
    public RestEndpoint? Endpoint { get; init; }

    public bool IsEndpointFile => Endpoint != null;

    public string Source => Endpoint == null
        ? $"{Key}.json – keine Endpunkt-Datei aus config.yaml"
        : $"{Key}.json · alle {FormatSeconds(Endpoint.Interval)}";

    [ObservableProperty]
    private DateTime? _lastWrite;

    [ObservableProperty]
    private string _age = "";

    public void UpdateAge()
    {
        LastWrite = File.Exists(Path) ? File.GetLastWriteTime(Path) : null;
        if (LastWrite is not { } t)
        {
            Age = "fehlt";
            return;
        }
        var span = DateTime.Now - t;
        Age = span.TotalSeconds < 0 ? "gerade eben" : $"vor {FormatSeconds(span.TotalSeconds)}";
        // stale: more than three poll intervals (min. 1 min) without update
        IsStale = Endpoint != null && span.TotalSeconds > Math.Max(60, Endpoint.Interval * 3);
    }

    [ObservableProperty]
    private bool _isStale;

    public static string FormatSeconds(double s)
    {
        if (s < 120)
        {
            return $"{s:0} s";
        }
        if (s < 7200)
        {
            return $"{s / 60:0} min";
        }
        if (s < 172800)
        {
            return $"{s / 3600:0.#} h";
        }
        return $"{s / 86400:0.#} Tagen";
    }
}

public sealed record KeyValueRowViewModel(string Label, string Value, string Key);

public sealed record KeyValueSectionViewModel(string Title, IReadOnlyList<KeyValueRowViewModel> Rows);

public sealed record TableSectionViewModel(string Title, IReadOnlyList<TableColumn> Columns, IReadOnlyList<string[]> Rows)
{
    public string Count => $"{Rows.Count} Einträge";
}

/// <summary>Browses download_rest: one entry per endpoint file, shown as readable sections.</summary>
public sealed partial class RestDataViewModel : ObservableObject, IDisposable
{
    private static readonly JsonSerializerOptions PrettyJson = new()
    {
        WriteIndented = true,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    private readonly MainWindowViewModel _main;
    private readonly DispatcherTimer _timer;
    private DateTime? _shownWrite;

    public ObservableCollection<RestFileItem> Files { get; } = [];
    public ObservableCollection<object> Sections { get; } = [];

    [ObservableProperty]
    private string _folder = "";

    [ObservableProperty]
    private string? _error;

    [ObservableProperty]
    private RestFileItem? _selectedFile;

    [ObservableProperty]
    private string _rawJson = "";

    [ObservableProperty]
    private bool _showSecrets;

    [ObservableProperty]
    private bool _onlyRegistered = true;

    [ObservableProperty]
    private bool _isSensorList;

    [ObservableProperty]
    private string _header = "";

    [ObservableProperty]
    private string _subHeader = "";

    public RestDataViewModel(MainWindowViewModel main)
    {
        _main = main;
        // download_sd.py keeps rewriting the files: reload the shown one when it changes
        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
        _timer.Tick += (_, _) => Tick();
        _timer.Start();
    }

    partial void OnSelectedFileChanged(RestFileItem? value) => LoadSelected(force: true);

    partial void OnShowSecretsChanged(bool value) => LoadSelected(force: true);

    partial void OnOnlyRegisteredChanged(bool value) => LoadSelected(force: true);

    [RelayCommand]
    public void Refresh()
    {
        var selectedKey = SelectedFile?.Key;
        Files.Clear();
        try
        {
            var config = _main.LoadConfig();
            Folder = config.RestOutDir;
            Error = Directory.Exists(Folder) ? null : "Ordner existiert (noch) nicht – erst die REST-Abfrage starten.";

            // endpoint files in config order, then everything else
            var known = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var e in config.Endpoints)
            {
                known.Add(e.File);
                var (title, desc) = RestFileTypes.Known.GetValueOrDefault(e.File, (e.File, e.Url));
                Files.Add(new RestFileItem
                {
                    Path = Path.Combine(Folder, e.File + ".json"),
                    Key = e.File,
                    Title = title,
                    Description = desc + "\n" + e.Url + (e.Paged ? " (seitenweise)" : ""),
                    Endpoint = e,
                });
            }
            if (Directory.Exists(Folder))
            {
                foreach (var f in Directory.EnumerateFiles(Folder, "*.json").OrderBy(f => f, StringComparer.OrdinalIgnoreCase))
                {
                    var key = Path.GetFileNameWithoutExtension(f);
                    if (known.Contains(key))
                    {
                        continue;
                    }
                    var (title, desc) = RestFileTypes.Known.GetValueOrDefault(key, (key, "Weitere JSON-Datei"));
                    Files.Add(new RestFileItem { Path = f, Key = key, Title = title, Description = desc });
                }
            }
            foreach (var f in Files)
            {
                f.UpdateAge();
            }
        }
        catch (Exception ex)
        {
            Error = $"config.yaml konnte nicht gelesen werden: {ex.Message}";
        }
        SelectedFile = Files.FirstOrDefault(f => f.Key == selectedKey) ?? Files.FirstOrDefault(f => f.LastWrite != null);
    }

    [RelayCommand]
    private void OpenFolder()
    {
        if (Directory.Exists(Folder))
        {
            Process.Start(new ProcessStartInfo("explorer.exe", Folder) { UseShellExecute = true });
        }
    }

    private void Tick()
    {
        foreach (var f in Files)
        {
            f.UpdateAge();
        }
        LoadSelected(force: false);
    }

    private void LoadSelected(bool force)
    {
        var file = SelectedFile;
        if (file == null)
        {
            Sections.Clear();
            RawJson = "";
            Header = "Keine Datei ausgewählt";
            SubHeader = "";
            _shownWrite = null;
            return;
        }
        file.UpdateAge();
        if (!force && file.LastWrite == _shownWrite)
        {
            UpdateSubHeader(file);
            return;
        }
        _shownWrite = file.LastWrite;
        Header = file.Title;
        IsSensorList = file.Key.StartsWith("sensor_", StringComparison.OrdinalIgnoreCase);
        Sections.Clear();
        if (file.LastWrite == null)
        {
            RawJson = "";
            UpdateSubHeader(file);
            return;
        }
        try
        {
            var text = File.ReadAllText(file.Path);
            var root = JsonNode.Parse(text);
            var raw = root?.DeepClone();
            if (!ShowSecrets)
            {
                RestFormatter.MaskSecrets(raw);
            }
            RawJson = raw?.ToJsonString(PrettyJson) ?? text;
            foreach (var section in RestFormatter.Format(root, IsSensorList && OnlyRegistered))
            {
                Sections.Add(section switch
                {
                    KeyValueSection kv => new KeyValueSectionViewModel(kv.Title,
                        kv.Rows.Select(r => new KeyValueRowViewModel(r.Label,
                            !ShowSecrets && RestFormatter.IsSecret(r.Key) && r.Value.Length > 0 ? "••••••••" : r.Value,
                            r.Key)).ToList()),
                    TableSection t => new TableSectionViewModel(t.Title, t.Columns, t.Rows),
                    _ => section,
                });
            }
        }
        catch (IOException)
        {
            // the downloader is replacing the file right now; try again on the next tick
            _shownWrite = null;
        }
        catch (Exception ex)
        {
            RawJson = "";
            Sections.Add(new KeyValueSectionViewModel("Fehler", [new KeyValueRowViewModel("Lesen", ex.Message, "")]));
        }
        UpdateSubHeader(file);
    }

    private void UpdateSubHeader(RestFileItem file)
    {
        var when = file.LastWrite is { } t ? $"aktualisiert {t:dd.MM.yyyy HH:mm:ss} ({file.Age})" : "Datei fehlt";
        SubHeader = $"{file.Description.Replace("\n", " · ")}  ·  {when}";
    }

    public void Dispose()
    {
        _timer.Stop();
    }
}
