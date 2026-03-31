using MauiMds.Models;

namespace MauiMds.Views;

public partial class WorkspaceExplorerView : ContentView
{
    public event EventHandler<PanUpdatedEventArgs>? ResizePanUpdated;
    public event EventHandler<PointerEventArgs>? ResizePointerEntered;
    public event EventHandler<PointerEventArgs>? ResizePointerExited;
    public event EventHandler<WorkspaceRenameEntryEventArgs>? RenameEntryLoaded;
    public event EventHandler<WorkspaceRenameEntryEventArgs>? RenameCompleted;
    public event EventHandler<WorkspaceRenameEntryEventArgs>? RenameUnfocused;

    public WorkspaceExplorerView()
    {
        InitializeComponent();
    }

    public CollectionView WorkspaceCollectionView => WorkspaceCollectionViewControl;
    public VisualElement ResizeHandleElement => WorkspaceResizeHandle;

    public void SetPaneWidth(double width)
    {
        WorkspacePaneHost.WidthRequest = width;
    }

    public void SetPanelState(bool isVisible, double opacity)
    {
        WorkspacePanelBorder.IsVisible = isVisible;
        WorkspacePanelBorder.Opacity = opacity;
        WorkspaceResizeHandle.IsVisible = isVisible;
    }

    private void OnResizePanUpdated(object? sender, PanUpdatedEventArgs e)
    {
        ResizePanUpdated?.Invoke(this, e);
    }

    private void OnResizePointerEntered(object? sender, PointerEventArgs e)
    {
        ResizePointerEntered?.Invoke(WorkspaceResizeHandle, e);
    }

    private void OnResizePointerExited(object? sender, PointerEventArgs e)
    {
        ResizePointerExited?.Invoke(WorkspaceResizeHandle, e);
    }

    private void OnWorkspaceRenameEntryLoaded(object? sender, EventArgs e)
    {
        if (sender is Entry entry && entry.BindingContext is WorkspaceTreeItem item)
        {
            RenameEntryLoaded?.Invoke(this, new WorkspaceRenameEntryEventArgs(entry, item));
        }
    }

    private void OnWorkspaceRenameCompleted(object? sender, EventArgs e)
    {
        if (sender is Entry entry && entry.BindingContext is WorkspaceTreeItem item)
        {
            RenameCompleted?.Invoke(this, new WorkspaceRenameEntryEventArgs(entry, item));
        }
    }

    private void OnWorkspaceRenameUnfocused(object? sender, FocusEventArgs e)
    {
        if (sender is Entry entry && entry.BindingContext is WorkspaceTreeItem item)
        {
            RenameUnfocused?.Invoke(this, new WorkspaceRenameEntryEventArgs(entry, item));
        }
    }
}

public sealed record WorkspaceRenameEntryEventArgs(Entry Entry, WorkspaceTreeItem Item);
