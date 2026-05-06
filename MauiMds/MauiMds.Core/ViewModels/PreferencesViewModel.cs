using MauiMds.Features.Editor;
using MauiMds.Logging;
using MauiMds.Models;
using MauiMds.Services;
using MauiMds.Transcription;
using Microsoft.Extensions.Logging;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;

namespace MauiMds.ViewModels;

public sealed class PreferencesViewModel : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;
    public event EventHandler<EditorPreferences>? PreferencesSaved;
    public event EventHandler<(string Message, Exception? Exception, string InlineMessage)>? SaveError;

    private readonly IEditorPreferencesService _preferencesService;
    private readonly FileLogLevelSwitch _fileLogLevelSwitch;
    private readonly IPlatformInfo _platformInfo;
    private readonly IApplicationLifetime _applicationLifetime;

    private bool _isPreferencesVisible;
    private string _preferencesAutoSaveDelaySecondsText = "30";
    private string _preferencesMaxLogFileSizeMbText = "2";
    private string _preferencesInitialViewerRenderLineCountText = "20";
    private string _preferencesFileLogLevelText = "Info";
    private bool _preferencesAutoSaveEnabled = true;
    private bool _preferencesUse24HourTime;
    private bool _isShortcutsTabActive;
    private bool _isTranscriptionTabActive;
    private TranscriptionEngineType _preferencesTranscriptionEngine = TranscriptionEngineType.AppleSpeech;
    private DiarizationEngineType _preferencesDiarizationEngine = DiarizationEngineType.None;
    private string _preferencesWhisperBinaryPath = string.Empty;
    private string _preferencesWhisperModelPath = string.Empty;
    private string _preferencesPyannotePythonPath = string.Empty;
    private string _preferencesPyannoteHfToken = string.Empty;
    private RecordingFormat _preferencesRecordingFormat = RecordingFormat.M4A;
    private string _shortcutKeyHeader1 = "1";
    private string _shortcutKeyHeader2 = "2";
    private string _shortcutKeyHeader3 = "3";
    private string _shortcutKeyBold = "B";
    private string _shortcutKeyItalic = "I";
    private int _preferencesWorkspaceRefreshIntervalSeconds = 30;

    public PreferencesViewModel(
        IEditorPreferencesService preferencesService,
        FileLogLevelSwitch fileLogLevelSwitch,
        IPlatformInfo platformInfo,
        IApplicationLifetime applicationLifetime)
    {
        _preferencesService = preferencesService;
        _fileLogLevelSwitch = fileLogLevelSwitch;
        _platformInfo = platformInfo;
        _applicationLifetime = applicationLifetime;

        Current = _preferencesService.Load();
        LoadFieldsFromCurrent();

        ShowPreferencesCommand = new RelayCommand(Show);
        SavePreferencesCommand = new RelayCommand(async () => await SaveAsync());
        CancelPreferencesCommand = new RelayCommand(Cancel);
        ShowGeneralTabCommand    = new RelayCommand(() => { IsShortcutsTabActive = false; IsTranscriptionTabActive = false; });
        ShowShortcutsTabCommand  = new RelayCommand(() => { IsShortcutsTabActive = true;  IsTranscriptionTabActive = false; });
        ShowTranscriptionTabCommand = new RelayCommand(() => { IsShortcutsTabActive = false; IsTranscriptionTabActive = true; });
    }

    public ICommand ShowPreferencesCommand { get; }
    public ICommand SavePreferencesCommand { get; }
    public ICommand CancelPreferencesCommand { get; }
    public ICommand ShowGeneralTabCommand { get; }
    public ICommand ShowShortcutsTabCommand { get; }
    public ICommand ShowTranscriptionTabCommand { get; }

    public EditorPreferences Current { get; private set; }

    public bool IsPreferencesVisible
    {
        get => _isPreferencesVisible;
        private set
        {
            if (_isPreferencesVisible == value) return;
            _isPreferencesVisible = value;
            OnPropertyChanged();
        }
    }

    public bool PreferencesAutoSaveEnabled
    {
        get => _preferencesAutoSaveEnabled;
        set { if (_preferencesAutoSaveEnabled != value) { _preferencesAutoSaveEnabled = value; OnPropertyChanged(); } }
    }

    public string PreferencesAutoSaveDelaySecondsText
    {
        get => _preferencesAutoSaveDelaySecondsText;
        set { if (_preferencesAutoSaveDelaySecondsText != value) { _preferencesAutoSaveDelaySecondsText = value; OnPropertyChanged(); } }
    }

    public string PreferencesMaxLogFileSizeMbText
    {
        get => _preferencesMaxLogFileSizeMbText;
        set { if (_preferencesMaxLogFileSizeMbText != value) { _preferencesMaxLogFileSizeMbText = value; OnPropertyChanged(); } }
    }

    public string PreferencesInitialViewerRenderLineCountText
    {
        get => _preferencesInitialViewerRenderLineCountText;
        set { if (_preferencesInitialViewerRenderLineCountText != value) { _preferencesInitialViewerRenderLineCountText = value; OnPropertyChanged(); } }
    }

    public IReadOnlyList<string> AvailableFileLogLevels { get; } = ["Info", "Warning", "Error", "Debug", "Trace"];

    public bool PreferencesUse24HourTime
    {
        get => _preferencesUse24HourTime;
        set { if (_preferencesUse24HourTime != value) { _preferencesUse24HourTime = value; OnPropertyChanged(); } }
    }

    public bool IsShortcutsTabActive
    {
        get => _isShortcutsTabActive;
        set
        {
            if (_isShortcutsTabActive == value) return;
            _isShortcutsTabActive = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsGeneralTabActive));
            OnPropertyChanged(nameof(IsTranscriptionTabActive));
        }
    }

    public bool IsTranscriptionTabActive
    {
        get => _isTranscriptionTabActive;
        set
        {
            if (_isTranscriptionTabActive == value) return;
            _isTranscriptionTabActive = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsGeneralTabActive));
            OnPropertyChanged(nameof(IsShortcutsTabActive));
        }
    }

    public bool IsGeneralTabActive => !_isShortcutsTabActive && !_isTranscriptionTabActive;

    public TranscriptionEngineType PreferencesTranscriptionEngine
    {
        get => _preferencesTranscriptionEngine;
        set
        {
            if (_preferencesTranscriptionEngine == value) return;
            _preferencesTranscriptionEngine = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsWhisperCppSelected));
        }
    }

    public DiarizationEngineType PreferencesDiarizationEngine
    {
        get => _preferencesDiarizationEngine;
        set
        {
            if (_preferencesDiarizationEngine == value) return;
            _preferencesDiarizationEngine = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsPyannoteSelected));
        }
    }

    public string PreferencesWhisperBinaryPath
    {
        get => _preferencesWhisperBinaryPath;
        set { if (_preferencesWhisperBinaryPath != value) { _preferencesWhisperBinaryPath = value; OnPropertyChanged(); } }
    }

    public string PreferencesWhisperModelPath
    {
        get => _preferencesWhisperModelPath;
        set { if (_preferencesWhisperModelPath != value) { _preferencesWhisperModelPath = value; OnPropertyChanged(); } }
    }

    public string PreferencesPyannotePythonPath
    {
        get => _preferencesPyannotePythonPath;
        set { if (_preferencesPyannotePythonPath != value) { _preferencesPyannotePythonPath = value; OnPropertyChanged(); } }
    }

    public string PreferencesPyannoteHfToken
    {
        get => _preferencesPyannoteHfToken;
        set { if (_preferencesPyannoteHfToken != value) { _preferencesPyannoteHfToken = value; OnPropertyChanged(); } }
    }

    public bool IsWhisperCppSelected => _preferencesTranscriptionEngine == TranscriptionEngineType.WhisperCpp;
    public bool IsPyannoteSelected   => _preferencesDiarizationEngine   == DiarizationEngineType.Pyannote;

    public RecordingFormat PreferencesRecordingFormat
    {
        get => _preferencesRecordingFormat;
        set { if (_preferencesRecordingFormat != value) { _preferencesRecordingFormat = value; OnPropertyChanged(); OnPropertyChanged(nameof(ShowWindowsFlacWarning)); } }
    }

    public bool IsWindowsPlatform => _platformInfo.IsWindows;
    public bool ShowWindowsFlacWarning => IsWindowsPlatform && _preferencesRecordingFormat == RecordingFormat.FLAC;
    public bool IsAppleSpeechAvailable => _platformInfo.IsMacCatalyst;

    public string ShortcutKeyHeader1
    {
        get => _shortcutKeyHeader1;
        set { if (_shortcutKeyHeader1 != value) { _shortcutKeyHeader1 = value; OnPropertyChanged(); } }
    }

    public string ShortcutKeyHeader2
    {
        get => _shortcutKeyHeader2;
        set { if (_shortcutKeyHeader2 != value) { _shortcutKeyHeader2 = value; OnPropertyChanged(); } }
    }

    public string ShortcutKeyHeader3
    {
        get => _shortcutKeyHeader3;
        set { if (_shortcutKeyHeader3 != value) { _shortcutKeyHeader3 = value; OnPropertyChanged(); } }
    }

    public string ShortcutKeyBold
    {
        get => _shortcutKeyBold;
        set { if (_shortcutKeyBold != value) { _shortcutKeyBold = value; OnPropertyChanged(); } }
    }

    public string ShortcutKeyItalic
    {
        get => _shortcutKeyItalic;
        set { if (_shortcutKeyItalic != value) { _shortcutKeyItalic = value; OnPropertyChanged(); } }
    }

    public IReadOnlyList<KeyboardShortcutDefinition> CurrentShortcuts => Current.KeyboardShortcuts;
    public int InitialViewerRenderLineCount => Math.Max(5, Current.InitialViewerRenderLineCount);
    public string PreferredTimeFormat => Current.Use24HourTime ? "HH:mm:ss" : "h:mm:ss tt";

    public string PreferencesFileLogLevelText
    {
        get => _preferencesFileLogLevelText;
        set { if (_preferencesFileLogLevelText != value) { _preferencesFileLogLevelText = value; OnPropertyChanged(); } }
    }

    public int PreferencesWorkspaceRefreshIntervalSeconds
    {
        get => _preferencesWorkspaceRefreshIntervalSeconds;
        set { if (_preferencesWorkspaceRefreshIntervalSeconds != value) { _preferencesWorkspaceRefreshIntervalSeconds = value; OnPropertyChanged(); } }
    }

    private void Show()
    {
        PreferencesAutoSaveEnabled = Current.AutoSaveEnabled;
        PreferencesUse24HourTime = Current.Use24HourTime;
        PreferencesAutoSaveDelaySecondsText = Current.AutoSaveDelaySeconds.ToString();
        PreferencesMaxLogFileSizeMbText = Current.MaxLogFileSizeMb.ToString();
        PreferencesInitialViewerRenderLineCountText = Current.InitialViewerRenderLineCount.ToString();
        PreferencesFileLogLevelText = FormatLogLevel(Current.FileLogLevel);
        IsShortcutsTabActive = false;
        IsTranscriptionTabActive = false;
        LoadShortcutKeyFields();
        LoadTranscriptionFields();
        IsPreferencesVisible = true;
    }

    private void Cancel() => IsPreferencesVisible = false;

    private void LoadFieldsFromCurrent()
    {
        _preferencesAutoSaveEnabled = Current.AutoSaveEnabled;
        _preferencesUse24HourTime = Current.Use24HourTime;
        _preferencesAutoSaveDelaySecondsText = Current.AutoSaveDelaySeconds.ToString();
        _preferencesMaxLogFileSizeMbText = Current.MaxLogFileSizeMb.ToString();
        _preferencesInitialViewerRenderLineCountText = Current.InitialViewerRenderLineCount.ToString();
        _preferencesFileLogLevelText = FormatLogLevel(Current.FileLogLevel);
        _preferencesWorkspaceRefreshIntervalSeconds = Current.WorkspaceRefreshIntervalSeconds;
        LoadShortcutKeyFields();
        LoadTranscriptionFields();
    }

    private void LoadShortcutKeyFields()
    {
        ShortcutKeyHeader1 = GetShortcutKey(EditorActionType.Header1);
        ShortcutKeyHeader2 = GetShortcutKey(EditorActionType.Header2);
        ShortcutKeyHeader3 = GetShortcutKey(EditorActionType.Header3);
        ShortcutKeyBold    = GetShortcutKey(EditorActionType.Bold);
        ShortcutKeyItalic  = GetShortcutKey(EditorActionType.Italic);
    }

    private void LoadTranscriptionFields()
    {
        _preferencesTranscriptionEngine = Current.TranscriptionEngine;
        _preferencesDiarizationEngine   = Current.DiarizationEngine;
        _preferencesWhisperBinaryPath   = Current.WhisperBinaryPath;
        _preferencesWhisperModelPath    = Current.WhisperModelPath;
        _preferencesPyannotePythonPath  = Current.PyannotePythonPath;
        _preferencesPyannoteHfToken     = Current.PyannoteHfToken;
        _preferencesRecordingFormat     = Current.RecordingFormat;
        OnPropertyChanged(nameof(PreferencesTranscriptionEngine));
        OnPropertyChanged(nameof(PreferencesDiarizationEngine));
        OnPropertyChanged(nameof(PreferencesWhisperBinaryPath));
        OnPropertyChanged(nameof(PreferencesWhisperModelPath));
        OnPropertyChanged(nameof(PreferencesPyannotePythonPath));
        OnPropertyChanged(nameof(PreferencesPyannoteHfToken));
        OnPropertyChanged(nameof(PreferencesRecordingFormat));
        OnPropertyChanged(nameof(IsWhisperCppSelected));
        OnPropertyChanged(nameof(IsPyannoteSelected));
    }

    private string GetShortcutKey(EditorActionType action) =>
        Current.KeyboardShortcuts.FirstOrDefault(s => s.Action == action)?.Key.ToUpperInvariant() ?? string.Empty;

    private async Task SaveAsync()
    {
        if (!int.TryParse(PreferencesAutoSaveDelaySecondsText, out var delaySeconds) || delaySeconds < 5)
        {
            SaveError?.Invoke(this, ("Invalid autosave preference.", null, "Autosave delay must be at least 5 seconds."));
            return;
        }

        if (!int.TryParse(PreferencesMaxLogFileSizeMbText, out var maxLogFileSizeMb) || maxLogFileSizeMb < 1)
        {
            SaveError?.Invoke(this, ("Invalid log size preference.", null, "Max log size must be at least 1 MB."));
            return;
        }

        if (!int.TryParse(PreferencesInitialViewerRenderLineCountText, out var initialViewerRenderLineCount) || initialViewerRenderLineCount < 5)
        {
            SaveError?.Invoke(this, ("Invalid viewer render preference.", null, "Initial viewer render lines must be at least 5."));
            return;
        }

        if (!TryParseFileLogLevel(PreferencesFileLogLevelText, out var fileLogLevel))
        {
            SaveError?.Invoke(this, ("Invalid log level preference.", null, "File log level must be Trace, Debug, Information, Warning, or Error."));
            return;
        }

        var saved = new EditorPreferences
        {
            AutoSaveEnabled = PreferencesAutoSaveEnabled,
            AutoSaveDelaySeconds = delaySeconds,
            MaxLogFileSizeMb = maxLogFileSizeMb,
            InitialViewerRenderLineCount = initialViewerRenderLineCount,
            Use24HourTime = PreferencesUse24HourTime,
            FileLogLevel = fileLogLevel,
            KeyboardShortcuts = BuildShortcutsFromFields(),
            TranscriptionEngine = _preferencesTranscriptionEngine,
            DiarizationEngine = _preferencesDiarizationEngine,
            WhisperBinaryPath = _preferencesWhisperBinaryPath,
            WhisperModelPath = _preferencesWhisperModelPath,
            PyannotePythonPath = _preferencesPyannotePythonPath,
            PyannoteHfToken = _preferencesPyannoteHfToken,
            RecordingFormat = _preferencesRecordingFormat,
            WorkspaceRefreshIntervalSeconds = Math.Max(0, _preferencesWorkspaceRefreshIntervalSeconds)
        };

        _preferencesService.Save(saved);
        _fileLogLevelSwitch.MinimumLevel = fileLogLevel;
        Current = saved;
        OnPropertyChanged(nameof(CurrentShortcuts));
        OnPropertyChanged(nameof(InitialViewerRenderLineCount));
        OnPropertyChanged(nameof(PreferredTimeFormat));
        IsPreferencesVisible = false;
        PreferencesSaved?.Invoke(this, saved);
    }

    private List<KeyboardShortcutDefinition> BuildShortcutsFromFields() =>
    [
        new KeyboardShortcutDefinition { Action = EditorActionType.Header1, Key = NormalizeShortcutKey(ShortcutKeyHeader1, "1") },
        new KeyboardShortcutDefinition { Action = EditorActionType.Header2, Key = NormalizeShortcutKey(ShortcutKeyHeader2, "2") },
        new KeyboardShortcutDefinition { Action = EditorActionType.Header3, Key = NormalizeShortcutKey(ShortcutKeyHeader3, "3") },
        new KeyboardShortcutDefinition { Action = EditorActionType.Bold,    Key = NormalizeShortcutKey(ShortcutKeyBold,    "B") },
        new KeyboardShortcutDefinition { Action = EditorActionType.Italic,  Key = NormalizeShortcutKey(ShortcutKeyItalic,  "I") },
    ];

    private static string NormalizeShortcutKey(string raw, string fallback)
    {
        var trimmed = raw.Trim().ToUpperInvariant();
        return trimmed.Length > 0 && char.IsLetterOrDigit(trimmed[0]) ? trimmed[0].ToString() : fallback;
    }

    private static string FormatLogLevel(LogLevel logLevel) =>
        logLevel == LogLevel.Information ? "Info" : logLevel.ToString();

    private static bool TryParseFileLogLevel(string text, out LogLevel logLevel)
    {
        if (string.Equals(text, "Info", StringComparison.OrdinalIgnoreCase))
        {
            logLevel = LogLevel.Information;
            return true;
        }
        return Enum.TryParse(text, ignoreCase: true, out logLevel) && logLevel != LogLevel.None;
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        if (_applicationLifetime.IsTerminating) return;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
