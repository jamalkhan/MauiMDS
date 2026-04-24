using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using MauiMds.AudioCapture;
using MauiMds.Transcription;
using MauiMds.Features.Editor;
using MauiMds.Features.Export;
using MauiMds.Features.Session;
using MauiMds.Features.Workspace;
using MauiMds.Logging;
using MauiMds.Models;
using MauiMds.Services;
using Microsoft.Extensions.Logging;

namespace MauiMds.ViewModels;

public class MainViewModel : INotifyPropertyChanged
{
    private static readonly TimeSpan ViewerParseDebounceDelay = TimeSpan.FromMilliseconds(250);
    private static readonly TimeSpan EditorParseDebounceDelay = TimeSpan.FromMilliseconds(900);
    private static readonly TimeSpan ExternalChangeDebounceDelay = TimeSpan.FromMilliseconds(400);
    private const double DefaultWorkspacePanelWidth = 260;
    private const double MinWorkspacePanelWidth = 180;
    private const double MaxWorkspacePanelWidth = 520;

    public event PropertyChangedEventHandler? PropertyChanged;
    public event EventHandler<MarkdownDocument>? DocumentApplied;
    public event EventHandler<EditorActionRequestedEventArgs>? EditorActionRequested;

    private readonly IMarkdownDocumentService _documentService;
    private readonly IWorkspaceBrowserService _workspaceBrowserService;
    private readonly IEditorPreferencesService _preferencesService;
    private readonly IDocumentWatchService _documentWatchService;
    private readonly IClock _clock;
    private readonly ILogger<MainViewModel> _logger;
    private readonly FileLogLevelSwitch _fileLogLevelSwitch;
    private readonly DocumentApplyController _documentApplyController;
    private readonly DocumentWorkflowController _documentWorkflowController;
    private readonly PreviewPipelineController _previewPipelineController;
    private readonly AutosaveCoordinator _autosaveCoordinator;
    private readonly SessionRestoreCoordinator _sessionRestoreCoordinator;
    private readonly EditorModeSupportController _editorModeSupportController;
    private readonly IPdfExportService _pdfExportService;
    private readonly IAudioCaptureService _audioCaptureService;
    private readonly ITranscriptionPipelineFactory _transcriptionPipelineFactory;

    private EditorDocumentState _document = new();
    private EditorViewMode _selectedViewMode = EditorViewMode.Viewer;
    private EditorPreferences _preferences;
    private SessionState _sessionState;

    private IReadOnlyList<MarkdownBlock> _parsedBlocks = Array.Empty<MarkdownBlock>();
    private bool _isInitialized;
    private bool _isOpeningDocument;
    private bool _isSavingDocument;
    private bool _isWorkspacePanelVisible;
    private bool _isPreferencesVisible;
    private bool _isLoadingDocument;
    private double _workspacePanelWidth = DefaultWorkspacePanelWidth;
    private string _editorText = string.Empty;
    private string _inlineErrorMessage = string.Empty;
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
    private string _shortcutKeyHeader1 = "1";
    private string _shortcutKeyHeader2 = "2";
    private string _shortcutKeyHeader3 = "3";
    private string _shortcutKeyBold = "B";
    private string _shortcutKeyItalic = "I";
    private bool _isViewerLoading;
    private string _viewerLoadingPreviewText = string.Empty;
    private DateTimeOffset? _lastParsedBlocksAssignedUtc;
    private string? _pendingDocumentFilePath;
    private string? _pendingDocumentFileName;
    private IReadOnlyList<MarkdownBlock> _deferredPreviewBlocks = Array.Empty<MarkdownBlock>();
    private string _deferredPreviewInlineErrorMessage = string.Empty;
    private string _deferredPreviewContent = string.Empty;
    private string _deferredPreviewFilePath = string.Empty;
    private EditorViewMode? _deferredPreviewViewMode;
    private bool _isRecording;
    private bool _isRecordingTransitioning;

    public MainViewModel(
        IMarkdownDocumentService documentService,
        IWorkspaceBrowserService workspaceBrowserService,
        IEditorPreferencesService preferencesService,
        IDocumentWatchService documentWatchService,
        IClock clock,
        FileLogLevelSwitch fileLogLevelSwitch,
        WorkspaceExplorerState workspaceExplorerState,
        DocumentApplyController documentApplyController,
        DocumentWorkflowController documentWorkflowController,
        PreviewPipelineController previewPipelineController,
        EditorModeSupportController editorModeSupportController,
        AutosaveCoordinator autosaveCoordinator,
        SessionRestoreCoordinator sessionRestoreCoordinator,
        IPdfExportService pdfExportService,
        IAudioCaptureService audioCaptureService,
        ITranscriptionPipelineFactory transcriptionPipelineFactory,
        ILogger<MainViewModel> logger)
    {
        _documentService = documentService;
        _workspaceBrowserService = workspaceBrowserService;
        _preferencesService = preferencesService;
        _documentWatchService = documentWatchService;
        _clock = clock;
        _fileLogLevelSwitch = fileLogLevelSwitch;
        _documentApplyController = documentApplyController;
        _documentWorkflowController = documentWorkflowController;
        _previewPipelineController = previewPipelineController;
        _editorModeSupportController = editorModeSupportController;
        _autosaveCoordinator = autosaveCoordinator;
        _sessionRestoreCoordinator = sessionRestoreCoordinator;
        _pdfExportService = pdfExportService;
        _audioCaptureService = audioCaptureService;
        _transcriptionPipelineFactory = transcriptionPipelineFactory;
        _logger = logger;
        _audioCaptureService.StateChanged += OnAudioCaptureStateChanged;
        _preferences = _preferencesService.Load();
        _sessionState = _sessionRestoreCoordinator.Load();
        Workspace = workspaceExplorerState;
        _preferencesAutoSaveEnabled = _preferences.AutoSaveEnabled;
        _preferencesUse24HourTime = _preferences.Use24HourTime;
        _preferencesAutoSaveDelaySecondsText = _preferences.AutoSaveDelaySeconds.ToString();
        _preferencesMaxLogFileSizeMbText = _preferences.MaxLogFileSizeMb.ToString();
        _preferencesInitialViewerRenderLineCountText = _preferences.InitialViewerRenderLineCount.ToString();
        _preferencesFileLogLevelText = FormatLogLevel(_preferences.FileLogLevel);
        _workspacePanelWidth = ClampWorkspacePanelWidth(_sessionState.WorkspacePanelWidth);

        _documentWatchService.DocumentChanged += OnWatchedDocumentChanged;
        Workspace.PropertyChanged += OnWorkspacePropertyChanged;

        OpenFileCommand = new Command(async () => await OpenFileAsync(), () => !IsBusy);
        NewDocumentCommand = new Command(async () => await NewDocumentAsync(), () => !IsBusy);
        SaveCommand = new Command(async () => await SaveDocumentAsync(), () => !IsBusy);
        SaveAsCommand = new Command(async () => await SaveDocumentAsAsync(), () => !IsBusy);
        RevertCommand = new Command(async () => await RevertDocumentAsync(), () => !IsBusy);
        CloseDocumentCommand = new Command(async () => await CloseDocumentAsync(), () => !IsBusy);
        ExportPdfCommand = new Command(async () => await ExportAsPdfAsync(), () => !IsBusy && _parsedBlocks.Count > 0);
        ShowPreferencesCommand = new Command(ShowPreferences);
        SavePreferencesCommand = new Command(async () => await SavePreferencesAsync());
        CancelPreferencesCommand = new Command(CancelPreferences);
        SetViewModeCommand = new Command<EditorViewMode>(SetViewMode);
        ToggleWorkspacePanelCommand = new Command(ToggleWorkspacePanel);
        OpenFolderCommand = new Command(async () => await OpenFolderAsync());
        SelectWorkspaceItemCommand = new Command<WorkspaceTreeItem>(async item => await SelectWorkspaceItemAsync(item));
        NavigateWorkspaceItemCommand = new Command<WorkspaceTreeItem>(async item => await NavigateWorkspaceItemAsync(item));
        ToggleWorkspaceItemExpansionCommand = new Command<WorkspaceTreeItem>(ToggleWorkspaceItemExpansion);
        BeginRenameWorkspaceItemCommand = new Command<WorkspaceTreeItem>(BeginRenameWorkspaceItem);
        CreateMdsCommand = new Command(async () => await CreateMdsAsync(), () => HasWorkspaceRoot);
        NavigateUpWorkspaceCommand = new Command(async () => await NavigateUpWorkspaceAsync(), () => CanNavigateUpWorkspace);
        SetWorkspaceFolderToCurrentCommand = new Command(async () => await SetWorkspaceFolderToCurrentAsync(), () => CanSetCurrentFolderAsWorkspace);
        UndoCommand = new Command(() => RequestEditorAction(EditorActionType.Undo));
        RedoCommand = new Command(() => RequestEditorAction(EditorActionType.Redo));
        CutCommand = new Command(() => RequestEditorAction(EditorActionType.Cut));
        CopyCommand = new Command(() => RequestEditorAction(EditorActionType.Copy));
        PasteCommand = new Command(() => RequestEditorAction(EditorActionType.Paste));
        FindCommand = new Command(() => RequestEditorAction(EditorActionType.Find));
        FormatParagraphCommand = new Command(() => RequestEditorAction(EditorActionType.Paragraph));
        FormatHeader1Command = new Command(() => RequestEditorAction(EditorActionType.Header1));
        FormatHeader2Command = new Command(() => RequestEditorAction(EditorActionType.Header2));
        FormatHeader3Command = new Command(() => RequestEditorAction(EditorActionType.Header3));
        FormatBulletCommand = new Command(() => RequestEditorAction(EditorActionType.Bullet));
        FormatChecklistCommand = new Command(() => RequestEditorAction(EditorActionType.Checklist));
        FormatQuoteCommand = new Command(() => RequestEditorAction(EditorActionType.Quote));
        FormatCodeCommand = new Command(() => RequestEditorAction(EditorActionType.Code));
        FormatBoldCommand = new Command(() => RequestEditorAction(EditorActionType.Bold));
        FormatItalicCommand = new Command(() => RequestEditorAction(EditorActionType.Italic));
        ShowGeneralTabCommand = new Command(() => { IsShortcutsTabActive = false; IsTranscriptionTabActive = false; });
        ShowShortcutsTabCommand = new Command(() => { IsShortcutsTabActive = true; IsTranscriptionTabActive = false; });
        ShowTranscriptionTabCommand = new Command(() => { IsShortcutsTabActive = false; IsTranscriptionTabActive = true; });
        ToggleRecordingCommand = new Command(async () => await ToggleRecordingAsync(), () => !_isRecordingTransitioning);
        TranscribeAudioCommand = new Command<WorkspaceTreeItem>(async item => await TranscribeAudioAsync(item));
        LoadShortcutKeyFields();
    }

