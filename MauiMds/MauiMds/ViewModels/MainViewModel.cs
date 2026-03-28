using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using MauiMds.Models;
using MauiMds.Processors;
using MauiMds.Services;
using Microsoft.Extensions.Logging;

namespace MauiMds.ViewModels;

public class MainViewModel : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;
    public event EventHandler<MarkdownDocument>? DocumentApplied;

    private readonly IMarkdownDocumentService _documentService;
    private readonly IWorkspaceBrowserService _workspaceBrowserService;
    private readonly ILogger<MainViewModel> _logger;
    private readonly MdsParser _parser;
    private readonly List<WorkspaceTreeItem> _workspaceRootItems = [];
    private bool _isInitialized;
    private bool _isOpeningDocument;
    private bool _isWorkspacePanelVisible;
    private string _filePath = string.Empty;
    private string _inlineErrorMessage = string.Empty;
    private string _workspaceRootPath = string.Empty;
    private string _workspaceSearchText = string.Empty;
    private WorkspaceTreeItem? _selectedWorkspaceItem;
    private WorkspaceTreeItem? _pendingRenameItem;
    private CancellationTokenSource? _workspaceSearchCancellationSource;

    public MarkdownBlockCollection ParsedBlocks { get; } = new();
    public ObservableCollection<WorkspaceTreeItem> WorkspaceItems { get; } = [];

    public ICommand OpenFileCommand { get; }
    public ICommand ToggleWorkspacePanelCommand { get; }
    public ICommand OpenFolderCommand { get; }
    public ICommand SelectWorkspaceItemCommand { get; }
    public ICommand ToggleWorkspaceItemExpansionCommand { get; }
    public ICommand BeginRenameWorkspaceItemCommand { get; }
    public ICommand CreateMdsCommand { get; }

    public string FilePath
    {
        get => _filePath;
        private set
        {
            if (_filePath == value)
            {
                return;
            }

            _filePath = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(FileName));
        }
    }

    public string FileName => string.IsNullOrWhiteSpace(FilePath) ? "No file loaded" : Path.GetFileName(FilePath);

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

    public MainViewModel(
        MdsParser parser,
        IMarkdownDocumentService documentService,
        IWorkspaceBrowserService workspaceBrowserService,
        ILogger<MainViewModel> logger)
    {
        _parser = parser;
        _documentService = documentService;
        _workspaceBrowserService = workspaceBrowserService;
        _logger = logger;
        _logger.LogInformation("MainViewModel created.");
        OpenFileCommand = new Command(async () => await OpenFileAsync(), () => !_isOpeningDocument);
        ToggleWorkspacePanelCommand = new Command(ToggleWorkspacePanel);
        OpenFolderCommand = new Command(async () => await OpenFolderAsync());
        SelectWorkspaceItemCommand = new Command<WorkspaceTreeItem>(async item => await SelectWorkspaceItemAsync(item));
        ToggleWorkspaceItemExpansionCommand = new Command<WorkspaceTreeItem>(ToggleWorkspaceItemExpansion);
        BeginRenameWorkspaceItemCommand = new Command<WorkspaceTreeItem>(BeginRenameWorkspaceItem);
        CreateMdsCommand = new Command(async () => await CreateMdsAsync(), () => HasWorkspaceRoot);
    }

    public async Task InitializeAsync()
    {
        if (_isInitialized)
        {
            return;
        }

        _isInitialized = true;

        try
        {
            var document = await _documentService.LoadInitialDocumentAsync();
            if (document is not null)
            {
                await ApplyDocumentAsync(document);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load the initial markdown document.");
            await MainThread.InvokeOnMainThreadAsync(() =>
            {
                FilePath = "Unable to load example.mds";
                ParsedBlocks.ReplaceAll([]);
                InlineErrorMessage = "The bundled example document could not be loaded.";
            });
        }
    }

    private async Task OpenFileAsync()
    {
        if (_isOpeningDocument)
        {
            return;
        }

        _isOpeningDocument = true;
        (OpenFileCommand as Command)?.ChangeCanExecute();

        try
        {
            var document = await _documentService.PickDocumentAsync();
            if (document is null)
            {
                await ClearInlineError();
                return;
            }

            await ApplyDocumentAsync(document);
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogWarning(ex, "The selected file could not be opened because it is not a supported markdown format.");
            await MainThread.InvokeOnMainThreadAsync(() =>
            {
                InlineErrorMessage = ex.Message;
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to open the selected markdown document.");
            await MainThread.InvokeOnMainThreadAsync(() =>
            {
                InlineErrorMessage = "The selected file could not be opened.";
            });
        }
        finally
        {
            _isOpeningDocument = false;
            (OpenFileCommand as Command)?.ChangeCanExecute();
        }
    }

    private async Task ApplyDocumentAsync(MarkdownDocument document)
    {
        _logger.LogDebug(
            "Applying markdown document. FileName: {FileName}, FilePath: {FilePath}, SizeKB: {SizeKb:F2}, LastModified: {LastModified}, CharacterCount: {CharacterCount}",
            document.FileName ?? Path.GetFileName(document.FilePath),
            document.FilePath,
            (document.FileSizeBytes ?? 0) / 1024d,
            document.LastModified,
            document.Content.Length);

        var blocks = _parser.Parse(document.Content);

        await MainThread.InvokeOnMainThreadAsync(() =>
        {
            InlineErrorMessage = string.Empty;
            ParsedBlocks.ReplaceAll(blocks);
            FilePath = document.FilePath;
            _logger.LogDebug(
                "Applied markdown document to the UI. DisplayedFilePath: {DisplayedFilePath}, BlockCount: {BlockCount}",
                FilePath,
                ParsedBlocks.Count);
            DocumentApplied?.Invoke(this, document);
        });

        _logger.LogInformation(
            "Loaded markdown document successfully. FileName: {FileName}, BlockCount: {BlockCount}",
            document.FileName ?? Path.GetFileName(document.FilePath),
            blocks.Count);
    }

    protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        _logger.LogDebug("Property changed: {PropertyName}", propertyName);
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        if (propertyName == nameof(HasWorkspaceRoot))
        {
            (CreateMdsCommand as Command)?.ChangeCanExecute();
        }
    }

    private Task ClearInlineError()
    {
        return MainThread.InvokeOnMainThreadAsync(() =>
        {
            InlineErrorMessage = string.Empty;
        });
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
                var renamedDocument = await _workspaceBrowserService.LoadDocumentAsync(updatedPath);
                await ApplyDocumentAsync(renamedDocument);
            }

            await LoadWorkspaceAsync(WorkspaceRootPath, selectedPath: updatedPath);
            await ClearInlineError();
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogWarning(ex, "Workspace file rename failed.");
            await MainThread.InvokeOnMainThreadAsync(() =>
            {
                item.IsRenaming = true;
                InlineErrorMessage = ex.Message;
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

    private void ToggleWorkspacePanel()
    {
        IsWorkspacePanelVisible = !IsWorkspacePanelVisible;
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
            await ClearInlineError();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to open the selected folder.");
            await MainThread.InvokeOnMainThreadAsync(() =>
            {
                InlineErrorMessage = "The selected folder could not be opened.";
            });
        }
    }

    private async Task LoadWorkspaceAsync(string folderPath, string? selectedPath = null, string? renamePath = null)
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
            await MainThread.InvokeOnMainThreadAsync(() =>
            {
                SetSelectedWorkspaceItem(FindWorkspaceItem(selectedPath));
            });
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

        await MainThread.InvokeOnMainThreadAsync(() =>
        {
            SetSelectedWorkspaceItem(item);
        });

        if (item.IsDirectory)
        {
            return;
        }

        try
        {
            var document = await _workspaceBrowserService.LoadDocumentAsync(item.FullPath);
            await ApplyDocumentAsync(document);
            await ClearInlineError();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to open markdown file from the workspace tree.");
            await MainThread.InvokeOnMainThreadAsync(() =>
            {
                InlineErrorMessage = "The selected file could not be opened.";
            });
        }
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
            await ClearInlineError();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create a new markdown_sharp file.");
            await MainThread.InvokeOnMainThreadAsync(() =>
            {
                InlineErrorMessage = "The new markdown_sharp file could not be created.";
            });
        }
    }

    private string SelectedWorkspaceDirectoryPath
    {
        get
        {
            if (_selectedWorkspaceItem is null)
            {
                return WorkspaceRootPath;
            }

            return _selectedWorkspaceItem.IsDirectory
                ? _selectedWorkspaceItem.FullPath
                : Path.GetDirectoryName(_selectedWorkspaceItem.FullPath) ?? WorkspaceRootPath;
        }
    }

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
}
