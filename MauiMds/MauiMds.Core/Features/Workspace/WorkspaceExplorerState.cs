using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using MauiMds.Models;
using MauiMds.Services;
using Microsoft.Extensions.Logging;

namespace MauiMds.Features.Workspace;

public sealed class WorkspaceExplorerState : INotifyPropertyChanged
{
    private readonly IWorkspaceBrowserService _workspaceBrowserService;
    private readonly IMainThreadDispatcher _dispatcher;
    private readonly ILogger<WorkspaceExplorerState> _logger;
    private readonly List<WorkspaceTreeItem> _workspaceRootItems = [];

    private string _workspaceRootPath = string.Empty;
    private string _currentWorkspaceFolderPath = string.Empty;
    private string _workspaceSearchText = string.Empty;
    private WorkspaceTreeItem? _selectedWorkspaceItem;
    private WorkspaceTreeItem? _pendingRenameItem;
    private CancellationTokenSource? _workspaceSearchCancellationSource;

    public WorkspaceExplorerState(
        IWorkspaceBrowserService workspaceBrowserService,
        IMainThreadDispatcher dispatcher,
        ILogger<WorkspaceExplorerState> logger)
    {
        _workspaceBrowserService = workspaceBrowserService;
        _dispatcher = dispatcher;
        _logger = logger;
        WorkspaceItems = new ObservableCollection<WorkspaceTreeItem>();
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public ObservableCollection<WorkspaceTreeItem> WorkspaceItems { get; }

    public string WorkspaceRootPath
    {
        get => _workspaceRootPath;
        private set
        {
            if (_workspaceRootPath == value) return;
            _workspaceRootPath = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(HasWorkspaceRoot));
        }
    }

    public bool HasWorkspaceRoot => !string.IsNullOrWhiteSpace(WorkspaceRootPath);

