using System.Collections.Specialized;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using EcowittGui.ViewModels;

namespace EcowittGui.Views;

public partial class RunnerView : UserControl
{
    private RunnerViewModel? _vm;

    public RunnerView()
    {
        InitializeComponent();
    }

    private RunnerViewModel Vm => (RunnerViewModel)DataContext!;

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (_vm != null)
        {
            _vm.Log.CollectionChanged -= OnLogChanged;
        }
        _vm = DataContext as RunnerViewModel;
        if (_vm != null)
        {
            _vm.Log.CollectionChanged += OnLogChanged;
        }
    }

    private void OnLogChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        // follow the output
        if (e.Action == NotifyCollectionChangedAction.Add && _vm is { Log.Count: > 0 })
        {
            var last = _vm.Log[^1];
            Dispatcher.UIThread.Post(() => LogList.ScrollIntoView(last), DispatcherPriority.Background);
        }
    }

    private string? ScriptDir()
    {
        try
        {
            return Path.GetDirectoryName(Path.GetFullPath(Vm.ScriptPath));
        }
        catch (Exception)
        {
            return null;
        }
    }

    private async Task<string?> PickFile(string title, string? startDir, params FilePickerFileType[] types)
    {
        var storage = TopLevel.GetTopLevel(this)!.StorageProvider;
        var options = new FilePickerOpenOptions { Title = title, AllowMultiple = false, FileTypeFilter = types };
        if (startDir != null && Directory.Exists(startDir))
        {
            options.SuggestedStartLocation = await storage.TryGetFolderFromPathAsync(startDir);
        }
        var files = await storage.OpenFilePickerAsync(options);
        return files.Count > 0 ? files[0].TryGetLocalPath() : null;
    }

    private async void BrowseConfig(object? sender, RoutedEventArgs e)
    {
        var path = await PickFile("Konfigurationsdatei wählen", ScriptDir(),
            new FilePickerFileType("YAML") { Patterns = ["*.yaml", "*.yml"] }, FilePickerFileTypes.All);
        if (path != null)
        {
            Vm.ConfigPath = path;
        }
    }

    private async void BrowseDataDir(object? sender, RoutedEventArgs e)
    {
        var storage = TopLevel.GetTopLevel(this)!.StorageProvider;
        var options = new FolderPickerOpenOptions { Title = "Datenordner wählen", AllowMultiple = false };
        var start = string.IsNullOrWhiteSpace(Vm.DataDir) ? ScriptDir() : Vm.DataDir;
        if (start != null && Directory.Exists(start))
        {
            options.SuggestedStartLocation = await storage.TryGetFolderFromPathAsync(start);
        }
        var folders = await storage.OpenFolderPickerAsync(options);
        if (folders.Count > 0 && folders[0].TryGetLocalPath() is { } path)
        {
            Vm.DataDir = path;
        }
    }

    private async void BrowsePython(object? sender, RoutedEventArgs e)
    {
        var path = await PickFile("Python-Interpreter wählen", null,
            new FilePickerFileType("Programme") { Patterns = ["*.exe"] }, FilePickerFileTypes.All);
        if (path != null)
        {
            Vm.PythonExe = path;
        }
    }

    private async void BrowseScript(object? sender, RoutedEventArgs e)
    {
        var path = await PickFile("download_sd.py wählen", ScriptDir(),
            new FilePickerFileType("Python") { Patterns = ["*.py"] });
        if (path != null)
        {
            Vm.ScriptPath = path;
        }
    }
}
