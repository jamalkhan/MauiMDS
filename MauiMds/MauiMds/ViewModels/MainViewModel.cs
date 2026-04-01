using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using MauiMds.Features.Editor;
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
        AutosaveCoordinator autosaveCoordinator,
        SessionRestoreCoordinator sessionRestoreCoordinator,
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
        _autosaveCoordinator = autosaveCoordinator;
        _sessionRestoreCoordinator = sessionRestoreCoordinator;
        _logger = logger;
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
        FormatHeader1Command = new Command(() => RequestEditorAction(EditorActionType.Header1));
        FormatHeader2Command = new Command(() => RequestEditorAction(EditorActionType.Header2));
        FormatHeader3Command = new Command(() => RequestEditorAction(EditorActionType.Header3));
        FallbackToMarkdownEditorCommand = new Command(() => SetViewMode(EditorViewMode.TextEditor));
    }

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
    public ICommand FormatHeader1Command { get; }
    public ICommand FormatHeader2Command { get; }
    public ICommand FormatHeader3Command { get; }
    public ICommand FallbackToMarkdownEditorCommand { get; }

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
        EditorViewMode.Viewer => "Read-Only Viewer",
        EditorViewMode.TextEditor => "Plaintext Markdown Editor",
        _ => "Rich Text Editor"
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
            OnPropertyChanged(nameof(IsViewerMode));
            OnPropertyChanged(nameof(IsTextEditorMode));
            OnPropertyChanged(nameof(IsRichTextEditorMode));
            OnPropertyChanged(nameof(IsEditorMode));
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

    public bool IsViewerMode => SelectedViewMode == EditorViewMode.Viewer;
    public bool IsTextEditorMode => SelectedViewMode == EditorViewMode.TextEditor;
    public bool IsRichTextEditorMode => SelectedViewMode == EditorViewMode.RichTextEditor;
    public bool IsEditorMode => SelectedViewMode != EditorViewMode.Viewer;

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
        IsPreferencesVisible = true;
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
            FileLogLevel = fileLogLevel
        };

        _preferencesService.Save(_preferences);
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
        SelectedViewMode = mode;
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

                IsViewerLoading = IsViewerMode;
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
        IsViewerLoading = IsViewerMode;
        ViewerLoadingPreviewText = BuildViewerLoadingPreview(snapshot.Content);

        _ = _previewPipelineController.SchedulePreviewAsync(
            snapshot,
            SelectedViewMode,
            IsViewerMode ? ViewerParseDebounceDelay : EditorParseDebounceDelay,
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
            EditorViewMode.Viewer => "Viewer",
            EditorViewMode.TextEditor => "Markdown editor",
            _ => "Rich text editor"
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
            IsViewerLoading = IsViewerMode;
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

    private void RefreshCommandStates()
    {
        (OpenFileCommand as Command)?.ChangeCanExecute();
        (NewDocumentCommand as Command)?.ChangeCanExecute();
        (SaveCommand as Command)?.ChangeCanExecute();
        (SaveAsCommand as Command)?.ChangeCanExecute();
        (RevertCommand as Command)?.ChangeCanExecute();
        (CloseDocumentCommand as Command)?.ChangeCanExecute();
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
            SelectedViewMode = _sessionState.LastViewMode;

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
