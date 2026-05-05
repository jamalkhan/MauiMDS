using MauiMds.Models;
using MauiMds.ViewModels;

namespace MauiMds.Views;

public partial class WorkspaceExplorerView : ContentView
{
    public event EventHandler<PanUpdatedEventArgs>? ResizePanUpdated;
    public event EventHandler<PointerEventArgs>? ResizePointerEntered;
    public event EventHandler<PointerEventArgs>? ResizePointerExited;
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

    private void OnResizePanUpdated(object? sender, PanUpdatedEventArgs e) => ResizePanUpdated?.Invoke(this, e);
    private void OnResizePointerEntered(object? sender, PointerEventArgs e) => ResizePointerEntered?.Invoke(WorkspaceResizeHandle, e);
    private void OnResizePointerExited(object? sender, PointerEventArgs e) => ResizePointerExited?.Invoke(WorkspaceResizeHandle, e);

    private void OnWorkspaceRenameEntryLoaded(object? sender, EventArgs e)
    {
        if (sender is not Entry entry || entry.BindingContext is not WorkspaceTreeItem item)
            return;

        if (BindingContext is MainViewModel vm && !ReferenceEquals(vm.PendingRenameItem, item))
            return;

        MainThread.BeginInvokeOnMainThread(async () =>
        {
            entry.Focus();
            // Give the native first-responder cycle time to settle before
            // applying the selection; without this Mac Catalyst resets it.
            await Task.Delay(80);
            entry.CursorPosition = 0;
            entry.SelectionLength = entry.Text?.Length ?? 0;
        });
    }

    private void OnWorkspaceRenameCompleted(object? sender, EventArgs e)
    {
        if (sender is Entry entry && entry.BindingContext is WorkspaceTreeItem item)
            RenameCompleted?.Invoke(this, new WorkspaceRenameEntryEventArgs(entry, item));
    }

    private void OnWorkspaceRenameUnfocused(object? sender, FocusEventArgs e)
    {
        if (sender is Entry entry && entry.BindingContext is WorkspaceTreeItem item)
            RenameUnfocused?.Invoke(this, new WorkspaceRenameEntryEventArgs(entry, item));
    }
}

public sealed record WorkspaceRenameEntryEventArgs(Entry Entry, WorkspaceTreeItem Item);
