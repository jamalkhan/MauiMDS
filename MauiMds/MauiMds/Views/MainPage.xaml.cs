using MauiMds;
using System.ComponentModel;
using MauiMds.Controls;
using MauiMds.Models;
using MauiMds.Services;
using MauiMds.ViewModels;
using Microsoft.Extensions.Logging;
#if MACCATALYST
using CoreGraphics;
using Foundation;
using UIKit;
#endif

namespace MauiMds.Views;

public partial class MainPage : ContentPage
{
    // Sanity limit on search queries; longer strings are almost certainly a user error.
    private const int FindMaxQueryLength = 200;

    private readonly ILogger<MainPage> _logger;
    private readonly WorkspacePaneController _workspacePaneController;
    private readonly LogsDockController _logsDockController;
#if MACCATALYST
    private UIPointerInteraction? _workspaceResizePointerInteraction;
    private UIPointerInteraction? _historyResizePointerInteraction;
#endif

    public MainPage(MainViewModel vm, ISnackbarService snackbarService, ILogger<MainPage> logger)
    {
        _logger = logger;
        _logger.LogDebug("Constructing MainPage.");

        try
        {
            InitializeComponent();
            BindingContext = vm;

            _workspacePaneController = new WorkspacePaneController(
                this,
                width => WorkspaceExplorer.SetPaneWidth(width),
                (isVisible, opacity) => WorkspaceExplorer.SetPanelState(isVisible, opacity));

            _logsDockController = new LogsDockController(
                this,
                (height, historyVisible, resizeVisible) => LogsDock.ApplyHistoryPaneState(height, historyVisible, resizeVisible),
                () => LogsDock.RefreshState());

            LogsDock.BindToSnackbar(snackbarService, GetPreferredTimeFormat, () => _logsDockController.CurrentHeight);

            vm.PropertyChanged += OnViewModelPropertyChanged;
            vm.EditorActionRequested += OnEditorActionRequested;
            vm.FindRequested += OnFindRequested;

            WorkspaceExplorer.ResizePanUpdated += OnWorkspaceResizePanUpdated;
            WorkspaceExplorer.ResizePointerEntered += OnResizeHandlePointerEntered;
            WorkspaceExplorer.ResizePointerExited += OnResizeHandlePointerExited;
            WorkspaceExplorer.RenameCompleted += OnWorkspaceRenameCompleted;
            WorkspaceExplorer.RenameUnfocused += OnWorkspaceRenameUnfocused;

            LogsDock.SnackbarTapped += OnSnackbarTapped;
            LogsDock.ResizePanUpdated += OnHistoryResizePanUpdated;
            LogsDock.ResizePointerEntered += OnResizeHandlePointerEntered;
            LogsDock.ResizePointerExited += OnResizeHandlePointerExited;

            Loaded += OnLoaded;
            SizeChanged += OnPageSizeChanged;
            _logger.LogDebug("MainPage initialized successfully.");

            HandlerChanging += OnPageHandlerChanging;
        }
        catch (Exception ex)
        {
            _logger.LogCritical(ex, "MainPage initialization failed.");
            throw;
        }
    }

    private void OnPageHandlerChanging(object? sender, HandlerChangingEventArgs args)
    {
        // When the native handler is removed (app quitting), unsubscribe all
        // managed event handlers so they cannot fire during UIKit teardown.
        // An unhandled exception inside _traitCollectionDidChange: causes SIGABRT.
        if (args.NewHandler is null && BindingContext is MainViewModel vm)
        {
            vm.PropertyChanged -= OnViewModelPropertyChanged;
            vm.EditorActionRequested -= OnEditorActionRequested;
            vm.FindRequested -= OnFindRequested;
            LogsDock.UnbindSnackbar();
        }
    }

    private void OnLoaded(object? sender, EventArgs e)
    {
        _logger.LogDebug("MainPage loaded.");

        if (BindingContext is MainViewModel vm)
        {
            RefreshHeader(vm);
            _ = InitializeViewModelAsync(vm);
        }
        else
        {
            _logger.LogWarning("MainPage loaded without a MainViewModel binding context.");
        }

        LogsDock.RenderInitial();
        RefreshWorkspacePaneState(initial: true);
        AttachResizePointerInteractions();
    }

