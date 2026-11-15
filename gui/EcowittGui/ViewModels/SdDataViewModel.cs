using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using System.Text.Json.Nodes;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using EcowittGui.Models;

namespace EcowittGui.ViewModels;

/// <summary>Top level of the SD tree: one file kind with its months.</summary>
public sealed class SdGroupNode(string title, string description)
{
    public string Title { get; } = title;
    public string Description { get; } = description;
    public ObservableCollection<object> Items { get; } = [];
}

/// <summary>Tree leaf for download/.sync_state.json.</summary>
public sealed class SyncStateNode(string path)
{
    public string Path { get; } = path;
    public string Label => "Sync-Status";
    public string Details => "Wann welche Datei geladen wurde";
}

/// <summary>Checkbox of one CSV column in the column picker.</summary>
public sealed partial class ColumnItemViewModel(CsvColumn column, Action changed) : ObservableObject
{
    public CsvColumn Column { get; } = column;
    public string Display => Column.DisplayName;
    public bool IsEnabled => Column.IsNumeric;

    [ObservableProperty]
    private bool _isChecked;

    partial void OnIsCheckedChanged(bool value) => changed();
}

public sealed class ColumnGroupViewModel(string title, IReadOnlyList<ColumnItemViewModel> columns)
{
    public string Title { get; } = title;
    public IReadOnlyList<ColumnItemViewModel> Columns { get; } = columns;
}

/// <summary>One line in the chart: column name, unit and points (NaN = no value).</summary>
public sealed record ChartSeries(string Name, string Unit, double[] Xs, double[] Ys);

/// <summary>Browses the SD card copies in the download folder, grouped by file kind and month.</summary>
public sealed partial class SdDataViewModel : ObservableObject
{
    private readonly MainWindowViewModel _main;
    private CsvTable? _table;
    private int _loadVersion;
    private bool _suppressChartUpdate;

    public ObservableCollection<SdGroupNode> Groups { get; } = [];
    public ObservableCollection<ColumnGroupViewModel> ColumnGroups { get; } = [];

    /// <summary>The chart selection changed; the view redraws from <see cref="GetChartSeries"/>.</summary>
    public event Action? ChartChanged;

    [ObservableProperty]
    private string _folder = "";

    [ObservableProperty]
    private string? _error;

    [ObservableProperty]
    private object? _selectedNode;

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private string _summary = "Links einen Monat auswählen.";

    [ObservableProperty]
    private IReadOnlyList<TableColumn> _tableColumns = [];

    [ObservableProperty]
    private IReadOnlyList<string[]> _tableRows = [];

    [ObservableProperty]
    private bool _hideEmptyColumns = true;

    [ObservableProperty]
    private bool _onlyCheckedInTable;

    [ObservableProperty]
    private string _columnFilter = "";

    [ObservableProperty]
    private bool _newestFirst = true;

    public SdDataViewModel(MainWindowViewModel main)
    {
        _main = main;
    }

    partial void OnSelectedNodeChanged(object? value) => LoadSelection();

    partial void OnHideEmptyColumnsChanged(bool value) => RebuildColumns(keepChecks: true);

    partial void OnColumnFilterChanged(string value) => RebuildColumns(keepChecks: true);

    partial void OnOnlyCheckedInTableChanged(bool value) => RebuildTable();

    partial void OnNewestFirstChanged(bool value) => RebuildTable();

