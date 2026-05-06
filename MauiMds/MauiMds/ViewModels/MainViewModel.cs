using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using MauiMds.AudioCapture;
using MauiMds.Controls;
using MauiMds.Features.Editor;
using MauiMds.Features.Export;
using MauiMds.Features.Session;
using MauiMds.Features.Workspace;
using MauiMds.Logging;
using MauiMds.Models;
using MauiMds.Services;
using MauiMds.Transcription;
using Microsoft.Extensions.Logging;

namespace MauiMds.ViewModels;

public class MainViewModel : INotifyPropertyChanged
{
    // Fast enough to feel live in viewer mode; the markdown parser is cheap for display-only rendering.
    private static readonly TimeSpan ViewerParseDebounceDelay = TimeSpan.FromMilliseconds(250);
    // Longer than typical inter-keystroke interval during burst typing so we don't re-parse mid-word.
    private static readonly TimeSpan EditorParseDebounceDelay = TimeSpan.FromMilliseconds(900);
    // Covers most file-save latencies; avoids reloading a partially-written file from a sync client.
    private static readonly TimeSpan ExternalChangeDebounceDelay = TimeSpan.FromMilliseconds(400);
    // Sync clients (Resilio, iCloud) fire directory events in rapid bursts; debounce lets the storm settle.
    private const int WorkspaceWatcherDebounceMs = 600;
    // Five seconds gives the user time to notice and cancel an accidental delete.
    private const int PendingDeleteCountdownMs = 5000;
    // Rename triggers on a slow double-tap: ≥500ms prevents accidental triggers; ≤2500ms catches slow intentional ones.
    private const double RenameTriggerMinElapsedMs = 500;
    private const double RenameTriggerMaxElapsedMs = 2500;
    // Tuned to display typical file/folder names without truncation on common laptop-width panels.
    private const double DefaultWorkspacePanelWidth = 260;
    private const double MinWorkspacePanelWidth = 180;
    private const double MaxWorkspacePanelWidth = 520;

    public event PropertyChangedEventHandler? PropertyChanged;
    public event EventHandler<EditorActionRequestedEventArgs>? EditorActionRequested;
    public event EventHandler? FindRequested;

    private readonly IMarkdownDocumentService _documentService;
    private readonly IWorkspaceBrowserService _workspaceBrowserService;
    private readonly IDocumentWatchService _documentWatchService;
    private readonly IClock _clock;
    private readonly ILogger<MainViewModel> _logger;
    private readonly DocumentApplyService _documentApplyController;
    private readonly DocumentWorkflowService _documentWorkflowController;
    private readonly PreviewPipelineCoordinator _previewPipelineController;
    private readonly AutosaveCoordinator _autosaveCoordinator;
    private readonly SessionRestoreCoordinator _sessionRestoreCoordinator;
    private readonly EditorModeSupportService _editorModeSupportController;
    private readonly IPdfExportService _pdfExportService;

    public RecordingSessionViewModel Recording { get; }
    public TranscriptionQueueViewModel TranscriptionQueue { get; }
    public PreferencesViewModel Preferences { get; }

    private EditorDocumentState _document = new();
    private EditorViewMode _selectedViewMode = EditorViewMode.Viewer;
    private SessionState _sessionState;

    private IReadOnlyList<MarkdownBlock> _parsedBlocks = Array.Empty<MarkdownBlock>();
    private bool _isInitialized;
    private bool _isOpeningDocument;
    private bool _isSavingDocument;
    private bool _isWorkspacePanelVisible;
    private double _workspacePanelWidth = DefaultWorkspacePanelWidth;
    private string _inlineErrorMessage = string.Empty;
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

    // Workspace auto-refresh
    private FileSystemWatcher? _workspaceWatcher;
    private Timer? _workspaceRefreshTimer;
    private Timer? _watcherDebounceTimer;

    // Pending-delete: item → cancellation source for the 5-second countdown
    private readonly Dictionary<WorkspaceTreeItem, CancellationTokenSource> _pendingDeletes = new();

    // Slow double-click rename tracking
    private WorkspaceTreeItem? _lastSingleTappedItem;
    private DateTime _lastSingleTapTime;