    private async Task InitializeViewModelAsync(MainViewModel vm)
    {
        await vm.InitializeAsync();
        RefreshHeader(vm);
    }

    private async void OnEditorActionRequested(object? sender, EditorActionRequestedEventArgs e)
    {
        var editor = GetActiveEditor();
        if (editor is null)
        {
            _logger.LogInformation("Editor action ignored — no active editor surface. ViewMode: {ViewMode}", (BindingContext as MainViewModel)?.SelectedViewMode);
            return;
        }
        await e.Action(editor);
    }

    private async void OnFindRequested(object? sender, EventArgs e)
    {
        var editor = GetActiveEditor();
        if (editor is null) return;
        await HandleFindAsync(editor);
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (App.IsTerminating) return;
        if (BindingContext is not MainViewModel vm)
            return;

        if (e.PropertyName is nameof(MainViewModel.FilePath)
            or nameof(MainViewModel.FileName)
            or nameof(MainViewModel.HeaderPathDisplay)
            or nameof(MainViewModel.StatusText)
            or nameof(MainViewModel.HasInlineError)
            or nameof(MainViewModel.InlineErrorMessage))
        {
            RefreshHeader(vm);
        }

        if (e.PropertyName is nameof(MainViewModel.IsWorkspacePanelVisible) or nameof(MainViewModel.WorkspacePanelWidth))
        {
            RefreshWorkspacePaneState(initial: false);
        }

        if (e.PropertyName == nameof(MainViewModel.PendingRenameItem) && vm.PendingRenameItem is not null)
        {
            WorkspaceExplorer.WorkspaceCollectionView.ScrollTo(vm.PendingRenameItem, position: ScrollToPosition.MakeVisible, animate: true);
        }
    }

    private async void OnSnackbarTapped(object? sender, TappedEventArgs e)
    {
        await _logsDockController.ToggleAsync(Height);
        if (_logsDockController.CurrentHeight > 0.5)
            await LogsDock.ScrollHistoryToBottomAsync();
    }

    private void OnHistoryResizePanUpdated(object? sender, PanUpdatedEventArgs e)
    {
        _logsDockController.HandleResizePan(e);
    }

    private void OnResizeHandlePointerEntered(object? sender, PointerEventArgs e)
    {
        if (sender is VisualElement element)
        {
            element.Opacity = 1;
            element.Scale = 1.02;
        }
    }

    private void OnResizeHandlePointerExited(object? sender, PointerEventArgs e)
    {
        if (sender is VisualElement element)
        {
            element.Opacity = 0.94;
            element.Scale = 1;
        }
    }

    private void OnPageSizeChanged(object? sender, EventArgs e)
    {
        _logsDockController.UpdateMaxHeight(Height);

#if MACCATALYST
        _workspaceResizePointerInteraction?.Invalidate();
        _historyResizePointerInteraction?.Invalidate();
#endif
    }

    private void OnWorkspaceResizePanUpdated(object? sender, PanUpdatedEventArgs e)
    {
        if (BindingContext is not MainViewModel vm)
            return;

        _workspacePaneController.HandleResizePan(
            e,
            vm.IsWorkspacePanelVisible,
            vm.WorkspacePanelWidth,
            vm.UpdateWorkspacePanelWidth);
    }

    private void RefreshWorkspacePaneState(bool initial)
    {
        if (BindingContext is not MainViewModel vm)
            return;

        _workspacePaneController.Refresh(initial, vm.IsWorkspacePanelVisible, vm.IsWorkspacePanelVisible ? vm.WorkspacePanelWidth : 0);
    }

    private void RefreshHeader(MainViewModel vm)
    {
        DocumentHeader.ApplyHeaderState(
            vm.FileName,
            vm.HeaderPathDisplay,
            vm.StatusText,
            vm.HasInlineError,
            vm.InlineErrorMessage);
    }