    [RelayCommand]
    public void Refresh()
    {
        var selectedKey = SelectionKey(SelectedNode);
        Groups.Clear();
        try
        {
            var config = _main.LoadConfig();
            Folder = config.SdOutDir;
            Error = Directory.Exists(Folder) ? null : "Ordner existiert (noch) nicht – erst einen SD-Sync starten.";

            var main = new SdGroupNode("Hauptstation", "Innen, Außen, Wind, Regen, Druck, Sonne (YYYYMM A/B/C.csv)");
            var all = new SdGroupNode("Alle Sensoren", "Kanal- und Zusatzsensoren (YYYYMMAllsensors_A/B/C.csv)");
            var other = new SdGroupNode("Sonstige Dateien", "CSV-Dateien mit unbekanntem Namensschema");
            foreach (var set in SdCatalog.Scan(Folder))
            {
                (set.Kind switch { SdFileKind.Main => main, SdFileKind.AllSensors => all, _ => other }).Items.Add(set);
            }
            var state = new SdGroupNode("Sync-Status", "download_sd.py-Zustand");
            if (File.Exists(config.SdStateFile))
            {
                state.Items.Add(new SyncStateNode(config.SdStateFile));
            }
            foreach (var g in new[] { main, all, other, state }.Where(g => g.Items.Count > 0))
            {
                Groups.Add(g);
            }
        }
        catch (Exception ex)
        {
            Error = $"config.yaml konnte nicht gelesen werden: {ex.Message}";
        }

        // keep the selection across a refresh (and reload it: the files may have grown)
        var again = Groups.SelectMany(g => g.Items).FirstOrDefault(i => SelectionKey(i) == selectedKey && selectedKey != null);
        if (again != null)
        {
            if (ReferenceEquals(again, SelectedNode))
            {
                LoadSelection();
            }
            else
            {
                SelectedNode = again;
            }
        }
    }

    private static string? SelectionKey(object? node) => node switch
    {
        SdMonthSet s => $"{s.Kind}|{s.Year}|{s.Month}|{s.Label}",
        SyncStateNode n => n.Path,
        _ => null,
    };

    [RelayCommand]
    private void OpenFolder()
    {
        if (Directory.Exists(Folder))
        {
            Process.Start(new ProcessStartInfo("explorer.exe", Folder) { UseShellExecute = true });
        }
    }

    private async void LoadSelection()
    {
        int version = ++_loadVersion;
        switch (SelectedNode)
        {
            case SdMonthSet set:
                IsLoading = true;
                Summary = $"Lade {set.Label} ...";
                var names = ChannelNames.Load(SafeRestDir());
                var previousChecks = CheckedNames();
                try
                {
                    var table = await Task.Run(() => CsvTable.Load(set.Files.Select(f => f.FullName), names));
                    if (version != _loadVersion)
                    {
                        return;
                    }
                    _table = table;
                    var first = table.Times.FirstOrDefault(t => t != DateTime.MinValue);
                    var last = table.Times.LastOrDefault(t => t != DateTime.MinValue);
                    var used = table.Columns.Count(c => !c.IsEmpty);
                    var de = CultureInfo.GetCultureInfo("de-DE");
                    Summary = $"{set.Label}: {table.Rows.Count.ToString("N0", de)} Zeilen"
                              + (first != default ? $" · {first.ToString("g", de)} – {last.ToString("g", de)}" : "")
                              + $" · {used} von {table.Columns.Count} Spalten mit Werten"
                              + $" · Dateien: {string.Join(", ", set.Files.Select(f => f.Name))}";
                    RebuildColumns(keepChecks: false, previousChecks);
                }
                catch (Exception ex)
                {
                    if (version == _loadVersion)
                    {
                        ClearTable($"Fehler beim Lesen: {ex.Message}");
                    }
                }
                finally
                {
                    if (version == _loadVersion)
                    {
                        IsLoading = false;
                    }
                }
                break;
            case SyncStateNode node:
                LoadSyncState(node);
                break;
            default:
                ClearTable("Links einen Monat auswählen.");
                break;
        }
    }

    private string SafeRestDir()
    {
        try
        {
            return _main.LoadConfig().RestOutDir;
        }
        catch (Exception)
        {
            return "";
        }
    }

    private void ClearTable(string summary)
    {
        _table = null;
        ColumnGroups.Clear();
        TableColumns = [];
        TableRows = [];
        Summary = summary;
        ChartChanged?.Invoke();
    }

    private void LoadSyncState(SyncStateNode node)
    {
        try
        {
            var root = JsonNode.Parse(File.ReadAllText(node.Path)) as JsonObject ?? [];
            var rows = root
                .Select(kv => new[]
                {
                    kv.Key,
                    kv.Value?["size"]?.ToString() ?? "",
                    (kv.Value?["downloaded"]?.ToString() ?? "").Replace('T', ' '),
                })
                .OrderByDescending(r => r[2])
                .ToList();
            _table = null;
            ColumnGroups.Clear();
            TableColumns = [new("Datei auf der SD-Karte", 0), new("Größe (gelistet)", 1), new("Heruntergeladen (Start)", 2)];
            TableRows = rows;
            Summary = $"Sync-Status: {rows.Count} Dateien · {node.Path}";
            ChartChanged?.Invoke();
        }
        catch (Exception ex)
        {
            ClearTable($"Fehler beim Lesen: {ex.Message}");
        }
    }

