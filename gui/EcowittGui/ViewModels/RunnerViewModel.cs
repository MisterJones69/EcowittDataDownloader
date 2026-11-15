using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using System.Text;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using EcowittGui.Models;

namespace EcowittGui.ViewModels;

public sealed record LogLine(string Text, bool IsError, bool IsInfo);

/// <summary>Builds the download_sd.py command line from the options and runs it.</summary>
public sealed partial class RunnerViewModel : ObservableObject
{
    private const int MaxLogLines = 5000;

    private readonly AppSettings _settings;
    private Process? _process;

    public ObservableCollection<LogLine> Log { get; } = [];

    /// <summary>--config or --data-dir changed: the data folders must be resolved again.</summary>
    public event Action? PathsChanged;
    public event Action? ProcessExited;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CommandPreview))]
    private string _pythonExe;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CommandPreview))]
    private string _scriptPath;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CommandPreview), nameof(IsLoop), nameof(IsSd), nameof(IsAll), nameof(IsRest),
        nameof(ModeDescription))]
    private RunMode _mode;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CommandPreview))]
    private bool _useInterval;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CommandPreview))]
    private decimal? _interval;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CommandPreview))]
    private string _configPath;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CommandPreview))]
    private string _dataDir;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(StartCommand), nameof(StopCommand), nameof(ShowHelpCommand))]
    [NotifyPropertyChangedFor(nameof(IsIdle))]
    private bool _isRunning;

    [ObservableProperty]
    private string _status = "Bereit.";

    public RunnerViewModel(AppSettings settings)
    {
        _settings = settings;
        _pythonExe = settings.PythonExe;
        _scriptPath = settings.ScriptPath;
        _mode = settings.Mode;
        _useInterval = settings.UseInterval;
        _interval = (decimal)settings.Interval;
        _configPath = settings.ConfigPath;
        _dataDir = settings.DataDir;
    }

    public bool IsIdle => !IsRunning;

    // radio buttons
    public bool IsLoop { get => Mode == RunMode.Loop; set { if (value) { Mode = RunMode.Loop; } } }
    public bool IsSd { get => Mode == RunMode.Sd; set { if (value) { Mode = RunMode.Sd; } } }
    public bool IsAll { get => Mode == RunMode.All; set { if (value) { Mode = RunMode.All; } } }
    public bool IsRest { get => Mode == RunMode.Rest; set { if (value) { Mode = RunMode.Rest; } } }

    public string ModeDescription => Mode switch
    {
        RunMode.Loop => "SD-Sync alle sd.interval Sekunden und REST-Abfragen in ihren Intervallen, in einer Endlosschleife (bis Stopp).",
        RunMode.Sd => "SD-Karte einmal synchronisieren (nur neue/geänderte Dateien und der laufende Monat), dann beenden.",
        RunMode.All => "SD-Karte einmal synchronisieren und dabei jede Datei neu laden, unabhängig vom Sync-Status.",
        RunMode.Rest => "Nur die lokale REST-API des Gateways abfragen, jeden Endpunkt in seinem Intervall (bis Stopp).",
        _ => "",
    };

    public string CommandPreview
    {
        get
        {
            var parts = new List<string> { PythonExe, ScriptPath };
            parts.AddRange(BuildArguments());
            return string.Join(" ", parts.Select(Quote));
        }
    }

    partial void OnConfigPathChanged(string value) => PathsChanged?.Invoke();

    partial void OnDataDirChanged(string value) => PathsChanged?.Invoke();

    partial void OnScriptPathChanged(string value) => PathsChanged?.Invoke();

    /// <summary>The script's arguments (without "-u" and the script path).</summary>
    public List<string> BuildArguments()
    {
        var args = new List<string>();
        switch (Mode)
        {
            case RunMode.Sd:
                args.Add("--sd");
                break;
            case RunMode.All:
                args.Add("--all");
                break;
            case RunMode.Rest:
                args.Add("--rest");
                // --interval is only evaluated together with --rest
                if (UseInterval && Interval is { } interval)
                {
                    args.Add("--interval");
                    args.Add(interval.ToString(CultureInfo.InvariantCulture));
                }
                break;
        }
        if (!string.IsNullOrWhiteSpace(ConfigPath))
        {
            args.Add("--config");
            args.Add(ConfigPath.Trim());
        }
        if (!string.IsNullOrWhiteSpace(DataDir))
        {
            args.Add("--data-dir");
            args.Add(DataDir.Trim());
        }
        return args;
    }

    private static string Quote(string s) => s.Length == 0 || s.Contains(' ') ? $"\"{s}\"" : s;

    private bool CanStart() => !IsRunning;

    private bool CanStop() => IsRunning;

    [RelayCommand(CanExecute = nameof(CanStart))]
    private void Start() => Run(BuildArguments());

    [RelayCommand(CanExecute = nameof(CanStart))]
    private void ShowHelp() => Run(["--help"]);

    [RelayCommand(CanExecute = nameof(CanStop))]
    private void Stop()
    {
        try
        {
            if (_process is { HasExited: false })
            {
                AddLine("Prozess wird beendet ...", isInfo: true);
                _process.Kill(entireProcessTree: true);
            }
        }
        catch (Exception ex)
        {
            AddLine($"Beenden fehlgeschlagen: {ex.Message}", isError: true);
        }
    }

    [RelayCommand]
    private void ClearLog() => Log.Clear();

    private async void Run(List<string> arguments)
    {
        SaveSettings();
        if (!File.Exists(ScriptPath))
        {
            AddLine($"Skript nicht gefunden: {ScriptPath}", isError: true);
            return;
        }
        var scriptDir = Path.GetDirectoryName(Path.GetFullPath(ScriptPath))!;
        var psi = new ProcessStartInfo(PythonExe)
        {
            // relative --config / --data-dir resolve against the script's folder
            WorkingDirectory = scriptDir,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };
        psi.ArgumentList.Add("-u");   // unbuffered: show output as it is printed
        psi.ArgumentList.Add(Path.GetFullPath(ScriptPath));
        foreach (var a in arguments)
        {
            psi.ArgumentList.Add(a);
        }
        psi.Environment["PYTHONIOENCODING"] = "utf-8";
        psi.Environment["PYTHONUNBUFFERED"] = "1";

        var process = new Process { StartInfo = psi };
        process.OutputDataReceived += (_, e) => { if (e.Data != null) { Post(e.Data, false); } };
        process.ErrorDataReceived += (_, e) => { if (e.Data != null) { Post(e.Data, true); } };

        var commandLine = string.Join(" ", new[] { PythonExe, "-u", ScriptPath }.Concat(arguments).Select(Quote));
        AddLine($"▶ {DateTime.Now:HH:mm:ss}  {commandLine}", isInfo: true);
        try
        {
            process.Start();
        }
        catch (Exception ex)
        {
            AddLine($"Start fehlgeschlagen: {ex.Message} (Python-Pfad prüfen)", isError: true);
            process.Dispose();
            return;
        }
        _process = process;
        IsRunning = true;
        Status = $"Läuft (PID {process.Id}) ...";
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        await process.WaitForExitAsync();
        // output lines are still queued on the dispatcher (posted at Normal priority);
        // let them through before the exit line
        await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);

        int code = process.ExitCode;
        AddLine($"■ {DateTime.Now:HH:mm:ss}  Prozess beendet, Exit-Code {code}", isError: code != 0, isInfo: code == 0);
        Status = code == 0 ? "Beendet." : $"Beendet mit Exit-Code {code}.";
        _process = null;
        process.Dispose();
        IsRunning = false;
        ProcessExited?.Invoke();
    }

    private void Post(string text, bool isError)
    {
        Dispatcher.UIThread.Post(() => AddLine(text, isError));
    }

    private void AddLine(string text, bool isError = false, bool isInfo = false)
    {
        Log.Add(new LogLine(text, isError, isInfo));
        while (Log.Count > MaxLogLines)
        {
            Log.RemoveAt(0);
        }
    }

    public void SaveSettings()
    {
        _settings.PythonExe = PythonExe;
        _settings.ScriptPath = ScriptPath;
        _settings.Mode = Mode;
        _settings.UseInterval = UseInterval;
        _settings.Interval = (double)(Interval ?? 0);
        _settings.ConfigPath = ConfigPath;
        _settings.DataDir = DataDir;
        _settings.Save();
    }

    public void Shutdown()
    {
        SaveSettings();
        try
        {
            if (_process is { HasExited: false })
            {
                _process.Kill(entireProcessTree: true);
            }
        }
        catch (Exception)
        {
            // the app is closing anyway
        }
    }
}
