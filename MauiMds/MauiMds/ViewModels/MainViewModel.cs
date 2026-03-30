using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using MauiMds.Models;
using MauiMds.Processors;
using MauiMds.Services;
using Microsoft.Extensions.Logging;

namespace MauiMds.ViewModels;

public class MainViewModel : INotifyPropertyChanged
{
    private static readonly TimeSpan ViewerParseDebounceDelay = TimeSpan.FromMilliseconds(250);
    private static readonly TimeSpan EditorParseDebounceDelay = TimeSpan.FromMilliseconds(900);
    private static readonly TimeSpan ExternalChangeDebounceDelay = TimeSpan.FromMilliseconds(400);

    public event PropertyChangedEventHandler? PropertyChanged;
    public event EventHandler<MarkdownDocument>? DocumentApplied;
    public event EventHandler<EditorActionRequestedEventArgs>? EditorActionRequested;

    private readonly IMarkdownDocumentService _documentService;
    private readonly IWorkspaceBrowserService _workspaceBrowserService;
    private readonly IEditorPreferencesService _preferencesService;
    private readonly ISessionStateService _sessionStateService;
    private readonly IDocumentWatchService _documentWatchService;
    private readonly ILogger<MainViewModel> _logger;
    private readonly MdsParser _parser;
    private readonly List<WorkspaceTreeItem> _workspaceRootItems = [];

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
    private string _editorText = string.Empty;
    private string _inlineErrorMessage = string.Empty;
    private string _workspaceRootPath = string.Empty;
    private string _workspaceSearchText = string.Empty;
    private string _preferencesAutoSaveDelaySecondsText = "30";
    private string _preferencesMaxLogFileSizeMbText = "2";
    private bool _preferencesAutoSaveEnabled = true;
    private WorkspaceTreeItem? _selectedWorkspaceItem;
    private WorkspaceTreeItem? _pendingRenameItem;
    private CancellationTokenSource? _workspaceSearchCancellationSource;
    private CancellationTokenSource? _parseCancellationSource;
    private CancellationTokenSource? _autosaveCancellationSource;
    private DateTimeOffset _lastSaveUtc;