    private HashSet<string> CheckedNames()
    {
        return ColumnGroups.SelectMany(g => g.Columns).Where(c => c.IsChecked).Select(c => c.Column.Name).ToHashSet();
    }

    private void RebuildColumns(bool keepChecks, HashSet<string>? previous = null)
    {
        if (_table == null)
        {
            return;
        }
        var checkedNames = keepChecks ? CheckedNames() : previous ?? [];
        // keep only names that exist in this table; otherwise pick a sensible default
        if (!checkedNames.Overlaps(_table.Columns.Where(c => c.IsNumeric).Select(c => c.Name)))
        {
            var preferred = _table.Columns.FirstOrDefault(c => c.IsNumeric && c.Name.StartsWith("Outdoor Temperature"))
                            ?? _table.Columns.FirstOrDefault(c => c.IsNumeric && c.Name.Contains("Temperature"))
                            ?? _table.Columns.FirstOrDefault(c => c.IsNumeric);
            checkedNames = preferred != null ? [preferred.Name] : [];
        }

        _suppressChartUpdate = true;
        ColumnGroups.Clear();
        var filter = ColumnFilter.Trim();
        var groups = _table.Columns
            .Where(c => c.Category != "Zeit")
            .Where(c => !HideEmptyColumns || !c.IsEmpty)
            .Where(c => filter.Length == 0 || c.DisplayName.Contains(filter, StringComparison.OrdinalIgnoreCase)
                        || c.Category.Contains(filter, StringComparison.OrdinalIgnoreCase))
            .GroupBy(c => c.Category)
            .OrderBy(g => ColumnCategories.Order(g.Key))
            .ThenBy(g => g.Key, StringComparer.CurrentCulture);
        foreach (var g in groups)
        {
            var items = g.Select(c => new ColumnItemViewModel(c, OnColumnCheckChanged)
            {
                IsChecked = checkedNames.Contains(c.Name) && c.IsNumeric,
            }).ToList();
            ColumnGroups.Add(new ColumnGroupViewModel(g.Key, items));
        }
        _suppressChartUpdate = false;

        RebuildTable();
        ChartChanged?.Invoke();
    }

    private void OnColumnCheckChanged()
    {
        if (_suppressChartUpdate)
        {
            return;
        }
        if (OnlyCheckedInTable)
        {
            RebuildTable();
        }
        ChartChanged?.Invoke();
    }

    private void RebuildTable()
    {
        if (_table == null)
        {
            return;
        }
        var checkedNames = CheckedNames();
        TableColumns = _table.Columns
            .Where(c => c.Category == "Zeit" ? c.Name == "Time"
                : (!HideEmptyColumns || !c.IsEmpty) && (!OnlyCheckedInTable || checkedNames.Contains(c.Name)))
            .Select(c => new TableColumn(c.DisplayName, c.Index))
            .ToList();
        TableRows = NewestFirst ? _table.Rows.Reverse().ToList() : _table.Rows;
    }

    /// <summary>Series for the checked numeric columns.</summary>
    public List<ChartSeries> GetChartSeries()
    {
        var result = new List<ChartSeries>();
        if (_table == null)
        {
            return result;
        }
        var xs = _table.Times.Select(t => t == DateTime.MinValue ? double.NaN : t.ToOADate()).ToArray();
        foreach (var item in ColumnGroups.SelectMany(g => g.Columns).Where(c => c.IsChecked))
        {
            var col = item.Column;
            var ys = _table.Rows.Select(r => CsvTable.ParseNumber(r[col.Index])).ToArray();
            result.Add(new ChartSeries(col.DisplayName, UnitOf(col.Name), xs, ys));
        }
        return result;
    }

    private static string UnitOf(string name)
    {
        int open = name.LastIndexOf('(');
        int close = name.LastIndexOf(')');
        return open >= 0 && close > open ? name[(open + 1)..close] : "";
    }
}