    public event EventHandler? KeyboardShortcutsChanged;

    public IReadOnlyList<MarkdownBlock> ParsedBlocks
    {
        get => _parsedBlocks;
        private set
        {
            if (ReferenceEquals(_parsedBlocks, value))
            {
                return;
            }

            _parsedBlocks = value;
            _lastParsedBlocksAssignedUtc = _clock.UtcNow;
            _logger.LogDebug(
                "ParsedBlocks assigned. BlockCount: {BlockCount}, FilePath: {FilePath}, AssignedUtc: {AssignedUtc:O}",
                value.Count,
                _document.FilePath,
                _lastParsedBlocksAssignedUtc);
            OnPropertyChanged();
            (ExportPdfCommand as Command)?.ChangeCanExecute();
        }
    }
    public WorkspaceExplorerState Workspace { get; }
    public ObservableCollection<WorkspaceTreeItem> WorkspaceItems => Workspace.WorkspaceItems;

    public ICommand OpenFileCommand { get; }
    public ICommand NewDocumentCommand { get; }
    public ICommand SaveCommand { get; }
    public ICommand SaveAsCommand { get; }
    public ICommand RevertCommand { get; }
    public ICommand CloseDocumentCommand { get; }
    public ICommand ExportPdfCommand { get; }
    public ICommand ShowPreferencesCommand { get; }
    public ICommand SavePreferencesCommand { get; }
    public ICommand CancelPreferencesCommand { get; }
    public ICommand SetViewModeCommand { get; }
    public ICommand ToggleWorkspacePanelCommand { get; }
    public ICommand OpenFolderCommand { get; }
    public ICommand SelectWorkspaceItemCommand { get; }
    public ICommand NavigateWorkspaceItemCommand { get; }
    public ICommand ToggleWorkspaceItemExpansionCommand { get; }
    public ICommand BeginRenameWorkspaceItemCommand { get; }
    public ICommand CreateMdsCommand { get; }
    public ICommand NavigateUpWorkspaceCommand { get; }
    public ICommand SetWorkspaceFolderToCurrentCommand { get; }
    public ICommand UndoCommand { get; }
    public ICommand RedoCommand { get; }
    public ICommand CutCommand { get; }
    public ICommand CopyCommand { get; }
    public ICommand PasteCommand { get; }
    public ICommand FindCommand { get; }
    public ICommand FormatParagraphCommand { get; }
    public ICommand FormatHeader1Command { get; }
    public ICommand FormatHeader2Command { get; }
    public ICommand FormatHeader3Command { get; }
    public ICommand FormatBulletCommand { get; }
    public ICommand FormatChecklistCommand { get; }
    public ICommand FormatQuoteCommand { get; }
    public ICommand FormatCodeCommand { get; }
    public ICommand FormatBoldCommand { get; }
    public ICommand FormatItalicCommand { get; }
    public ICommand ShowGeneralTabCommand { get; }
    public ICommand ShowShortcutsTabCommand { get; }
    public ICommand ShowTranscriptionTabCommand { get; }
    public ICommand ToggleRecordingCommand { get; }
    public ICommand TranscribeAudioCommand { get; }