    public MainViewModel(
        MdsParser parser,
        IMarkdownDocumentService documentService,
        IWorkspaceBrowserService workspaceBrowserService,
        IEditorPreferencesService preferencesService,
        ISessionStateService sessionStateService,
        IDocumentWatchService documentWatchService,
        ILogger<MainViewModel> logger)
    {
        _parser = parser;
        _documentService = documentService;
        _workspaceBrowserService = workspaceBrowserService;
        _preferencesService = preferencesService;
        _sessionStateService = sessionStateService;
        _documentWatchService = documentWatchService;
        _logger = logger;
        _preferences = _preferencesService.Load();
        _sessionState = _sessionStateService.Load();
        _preferencesAutoSaveEnabled = _preferences.AutoSaveEnabled;
        _preferencesAutoSaveDelaySecondsText = _preferences.AutoSaveDelaySeconds.ToString();
        _preferencesMaxLogFileSizeMbText = _preferences.MaxLogFileSizeMb.ToString();

        _documentWatchService.DocumentChanged += OnWatchedDocumentChanged;

        WorkspaceItems = new ObservableCollection<WorkspaceTreeItem>();

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
        ToggleWorkspaceItemExpansionCommand = new Command<WorkspaceTreeItem>(ToggleWorkspaceItemExpansion);
        BeginRenameWorkspaceItemCommand = new Command<WorkspaceTreeItem>(BeginRenameWorkspaceItem);
        CreateMdsCommand = new Command(async () => await CreateMdsAsync(), () => HasWorkspaceRoot);
        UndoCommand = new Command(() => RequestEditorAction(EditorActionType.Undo));
        RedoCommand = new Command(() => RequestEditorAction(EditorActionType.Redo));
        CutCommand = new Command(() => RequestEditorAction(EditorActionType.Cut));
        CopyCommand = new Command(() => RequestEditorAction(EditorActionType.Copy));
        PasteCommand = new Command(() => RequestEditorAction(EditorActionType.Paste));
        FindCommand = new Command(() => RequestEditorAction(EditorActionType.Find));
        FormatHeader1Command = new Command(() => RequestEditorAction(EditorActionType.Header1));
        FormatHeader2Command = new Command(() => RequestEditorAction(EditorActionType.Header2));
        FormatHeader3Command = new Command(() => RequestEditorAction(EditorActionType.Header3));
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
            OnPropertyChanged();
        }
    }
    public ObservableCollection<WorkspaceTreeItem> WorkspaceItems { get; }

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
    public ICommand ToggleWorkspaceItemExpansionCommand { get; }
    public ICommand BeginRenameWorkspaceItemCommand { get; }
    public ICommand CreateMdsCommand { get; }
    public ICommand UndoCommand { get; }
    public ICommand RedoCommand { get; }
    public ICommand CutCommand { get; }
    public ICommand CopyCommand { get; }
    public ICommand PasteCommand { get; }
    public ICommand FindCommand { get; }
    public ICommand FormatHeader1Command { get; }
    public ICommand FormatHeader2Command { get; }
    public ICommand FormatHeader3Command { get; }

    public string FilePath
    {
        get => _document.FilePath;
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
        get => _document.FileName;
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
        _ => "Rich Text Editor (Plaintext Stub)"
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
    public bool IsUntitled => _document.IsUntitled;
    public string StatusText => BuildStatusText();

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
            OnPropertyChanged(nameof(WorkspacePanelWidth));
            OnPropertyChanged(nameof(WorkspaceToggleLabel));
        }
    }

    public double WorkspacePanelWidth => IsWorkspacePanelVisible ? 150 : 0;
    public string WorkspaceToggleLabel => IsWorkspacePanelVisible ? "Hide Explorer" : "Show Explorer";

    public string WorkspaceRootPath
    {
        get => _workspaceRootPath;
        private set
        {
            if (_workspaceRootPath == value)
            {
                return;
            }

            _workspaceRootPath = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(HasWorkspaceRoot));
        }
    }

    public bool HasWorkspaceRoot => !string.IsNullOrWhiteSpace(WorkspaceRootPath);

    public string WorkspaceSearchText
    {
        get => _workspaceSearchText;
        set
        {
            if (_workspaceSearchText == value)
            {
                return;
            }

            _workspaceSearchText = value;
            OnPropertyChanged();
            _ = RefreshWorkspaceItemsAsync();
        }
    }

    public WorkspaceTreeItem? PendingRenameItem
    {
        get => _pendingRenameItem;
        private set
        {
            if (_pendingRenameItem == value)
            {
                return;
            }

            _pendingRenameItem = value;
            OnPropertyChanged();
        }
    }

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
                PendingRenameItem = null;
            });

            var updatedPath = await _workspaceBrowserService.RenameMarkdownFileAsync(item.FullPath, item.RenameText);

            if (string.Equals(FilePath, item.FullPath, StringComparison.Ordinal))
            {
                var renamedDocument = await _documentService.LoadDocumentAsync(updatedPath);
                await LoadDocumentIntoStateAsync(renamedDocument);
            }

            await LoadWorkspaceAsync(WorkspaceRootPath, selectedPath: updatedPath);
            await ClearInlineErrorAsync();
        }
        catch (Exception ex)
        {
            await ReportErrorAsync("Workspace file rename failed.", ex, ex is InvalidOperationException ? ex.Message : "The file could not be renamed.");
            await MainThread.InvokeOnMainThreadAsync(() =>
            {
                item.IsRenaming = true;
                PendingRenameItem = item;
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
            item.IsRenaming = false;
            item.ResetRenameText();
            PendingRenameItem = null;
        });
    }

    protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

        if (propertyName is nameof(HasWorkspaceRoot) or nameof(IsBusy) or nameof(IsDirty) or nameof(IsUntitled))
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
            var document = await _documentService.PickDocumentAsync();
            await OpenDocumentAsync(
                () => Task.FromResult(document),
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
        Func<Task<MarkdownDocument?>> documentProvider,
        string logMessage,
        string inlineMessage)
    {
        await SaveIfNeededAsync();

        var document = await documentProvider();
        if (document is null)
        {
            await ClearInlineErrorAsync();
            return;
        }

        RememberOpenedDocument(document);

        try
        {
            await LoadDocumentIntoStateAsync(document);
            await ClearInlineErrorAsync();
        }
        catch (Exception ex)
        {
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
        PreferencesAutoSaveDelaySecondsText = _preferences.AutoSaveDelaySeconds.ToString();
        PreferencesMaxLogFileSizeMbText = _preferences.MaxLogFileSizeMb.ToString();
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

        _preferences = new EditorPreferences
        {
            AutoSaveEnabled = PreferencesAutoSaveEnabled,
            AutoSaveDelaySeconds = delaySeconds,
            MaxLogFileSizeMb = maxLogFileSizeMb
        };

        _preferencesService.Save(_preferences);
        IsPreferencesVisible = false;
        OnPropertyChanged(nameof(StatusText));
        ScheduleAutoSave();
        PersistSessionState();
        await ClearInlineErrorAsync();
    }

    private void CancelPreferences()
    {
        IsPreferencesVisible = false;
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
        FilePath = result.FilePath;
        FileName = result.FileName;
        _document.IsUntitled = false;
        _document.IsDirty = false;
        _document.OriginalContent = _document.Content;
        _document.FileSizeBytes = result.FileSizeBytes;
        _document.LastModified = result.LastModified;
        _lastSaveUtc = DateTimeOffset.UtcNow;

        OnPropertyChanged(nameof(IsUntitled));
        OnPropertyChanged(nameof(IsDirty));
        OnPropertyChanged(nameof(StatusText));
        OnPropertyChanged(nameof(HeaderPathDisplay));

        _documentWatchService.Watch(result.FilePath);
        PersistSessionState();
    }

    private async Task LoadDocumentIntoStateAsync(MarkdownDocument document, bool persistSessionState = true)
    {
        var overallStopwatch = Stopwatch.StartNew();
        _isLoadingDocument = true;

        try
        {
            _document = new EditorDocumentState
            {
                FilePath = document.IsUntitled ? string.Empty : document.FilePath,
                FileName = document.FileName ?? Path.GetFileName(document.FilePath),
                Content = document.Content,
                OriginalContent = document.Content,
                IsUntitled = document.IsUntitled || !Path.IsPathRooted(document.FilePath),
                IsDirty = false,
                FileSizeBytes = document.FileSizeBytes,
                LastModified = document.LastModified,
                EncodingName = document.EncodingName,
                NewLine = document.NewLine
            };

            var uiStateStopwatch = Stopwatch.StartNew();
            await MainThread.InvokeOnMainThreadAsync(() =>
            {
                FilePath = _document.FilePath;
                FileName = _document.FileName;
                EditorText = _document.Content;
                InlineErrorMessage = string.Empty;
                OnPropertyChanged(nameof(IsDirty));
                OnPropertyChanged(nameof(IsUntitled));
                OnPropertyChanged(nameof(StatusText));
                OnPropertyChanged(nameof(HeaderPathDisplay));
            });
            uiStateStopwatch.Stop();

            var watchStopwatch = Stopwatch.StartNew();
            if (_document.IsUntitled || string.IsNullOrWhiteSpace(_document.FilePath))
            {
                _documentWatchService.Stop();
            }
            else
            {
                _documentWatchService.Watch(_document.FilePath);
            }
            watchStopwatch.Stop();

            var sessionStopwatch = Stopwatch.StartNew();
            if (persistSessionState)
            {
                PersistSessionState();
            }
            sessionStopwatch.Stop();

            var applyStopwatch = Stopwatch.StartNew();
            await ParseAndApplyPreviewAsync(_document.Content);
            applyStopwatch.Stop();

            _logger.LogInformation(
                "Document load/apply pipeline completed. FilePath: {FilePath}, ContentLength: {ContentLength}, UiStateMs: {UiStateMs}, WatchMs: {WatchMs}, SessionPersistMs: {SessionPersistMs}, PreviewApplyMs: {PreviewApplyMs}, TotalElapsedMs: {TotalElapsedMs}, ViewMode: {ViewMode}",
                _document.FilePath,
                _document.Content.Length,
                uiStateStopwatch.ElapsedMilliseconds,
                watchStopwatch.ElapsedMilliseconds,
                sessionStopwatch.ElapsedMilliseconds,
                applyStopwatch.ElapsedMilliseconds,
                overallStopwatch.ElapsedMilliseconds,
                SelectedViewMode);
        }
        finally
        {
            _isLoadingDocument = false;
        }
    }

    private async Task ParseAndApplyPreviewAsync(string text)
    {
        var pipelineStopwatch = Stopwatch.StartNew();
        IReadOnlyList<MarkdownBlock> blocks;
        var parseStopwatch = Stopwatch.StartNew();
        try
        {
            blocks = _parser.Parse(text);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Markdown parsing failed. Falling back to plaintext paragraph rendering.");
            blocks = [new MarkdownBlock { Type = BlockType.Paragraph, Content = text }];

            if (SelectedViewMode == EditorViewMode.RichTextEditor)
            {
                SelectedViewMode = EditorViewMode.TextEditor;
            }

            await MainThread.InvokeOnMainThreadAsync(() =>
            {
                InlineErrorMessage = "Markdown parsing failed. The document is shown in a safe fallback mode.";
            });
        }
        finally
        {
            parseStopwatch.Stop();
        }

        var applyStopwatch = Stopwatch.StartNew();
        await MainThread.InvokeOnMainThreadAsync(() =>
        {
            ParsedBlocks = blocks;
            DocumentApplied?.Invoke(this, new MarkdownDocument
            {
                FilePath = _document.FilePath,
                FileName = _document.FileName,
                FileSizeBytes = _document.FileSizeBytes,
                LastModified = _document.LastModified,
                Content = text,
                IsUntitled = _document.IsUntitled,
                EncodingName = _document.EncodingName,
                NewLine = _document.NewLine
            });
        });
        applyStopwatch.Stop();

        _logger.LogInformation(
            "Preview parse/apply completed. FilePath: {FilePath}, BlockCount: {BlockCount}, ParseElapsedMs: {ParseElapsedMs}, UiApplyElapsedMs: {UiApplyElapsedMs}, TotalElapsedMs: {TotalElapsedMs}, ViewMode: {ViewMode}",
            _document.FilePath,
            blocks.Count,
            parseStopwatch.ElapsedMilliseconds,
            applyStopwatch.ElapsedMilliseconds,
            pipelineStopwatch.ElapsedMilliseconds,
            SelectedViewMode);
    }

    private void ScheduleParse()
    {
        _parseCancellationSource?.Cancel();
        _parseCancellationSource?.Dispose();
        _parseCancellationSource = new CancellationTokenSource();
        var token = _parseCancellationSource.Token;

        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(IsViewerMode ? ViewerParseDebounceDelay : EditorParseDebounceDelay, token);
                token.ThrowIfCancellationRequested();
                await ParseAndApplyPreviewAsync(_document.Content);
            }
            catch (OperationCanceledException)
            {
            }
        }, token);
    }

    private void ScheduleAutoSave()
    {
        _autosaveCancellationSource?.Cancel();
        _autosaveCancellationSource?.Dispose();

        if (!_preferences.AutoSaveEnabled || _document.IsUntitled || !_document.IsDirty || string.IsNullOrWhiteSpace(_document.FilePath))
        {
            return;
        }

        _autosaveCancellationSource = new CancellationTokenSource();
        var token = _autosaveCancellationSource.Token;
        var delay = TimeSpan.FromSeconds(_preferences.AutoSaveDelaySeconds);

        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(delay, token);
                token.ThrowIfCancellationRequested();
                await MainThread.InvokeOnMainThreadAsync(async () => await SaveCurrentDocumentInternalAsync(forceSaveAs: false));
            }
            catch (OperationCanceledException)
            {
            }
        }, token);
    }

    private async void OnWatchedDocumentChanged(object? sender, string filePath)
    {
        if (_document.IsUntitled || string.IsNullOrWhiteSpace(_document.FilePath) || !string.Equals(filePath, _document.FilePath, StringComparison.Ordinal))
        {
            return;
        }

        if (_isSavingDocument || DateTimeOffset.UtcNow - _lastSaveUtc < TimeSpan.FromSeconds(1))
        {
            return;
        }

        try
        {
            await Task.Delay(ExternalChangeDebounceDelay);

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

        parts.Add(_document.IsUntitled ? "Untitled" : "Saved file");

        if (_document.IsDirty)
        {
            parts.Add("Unsaved changes");
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
    }

    private void ToggleWorkspacePanel()
    {
        IsWorkspacePanelVisible = !IsWorkspacePanelVisible;
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
            await LoadWorkspaceAsync(folderPath);
            await ClearInlineErrorAsync();
        }
        catch (Exception ex)
        {
            await ReportErrorAsync("Failed to open the selected folder.", ex, "The selected folder could not be opened.");
        }
    }

    private async Task LoadWorkspaceAsync(string folderPath, string? selectedPath = null, string? renamePath = null, bool persistSessionState = true)
    {
        if (string.IsNullOrWhiteSpace(folderPath))
        {
            return;
        }

        var tree = await _workspaceBrowserService.LoadWorkspaceTreeAsync(folderPath);
        var rootItems = tree.Select(info => BuildWorkspaceItem(info, 0, null)).ToList();

        await MainThread.InvokeOnMainThreadAsync(() =>
        {
            WorkspaceRootPath = folderPath;
            _workspaceRootItems.Clear();
            _workspaceRootItems.AddRange(rootItems);
        });

        await RefreshWorkspaceItemsAsync();

        if (!string.IsNullOrWhiteSpace(selectedPath))
        {
            await MainThread.InvokeOnMainThreadAsync(() => SetSelectedWorkspaceItem(FindWorkspaceItem(selectedPath)));
        }

        if (!string.IsNullOrWhiteSpace(renamePath))
        {
            await MainThread.InvokeOnMainThreadAsync(() =>
            {
                var itemToRename = FindWorkspaceItem(renamePath);
                if (itemToRename is not null)
                {
                    BeginRenameWorkspaceItem(itemToRename);
                }
            });
        }

        if (persistSessionState)
        {
            PersistSessionState();
        }
    }

    private WorkspaceTreeItem BuildWorkspaceItem(WorkspaceNodeInfo info, int depth, WorkspaceTreeItem? parent)
    {
        var item = new WorkspaceTreeItem(info.FullPath, info.IsDirectory, depth, parent);
        foreach (var child in info.Children)
        {
            item.Children.Add(BuildWorkspaceItem(child, depth + 1, item));
        }

        return item;
    }

    private async Task RefreshWorkspaceItemsAsync()
    {
        _workspaceSearchCancellationSource?.Cancel();
        _workspaceSearchCancellationSource?.Dispose();
        _workspaceSearchCancellationSource = new CancellationTokenSource();
        var cancellationToken = _workspaceSearchCancellationSource.Token;

        try
        {
            var visibleItems = string.IsNullOrWhiteSpace(WorkspaceSearchText)
                ? BuildExpandedWorkspaceItems()
                : await BuildSearchedWorkspaceItemsAsync(WorkspaceSearchText.Trim(), cancellationToken);

            await MainThread.InvokeOnMainThreadAsync(() =>
            {
                WorkspaceItems.Clear();
                foreach (var item in visibleItems)
                {
                    WorkspaceItems.Add(item);
                }
            });
        }
        catch (OperationCanceledException)
        {
        }
    }

    private List<WorkspaceTreeItem> BuildExpandedWorkspaceItems()
    {
        var visibleItems = new List<WorkspaceTreeItem>();
        foreach (var item in _workspaceRootItems)
        {
            AppendExpandedWorkspaceItems(item, visibleItems);
        }

        return visibleItems;
    }

    private void AppendExpandedWorkspaceItems(WorkspaceTreeItem item, List<WorkspaceTreeItem> visibleItems)
    {
        visibleItems.Add(item);

        if (!item.IsDirectory || !item.IsExpanded)
        {
            return;
        }

        foreach (var child in item.Children)
        {
            AppendExpandedWorkspaceItems(child, visibleItems);
        }
    }

    private async Task<List<WorkspaceTreeItem>> BuildSearchedWorkspaceItemsAsync(string query, CancellationToken cancellationToken)
    {
        var visibleItems = new List<WorkspaceTreeItem>();

        foreach (var rootItem in _workspaceRootItems)
        {
            await AppendMatchingWorkspaceItemsAsync(rootItem, query, visibleItems, cancellationToken);
        }

        return visibleItems;
    }

    private async Task<bool> AppendMatchingWorkspaceItemsAsync(WorkspaceTreeItem item, string query, List<WorkspaceTreeItem> visibleItems, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var matchingChildren = new List<WorkspaceTreeItem>();
        foreach (var child in item.Children)
        {
            await AppendMatchingWorkspaceItemsAsync(child, query, matchingChildren, cancellationToken);
        }

        var selfMatches = item.Name.Contains(query, StringComparison.OrdinalIgnoreCase);
        if (!selfMatches && !item.IsDirectory)
        {
            selfMatches = await _workspaceBrowserService.FileContainsTextAsync(item.FullPath, query, cancellationToken);
        }

        if (!selfMatches && matchingChildren.Count == 0)
        {
            return false;
        }

        visibleItems.Add(item);
        visibleItems.AddRange(matchingChildren);
        return true;
    }

    private async Task SelectWorkspaceItemAsync(WorkspaceTreeItem? item)
    {
        if (item is null)
        {
            return;
        }

        await MainThread.InvokeOnMainThreadAsync(() => SetSelectedWorkspaceItem(item));

        if (item.IsDirectory)
        {
            return;
        }

        await OpenDocumentAsync(
            () => _documentService.LoadDocumentAsync(item.FullPath),
            "Failed to open markdown file from the workspace tree.",
            "The selected file could not be opened.");
    }

    private void ToggleWorkspaceItemExpansion(WorkspaceTreeItem? item)
    {
        if (item is null || !item.IsDirectory)
        {
            return;
        }

        item.IsExpanded = !item.IsExpanded;
        _ = RefreshWorkspaceItemsAsync();
    }

    private void BeginRenameWorkspaceItem(WorkspaceTreeItem? item)
    {
        if (item is null || !item.CanRename)
        {
            return;
        }

        if (PendingRenameItem is not null && !ReferenceEquals(PendingRenameItem, item))
        {
            PendingRenameItem.IsRenaming = false;
            PendingRenameItem.ResetRenameText();
        }

        item.ResetRenameText();
        item.IsRenaming = true;
        PendingRenameItem = item;
        SetSelectedWorkspaceItem(item);
    }

    private async Task CreateMdsAsync()
    {
        if (string.IsNullOrWhiteSpace(WorkspaceRootPath))
        {
            return;
        }

        var targetDirectory = SelectedWorkspaceDirectoryPath;

        try
        {
            WorkspaceSearchText = string.Empty;
            var createdFilePath = await _workspaceBrowserService.CreateMarkdownSharpFileAsync(targetDirectory);
            await LoadWorkspaceAsync(WorkspaceRootPath, selectedPath: createdFilePath, renamePath: createdFilePath);
            await ClearInlineErrorAsync();
        }
        catch (Exception ex)
        {
            await ReportErrorAsync("Failed to create a new markdown_sharp file.", ex, "The new markdown_sharp file could not be created.");
        }
    }

    private string SelectedWorkspaceDirectoryPath =>
        _selectedWorkspaceItem is null
            ? WorkspaceRootPath
            : _selectedWorkspaceItem.IsDirectory
                ? _selectedWorkspaceItem.FullPath
                : Path.GetDirectoryName(_selectedWorkspaceItem.FullPath) ?? WorkspaceRootPath;

    private void SetSelectedWorkspaceItem(WorkspaceTreeItem? item)
    {
        if (ReferenceEquals(_selectedWorkspaceItem, item))
        {
            return;
        }

        if (_selectedWorkspaceItem is not null)
        {
            _selectedWorkspaceItem.IsSelected = false;
        }

        _selectedWorkspaceItem = item;

        if (_selectedWorkspaceItem is not null)
        {
            _selectedWorkspaceItem.IsSelected = true;
        }
    }

    private WorkspaceTreeItem? FindWorkspaceItem(string fullPath)
    {
        foreach (var rootItem in _workspaceRootItems)
        {
            var match = FindWorkspaceItemRecursive(rootItem, fullPath);
            if (match is not null)
            {
                return match;
            }
        }

        return null;
    }

    private static WorkspaceTreeItem? FindWorkspaceItemRecursive(WorkspaceTreeItem item, string fullPath)
    {
        if (string.Equals(item.FullPath, fullPath, StringComparison.Ordinal))
        {
            return item;
        }

        foreach (var child in item.Children)
        {
            var match = FindWorkspaceItemRecursive(child, fullPath);
            if (match is not null)
            {
                return match;
            }
        }

        return null;
    }

    private async Task RestoreSessionAsync()
    {
        var stopwatch = Stopwatch.StartNew();
        try
        {
            IsWorkspacePanelVisible = _sessionState.IsWorkspacePanelVisible;

            var restoredWorkspacePath = ResolveWorkspaceRestorePath(out var workspaceRepickMessage);
            var hasWorkspaceAccess = !string.IsNullOrWhiteSpace(restoredWorkspacePath) && Directory.Exists(restoredWorkspacePath);
            if (hasWorkspaceAccess)
            {
                await TryRestoreWorkspaceAsync(restoredWorkspacePath);
            }

            var documentRestore = await TryRestoreSessionDocumentAsync(hasWorkspaceAccess);
            if (documentRestore.Document is not null)
            {
                await LoadDocumentIntoStateAsync(documentRestore.Document);
            }
            else
            {
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

            SelectedViewMode = _sessionState.LastViewMode;
            if (documentRestore.Document is not null || string.IsNullOrWhiteSpace(_sessionState.DocumentFilePath))
            {
                PersistSessionState();
            }
            else
            {
                _sessionState.LastViewMode = SelectedViewMode;
                _sessionState.IsWorkspacePanelVisible = IsWorkspacePanelVisible;
                _sessionStateService.Save(_sessionState);
            }
            _logger.LogInformation("Restored previous session. WorkspaceRootPath: {WorkspaceRootPath}, DocumentFilePath: {DocumentFilePath}, ViewMode: {ViewMode}, ElapsedMs: {ElapsedMs}",
                _sessionState.WorkspaceRootPath,
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

    private async Task<(MarkdownDocument? Document, string? RepickMessage)> TryRestoreSessionDocumentAsync(bool hasWorkspaceAccess)
    {
        var filePath = ResolveDocumentRestorePath(hasWorkspaceAccess, out var needsRepick);
        _logger.LogInformation(
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
                return (null, "The previous markdown file needs permission again. Please use Open to re-pick it.");
            }

            return (null, null);
        }

        try
        {
            return (await _documentService.LoadDocumentAsync(filePath), null);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Unable to restore previous session document {FilePath}", filePath);
            return (null, "The previous markdown file could not be reopened. Please use Open to re-pick it.");
        }
    }

    private void PersistSessionState()
    {
        _sessionState = new SessionState
        {
            WorkspaceRootPath = string.IsNullOrWhiteSpace(WorkspaceRootPath) ? null : WorkspaceRootPath,
            WorkspaceRootBookmark = string.IsNullOrWhiteSpace(WorkspaceRootPath) ? null : _workspaceBrowserService.TryCreatePersistentAccessBookmark(WorkspaceRootPath),
            DocumentFilePath = !IsUntitled && !string.IsNullOrWhiteSpace(FilePath) ? FilePath : null,
            DocumentFileBookmark = !IsUntitled && !string.IsNullOrWhiteSpace(FilePath) ? _documentService.TryCreatePersistentAccessBookmark(FilePath) : null,
            LastViewMode = SelectedViewMode,
            IsWorkspacePanelVisible = IsWorkspacePanelVisible
        };

        _sessionStateService.Save(_sessionState);
        _logger.LogInformation(
            "Persisted session state. WorkspaceRootPath: {WorkspaceRootPath}, DocumentFilePath: {DocumentFilePath}, HasWorkspaceBookmark: {HasWorkspaceBookmark}, HasDocumentBookmark: {HasDocumentBookmark}, ViewMode: {ViewMode}",
            _sessionState.WorkspaceRootPath,
            _sessionState.DocumentFilePath,
            !string.IsNullOrWhiteSpace(_sessionState.WorkspaceRootBookmark),
            !string.IsNullOrWhiteSpace(_sessionState.DocumentFileBookmark),
            _sessionState.LastViewMode);
    }

    private void RememberOpenedDocument(MarkdownDocument document)
    {
        if (document.IsUntitled || string.IsNullOrWhiteSpace(document.FilePath))
        {
            return;
        }

        _sessionState.DocumentFilePath = document.FilePath;
        _sessionState.DocumentFileBookmark = _documentService.TryCreatePersistentAccessBookmark(document.FilePath);
        _sessionState.LastViewMode = SelectedViewMode;
        _sessionState.IsWorkspacePanelVisible = IsWorkspacePanelVisible;
        _sessionStateService.Save(_sessionState);

        _logger.LogInformation(
            "Remembered opened document for session restore. DocumentFilePath: {DocumentFilePath}, HasDocumentBookmark: {HasDocumentBookmark}",
            _sessionState.DocumentFilePath,
            !string.IsNullOrWhiteSpace(_sessionState.DocumentFileBookmark));
    }

    private async Task TryRestoreWorkspaceAsync(string workspaceRootPath)
    {
        var stopwatch = Stopwatch.StartNew();

        try
        {
            _ = Directory.EnumerateFileSystemEntries(workspaceRootPath).Take(1).Any();
            await LoadWorkspaceAsync(workspaceRootPath, persistSessionState: false);
            _logger.LogInformation("Restored workspace from session. WorkspaceRootPath: {WorkspaceRootPath}, ElapsedMs: {ElapsedMs}",
                workspaceRootPath,
                stopwatch.ElapsedMilliseconds);
        }
        catch (UnauthorizedAccessException ex)
        {
            _logger.LogWarning(ex, "Skipping workspace restore because access was denied. WorkspaceRootPath: {WorkspaceRootPath}, ElapsedMs: {ElapsedMs}",
                workspaceRootPath,
                stopwatch.ElapsedMilliseconds);

            WorkspaceRootPath = string.Empty;
            _workspaceRootItems.Clear();
            WorkspaceItems.Clear();
        }
    }

    private string? ResolveWorkspaceRestorePath(out string? repickMessage)
    {
        repickMessage = null;

#if MACCATALYST
        if (!string.IsNullOrWhiteSpace(_sessionState.WorkspaceRootBookmark))
        {
            if (_workspaceBrowserService.TryRestorePersistentAccessFromBookmark(_sessionState.WorkspaceRootBookmark, out var restoredPath, out var isStale) &&
                !string.IsNullOrWhiteSpace(restoredPath))
            {
                if (isStale)
                {
                    _logger.LogWarning("Workspace bookmark resolved but is stale. WorkspaceRootPath: {WorkspaceRootPath}", restoredPath);
                }

                return restoredPath;
            }

            repickMessage = "The previous workspace folder needs permission again. Please use Open Folder to re-pick it.";
            return null;
        }

        return null;
#else
        return _sessionState.WorkspaceRootPath;
#endif
    }

    private string? ResolveDocumentRestorePath(bool hasWorkspaceAccess, out bool needsRepick)
    {
        needsRepick = false;

#if MACCATALYST
        if (!string.IsNullOrWhiteSpace(_sessionState.DocumentFileBookmark))
        {
            if (_documentService.TryRestorePersistentAccessFromBookmark(_sessionState.DocumentFileBookmark, out var restoredPath, out var isStale) &&
                !string.IsNullOrWhiteSpace(restoredPath))
            {
                if (isStale)
                {
                    _logger.LogWarning("Document bookmark resolved but is stale. DocumentFilePath: {DocumentFilePath}", restoredPath);
                }

                return restoredPath;
            }

            needsRepick = true;
            return null;
        }

        if (hasWorkspaceAccess)
        {
            return _sessionState.DocumentFilePath;
        }

        return null;
#else
        return _sessionState.DocumentFilePath;
#endif
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
}