    public string CurrentWorkspaceFolderPath
    {
        get => _currentWorkspaceFolderPath;
        private set
        {
            if (_currentWorkspaceFolderPath == value) return;
            _currentWorkspaceFolderPath = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(CurrentWorkspaceFolderName));
            OnPropertyChanged(nameof(CurrentWorkspaceFolderDisplay));
            OnPropertyChanged(nameof(CanNavigateUpWorkspace));
            OnPropertyChanged(nameof(CanSetCurrentFolderAsWorkspace));
        }
    }

    public string CurrentWorkspaceFolderName => string.IsNullOrWhiteSpace(CurrentWorkspaceFolderPath) ? "(none)" : Path.GetFileName(CurrentWorkspaceFolderPath.TrimEnd(Path.DirectorySeparatorChar)) switch
    {
        "" => CurrentWorkspaceFolderPath,
        var name => name
    };

    public string CurrentWorkspaceFolderDisplay => string.IsNullOrWhiteSpace(CurrentWorkspaceFolderPath) ? "No current folder" : CurrentWorkspaceFolderPath;

    public bool CanNavigateUpWorkspace =>
        !string.IsNullOrWhiteSpace(CurrentWorkspaceFolderPath) &&
        !string.IsNullOrWhiteSpace(WorkspaceRootPath) &&
        !string.Equals(CurrentWorkspaceFolderPath, WorkspaceRootPath, StringComparison.Ordinal);

    public bool CanSetCurrentFolderAsWorkspace =>
        !string.IsNullOrWhiteSpace(CurrentWorkspaceFolderPath) &&
        !string.IsNullOrWhiteSpace(WorkspaceRootPath) &&
        !string.Equals(CurrentWorkspaceFolderPath, WorkspaceRootPath, StringComparison.Ordinal);

    public string WorkspaceSearchText
    {
        get => _workspaceSearchText;
        set
        {
            if (_workspaceSearchText == value) return;
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
            if (_pendingRenameItem == value) return;
            _pendingRenameItem = value;
            OnPropertyChanged();
        }
    }

    public string SelectedWorkspaceDirectoryPath =>
        !string.IsNullOrWhiteSpace(CurrentWorkspaceFolderPath)
            ? CurrentWorkspaceFolderPath
            : WorkspaceRootPath;

    public async Task LoadWorkspaceAsync(string folderPath, string? currentFolderPath = null, string? selectedPath = null)
    {
        if (string.IsNullOrWhiteSpace(folderPath)) return;

        var tree = await _workspaceBrowserService.LoadWorkspaceTreeAsync(folderPath);
        var rootItems = tree.Select(info => BuildWorkspaceItem(info, 0, null)).ToList();

        await _dispatcher.InvokeOnMainThreadAsync(() =>
        {
            WorkspaceRootPath = folderPath;
            _workspaceRootItems.Clear();
            _workspaceRootItems.AddRange(rootItems);
            CurrentWorkspaceFolderPath = ResolveCurrentWorkspaceFolderPath(folderPath, currentFolderPath);
            SetSelectedWorkspaceItem(string.IsNullOrWhiteSpace(selectedPath) ? null : FindWorkspaceItem(selectedPath));
        });

        await RefreshWorkspaceItemsAsync();
    }

    public async Task RefreshWorkspaceItemsAsync()
    {
        _workspaceSearchCancellationSource?.Cancel();
        _workspaceSearchCancellationSource?.Dispose();
        _workspaceSearchCancellationSource = new CancellationTokenSource();
        var cancellationToken = _workspaceSearchCancellationSource.Token;

        try
        {
            var visibleItems = string.IsNullOrWhiteSpace(WorkspaceSearchText)
                ? BuildCurrentFolderItems()
                : await BuildSearchedWorkspaceItemsAsync(WorkspaceSearchText.Trim(), cancellationToken);

            await _dispatcher.InvokeOnMainThreadAsync(() =>
            {
                WorkspaceItems.Clear();
                foreach (var item in visibleItems)
                    WorkspaceItems.Add(item);
            });
        }
        catch (OperationCanceledException)
        {
        }
    }

    public void SelectItem(WorkspaceTreeItem? item)
    {
        if (item is null) return;
        SetSelectedWorkspaceItem(item);
    }

    public async Task NavigateToItemAsync(WorkspaceTreeItem? item)
    {
        if (item is null) return;
        SetSelectedWorkspaceItem(item);

        if (item.IsDirectory)
        {
            SetSelectedWorkspaceItem(null);
            await LoadWorkspaceAsync(WorkspaceRootPath, currentFolderPath: item.FullPath);
        }
    }

    public async Task NavigateUpAsync()
    {
        if (!CanNavigateUpWorkspace) return;

        var parentDirectory = Path.GetDirectoryName(CurrentWorkspaceFolderPath.TrimEnd(Path.DirectorySeparatorChar));
        if (string.IsNullOrWhiteSpace(parentDirectory)) return;

        if (!parentDirectory.StartsWith(WorkspaceRootPath, StringComparison.Ordinal))
            parentDirectory = WorkspaceRootPath;

        SetSelectedWorkspaceItem(null);
        await LoadWorkspaceAsync(WorkspaceRootPath, currentFolderPath: parentDirectory);
    }

    public async Task ReloadFromDiskAsync()
    {
        if (string.IsNullOrWhiteSpace(WorkspaceRootPath)) return;
        var tree = await _workspaceBrowserService.LoadWorkspaceTreeAsync(WorkspaceRootPath);
        var rootItems = tree.Select(info => BuildWorkspaceItem(info, 0, null)).ToList();
        await _dispatcher.InvokeOnMainThreadAsync(() =>
        {
            _workspaceRootItems.Clear();
            _workspaceRootItems.AddRange(rootItems);
        });
        await RefreshWorkspaceItemsAsync();
    }

    public async Task SetWorkspaceFolderToCurrentAsync()
    {
        if (!CanSetCurrentFolderAsWorkspace) return;
        var newWorkspaceRoot = CurrentWorkspaceFolderPath;
        WorkspaceSearchText = string.Empty;
        await LoadWorkspaceAsync(newWorkspaceRoot, currentFolderPath: newWorkspaceRoot);
    }

    public void BeginRename(WorkspaceTreeItem? item)
    {
        if (item is null || !item.CanRename) return;

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

    public void CancelRename(WorkspaceTreeItem? item)
    {
        if (item is null) return;
        item.IsRenaming = false;
        item.ResetRenameText();
        PendingRenameItem = null;
    }

    public void MarkRenameCommitted() => PendingRenameItem = null;

    public WorkspaceTreeItem? FindWorkspaceItem(string fullPath)
    {
        foreach (var rootItem in _workspaceRootItems)
        {
            var match = FindWorkspaceItemRecursive(rootItem, fullPath);
            if (match is not null) return match;
        }
        return null;
    }

    public void Clear()
    {
        WorkspaceRootPath = string.Empty;
        CurrentWorkspaceFolderPath = string.Empty;
        _workspaceRootItems.Clear();
        WorkspaceItems.Clear();
        PendingRenameItem = null;
        SetSelectedWorkspaceItem(null);
    }

    private WorkspaceTreeItem BuildWorkspaceItem(WorkspaceNodeInfo info, int depth, WorkspaceTreeItem? parent)
    {
        var item = new WorkspaceTreeItem(info.FullPath, info.IsDirectory, depth, parent, info.RecordingGroup);
        foreach (var child in info.Children)
            item.Children.Add(BuildWorkspaceItem(child, depth + 1, item));
        return item;
    }

    private List<WorkspaceTreeItem> BuildCurrentFolderItems()
    {
        if (string.IsNullOrWhiteSpace(CurrentWorkspaceFolderPath) || string.Equals(CurrentWorkspaceFolderPath, WorkspaceRootPath, StringComparison.Ordinal))
        {
            return _workspaceRootItems
                .OrderBy(item => item.IsDirectory ? 0 : 1)
                .ThenBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        var currentFolder = FindWorkspaceItem(CurrentWorkspaceFolderPath);
        if (currentFolder is null) return [];

        return currentFolder.Children
            .OrderBy(item => item.IsDirectory ? 0 : 1)
            .ThenBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private async Task<List<WorkspaceTreeItem>> BuildSearchedWorkspaceItemsAsync(string query, CancellationToken cancellationToken)
    {
        var visibleItems = new List<WorkspaceTreeItem>();
        var searchRoots = string.Equals(CurrentWorkspaceFolderPath, WorkspaceRootPath, StringComparison.Ordinal) || string.IsNullOrWhiteSpace(CurrentWorkspaceFolderPath)
            ? _workspaceRootItems
            : FindWorkspaceItem(CurrentWorkspaceFolderPath) is { } currentFolder
                ? currentFolder.Children
                : (IEnumerable<WorkspaceTreeItem>)[];

        foreach (var rootItem in searchRoots)
            await AppendMatchingWorkspaceItemsAsync(rootItem, query, visibleItems, cancellationToken);

        return visibleItems;
    }

    private async Task<bool> AppendMatchingWorkspaceItemsAsync(WorkspaceTreeItem item, string query, List<WorkspaceTreeItem> visibleItems, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var matchingChildren = new List<WorkspaceTreeItem>();
        foreach (var child in item.Children)
            await AppendMatchingWorkspaceItemsAsync(child, query, matchingChildren, cancellationToken);

        var selfMatches = item.Name.Contains(query, StringComparison.OrdinalIgnoreCase);
        if (!selfMatches && !item.IsDirectory)
            selfMatches = await _workspaceBrowserService.FileContainsTextAsync(item.FullPath, query, cancellationToken);

        if (!selfMatches && matchingChildren.Count == 0) return false;

        visibleItems.Add(item);
        visibleItems.AddRange(matchingChildren);
        return true;
    }

    private void SetSelectedWorkspaceItem(WorkspaceTreeItem? item)
    {
        if (ReferenceEquals(_selectedWorkspaceItem, item)) return;

        if (_selectedWorkspaceItem is not null)
            _selectedWorkspaceItem.IsSelected = false;

        _selectedWorkspaceItem = item;

        if (_selectedWorkspaceItem is not null)
            _selectedWorkspaceItem.IsSelected = true;
    }

    private string ResolveCurrentWorkspaceFolderPath(string workspaceRootPath, string? currentFolderPath)
    {
        if (string.IsNullOrWhiteSpace(workspaceRootPath)) return string.Empty;

        if (string.IsNullOrWhiteSpace(currentFolderPath) || !Directory.Exists(currentFolderPath))
            return workspaceRootPath;

        if (!currentFolderPath.StartsWith(workspaceRootPath, StringComparison.Ordinal))
            return workspaceRootPath;

        return currentFolderPath;
    }

    private static WorkspaceTreeItem? FindWorkspaceItemRecursive(WorkspaceTreeItem item, string fullPath)
    {
        if (string.Equals(item.FullPath, fullPath, StringComparison.Ordinal)) return item;

        foreach (var child in item.Children)
        {
            var match = FindWorkspaceItemRecursive(child, fullPath);
            if (match is not null) return match;
        }
        return null;
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