    public bool IsRecording
    {
        get => _isRecording;
        private set
        {
            if (_isRecording == value) return;
            _isRecording = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(RecordButtonLabel));
        }
    }

    public string RecordButtonLabel => IsRecording ? "Stop Recording..." : "Record";

    public string FilePath
    {
        get => _pendingDocumentFilePath ?? _document.FilePath;
        private set
        {
            if (_document.FilePath == value)
            {
                return;
            }

            _document.FilePath = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(FileName));
            OnPropertyChanged(nameof(HasFilePath));
            OnPropertyChanged(nameof(HeaderPathDisplay));
        }
    }

    public string FileName
    {
        get => _pendingDocumentFileName ?? _document.FileName;
        private set
        {
            if (_document.FileName == value)
            {
                return;
            }

            _document.FileName = value;
            OnPropertyChanged();
        }
    }

    public bool HasFilePath => !string.IsNullOrWhiteSpace(FilePath);
    public string HeaderPathDisplay => IsUntitled ? "Unsaved document" : FilePath;
    public string CurrentViewLabel => SelectedViewMode switch
    {
        EditorViewMode.Viewer => "Reader",
        EditorViewMode.TextEditor => "Text Editor",
        _ => "Visual Editor"
    };

    public string EditorText
    {
        get => _editorText;
        set
        {
            if (_editorText == value)
            {
                return;
            }

            _editorText = value;
            OnPropertyChanged();

            if (_isLoadingDocument)
            {
                return;
            }

            _document.Content = value;
            _document.IsDirty = !string.Equals(_document.Content, _document.OriginalContent, StringComparison.Ordinal);
            OnPropertyChanged(nameof(IsDirty));
            OnPropertyChanged(nameof(StatusText));
            ScheduleParse();
            ScheduleAutoSave();
        }
    }

    public EditorViewMode SelectedViewMode
    {
        get => _selectedViewMode;
        private set
        {
            if (_selectedViewMode == value)
            {
                return;
            }

            _selectedViewMode = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsReaderMode));
            OnPropertyChanged(nameof(IsTextEditorMode));
            OnPropertyChanged(nameof(IsVisualEditorMode));
            OnPropertyChanged(nameof(IsEditorMode));
            OnPropertyChanged(nameof(IsVisualEditorSupported));
            OnPropertyChanged(nameof(VisualEditorUnavailableMessage));
            OnPropertyChanged(nameof(CurrentViewLabel));
            OnPropertyChanged(nameof(StatusText));

            if (_isInitialized && !string.IsNullOrWhiteSpace(_document.Content))
            {
                if (value == EditorViewMode.Viewer && TryApplyDeferredPreview())
                {
                    return;
                }

                ScheduleParse();
            }
        }
    }

    public bool IsReaderMode => SelectedViewMode == EditorViewMode.Viewer;
    public bool IsTextEditorMode => SelectedViewMode == EditorViewMode.TextEditor;
    public bool IsVisualEditorMode => SelectedViewMode == EditorViewMode.RichTextEditor;
    public bool IsEditorMode => SelectedViewMode != EditorViewMode.Viewer;
    public bool IsVisualEditorSupported => _editorModeSupportController.IsVisualEditorSupported;
    public string VisualEditorUnavailableMessage => _editorModeSupportController.VisualEditorUnavailableMessage;

    public bool IsBusy => _isOpeningDocument || _isSavingDocument;
    public bool IsDirty => _document.IsDirty;
    public bool IsUntitled => _pendingDocumentFilePath is null && _document.IsUntitled;
    public string StatusText => BuildStatusText();

    public string ViewerLoadingPreviewText
    {
        get => _viewerLoadingPreviewText;
        private set
        {
            if (_viewerLoadingPreviewText == value)
            {
                return;
            }

            _viewerLoadingPreviewText = value;
            OnPropertyChanged();
        }
    }

    public bool IsWorkspacePanelVisible
    {
        get => _isWorkspacePanelVisible;
        private set
        {
            if (_isWorkspacePanelVisible == value)
            {
                return;
            }

            _isWorkspacePanelVisible = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(WorkspaceToggleLabel));
        }
    }

    public double WorkspacePanelWidth
    {
        get => _workspacePanelWidth;
        private set
        {
            var clampedWidth = ClampWorkspacePanelWidth(value);
            if (Math.Abs(_workspacePanelWidth - clampedWidth) < 0.5)
            {
                return;
            }

            _workspacePanelWidth = clampedWidth;
            OnPropertyChanged();
        }
    }

    public string WorkspaceToggleLabel => IsWorkspacePanelVisible ? "Hide Explorer" : "Show Explorer";

    public string WorkspaceRootPath => Workspace.WorkspaceRootPath;
    public bool HasWorkspaceRoot => Workspace.HasWorkspaceRoot;
    public string CurrentWorkspaceFolderPath => Workspace.CurrentWorkspaceFolderPath;
    public string CurrentWorkspaceFolderName => Workspace.CurrentWorkspaceFolderName;
    public string CurrentWorkspaceFolderDisplay => Workspace.CurrentWorkspaceFolderDisplay;
    public bool CanNavigateUpWorkspace => Workspace.CanNavigateUpWorkspace;
    public bool CanSetCurrentFolderAsWorkspace => Workspace.CanSetCurrentFolderAsWorkspace;

    public string WorkspaceSearchText
    {
        get => Workspace.WorkspaceSearchText;
        set => Workspace.WorkspaceSearchText = value;
    }

    public WorkspaceTreeItem? PendingRenameItem => Workspace.PendingRenameItem;

    public string InlineErrorMessage
    {
        get => _inlineErrorMessage;
        private set
        {
            if (_inlineErrorMessage == value)
            {
                return;
            }

            _inlineErrorMessage = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(HasInlineError));
        }
    }

    public bool HasInlineError => !string.IsNullOrWhiteSpace(InlineErrorMessage);

    public bool IsPreferencesVisible
    {
        get => _isPreferencesVisible;
        private set
        {
            if (_isPreferencesVisible == value)
            {
                return;
            }

            _isPreferencesVisible = value;
            OnPropertyChanged();
        }
    }

    public bool PreferencesAutoSaveEnabled
    {
        get => _preferencesAutoSaveEnabled;
        set
        {
            if (_preferencesAutoSaveEnabled == value)
            {
                return;
            }

            _preferencesAutoSaveEnabled = value;
            OnPropertyChanged();
        }
    }

    public string PreferencesAutoSaveDelaySecondsText
    {
        get => _preferencesAutoSaveDelaySecondsText;
        set
        {
            if (_preferencesAutoSaveDelaySecondsText == value)
            {
                return;
            }

            _preferencesAutoSaveDelaySecondsText = value;
            OnPropertyChanged();
        }
    }

    public string PreferencesMaxLogFileSizeMbText
    {
        get => _preferencesMaxLogFileSizeMbText;
        set
        {
            if (_preferencesMaxLogFileSizeMbText == value)
            {
                return;
            }

            _preferencesMaxLogFileSizeMbText = value;
            OnPropertyChanged();
        }
    }

    public string PreferencesInitialViewerRenderLineCountText
    {
        get => _preferencesInitialViewerRenderLineCountText;
        set
        {
            if (_preferencesInitialViewerRenderLineCountText == value)
            {
                return;
            }

            _preferencesInitialViewerRenderLineCountText = value;
            OnPropertyChanged();
        }
    }

    public int InitialViewerRenderLineCount => Math.Max(5, _preferences.InitialViewerRenderLineCount);
    public IReadOnlyList<string> AvailableFileLogLevels { get; } =
        ["Info", "Warning", "Error", "Debug", "Trace"];

    public bool PreferencesUse24HourTime
    {
        get => _preferencesUse24HourTime;
        set
        {
            if (_preferencesUse24HourTime == value)
            {
                return;
            }

            _preferencesUse24HourTime = value;
            OnPropertyChanged();
        }
    }

    public string PreferredTimeFormat => _preferences.Use24HourTime ? "HH:mm:ss" : "h:mm:ss tt";

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

    public bool IsWhisperCppSelected => _preferencesTranscriptionEngine == TranscriptionEngineType.WhisperCpp;
    public bool IsPyannoteSelected => _preferencesDiarizationEngine == DiarizationEngineType.Pyannote;

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

    public IReadOnlyList<KeyboardShortcutDefinition> CurrentShortcuts => _preferences.KeyboardShortcuts;

    public string PreferencesFileLogLevelText
    {
        get => _preferencesFileLogLevelText;
        set
        {
            if (_preferencesFileLogLevelText == value)
            {
                return;
            }

            _preferencesFileLogLevelText = value;
            OnPropertyChanged();
        }
    }

    public bool IsViewerLoading
    {
        get => _isViewerLoading;
        private set
        {
            if (_isViewerLoading == value)
            {
                return;
            }

            _isViewerLoading = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(StatusText));
        }
    }

    public async Task InitializeAsync()
    {
        if (_isInitialized)
        {
            return;
        }

        _isInitialized = true;
        await RestoreSessionAsync();
    }

    public async Task CommitWorkspaceRenameAsync(WorkspaceTreeItem? item)
    {
        if (item is null || !item.CanRename || !item.IsRenaming)
        {
            return;
        }

        try
        {
            await MainThread.InvokeOnMainThreadAsync(() =>
            {
                item.IsRenaming = false;
                Workspace.MarkRenameCommitted();
            });

            var updatedPath = await _workspaceBrowserService.RenameMarkdownFileAsync(item.FullPath, item.RenameText);

            if (string.Equals(FilePath, item.FullPath, StringComparison.Ordinal))
            {
                var renamedDocument = await _documentService.LoadDocumentAsync(updatedPath);
                await LoadDocumentIntoStateAsync(renamedDocument);
            }

            await Workspace.LoadWorkspaceAsync(
                WorkspaceRootPath,
                currentFolderPath: Path.GetDirectoryName(updatedPath) ?? WorkspaceRootPath,
                selectedPath: updatedPath);
            PersistSessionState();
            await ClearInlineErrorAsync();
        }
        catch (Exception ex)
        {
            await ReportErrorAsync("Workspace file rename failed.", ex, ex is InvalidOperationException ? ex.Message : "The file could not be renamed.");
            await MainThread.InvokeOnMainThreadAsync(() =>
            {
                item.IsRenaming = true;
                Workspace.BeginRename(item);
            });
        }
    }

    public Task CancelWorkspaceRenameAsync(WorkspaceTreeItem? item)
    {
        if (item is null)
        {
            return Task.CompletedTask;
        }

        return MainThread.InvokeOnMainThreadAsync(() =>
        {
            Workspace.CancelRename(item);
        });
    }

    private void OnWorkspacePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(WorkspaceExplorerState.WorkspaceRootPath)
            or nameof(WorkspaceExplorerState.HasWorkspaceRoot)
            or nameof(WorkspaceExplorerState.CurrentWorkspaceFolderPath)
            or nameof(WorkspaceExplorerState.CurrentWorkspaceFolderName)
            or nameof(WorkspaceExplorerState.CurrentWorkspaceFolderDisplay)
            or nameof(WorkspaceExplorerState.CanNavigateUpWorkspace)
            or nameof(WorkspaceExplorerState.CanSetCurrentFolderAsWorkspace)
            or nameof(WorkspaceExplorerState.PendingRenameItem)
            or nameof(WorkspaceExplorerState.WorkspaceSearchText))
        {
            OnPropertyChanged(e.PropertyName switch
            {
                nameof(WorkspaceExplorerState.WorkspaceRootPath) => nameof(WorkspaceRootPath),
                nameof(WorkspaceExplorerState.HasWorkspaceRoot) => nameof(HasWorkspaceRoot),
                nameof(WorkspaceExplorerState.CurrentWorkspaceFolderPath) => nameof(CurrentWorkspaceFolderPath),
                nameof(WorkspaceExplorerState.CurrentWorkspaceFolderName) => nameof(CurrentWorkspaceFolderName),
                nameof(WorkspaceExplorerState.CurrentWorkspaceFolderDisplay) => nameof(CurrentWorkspaceFolderDisplay),
                nameof(WorkspaceExplorerState.CanNavigateUpWorkspace) => nameof(CanNavigateUpWorkspace),
                nameof(WorkspaceExplorerState.CanSetCurrentFolderAsWorkspace) => nameof(CanSetCurrentFolderAsWorkspace),
                nameof(WorkspaceExplorerState.PendingRenameItem) => nameof(PendingRenameItem),
                nameof(WorkspaceExplorerState.WorkspaceSearchText) => nameof(WorkspaceSearchText),
                _ => null
            });
        }
    }

    protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

        if (propertyName is nameof(HasWorkspaceRoot) or nameof(IsBusy) or nameof(IsDirty) or nameof(IsUntitled) or nameof(CanNavigateUpWorkspace) or nameof(CanSetCurrentFolderAsWorkspace))
        {
            RefreshCommandStates();
        }
    }

    private async Task OpenFileAsync()
    {
        if (IsBusy)
        {
            return;
        }

        _isOpeningDocument = true;
        OnPropertyChanged(nameof(IsBusy));

        try
        {
            var filePath = await _documentService.PickDocumentPathAsync();
            await OpenDocumentAsync(
                () => Task.FromResult(filePath),
                "Failed to open the selected markdown document.",
                "The selected file could not be opened.");
        }
        catch (Exception ex)
        {
            await ReportErrorAsync("Failed to open the selected markdown document.", ex, ex is InvalidOperationException ? ex.Message : "The selected file could not be opened.");
        }
        finally
        {
            _isOpeningDocument = false;
            OnPropertyChanged(nameof(IsBusy));
        }
    }

    private async Task NewDocumentAsync()
    {
        try
        {
            await SaveIfNeededAsync();
            var untitled = await _documentService.CreateUntitledDocumentAsync();
            await LoadDocumentIntoStateAsync(untitled);
        }
        catch (Exception ex)
        {
            await ReportErrorAsync("Failed to create a new document.", ex, "A new document could not be created.");
        }
    }

    private async Task OpenDocumentAsync(
        Func<Task<string?>> filePathProvider,
        string logMessage,
        string inlineMessage)
    {
        await SaveIfNeededAsync();

        var filePath = await filePathProvider();
        if (string.IsNullOrWhiteSpace(filePath))
        {
            await ClearInlineErrorAsync();
            return;
        }

        try
        {
            ShowPendingDocumentShell(filePath);
            var document = await _documentService.LoadDocumentAsync(filePath);
            await LoadDocumentIntoStateAsync(document);
            await ClearInlineErrorAsync();
        }
        catch (Exception ex)
        {
            ClearPendingDocumentShell();
            IsViewerLoading = false;
            await ReportErrorAsync(logMessage, ex, ex is InvalidOperationException ? ex.Message : inlineMessage);
        }
    }

    private async Task SaveDocumentAsync()
    {
        await SaveCurrentDocumentInternalAsync(forceSaveAs: false);
    }

    private async Task SaveDocumentAsAsync()
    {
        await SaveCurrentDocumentInternalAsync(forceSaveAs: true);
    }

    private async Task RevertDocumentAsync()
    {
        try
        {
            if (_document.IsUntitled || string.IsNullOrWhiteSpace(_document.FilePath))
            {
                await LoadDocumentIntoStateAsync(await _documentService.CreateUntitledDocumentAsync());
                return;
            }

            var reverted = await _documentService.LoadDocumentAsync(_document.FilePath);
            await LoadDocumentIntoStateAsync(reverted);
            await ClearInlineErrorAsync();
        }
        catch (Exception ex)
        {
            await ReportErrorAsync("Failed to revert the current document.", ex, "The document could not be reverted.");
        }
    }

    private async Task CloseDocumentAsync()
    {
        try
        {
            await SaveIfNeededAsync();
            _documentWatchService.Stop();
            await LoadDocumentIntoStateAsync(await _documentService.CreateUntitledDocumentAsync());
        }
        catch (Exception ex)
        {
            await ReportErrorAsync("Failed to close the current document.", ex, "The document could not be closed.");
        }
    }

    private void ShowPreferences()
    {
        PreferencesAutoSaveEnabled = _preferences.AutoSaveEnabled;
        PreferencesUse24HourTime = _preferences.Use24HourTime;
        PreferencesAutoSaveDelaySecondsText = _preferences.AutoSaveDelaySeconds.ToString();
        PreferencesMaxLogFileSizeMbText = _preferences.MaxLogFileSizeMb.ToString();
        PreferencesInitialViewerRenderLineCountText = _preferences.InitialViewerRenderLineCount.ToString();
        PreferencesFileLogLevelText = FormatLogLevel(_preferences.FileLogLevel);
        IsShortcutsTabActive = false;
        IsTranscriptionTabActive = false;
        LoadShortcutKeyFields();
        LoadTranscriptionFields();
        IsPreferencesVisible = true;
    }

    private void LoadShortcutKeyFields()
    {
        ShortcutKeyHeader1 = GetShortcutKey(EditorActionType.Header1);
        ShortcutKeyHeader2 = GetShortcutKey(EditorActionType.Header2);
        ShortcutKeyHeader3 = GetShortcutKey(EditorActionType.Header3);
        ShortcutKeyBold = GetShortcutKey(EditorActionType.Bold);
        ShortcutKeyItalic = GetShortcutKey(EditorActionType.Italic);
    }

    private void LoadTranscriptionFields()
    {
        _preferencesTranscriptionEngine = _preferences.TranscriptionEngine;
        _preferencesDiarizationEngine = _preferences.DiarizationEngine;
        _preferencesWhisperBinaryPath = _preferences.WhisperBinaryPath;
        _preferencesWhisperModelPath = _preferences.WhisperModelPath;
        _preferencesPyannotePythonPath = _preferences.PyannotePythonPath;
        OnPropertyChanged(nameof(PreferencesTranscriptionEngine));
        OnPropertyChanged(nameof(PreferencesDiarizationEngine));
        OnPropertyChanged(nameof(PreferencesWhisperBinaryPath));
        OnPropertyChanged(nameof(PreferencesWhisperModelPath));
        OnPropertyChanged(nameof(PreferencesPyannotePythonPath));
        OnPropertyChanged(nameof(IsWhisperCppSelected));
        OnPropertyChanged(nameof(IsPyannoteSelected));
    }

    private string GetShortcutKey(EditorActionType action)
    {
        return _preferences.KeyboardShortcuts
            .FirstOrDefault(s => s.Action == action)?.Key.ToUpperInvariant() ?? string.Empty;
    }

    private async Task SavePreferencesAsync()
    {
        if (!int.TryParse(PreferencesAutoSaveDelaySecondsText, out var delaySeconds) || delaySeconds < 5)
        {
            await ReportErrorAsync("Invalid autosave preference.", null, "Autosave delay must be at least 5 seconds.");
            return;
        }

        if (!int.TryParse(PreferencesMaxLogFileSizeMbText, out var maxLogFileSizeMb) || maxLogFileSizeMb < 1)
        {
            await ReportErrorAsync("Invalid log size preference.", null, "Max log size must be at least 1 MB.");
            return;
        }

        if (!int.TryParse(PreferencesInitialViewerRenderLineCountText, out var initialViewerRenderLineCount) || initialViewerRenderLineCount < 5)
        {
            await ReportErrorAsync("Invalid viewer render preference.", null, "Initial viewer render lines must be at least 5.");
            return;
        }

        if (!TryParseFileLogLevel(PreferencesFileLogLevelText, out var fileLogLevel))
        {
            await ReportErrorAsync("Invalid log level preference.", null, "File log level must be Trace, Debug, Information, Warning, or Error.");
            return;
        }

        _preferences = new EditorPreferences
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
            PyannotePythonPath = _preferencesPyannotePythonPath
        };

        _preferencesService.Save(_preferences);
        KeyboardShortcutsChanged?.Invoke(this, EventArgs.Empty);
        _fileLogLevelSwitch.MinimumLevel = fileLogLevel;
        IsPreferencesVisible = false;
        OnPropertyChanged(nameof(InitialViewerRenderLineCount));
        OnPropertyChanged(nameof(PreferredTimeFormat));
        OnPropertyChanged(nameof(StatusText));
        ScheduleAutoSave();
        PersistSessionState();
        await ClearInlineErrorAsync();
    }

    private void CancelPreferences()
    {
        IsPreferencesVisible = false;
    }

    private List<KeyboardShortcutDefinition> BuildShortcutsFromFields()
    {
        return
        [
            new KeyboardShortcutDefinition { Action = EditorActionType.Header1, Key = NormalizeShortcutKey(ShortcutKeyHeader1, "1") },
            new KeyboardShortcutDefinition { Action = EditorActionType.Header2, Key = NormalizeShortcutKey(ShortcutKeyHeader2, "2") },
            new KeyboardShortcutDefinition { Action = EditorActionType.Header3, Key = NormalizeShortcutKey(ShortcutKeyHeader3, "3") },
            new KeyboardShortcutDefinition { Action = EditorActionType.Bold,    Key = NormalizeShortcutKey(ShortcutKeyBold, "B") },
            new KeyboardShortcutDefinition { Action = EditorActionType.Italic,  Key = NormalizeShortcutKey(ShortcutKeyItalic, "I") },
        ];
    }

    private static string NormalizeShortcutKey(string raw, string fallback)
    {
        var trimmed = raw.Trim().ToUpperInvariant();
        return trimmed.Length > 0 && (char.IsLetterOrDigit(trimmed[0])) ? trimmed[0].ToString() : fallback;
    }

    private static string FormatLogLevel(LogLevel logLevel)
    {
        return logLevel == LogLevel.Information ? "Info" : logLevel.ToString();
    }

    private static bool TryParseFileLogLevel(string text, out LogLevel logLevel)
    {
        if (string.Equals(text, "Info", StringComparison.OrdinalIgnoreCase))
        {
            logLevel = LogLevel.Information;
            return true;
        }

        return Enum.TryParse(text, ignoreCase: true, out logLevel) && logLevel != LogLevel.None;
    }

    private void SetViewMode(EditorViewMode mode)
    {
        SelectedViewMode = _editorModeSupportController.ResolveSupportedViewMode(mode, showUnsupportedSnackbar: true);
        PersistSessionState();
    }

    private async Task SaveIfNeededAsync()
    {
        if (!_document.IsDirty)
        {
            return;
        }

        await SaveCurrentDocumentInternalAsync(forceSaveAs: false);
    }

    private async Task SaveCurrentDocumentInternalAsync(bool forceSaveAs)
    {
        if (IsBusy)
        {
            return;
        }

        _isSavingDocument = true;
        OnPropertyChanged(nameof(IsBusy));

        try
        {
            var result = forceSaveAs
                ? await _documentService.SaveAsAsync(_document)
                : await _documentService.SaveAsync(_document);

            if (result is null)
            {
                return;
            }

            ApplySaveResult(result);
            await ClearInlineErrorAsync();
        }
        catch (Exception ex)
        {
            await ReportErrorAsync("Failed to save the current document.", ex, BuildSaveFailureMessage(ex));
        }
        finally
        {
            _isSavingDocument = false;
            OnPropertyChanged(nameof(IsBusy));
        }
    }

    private void ApplySaveResult(SaveDocumentResult result)
    {
        var previousFilePath = _document.FilePath;
        var previousFileName = _document.FileName;
        var previousIsUntitled = _document.IsUntitled;
        var previousIsDirty = _document.IsDirty;

        _documentWorkflowController.ApplySaveResult(_document, result);
        _previewPipelineController.MarkSaved();

        if (!string.Equals(previousFilePath, _document.FilePath, StringComparison.Ordinal))
        {
            OnPropertyChanged(nameof(FilePath));
            OnPropertyChanged(nameof(HeaderPathDisplay));
            OnPropertyChanged(nameof(HasFilePath));
        }

        if (!string.Equals(previousFileName, _document.FileName, StringComparison.Ordinal))
        {
            OnPropertyChanged(nameof(FileName));
        }

        if (previousIsUntitled != _document.IsUntitled)
        {
            OnPropertyChanged(nameof(IsUntitled));
        }

        if (previousIsDirty != _document.IsDirty)
        {
            OnPropertyChanged(nameof(IsDirty));
        }

        OnPropertyChanged(nameof(StatusText));

        _documentWatchService.Watch(result.FilePath);
        PersistSessionState();
    }

    private async Task LoadDocumentIntoStateAsync(MarkdownDocument document, bool persistSessionState = true)
    {
        var overallStopwatch = Stopwatch.StartNew();
        _isLoadingDocument = true;

        try
        {
            var currentViewMode = SelectedViewMode;
            _previewPipelineController.CancelPreview();
            ClearDeferredPreview();
            var applyResult = _documentApplyController.PrepareApply(_document, document);
            _document = applyResult.DocumentState;

            var uiStateStopwatch = Stopwatch.StartNew();
            await MainThread.InvokeOnMainThreadAsync(() =>
            {
                ClearPendingDocumentShell(raiseNotifications: false);

                if (applyResult.FilePathChanged)
                {
                    OnPropertyChanged(nameof(FilePath));
                    OnPropertyChanged(nameof(HeaderPathDisplay));
                    OnPropertyChanged(nameof(HasFilePath));
                }

                if (applyResult.FileNameChanged)
                {
                    OnPropertyChanged(nameof(FileName));
                }

                EditorText = _document.Content;
                InlineErrorMessage = string.Empty;
                ViewerLoadingPreviewText = BuildViewerLoadingPreview(document.Content);
                if (applyResult.IsDirtyChanged)
                {
                    OnPropertyChanged(nameof(IsDirty));
                }

                if (applyResult.IsUntitledChanged)
                {
                    OnPropertyChanged(nameof(IsUntitled));
                }

                IsViewerLoading = IsReaderMode;
                OnPropertyChanged(nameof(StatusText));
            });
            uiStateStopwatch.Stop();

            var watchStopwatch = Stopwatch.StartNew();
            if (!applyResult.ShouldWatchDocument || string.IsNullOrWhiteSpace(applyResult.WatchFilePath))
            {
                _documentWatchService.Stop();
            }
            else
            {
                _documentWatchService.Watch(applyResult.WatchFilePath);
            }
            watchStopwatch.Stop();

            var sessionStopwatch = Stopwatch.StartNew();
            if (persistSessionState)
            {
                _ = PersistSessionStateAsync();
            }
            sessionStopwatch.Stop();

            StartPreviewPreparation(document, currentViewMode);

            _logger.LogInformation(
                "Document load/apply pipeline completed. FilePath: {FilePath}, ContentLength: {ContentLength}, UiStateMs: {UiStateMs}, WatchMs: {WatchMs}, SessionPersistMs: {SessionPersistMs}, TotalElapsedMs: {TotalElapsedMs}, ViewMode: {ViewMode}",
                _document.FilePath,
                _document.Content.Length,
                uiStateStopwatch.ElapsedMilliseconds,
                watchStopwatch.ElapsedMilliseconds,
                sessionStopwatch.ElapsedMilliseconds,
                overallStopwatch.ElapsedMilliseconds,
                SelectedViewMode);
        }
        finally
        {
            _isLoadingDocument = false;
        }
    }

    private async Task ApplyPreparedPreviewAsync(MarkdownDocument snapshot, DocumentPreviewResult preparedPreview, TimeSpan parseElapsed)
    {
        var pipelineStopwatch = Stopwatch.StartNew();
        var applyStopwatch = Stopwatch.StartNew();
        var previewChanged = false;
        var previewDeferred = false;
        await MainThread.InvokeOnMainThreadAsync(() =>
        {
            if (SelectedViewMode != preparedPreview.ViewMode)
            {
                SelectedViewMode = preparedPreview.ViewMode;
            }

            if (preparedPreview.ViewMode == EditorViewMode.TextEditor)
            {
                CacheDeferredPreview(snapshot, preparedPreview);
                previewDeferred = true;
            }
            else
            {
                ClearDeferredPreview();
                if (!AreEquivalentBlocks(_parsedBlocks, preparedPreview.Blocks))
                {
                    ParsedBlocks = preparedPreview.Blocks;
                    previewChanged = true;
                }
            }

            var inlineErrorMessage = preparedPreview.InlineErrorMessage ?? string.Empty;
            if (!string.Equals(InlineErrorMessage, inlineErrorMessage, StringComparison.Ordinal))
            {
                InlineErrorMessage = inlineErrorMessage;
            }

            IsViewerLoading = false;
            if (!previewDeferred)
            {
                DocumentApplied?.Invoke(this, snapshot);
            }
        });
        applyStopwatch.Stop();

        _logger.LogDebug(
            "Preview parse/apply completed. FilePath: {FilePath}, BlockCount: {BlockCount}, PreviewChanged: {PreviewChanged}, PreviewDeferred: {PreviewDeferred}, ParseElapsedMs: {ParseElapsedMs}, UiApplyElapsedMs: {UiApplyElapsedMs}, TotalElapsedMs: {TotalElapsedMs}, ViewMode: {ViewMode}",
            _document.FilePath,
            preparedPreview.Blocks.Count,
            previewChanged,
            previewDeferred,
            parseElapsed.TotalMilliseconds,
            applyStopwatch.ElapsedMilliseconds,
            pipelineStopwatch.ElapsedMilliseconds,
            SelectedViewMode);
    }

    private void ScheduleParse()
    {
        var snapshot = CreateCurrentDocumentSnapshot(_document.Content);
        IsViewerLoading = IsReaderMode;
        ViewerLoadingPreviewText = BuildViewerLoadingPreview(snapshot.Content);

        _ = _previewPipelineController.SchedulePreviewAsync(
            snapshot,
            SelectedViewMode,
            IsReaderMode ? ViewerParseDebounceDelay : EditorParseDebounceDelay,
            ApplyPreparedPreviewAsync);
    }

    private void CacheDeferredPreview(MarkdownDocument snapshot, DocumentPreviewResult preparedPreview)
    {
        _deferredPreviewBlocks = preparedPreview.Blocks;
        _deferredPreviewInlineErrorMessage = preparedPreview.InlineErrorMessage ?? string.Empty;
        _deferredPreviewContent = snapshot.Content;
        _deferredPreviewFilePath = snapshot.FilePath ?? string.Empty;
        _deferredPreviewViewMode = preparedPreview.ViewMode;
    }

    private bool TryApplyDeferredPreview()
    {
        if (_deferredPreviewViewMode is null
            || _deferredPreviewViewMode == EditorViewMode.Viewer
            || !string.Equals(_deferredPreviewContent, _document.Content, StringComparison.Ordinal)
            || !string.Equals(_deferredPreviewFilePath, _document.FilePath ?? string.Empty, StringComparison.Ordinal))
        {
            return false;
        }

        var previewChanged = false;
        if (!AreEquivalentBlocks(_parsedBlocks, _deferredPreviewBlocks))
        {
            ParsedBlocks = _deferredPreviewBlocks;
            previewChanged = true;
        }

        if (!string.Equals(InlineErrorMessage, _deferredPreviewInlineErrorMessage, StringComparison.Ordinal))
        {
            InlineErrorMessage = _deferredPreviewInlineErrorMessage;
        }

        IsViewerLoading = false;
        DocumentApplied?.Invoke(this, CreateCurrentDocumentSnapshot(_document.Content));
        ClearDeferredPreview();

        _logger.LogDebug(
            "Applied deferred preview for current document. FilePath: {FilePath}, BlockCount: {BlockCount}, PreviewChanged: {PreviewChanged}",
            _document.FilePath,
            _parsedBlocks.Count,
            previewChanged);
        return true;
    }

    private void ClearDeferredPreview()
    {
        _deferredPreviewBlocks = Array.Empty<MarkdownBlock>();
        _deferredPreviewInlineErrorMessage = string.Empty;
        _deferredPreviewContent = string.Empty;
        _deferredPreviewFilePath = string.Empty;
        _deferredPreviewViewMode = null;
    }

    private void ScheduleAutoSave()
    {
        _autosaveCoordinator.Schedule(
            _preferences.AutoSaveEnabled,
            _document.IsUntitled,
            _document.IsDirty,
            _document.FilePath,
            TimeSpan.FromSeconds(_preferences.AutoSaveDelaySeconds),
            () => SaveCurrentDocumentInternalAsync(forceSaveAs: false));
    }

    private async void OnWatchedDocumentChanged(object? sender, string filePath)
    {
        if (_document.IsUntitled || string.IsNullOrWhiteSpace(_document.FilePath) || !string.Equals(filePath, _document.FilePath, StringComparison.Ordinal))
        {
            return;
        }

        if (_previewPipelineController.ShouldSuppressExternalReload(_isSavingDocument, TimeSpan.FromSeconds(1)))
        {
            return;
        }

        try
        {
            await _previewPipelineController.ScheduleExternalReloadAsync(ExternalChangeDebounceDelay, async () =>
            {
                if (_document.IsDirty)
                {
                    await MainThread.InvokeOnMainThreadAsync(() =>
                    {
                        InlineErrorMessage = "The file changed on disk while you have unsaved edits. Save or revert to reconcile it.";
                    });
                    return;
                }

                var updatedDocument = await _documentService.LoadDocumentAsync(_document.FilePath);
                await LoadDocumentIntoStateAsync(updatedDocument);
                await MainThread.InvokeOnMainThreadAsync(() =>
                {
                    InlineErrorMessage = "The file changed on disk and was automatically reloaded.";
                });
            });
        }
        catch (Exception ex)
        {
            await ReportErrorAsync("External file change handling failed.", ex, "The file changed on disk but could not be reloaded.");
        }
    }

    private async Task ClearInlineErrorAsync()
    {
        await MainThread.InvokeOnMainThreadAsync(() =>
        {
            InlineErrorMessage = string.Empty;
        });
    }

    private async Task ReportErrorAsync(string message, Exception? exception, string inlineMessage)
    {
        if (exception is null)
        {
            _logger.LogWarning("{Message}", message);
        }
        else
        {
            _logger.LogError(exception, "{Message}", message);
        }

        await MainThread.InvokeOnMainThreadAsync(() =>
        {
            InlineErrorMessage = inlineMessage;
        });
    }

    private string BuildStatusText()
    {
        var parts = new List<string>();

        parts.Add(IsUntitled ? "Untitled" : "Saved file");

        if (_pendingDocumentFilePath is not null)
        {
            parts.Add("Loading document");
        }

        if (_document.IsDirty)
        {
            parts.Add("Unsaved changes");
        }

        if (IsViewerLoading)
        {
            parts.Add("Loading preview");
        }

        parts.Add(_preferences.AutoSaveEnabled ? $"Autosave {_preferences.AutoSaveDelaySeconds}s" : "Autosave off");
        parts.Add(SelectedViewMode switch
        {
            EditorViewMode.Viewer => "Reader",
            EditorViewMode.TextEditor => "Text Editor",
            _ => "Visual Editor"
        });

        return string.Join(" • ", parts);
    }

    private MarkdownDocument CreateCurrentDocumentSnapshot(string content)
    {
        return new MarkdownDocument
        {
            FilePath = _document.FilePath,
            FileName = _document.FileName,
            FileSizeBytes = _document.FileSizeBytes,
            LastModified = _document.LastModified,
            Content = content,
            IsUntitled = _document.IsUntitled,
            EncodingName = _document.EncodingName,
            NewLine = _document.NewLine
        };
    }

    private void ShowPendingDocumentShell(string filePath)
    {
        _pendingDocumentFilePath = filePath;
        _pendingDocumentFileName = Path.GetFileName(filePath);

        MainThread.BeginInvokeOnMainThread(() =>
        {
            IsViewerLoading = IsReaderMode;
            OnPropertyChanged(nameof(FilePath));
            OnPropertyChanged(nameof(FileName));
            OnPropertyChanged(nameof(HasFilePath));
            OnPropertyChanged(nameof(HeaderPathDisplay));
            OnPropertyChanged(nameof(IsUntitled));
            OnPropertyChanged(nameof(StatusText));
        });
    }

    private void ClearPendingDocumentShell(bool raiseNotifications = true)
    {
        if (_pendingDocumentFilePath is null && _pendingDocumentFileName is null)
        {
            return;
        }

        _pendingDocumentFilePath = null;
        _pendingDocumentFileName = null;

        if (!raiseNotifications)
        {
            return;
        }

        OnPropertyChanged(nameof(FilePath));
        OnPropertyChanged(nameof(FileName));
        OnPropertyChanged(nameof(HasFilePath));
        OnPropertyChanged(nameof(HeaderPathDisplay));
        OnPropertyChanged(nameof(IsUntitled));
        OnPropertyChanged(nameof(StatusText));
    }

    private void StartPreviewPreparation(MarkdownDocument document, EditorViewMode currentViewMode)
    {
        var snapshot = new MarkdownDocument
        {
            FilePath = document.FilePath,
            FileName = document.FileName,
            FileSizeBytes = document.FileSizeBytes,
            LastModified = document.LastModified,
            Content = document.Content,
            IsUntitled = document.IsUntitled,
            EncodingName = document.EncodingName,
            NewLine = document.NewLine
        };

        _ = _previewPipelineController.SchedulePreviewAsync(
            snapshot,
            currentViewMode,
            TimeSpan.Zero,
            async (preparedSnapshot, preparedPreview, parseElapsed) =>
            {
                var pipelineStopwatch = Stopwatch.StartNew();
                await ApplyPreparedPreviewAsync(preparedSnapshot, preparedPreview, parseElapsed);
                _logger.LogDebug(
                    "Initial preview preparation completed. FilePath: {FilePath}, BlockCount: {BlockCount}, ParseElapsedMs: {ParseElapsedMs}, TotalElapsedMs: {TotalElapsedMs}",
                    preparedSnapshot.FilePath,
                    preparedPreview.Blocks.Count,
                    parseElapsed.TotalMilliseconds,
                    pipelineStopwatch.ElapsedMilliseconds);
            });
    }

    private Task PersistSessionStateAsync()
    {
        return Task.Run(() =>
        {
            try
            {
                PersistSessionState();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Persisting session state asynchronously failed.");
            }
        });
    }

    private static string BuildViewerLoadingPreview(string content)
    {
        if (string.IsNullOrWhiteSpace(content))
        {
            return "Preparing markdown preview...";
        }

        const int maxLines = 28;
        const int maxCharacters = 2200;

        var normalized = content.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');
        if (normalized.Length > maxCharacters)
        {
            normalized = normalized[..maxCharacters];
        }

        var lines = normalized.Split('\n');
        if (lines.Length > maxLines)
        {
            normalized = string.Join(Environment.NewLine, lines.Take(maxLines));
        }
        else
        {
            normalized = normalized.Replace("\n", Environment.NewLine, StringComparison.Ordinal);
        }

        if (content.Length > normalized.Length)
        {
            normalized = normalized.TrimEnd() + Environment.NewLine + Environment.NewLine + "...";
        }

        return normalized;
    }

    private static string BuildSaveFailureMessage(Exception exception)
    {
        return exception switch
        {
            UnauthorizedAccessException => "The file could not be saved because access was denied.",
            IOException => "The file could not be saved because it is locked or unavailable.",
            InvalidOperationException => exception.Message,
            _ => "The file could not be saved."
        };
    }

    private async Task ExportAsPdfAsync()
    {
        if (IsBusy) return;

        _isSavingDocument = true;
        OnPropertyChanged(nameof(IsBusy));

        try
        {
            var blocks = _parsedBlocks;
            if (blocks.Count == 0)
            {
                await ReportErrorAsync("Nothing to export.", null, "The document has no content to export as PDF.");
                return;
            }

            var suggestedName = _document.IsUntitled ? "document" : Path.GetFileNameWithoutExtension(_document.FileName);
            var exported = await _pdfExportService.ExportAsync(blocks, suggestedName);

            if (!exported)
            {
                _logger.LogDebug("PDF export was cancelled.");
            }
        }
        catch (Exception ex)
        {
            await ReportErrorAsync("PDF export failed.", ex, ex is InvalidOperationException ? ex.Message : "The document could not be exported as PDF.");
        }
        finally
        {
            _isSavingDocument = false;
            OnPropertyChanged(nameof(IsBusy));
        }
    }

    public async Task StopRecordingAsync()
    {
        if (!_isRecording) return;
        var result = await _audioCaptureService.StopAsync();
        if (!result.Success)
        {
            _logger.LogWarning("StopRecordingAsync failed: {Error}", result.ErrorMessage);
        }
    }

    private async Task ToggleRecordingAsync()
    {
        if (_isRecordingTransitioning) return;

        if (_isRecording)
        {
            _isRecordingTransitioning = true;
            (ToggleRecordingCommand as Command)?.ChangeCanExecute();
            try
            {
                var result = await _audioCaptureService.StopAsync();
                if (!result.Success)
                {
                    _logger.LogWarning("Recording stop failed: {Error}", result.ErrorMessage);
                    await ReportErrorAsync("Recording stop failed.", null, result.ErrorMessage ?? "The recording could not be stopped.");
                }
                else
                {
                    _logger.LogInformation("Recording saved: {Path}, Duration: {Duration:g}", result.FilePath, result.Duration);
                    if (!string.IsNullOrWhiteSpace(WorkspaceRootPath))
                    {
                        await Workspace.LoadWorkspaceAsync(WorkspaceRootPath, currentFolderPath: CurrentWorkspaceFolderPath);
                    }
                }
            }
            catch (Exception ex)
            {
                await ReportErrorAsync("Recording stop failed.", ex, "The recording could not be stopped.");
            }
            finally
            {
                _isRecordingTransitioning = false;
                (ToggleRecordingCommand as Command)?.ChangeCanExecute();
            }
        }
        else
        {
            _isRecordingTransitioning = true;
            (ToggleRecordingCommand as Command)?.ChangeCanExecute();
            try
            {
                var permission = await _audioCaptureService.CheckMicrophonePermissionAsync();
                if (permission == AudioPermissionStatus.Denied)
                {
                    await ReportErrorAsync("Microphone permission denied.", null, "Microphone access is required for recording. Please grant permission in System Settings.");
                    return;
                }

                if (permission == AudioPermissionStatus.NotDetermined)
                {
                    var granted = await _audioCaptureService.RequestMicrophonePermissionAsync();
                    if (granted == AudioPermissionStatus.Denied)
                    {
                        await ReportErrorAsync("Microphone permission denied.", null, "Microphone access is required for recording.");
                        return;
                    }
                }

                var baseFolder = !string.IsNullOrWhiteSpace(WorkspaceRootPath)
                    ? WorkspaceRootPath
                    : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "MauiMds");

                var outputPath = RecordingPathBuilder.Build(baseFolder, _clock.UtcNow);
                var options = new AudioCaptureOptions { OutputPath = outputPath };

                await _audioCaptureService.StartAsync(options);
                _logger.LogInformation("Recording started: {Path}", outputPath);

                if (_audioCaptureService.LastStartWarning is { } warning)
                {
                    _logger.LogWarning("Recording started with warning: {Warning}", warning);
                    await Application.Current!.Windows[0].Page!.DisplayAlert("Recording started", warning, "OK");
                }
            }
            catch (Exception ex)
            {
                await ReportErrorAsync("Recording could not start.", ex, "The recording could not be started. Check permissions and try again.");
            }
            finally
            {
                _isRecordingTransitioning = false;
                (ToggleRecordingCommand as Command)?.ChangeCanExecute();
            }
        }
    }

    private async Task TranscribeAudioAsync(WorkspaceTreeItem? item)
    {
        if (item is null) return;

        var audioPath = item.FullPath;
        var transcriptPath = Path.Combine(
            Path.GetDirectoryName(audioPath)!,
            Path.GetFileNameWithoutExtension(audioPath) + "_transcript.md");

        InlineErrorMessage = "Transcribing…";

        try
        {
            var pipeline = _transcriptionPipelineFactory.Create(
                _preferences.TranscriptionEngine,
                _preferences.DiarizationEngine,
                _preferences.WhisperBinaryPath,
                _preferences.WhisperModelPath,
                _preferences.PyannotePythonPath);

            var progress = new Progress<double>(v => MainThread.BeginInvokeOnMainThread(() =>
                InlineErrorMessage = $"Transcribing… {v:P0}"));

            var doc = await pipeline.RunAsync(audioPath, progress);

            var markdown = BuildTranscriptMarkdown(doc, audioPath);
            await File.WriteAllTextAsync(transcriptPath, markdown);

            InlineErrorMessage = string.Empty;

            await MainThread.InvokeOnMainThreadAsync(async () =>
            {
                var page = Microsoft.Maui.Controls.Application.Current?.MainPage;
                if (page is not null)
                    await page.DisplayAlert(
                        "Transcription Complete",
                        $"Transcript saved to:\n{Path.GetFileName(transcriptPath)}",
                        "OK");
            });
        }
        catch (Exception ex)
        {
            await ReportErrorAsync("Transcription failed.", ex, ex.Message);
        }
    }

    private static string BuildTranscriptMarkdown(TranscriptDocument doc, string audioPath)
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"# Transcript: {Path.GetFileName(audioPath)}");
        sb.AppendLine($"Generated: {DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss}");
        sb.AppendLine($"Engine: {doc.TranscriptionEngineName} | {doc.DiarizationEngineName}");
        sb.AppendLine($"Duration: {doc.Duration:hh\\:mm\\:ss}");
        sb.AppendLine();
        sb.AppendLine("---");
        sb.AppendLine();

        foreach (var seg in doc.Segments)
        {
            sb.AppendLine($"**[{seg.Start:hh\\:mm\\:ss} – {seg.End:hh\\:mm\\:ss}] {seg.SpeakerLabel}**");
            sb.AppendLine(seg.Text);
            sb.AppendLine();
        }

        return sb.ToString();
    }

    private void OnAudioCaptureStateChanged(object? sender, AudioCaptureState state)
    {
        MainThread.BeginInvokeOnMainThread(() =>
        {
            IsRecording = state == AudioCaptureState.Recording;
        });
    }

    private void RefreshCommandStates()
    {
        (OpenFileCommand as Command)?.ChangeCanExecute();
        (NewDocumentCommand as Command)?.ChangeCanExecute();
        (SaveCommand as Command)?.ChangeCanExecute();
        (SaveAsCommand as Command)?.ChangeCanExecute();
        (RevertCommand as Command)?.ChangeCanExecute();
        (CloseDocumentCommand as Command)?.ChangeCanExecute();
        (ExportPdfCommand as Command)?.ChangeCanExecute();
        (CreateMdsCommand as Command)?.ChangeCanExecute();
        (NavigateUpWorkspaceCommand as Command)?.ChangeCanExecute();
        (SetWorkspaceFolderToCurrentCommand as Command)?.ChangeCanExecute();
    }

    private void ToggleWorkspacePanel()
    {
        IsWorkspacePanelVisible = !IsWorkspacePanelVisible;
        PersistSessionState();
    }

    public void UpdateWorkspacePanelWidth(double width)
    {
        WorkspacePanelWidth = width;
        PersistSessionState();
    }

    private void RequestEditorAction(EditorActionType actionType)
    {
        _logger.LogInformation("Editor action requested: {ActionType}", actionType);
        EditorActionRequested?.Invoke(this, new EditorActionRequestedEventArgs
        {
            ActionType = actionType
        });
    }

    private async Task OpenFolderAsync()
    {
        try
        {
            var folderPath = await _workspaceBrowserService.PickFolderAsync();
            if (string.IsNullOrWhiteSpace(folderPath))
            {
                return;
            }

            IsWorkspacePanelVisible = true;
            WorkspaceSearchText = string.Empty;
            await Workspace.LoadWorkspaceAsync(folderPath);
            PersistSessionState();
            await ClearInlineErrorAsync();
        }
        catch (Exception ex)
        {
            await ReportErrorAsync("Failed to open the selected folder.", ex, "The selected folder could not be opened.");
        }
    }

    private async Task SelectWorkspaceItemAsync(WorkspaceTreeItem? item)
    {
        if (item is null)
        {
            return;
        }

        await MainThread.InvokeOnMainThreadAsync(() => Workspace.SelectItem(item));

        if (!item.IsDirectory)
        {
            await OpenDocumentAsync(
                () => Task.FromResult<string?>(item.FullPath),
                "Failed to open markdown file from the workspace tree.",
                "The selected file could not be opened.");
        }
    }

    private async Task NavigateWorkspaceItemAsync(WorkspaceTreeItem? item)
    {
        if (item is null)
        {
            return;
        }

        await MainThread.InvokeOnMainThreadAsync(() => Workspace.SelectItem(item));

        if (item.IsDirectory)
        {
            await Workspace.NavigateToItemAsync(item);
            PersistSessionState();
            return;
        }

        await OpenDocumentAsync(
            () => Task.FromResult<string?>(item.FullPath),
            "Failed to open markdown file from the workspace tree.",
            "The selected file could not be opened.");
    }

    private void ToggleWorkspaceItemExpansion(WorkspaceTreeItem? item)
    {
        if (item is null)
        {
            return;
        }
    }

    private void BeginRenameWorkspaceItem(WorkspaceTreeItem? item)
    {
        Workspace.BeginRename(item);
    }

    private async Task CreateMdsAsync()
    {
        if (string.IsNullOrWhiteSpace(WorkspaceRootPath))
        {
            return;
        }

        var targetDirectory = Workspace.SelectedWorkspaceDirectoryPath;

        try
        {
            WorkspaceSearchText = string.Empty;
            var createdFilePath = await _workspaceBrowserService.CreateMarkdownSharpFileAsync(targetDirectory);
            await Workspace.LoadWorkspaceAsync(WorkspaceRootPath, currentFolderPath: CurrentWorkspaceFolderPath, selectedPath: createdFilePath);
            Workspace.BeginRename(Workspace.FindWorkspaceItem(createdFilePath));
            PersistSessionState();
            await ClearInlineErrorAsync();
        }
        catch (Exception ex)
        {
            await ReportErrorAsync("Failed to create a new markdown_sharp file.", ex, "The new markdown_sharp file could not be created.");
        }
    }

    private async Task NavigateUpWorkspaceAsync()
    {
        if (!CanNavigateUpWorkspace)
        {
            return;
        }
        
        await Workspace.NavigateUpAsync();
        PersistSessionState();
    }

    private async Task SetWorkspaceFolderToCurrentAsync()
    {
        if (!CanSetCurrentFolderAsWorkspace)
        {
            return;
        }

        WorkspaceSearchText = string.Empty;
        await Workspace.SetWorkspaceFolderToCurrentAsync();
        PersistSessionState();
        await ClearInlineErrorAsync();
    }

    private async Task RestoreSessionAsync()
    {
        var stopwatch = Stopwatch.StartNew();
        var shouldPersistRestoredSessionState = true;
        var clearWorkspaceRestoreTarget = false;
        try
        {
            IsWorkspacePanelVisible = _sessionState.IsWorkspacePanelVisible;
            WorkspacePanelWidth = _sessionState.WorkspacePanelWidth;
            SelectedViewMode = _editorModeSupportController.ResolveSupportedViewMode(_sessionState.LastViewMode, showUnsupportedSnackbar: true);

            var restoredWorkspacePath = _sessionRestoreCoordinator.ResolveWorkspaceRestorePath(_sessionState, out var workspaceRepickMessage);
            var hasWorkspaceAccess = !string.IsNullOrWhiteSpace(restoredWorkspacePath) && Directory.Exists(restoredWorkspacePath);
            if (hasWorkspaceAccess)
            {
                try
                {
                    await Workspace.LoadWorkspaceAsync(
                        restoredWorkspacePath!,
                        ResolveCurrentWorkspaceFolderPath(restoredWorkspacePath!, _sessionState.CurrentFolderPath),
                        _sessionState.DocumentFilePath);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to restore the previous workspace tree. Continuing session restore without the workspace explorer.");
                    Workspace.Clear();
                    hasWorkspaceAccess = false;
                    if (ex is UnauthorizedAccessException)
                    {
                        clearWorkspaceRestoreTarget = true;
                    }
                }
            }
            else
            {
                Workspace.Clear();
            }

            var documentRestore = await TryRestoreSessionDocumentAsync(hasWorkspaceAccess);
            if (documentRestore.Document is not null)
            {
                await LoadDocumentIntoStateAsync(documentRestore.Document, persistSessionState: false);
            }
            else
            {
                shouldPersistRestoredSessionState = false;
                await LoadDocumentIntoStateAsync(
                    await _documentService.LoadInitialDocumentAsync() ?? await _documentService.CreateUntitledDocumentAsync(),
                    persistSessionState: false);
                var repickMessage = documentRestore.RepickMessage ?? workspaceRepickMessage;
                if (!string.IsNullOrWhiteSpace(repickMessage))
                {
                    await MainThread.InvokeOnMainThreadAsync(() =>
                    {
                        InlineErrorMessage = repickMessage;
                    });
                }
            }

            if (clearWorkspaceRestoreTarget || documentRestore.ClearRestoreTarget)
            {
                ClearInvalidRestoreTargets(clearWorkspaceRestoreTarget, documentRestore.ClearRestoreTarget);
            }

            if (shouldPersistRestoredSessionState)
            {
                PersistSessionState();
            }
            _logger.LogInformation("Restored previous session. WorkspaceRootPath: {WorkspaceRootPath}, CurrentFolderPath: {CurrentFolderPath}, DocumentFilePath: {DocumentFilePath}, ViewMode: {ViewMode}, ElapsedMs: {ElapsedMs}",
                _sessionState.WorkspaceRootPath,
                _sessionState.CurrentFolderPath,
                _sessionState.DocumentFilePath,
                _sessionState.LastViewMode,
                stopwatch.ElapsedMilliseconds);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to restore the previous session. Falling back to the default startup document.");
            await LoadFallbackStartupDocumentAsync(stopwatch);
        }
    }

    private async Task<(MarkdownDocument? Document, string? RepickMessage, bool ClearRestoreTarget)> TryRestoreSessionDocumentAsync(bool hasWorkspaceAccess)
    {
        var filePath = _sessionRestoreCoordinator.ResolveDocumentRestorePath(_sessionState, hasWorkspaceAccess, out var needsRepick);
        _logger.LogDebug(
            "Resolved session document restore path. FilePath: {FilePath}, HasWorkspaceAccess: {HasWorkspaceAccess}, NeedsRepick: {NeedsRepick}, SavedDocumentFilePath: {SavedDocumentFilePath}, HasDocumentBookmark: {HasDocumentBookmark}",
            filePath,
            hasWorkspaceAccess,
            needsRepick,
            _sessionState.DocumentFilePath,
            !string.IsNullOrWhiteSpace(_sessionState.DocumentFileBookmark));

        if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
        {
            if (needsRepick)
            {
                return (null, "The previous markdown file needs permission again. Please use Open to re-pick it.", true);
            }

            return (null, null, false);
        }

        try
        {
            return (await _documentService.LoadDocumentAsync(filePath), null, false);
        }
        catch (UnauthorizedAccessException ex)
        {
            _logger.LogWarning(ex, "Unable to restore previous session document {FilePath} because access was denied.", filePath);
            return (null, "The previous markdown file needs permission again. Please use Open to re-pick it.", true);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Unable to restore previous session document {FilePath}", filePath);
            return (null, "The previous markdown file could not be reopened. Please use Open to re-pick it.", false);
        }
    }

    private void ClearInvalidRestoreTargets(bool clearWorkspace, bool clearDocument)
    {
        if (clearWorkspace)
        {
            _sessionState.WorkspaceRootPath = null;
            _sessionState.WorkspaceRootBookmark = null;
            _sessionState.CurrentFolderPath = null;
        }

        if (clearDocument)
        {
            _sessionState.DocumentFilePath = null;
            _sessionState.DocumentFileBookmark = null;
        }

        _sessionRestoreCoordinator.Save(new SessionPersistenceRequest
        {
            WorkspaceRootPath = _sessionState.WorkspaceRootPath,
            DocumentFilePath = _sessionState.DocumentFilePath,
            CurrentFolderPath = _sessionState.CurrentFolderPath,
            ViewMode = _sessionState.LastViewMode,
            IsWorkspacePanelVisible = _sessionState.IsWorkspacePanelVisible,
            WorkspacePanelWidth = _sessionState.WorkspacePanelWidth
        });

        _logger.LogInformation(
            "Cleared invalid session restore targets. ClearWorkspace: {ClearWorkspace}, ClearDocument: {ClearDocument}, WorkspaceRootPath: {WorkspaceRootPath}, DocumentFilePath: {DocumentFilePath}",
            clearWorkspace,
            clearDocument,
            _sessionState.WorkspaceRootPath,
            _sessionState.DocumentFilePath);
    }

    private void PersistSessionState()
    {
        _sessionState = new SessionState
        {
            WorkspaceRootPath = string.IsNullOrWhiteSpace(WorkspaceRootPath) ? null : WorkspaceRootPath,
            DocumentFilePath = !IsUntitled && !string.IsNullOrWhiteSpace(FilePath) ? FilePath : null,
            CurrentFolderPath = string.IsNullOrWhiteSpace(CurrentWorkspaceFolderPath) ? WorkspaceRootPath : CurrentWorkspaceFolderPath,
            LastViewMode = SelectedViewMode,
            IsWorkspacePanelVisible = IsWorkspacePanelVisible,
            WorkspacePanelWidth = WorkspacePanelWidth
        };

        _sessionRestoreCoordinator.Save(new SessionPersistenceRequest
        {
            WorkspaceRootPath = _sessionState.WorkspaceRootPath,
            DocumentFilePath = _sessionState.DocumentFilePath,
            CurrentFolderPath = _sessionState.CurrentFolderPath,
            ViewMode = _sessionState.LastViewMode,
            IsWorkspacePanelVisible = _sessionState.IsWorkspacePanelVisible,
            WorkspacePanelWidth = _sessionState.WorkspacePanelWidth
        });
        _logger.LogDebug(
            "Persisted session state. WorkspaceRootPath: {WorkspaceRootPath}, CurrentFolderPath: {CurrentFolderPath}, DocumentFilePath: {DocumentFilePath}, HasWorkspaceBookmark: {HasWorkspaceBookmark}, HasDocumentBookmark: {HasDocumentBookmark}, ViewMode: {ViewMode}",
            _sessionState.WorkspaceRootPath,
            _sessionState.CurrentFolderPath,
            _sessionState.DocumentFilePath,
            !string.IsNullOrWhiteSpace(_sessionState.WorkspaceRootPath),
            !string.IsNullOrWhiteSpace(_sessionState.DocumentFilePath),
            _sessionState.LastViewMode);
    }

    private void RememberOpenedDocument(MarkdownDocument document)
    {
        if (document.IsUntitled || string.IsNullOrWhiteSpace(document.FilePath))
        {
            return;
        }

        _sessionState.DocumentFilePath = document.FilePath;
        _sessionState.CurrentFolderPath = CurrentWorkspaceFolderPath;
        _sessionState.LastViewMode = SelectedViewMode;
        _sessionState.IsWorkspacePanelVisible = IsWorkspacePanelVisible;
        _sessionState.WorkspacePanelWidth = WorkspacePanelWidth;
        PersistSessionState();

        _logger.LogDebug(
            "Remembered opened document for session restore. DocumentFilePath: {DocumentFilePath}, HasDocumentBookmark: {HasDocumentBookmark}",
            _sessionState.DocumentFilePath,
            !string.IsNullOrWhiteSpace(_sessionState.DocumentFilePath));
    }

    private async Task LoadFallbackStartupDocumentAsync(Stopwatch startupStopwatch)
    {
        var fallbackStopwatch = Stopwatch.StartNew();
        var fallbackDocument = await _documentService.LoadInitialDocumentAsync() ?? await _documentService.CreateUntitledDocumentAsync();
        await LoadDocumentIntoStateAsync(fallbackDocument);

        _logger.LogInformation(
            "Fallback startup document load completed. FileName: {FileName}, ElapsedMs: {ElapsedMs}, TotalSessionInitElapsedMs: {TotalElapsedMs}",
            fallbackDocument.FileName,
            fallbackStopwatch.ElapsedMilliseconds,
            startupStopwatch.ElapsedMilliseconds);
    }

    private static string ResolveCurrentWorkspaceFolderPath(string workspaceRootPath, string? currentFolderPath)
    {
        if (string.IsNullOrWhiteSpace(workspaceRootPath))
        {
            return string.Empty;
        }

        if (string.IsNullOrWhiteSpace(currentFolderPath) || !Directory.Exists(currentFolderPath))
        {
            return workspaceRootPath;
        }

        if (!currentFolderPath.StartsWith(workspaceRootPath, StringComparison.Ordinal))
        {
            return workspaceRootPath;
        }

        return currentFolderPath;
    }

    private static double ClampWorkspacePanelWidth(double width)
    {
        if (width <= 0)
        {
            return DefaultWorkspacePanelWidth;
        }

        return Math.Clamp(width, MinWorkspacePanelWidth, MaxWorkspacePanelWidth);
    }

    private static bool AreEquivalentBlocks(IReadOnlyList<MarkdownBlock> current, IReadOnlyList<MarkdownBlock> next)
    {
        if (ReferenceEquals(current, next))
        {
            return true;
        }

        if (current.Count != next.Count)
        {
            return false;
        }

        for (var index = 0; index < current.Count; index++)
        {
            if (!AreEquivalentBlocks(current[index], next[index]))
            {
                return false;
            }
        }

        return true;
    }

    private static bool AreEquivalentBlocks(MarkdownBlock current, MarkdownBlock next)
    {
        return current.Type == next.Type
            && current.HeaderLevel == next.HeaderLevel
            && string.Equals(current.Content, next.Content, StringComparison.Ordinal)
            && string.Equals(current.CodeLanguage, next.CodeLanguage, StringComparison.Ordinal)
            && current.ListLevel == next.ListLevel
            && current.OrderedNumber == next.OrderedNumber
            && current.IsChecked == next.IsChecked
            && current.QuoteLevel == next.QuoteLevel
            && string.Equals(current.ImageSource, next.ImageSource, StringComparison.Ordinal)
            && string.Equals(current.ImageAltText, next.ImageAltText, StringComparison.Ordinal)
            && string.Equals(current.FootnoteId, next.FootnoteId, StringComparison.Ordinal)
            && current.TableHeaders.SequenceEqual(next.TableHeaders, StringComparer.Ordinal)
            && current.TableAlignments.SequenceEqual(next.TableAlignments)
            && AreEquivalentTableRows(current.TableRows, next.TableRows);
    }

    private static bool AreEquivalentTableRows(IReadOnlyList<List<string>> current, IReadOnlyList<List<string>> next)
    {
        if (current.Count != next.Count)
        {
            return false;
        }

        for (var index = 0; index < current.Count; index++)
        {
            if (!current[index].SequenceEqual(next[index], StringComparer.Ordinal))
            {
                return false;
            }
        }

        return true;
    }
}