    public MainViewModel(
        IMarkdownDocumentService documentService,
        IWorkspaceBrowserService workspaceBrowserService,
        PreferencesViewModel preferencesViewModel,
        IDocumentWatchService documentWatchService,
        IClock clock,
        WorkspaceExplorerState workspaceExplorerState,
        DocumentApplyService documentApplyController,
        DocumentWorkflowService documentWorkflowController,
        PreviewPipelineCoordinator previewPipelineController,
        EditorModeSupportService editorModeSupportController,
        AutosaveCoordinator autosaveCoordinator,
        SessionRestoreCoordinator sessionRestoreCoordinator,
        IPdfExportService pdfExportService,
        IAudioCaptureService audioCaptureService,
        IAudioPlayerService audioPlayerService,
        ITranscriptionPipelineFactory transcriptionPipelineFactory,
        ILoggerFactory loggerFactory,
        ILogger<MainViewModel> logger)
    {
        _documentService = documentService;
        _workspaceBrowserService = workspaceBrowserService;
        _documentWatchService = documentWatchService;
        _clock = clock;
        _documentApplyController = documentApplyController;
        _documentWorkflowController = documentWorkflowController;
        _previewPipelineController = previewPipelineController;
        _editorModeSupportController = editorModeSupportController;
        _autosaveCoordinator = autosaveCoordinator;
        _sessionRestoreCoordinator = sessionRestoreCoordinator;
        _pdfExportService = pdfExportService;
        _logger = logger;
        _sessionState = _sessionRestoreCoordinator.Load();
        Workspace = workspaceExplorerState;
        _workspacePanelWidth = ClampWorkspacePanelWidth(_sessionState.WorkspacePanelWidth);

        Preferences = preferencesViewModel;
        Preferences.SaveError += async (_, args) =>
            await ReportErrorAsync(args.Message, args.Exception, args.InlineMessage);

        _documentWatchService.DocumentChanged += OnWatchedDocumentChanged;
        Workspace.PropertyChanged += OnWorkspacePropertyChanged;
        Workspace.WorkspaceItems.CollectionChanged += OnWorkspaceItemsChanged;

        OpenFileCommand = new Command(async () => await OpenFileAsync(), () => !IsBusy);
        NewDocumentCommand = new Command(async () => await NewDocumentAsync(), () => !IsBusy);
        SaveCommand = new Command(async () => await SaveDocumentAsync(), () => !IsBusy);
        SaveAsCommand = new Command(async () => await SaveDocumentAsAsync(), () => !IsBusy);
        RevertCommand = new Command(async () => await RevertDocumentAsync(), () => !IsBusy);
        CloseDocumentCommand = new Command(async () => await CloseDocumentAsync(), () => !IsBusy);
        ExportPdfCommand = new Command(async () => await ExportAsPdfAsync(), () => !IsBusy && _parsedBlocks.Count > 0);
        SetViewModeCommand = new Command<EditorViewMode>(SetViewMode);
        ToggleWorkspacePanelCommand = new Command(ToggleWorkspacePanel);
        OpenFolderCommand = new Command(async () => await OpenFolderAsync());
        SelectWorkspaceItemCommand = new Command<WorkspaceTreeItem>(async item =>
        {
            if (item is null) return;
            if (item.IsPendingDelete) { CancelPendingDelete(item); return; }

            var now = DateTime.UtcNow;
            var elapsed = (now - _lastSingleTapTime).TotalMilliseconds;

            if (item == _lastSingleTappedItem
                && item.IsSelected
                && item.CanRename
                && elapsed is >= RenameTriggerMinElapsedMs and <= RenameTriggerMaxElapsedMs)
            {
                _lastSingleTappedItem = null;
                BeginRenameWorkspaceItem(item);
                return;
            }

            _lastSingleTappedItem = item;
            _lastSingleTapTime = now;
            await SelectWorkspaceItemAsync(item);
        });
        NavigateWorkspaceItemCommand = new Command<WorkspaceTreeItem>(async item =>
        {
            if (item is null) return;
            if (item.IsPendingDelete) { CancelPendingDelete(item); return; }
            _lastSingleTappedItem = null;
            await NavigateWorkspaceItemAsync(item);
        });
        DeleteWorkspaceItemCommand = new Command<WorkspaceTreeItem>(async item => await StartDeleteAsync(item));
        ToggleWorkspaceItemExpansionCommand = new Command<WorkspaceTreeItem>(ToggleWorkspaceItemExpansion);
        BeginRenameWorkspaceItemCommand = new Command<WorkspaceTreeItem>(BeginRenameWorkspaceItem);
        CreateMdsCommand = new Command(async () => await CreateMdsAsync(), () => HasWorkspaceRoot);
        NavigateUpWorkspaceCommand = new Command(async () => await NavigateUpWorkspaceAsync(), () => CanNavigateUpWorkspace);
        SetWorkspaceFolderToCurrentCommand = new Command(async () => await SetWorkspaceFolderToCurrentAsync(), () => CanSetCurrentFolderAsWorkspace);
        UndoCommand = new Command(() => RequestEditorAction(e => { e.Undo(); return Task.CompletedTask; }));
        RedoCommand = new Command(() => RequestEditorAction(e => { e.Redo(); return Task.CompletedTask; }));
        CutCommand = new Command(() => RequestEditorAction(e => e.CutSelectionAsync()));
        CopyCommand = new Command(() => RequestEditorAction(e => e.CopySelectionAsync()));
        PasteCommand = new Command(() => RequestEditorAction(e => e.PasteAsync()));
        FindCommand = new Command(() => FindRequested?.Invoke(this, EventArgs.Empty));
        FormatParagraphCommand = new Command(() => RequestEditorAction(e => { e.ApplyParagraphStyle(); return Task.CompletedTask; }));
        FormatHeader1Command = new Command(() => RequestEditorAction(e => { e.ApplyHeaderPrefix(1); return Task.CompletedTask; }));
        FormatHeader2Command = new Command(() => RequestEditorAction(e => { e.ApplyHeaderPrefix(2); return Task.CompletedTask; }));
        FormatHeader3Command = new Command(() => RequestEditorAction(e => { e.ApplyHeaderPrefix(3); return Task.CompletedTask; }));
        FormatBulletCommand = new Command(() => RequestEditorAction(e => { e.ApplyBulletStyle(); return Task.CompletedTask; }));
        FormatChecklistCommand = new Command(() => RequestEditorAction(e => { e.ApplyChecklistStyle(); return Task.CompletedTask; }));
        FormatQuoteCommand = new Command(() => RequestEditorAction(e => { e.ApplyQuoteStyle(); return Task.CompletedTask; }));
        FormatCodeCommand = new Command(() => RequestEditorAction(e => { e.ApplyCodeStyle(); return Task.CompletedTask; }));
        FormatBoldCommand = new Command(() => RequestEditorAction(e => { e.ApplyBoldStyle(); return Task.CompletedTask; }));
        FormatItalicCommand = new Command(() => RequestEditorAction(e => { e.ApplyItalicStyle(); return Task.CompletedTask; }));
        RefreshWorkspaceCommand = new Command(async () => await RefreshWorkspaceFromDiskAsync());

        Recording = new RecordingSessionViewModel(
            audioCaptureService, audioPlayerService, clock,
            loggerFactory.CreateLogger<RecordingSessionViewModel>(),
            () => Preferences.Current.RecordingFormat,
            () => WorkspaceRootPath,
            ReportErrorAsync);

        TranscriptionQueue = new TranscriptionQueueViewModel(
            transcriptionPipelineFactory, Workspace,
            loggerFactory.CreateLogger<TranscriptionQueueViewModel>(),
            () => new TranscriptionConfig(
                Preferences.Current.TranscriptionEngine, Preferences.Current.DiarizationEngine,
                Preferences.Current.WhisperBinaryPath, Preferences.Current.WhisperModelPath,
                Preferences.Current.PyannotePythonPath, Preferences.Current.PyannoteHfToken),
            () => Recording.SelectedRecordingGroup,
            group => Recording.SelectedRecordingGroup = group,
            ReportErrorAsync,
            msg => MainThread.BeginInvokeOnMainThread(() => InlineErrorMessage = msg));

        Recording.RecordingStopped += async (_, args) =>
        {
            if (!string.IsNullOrWhiteSpace(WorkspaceRootPath))
                await Workspace.LoadWorkspaceAsync(WorkspaceRootPath, currentFolderPath: CurrentWorkspaceFolderPath);

            if (!string.IsNullOrEmpty(args.BaseName))
            {
                var newGroup = Workspace.WorkspaceItems
                    .FirstOrDefault(i => i.IsRecordingGroup &&
                        string.Equals(i.RecordingGroup!.BaseName, args.BaseName, StringComparison.Ordinal))
                    ?.RecordingGroup;

                if (newGroup is not null)
                    TranscriptionQueue.Enqueue(newGroup);
            }
        };

        Recording.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(RecordingSessionViewModel.SelectedRecordingGroup))
                TranscriptionQueue.NotifyCanReTranscribeGroupChanged();
        };

        TranscriptionQueue.LoadDocumentRequested += (_, doc) =>
            _ = LoadDocumentIntoStateAsync(doc, persistSessionState: false);

        TranscriptionQueue.OpenDocumentRequested += (_, path) =>
            _ = OpenDocumentAsync(() => Task.FromResult<string?>(path),
                "Failed to open transcript.", "The transcript could not be opened.");

        TranscriptionQueue.EditorProgressUpdated += (_, args) =>
        {
            if (ReferenceEquals(Recording.SelectedRecordingGroup, args.Group) && _document.IsUntitled)
                MainThread.BeginInvokeOnMainThread(() => EditorText = args.Content);
        };

        TranscriptionQueue.WorkspaceRefreshNeeded += async (_, _) =>
        {
            if (!string.IsNullOrWhiteSpace(WorkspaceRootPath))
                await Workspace.LoadWorkspaceAsync(WorkspaceRootPath, currentFolderPath: CurrentWorkspaceFolderPath);
        };

        Preferences.PreferencesSaved += OnPreferencesSaved;
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
    public ICommand SetViewModeCommand { get; }
    public ICommand ToggleWorkspacePanelCommand { get; }
    public ICommand OpenFolderCommand { get; }
    public ICommand SelectWorkspaceItemCommand { get; }
    public ICommand NavigateWorkspaceItemCommand { get; }
    public ICommand DeleteWorkspaceItemCommand { get; }
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
    public ICommand RefreshWorkspaceCommand { get; }

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
        get => _document.Content;
        set
        {
            if (_document.Content == value)
            {
                return;
            }

            _document.Content = value;
            _document.IsDirty = !string.Equals(_document.Content, _document.OriginalContent, StringComparison.Ordinal);
            OnPropertyChanged();
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
        _ = Recording.RequestMicrophonePermissionAsync();
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

            if (item.IsRecordingGroup)
            {
                var group = item.RecordingGroup!;
                var newBaseName = item.RenameText;
                await _workspaceBrowserService.RenameRecordingGroupAsync(group, newBaseName);

                // If the open document is the old transcript, reload it from the renamed path.
                if (group.TranscriptPath is { } oldTranscript
                    && string.Equals(FilePath, oldTranscript, StringComparison.Ordinal))
                {
                    var oldPrefix = Path.Combine(group.DirectoryPath, group.BaseName);
                    var newPrefix = Path.Combine(group.DirectoryPath, newBaseName);
                    var newTranscriptPath = newPrefix + oldTranscript[oldPrefix.Length..];
                    var renamedDocument = await _documentService.LoadDocumentAsync(newTranscriptPath);
                    await LoadDocumentIntoStateAsync(renamedDocument);
                }

                await Workspace.LoadWorkspaceAsync(WorkspaceRootPath, currentFolderPath: group.DirectoryPath);
            }
            else
            {
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
            }

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
        if (App.IsTerminating) return;
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

    private async void OnPreferencesSaved(object? sender, EditorPreferences saved)
    {
        KeyboardShortcutsChanged?.Invoke(this, EventArgs.Empty);
        ApplyWorkspaceRefreshSettings();
        OnPropertyChanged(nameof(StatusText));
        ScheduleAutoSave();
        PersistSessionState();
        await ClearInlineErrorAsync();
    }

    private void SetViewMode(EditorViewMode mode)
    {
        SelectedViewMode = _editorModeSupportController.ResolveSupportedViewMode(mode, showUnsupportedSnackbar: true);
        PersistSessionState();
    }

    private async Task SaveIfNeededAsync()
    {
        // Never prompt-save an untitled document (e.g. the transcription progress doc).
        // Untitled documents are ephemeral; the user must explicitly choose Save As.
        if (!_document.IsDirty || _document.IsUntitled)
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

                OnPropertyChanged(nameof(EditorText));
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
            Preferences.Current.AutoSaveEnabled,
            _document.IsUntitled,
            _document.IsDirty,
            _document.FilePath,
            TimeSpan.FromSeconds(Preferences.Current.AutoSaveDelaySeconds),
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
        catch (IOException ioEx)
        {
            // Transient IO failure (e.g. file locked briefly by a sync client). The watcher
            // fires again once the file stabilises, so no user-visible error is needed.
            _logger.LogDebug(ioEx, "External file change: transient IO error for {Path}.", filePath);
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

        parts.Add(Preferences.Current.AutoSaveEnabled ? $"Autosave {Preferences.Current.AutoSaveDelaySeconds}s" : "Autosave off");
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

        const int maxLines = 28;         // enough to fill a typical viewer window during the parse delay
        const int maxCharacters = 2200;  // ~1.5 screens of text; avoids layout work on huge documents

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

    public Task StopRecordingAsync() => Recording.StopRecordingAsync();

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

    private void RequestEditorAction(Func<IEditorSurface, Task> action)
    {
        EditorActionRequested?.Invoke(this, new EditorActionRequestedEventArgs { Action = action });
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
            StartWorkspaceWatcher(folderPath);
            ApplyWorkspaceRefreshSettings();
            PersistSessionState();
            await ClearInlineErrorAsync();
        }
        catch (Exception ex)
        {
            await ReportErrorAsync("Failed to open the selected folder.", ex, "The selected folder could not be opened.");
        }
    }

    private async Task RefreshWorkspaceFromDiskAsync()
    {
        if (!HasWorkspaceRoot) return;
        try { await Workspace.ReloadFromDiskAsync(); }
        catch (Exception ex) { _logger.LogWarning(ex, "Workspace refresh failed."); }
    }

    private void OnWorkspaceItemsChanged(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
    {
        var items = e.Action == System.Collections.Specialized.NotifyCollectionChangedAction.Add && e.NewItems is not null
            ? e.NewItems.Cast<WorkspaceTreeItem>()
            : (IEnumerable<WorkspaceTreeItem>)Workspace.WorkspaceItems;

        Recording.ApplyHighlights(items);
        TranscriptionQueue.ApplyHighlights(items);
    }

    private async Task StartDeleteAsync(WorkspaceTreeItem? item)
    {
        if (item is null) return;

        if (item.IsPendingDelete)
        {
            CancelPendingDelete(item);
            return;
        }

        var cts = new CancellationTokenSource();
        _pendingDeletes[item] = cts;
        item.IsPendingDelete = true;

        try
        {
            await Task.Delay(PendingDeleteCountdownMs, cts.Token);
            await ExecuteDeleteAsync(item);
            if (!string.IsNullOrWhiteSpace(WorkspaceRootPath))
                await Workspace.LoadWorkspaceAsync(WorkspaceRootPath, currentFolderPath: CurrentWorkspaceFolderPath);
        }
        catch (OperationCanceledException)
        {
            item.IsPendingDelete = false;
        }
        finally
        {
            _pendingDeletes.Remove(item);
            cts.Dispose();
        }
    }

    private void CancelPendingDelete(WorkspaceTreeItem item)
    {
        if (_pendingDeletes.TryGetValue(item, out var cts))
            cts.Cancel();
    }

    private Task ExecuteDeleteAsync(WorkspaceTreeItem item)
    {
        if (item.IsRecordingGroup)
        {
            var group = item.RecordingGroup!;

            if (group.MicFilePath is not null) MoveToTrash(group.MicFilePath);
            if (group.SysFilePath is not null) MoveToTrash(group.SysFilePath);

            if (Directory.Exists(group.DirectoryPath))
            {
                var transcriptBase = group.BaseName + "_transcript";
                foreach (var file in Directory.GetFiles(group.DirectoryPath))
                {
                    var name = Path.GetFileName(file);
                    if (name.StartsWith(transcriptBase, StringComparison.OrdinalIgnoreCase) &&
                        (name.EndsWith(".md", StringComparison.OrdinalIgnoreCase) ||
                         name.EndsWith(".mds", StringComparison.OrdinalIgnoreCase)))
                        MoveToTrash(file);
                }
            }
        }
        else
        {
            MoveToTrash(item.FullPath);
        }

        _logger.LogInformation("Deleted workspace item: {Path}", item.FullPath);
        return Task.CompletedTask;
    }

    private static void MoveToTrash(string path)
    {
        if (!File.Exists(path)) return;
#if MACCATALYST
        var url = Foundation.NSUrl.FromFilename(path);
        Foundation.NSFileManager.DefaultManager.TrashItem(url, out _, out _);
#else
        File.Delete(path);
#endif
    }

    private void StartWorkspaceWatcher(string folderPath)
    {
        _workspaceWatcher?.Dispose();
        _workspaceWatcher = null;

        try
        {
            _workspaceWatcher = new FileSystemWatcher(folderPath)
            {
                IncludeSubdirectories = true,
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.LastWrite,
                EnableRaisingEvents = true
            };
            _workspaceWatcher.Created += OnWorkspaceFsEvent;
            _workspaceWatcher.Deleted += OnWorkspaceFsEvent;
            _workspaceWatcher.Renamed += OnWorkspaceFsEvent;
            _logger.LogInformation("Workspace FileSystemWatcher started for {Path}", folderPath);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not start FileSystemWatcher for {Path}", folderPath);
        }
    }

    private void OnWorkspaceFsEvent(object sender, FileSystemEventArgs e)
    {
        // Debounce: reset the debounce timer on every event.
        _watcherDebounceTimer?.Dispose();
        _watcherDebounceTimer = new Timer(_ =>
        {
            MainThread.BeginInvokeOnMainThread(async () =>
            {
                await RefreshWorkspaceFromDiskAsync();
            });
        }, null, WorkspaceWatcherDebounceMs, Timeout.Infinite);
    }

    private void ApplyWorkspaceRefreshSettings()
    {
        _workspaceRefreshTimer?.Dispose();
        _workspaceRefreshTimer = null;

        var intervalSeconds = Preferences.Current.WorkspaceRefreshIntervalSeconds;
        if (intervalSeconds > 0 && HasWorkspaceRoot)
        {
            var intervalMs = intervalSeconds * 1000;
            _workspaceRefreshTimer = new Timer(_ =>
            {
                MainThread.BeginInvokeOnMainThread(async () =>
                {
                    await RefreshWorkspaceFromDiskAsync();
                });
            }, null, intervalMs, intervalMs);
            _logger.LogInformation("Workspace periodic refresh set to {Interval}s", intervalSeconds);
        }
    }

    private async Task SelectWorkspaceItemAsync(WorkspaceTreeItem? item)
    {
        if (item is null) return;

        await MainThread.InvokeOnMainThreadAsync(() => Workspace.SelectItem(item));

        if (item.IsRecordingGroup && item.RecordingGroup is { } group)
        {
            Recording.SelectedRecordingGroup = group;
            Recording.StopPlayback();
            SetViewMode(EditorViewMode.Viewer);

            if (group.HasTranscript && group.TranscriptPath is { } transcriptPath)
            {
                _logger.LogInformation(
                    "Recording group selected: {Name}. Transcript already exists at {Path} — not regenerating.",
                    group.DisplayName, transcriptPath);
                await OpenDocumentAsync(
                    () => Task.FromResult<string?>(transcriptPath),
                    "Failed to open transcript.",
                    "The transcript could not be opened.");
            }
            else if (group.AudioFilePaths.Count > 0)
            {
                _logger.LogInformation(
                    "Recording group selected: {Name}. No transcript found — queuing for transcription.",
                    group.DisplayName);
                TranscriptionQueue.EnqueueWithProgressDocument(group);
            }

            return;
        }

        Recording.SelectedRecordingGroup = null;
        Recording.StopPlayback();

        if (item.IsAudioFile)
        {
            await Recording.PlayAudioAsync(item.FullPath);
            return;
        }

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
                    StartWorkspaceWatcher(restoredWorkspacePath!);
                    ApplyWorkspaceRefreshSettings();
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
