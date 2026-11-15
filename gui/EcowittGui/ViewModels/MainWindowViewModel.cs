using CommunityToolkit.Mvvm.ComponentModel;
using EcowittGui.Models;

namespace EcowittGui.ViewModels;

public sealed partial class MainWindowViewModel : ObservableObject
{
    public AppSettings Settings { get; }
    public RunnerViewModel Runner { get; }
    public SdDataViewModel SdData { get; }
    public RestDataViewModel RestData { get; }

    [ObservableProperty]
    private int _selectedTab;

    public MainWindowViewModel()
    {
        Settings = AppSettings.Load();
        Runner = new RunnerViewModel(Settings);
        SdData = new SdDataViewModel(this);
        RestData = new RestDataViewModel(this);

        // the data folders depend on --config / --data-dir of the downloader tab
        Runner.PathsChanged += () =>
        {
            SdData.Refresh();
            RestData.Refresh();
        };
        // new SD files after a run
        Runner.ProcessExited += () => SdData.Refresh();

        SdData.Refresh();
        RestData.Refresh();
    }

    /// <summary>Reads config.yaml with the current --config / --data-dir values.</summary>
    public DownloaderConfig LoadConfig()
    {
        var scriptDir = Path.GetDirectoryName(Path.GetFullPath(Runner.ScriptPath))
                        ?? throw new InvalidOperationException("Skriptpfad ist ungültig.");
        return DownloaderConfig.Load(scriptDir, Runner.ConfigPath, Runner.DataDir);
    }

    public void Shutdown()
    {
        Runner.Shutdown();
        RestData.Dispose();
        Settings.Save();
    }
}