    private IEditorSurface? GetActiveEditor()
    {
        if (BindingContext is not MainViewModel vm)
            return null;

        if (vm.IsTextEditorMode)
            return ViewerHost.TextEditorSurface;

        if (vm.IsVisualEditorMode)
            return ViewerHost.VisualEditorSurface;

        return null;
    }

    private async Task HandleFindAsync(IEditorSurface editor)
    {
        var query = await DisplayPromptAsync("Find", "Find text in the current document:", "Find", "Cancel", maxLength: FindMaxQueryLength);
        if (string.IsNullOrWhiteSpace(query))
            return;

        if (!editor.FindNext(query))
            await DisplayAlertAsync("Find", $"No matches found for \"{query}\".", "OK");
    }

    private string GetPreferredTimeFormat() =>
        BindingContext is MainViewModel vm ? vm.Preferences.PreferredTimeFormat : "h:mm:ss tt";

    private void AttachResizePointerInteractions()
    {
#if MACCATALYST
        _workspaceResizePointerInteraction ??= AttachResizePointerInteraction(WorkspaceExplorer.ResizeHandleElement, UIAxis.Horizontal);
        _historyResizePointerInteraction ??= AttachResizePointerInteraction(LogsDock.ResizeHandleElement, UIAxis.Vertical);
#endif
    }

#if MACCATALYST
    private static UIPointerInteraction? AttachResizePointerInteraction(VisualElement element, UIAxis axis)
    {
        if (element.Handler?.PlatformView is not UIView nativeView)
        {
            element.HandlerChanged += OnHandlerChanged;
            return null;
        }

        nativeView.UserInteractionEnabled = true;
        var interaction = new UIPointerInteraction(new ResizePointerInteractionDelegate(nativeView, axis));
        nativeView.AddInteraction(interaction);
        return interaction;

        void OnHandlerChanged(object? sender, EventArgs e)
        {
            element.HandlerChanged -= OnHandlerChanged;
            AttachResizePointerInteraction(element, axis);
        }
    }

    private sealed class ResizePointerInteractionDelegate : UIPointerInteractionDelegate
    {
        private readonly WeakReference<UIView> _viewReference;
        private readonly UIAxis _axis;
        private readonly NSString _identifier;

        public ResizePointerInteractionDelegate(UIView view, UIAxis axis)
        {
            _viewReference = new WeakReference<UIView>(view);
            _axis = axis;
            _identifier = new NSString(axis == UIAxis.Horizontal ? "workspace-resize" : "history-resize");
        }

        public override UIPointerRegion? GetRegionForRequest(UIPointerInteraction interaction, UIPointerRegionRequest request, UIPointerRegion? defaultRegion)
        {
            if (!_viewReference.TryGetTarget(out var view))
                return defaultRegion;

            return UIPointerRegion.Create(view.Bounds, _identifier);
        }

        public override UIPointerStyle? GetStyleForRegion(UIPointerInteraction interaction, UIPointerRegion region)
        {
            if (!_viewReference.TryGetTarget(out var view))
                return UIPointerStyle.CreateSystemPointerStyle();

            var preferredLength = _axis == UIAxis.Horizontal
                ? Math.Max(18, view.Bounds.Height)
                : Math.Max(18, view.Bounds.Width);

            var shape = UIPointerShape.CreateBeam((nfloat)preferredLength, _axis);
            return UIPointerStyle.Create(shape, _axis);
        }
    }
#endif

    private async void OnWorkspaceRenameCompleted(object? sender, WorkspaceRenameEntryEventArgs e)
    {
        if (BindingContext is not MainViewModel vm)
            return;

        await vm.CommitWorkspaceRenameAsync(e.Item);
    }

    private async void OnWorkspaceRenameUnfocused(object? sender, WorkspaceRenameEntryEventArgs e)
    {
        if (BindingContext is not MainViewModel vm)
            return;

        await vm.CommitWorkspaceRenameAsync(e.Item);
    }
}
