using MauiMds;
using System.Collections.Specialized;
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
    private readonly SnackbarService _snackbarService;
    private readonly WorkspacePaneController _workspacePaneController;
    private readonly LogsDockController _logsDockController;
#if MACCATALYST
    private UIPointerInteraction? _workspaceResizePointerInteraction;
    private UIPointerInteraction? _historyResizePointerInteraction;
#endif

    public MainPage(MainViewModel vm, SnackbarService snackbarService, ILogger<MainPage> logger)
    {
        _logger = logger;
        _snackbarService = snackbarService;
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
                RefreshHistoryPaneState);

            vm.PropertyChanged += OnViewModelPropertyChanged;
            vm.EditorActionRequested += OnEditorActionRequested;
            vm.FindRequested += OnFindRequested;
            _snackbarService.PropertyChanged += OnSnackbarPropertyChanged;
            _snackbarService.History.CollectionChanged += OnSnackbarHistoryChanged;

            WorkspaceExplorer.ResizePanUpdated += OnWorkspaceResizePanUpdated;
            WorkspaceExplorer.ResizePointerEntered += OnResizeHandlePointerEntered;
            WorkspaceExplorer.ResizePointerExited += OnResizeHandlePointerExited;
            WorkspaceExplorer.RenameEntryLoaded += OnWorkspaceRenameEntryLoaded;
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
            _snackbarService.PropertyChanged -= OnSnackbarPropertyChanged;
            _snackbarService.History.CollectionChanged -= OnSnackbarHistoryChanged;
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

        RefreshHistoryPaneState();
        RefreshSnackbar();
        RenderSnackbarHistory();
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
        {
            return;
        }

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

    private void OnSnackbarPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (App.IsTerminating) return;
        if (e.PropertyName is nameof(SnackbarService.CurrentMessage) or nameof(SnackbarService.HasCurrentMessage))
        {
            RefreshSnackbar();
        }
    }

    private void OnSnackbarHistoryChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (App.IsTerminating) return;

        // Incremental updates — avoid the full Children.Clear() + rebuild on every
        // Add/Remove, which caused O(n) view creation per log message and pegged
        // the CPU when whisper logged hundreds of lines during transcription.
        switch (e.Action)
        {
            case NotifyCollectionChangedAction.Add when e.NewItems?.Count == 1:
                LogsDock.HistoryStack.Children.Add(CreateHistoryEntry((SnackbarMessage)e.NewItems[0]!));
                UpdateHistoryCountLabel();
                RefreshHistoryPaneState();
                RefreshSnackbar();
                break;

            case NotifyCollectionChangedAction.Remove when e.OldItems?.Count == 1:
                if (e.OldStartingIndex >= 0 && e.OldStartingIndex < LogsDock.HistoryStack.Children.Count)
                    LogsDock.HistoryStack.Children.RemoveAt(e.OldStartingIndex);
                UpdateHistoryCountLabel();
                break;

            default:
                RenderSnackbarHistory();
                RefreshHistoryPaneState();
                RefreshSnackbar();
                break;
        }
    }

    private void UpdateHistoryCountLabel() =>
        LogsDock.HistoryCountLabelControl.Text =
            $"{_snackbarService.History.Count} / {_snackbarService.HistoryCapacity}";

    private async void OnSnackbarTapped(object? sender, TappedEventArgs e)
    {
        await _logsDockController.ToggleAsync(Height);
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
        {
            return;
        }

        _workspacePaneController.HandleResizePan(
            e,
            vm.IsWorkspacePanelVisible,
            vm.WorkspacePanelWidth,
            vm.UpdateWorkspacePanelWidth);
    }

    private void RefreshWorkspacePaneState(bool initial)
    {
        if (BindingContext is not MainViewModel vm)
        {
            return;
        }

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
        {
            return null;
        }

        if (vm.IsTextEditorMode)
        {
            return ViewerHost.TextEditorSurface;
        }

        if (vm.IsVisualEditorMode)
        {
            return ViewerHost.VisualEditorSurface;
        }

        return null;
    }

    private async Task HandleFindAsync(IEditorSurface editor)
    {
        var query = await DisplayPromptAsync("Find", "Find text in the current document:", "Find", "Cancel", maxLength: FindMaxQueryLength);
        if (string.IsNullOrWhiteSpace(query))
        {
            return;
        }

        if (!editor.FindNext(query))
        {
            await DisplayAlertAsync("Find", $"No matches found for \"{query}\".", "OK");
        }
    }

    private void RefreshSnackbar()
    {
        var message = _snackbarService.History.Count > 0
            ? _snackbarService.History[_snackbarService.History.Count - 1]
            : null;

        var timeFormat = GetPreferredTimeFormat();

        LogsDock.SnackbarLevelLabelControl.Text = "Logs";
        LogsDock.SnackbarCategoryLabelControl.Text = _snackbarService.History.Count == 0
            ? "No recent events"
            : $"{_snackbarService.History.Count} message{(_snackbarService.History.Count == 1 ? string.Empty : "s")} in history";
        LogsDock.SnackbarTimeLabelControl.Text = message?.Timestamp.ToLocalTime().ToString(timeFormat) ?? string.Empty;
        LogsDock.HistoryStateLabelControl.Text = _logsDockController.CurrentHeight > 0.5 ? "Hide log history" : "Open log history";

        ApplySnackbarTheme(
            message?.Level ?? SnackbarMessageLevel.Info,
            LogsDock.SnackbarBorderControl,
            LogsDock.SnackbarAccentControl,
            LogsDock.SnackbarLevelLabelControl,
            LogsDock.SnackbarCategoryLabelControl,
            LogsDock.SnackbarTimeLabelControl,
            LogsDock.HistoryStateLabelControl);
    }

    private void RenderSnackbarHistory()
    {
        LogsDock.HistoryStack.Children.Clear();

        foreach (var message in _snackbarService.History)
        {
            LogsDock.HistoryStack.Children.Add(CreateHistoryEntry(message));
        }

        LogsDock.HistoryCountLabelControl.Text = $"{_snackbarService.History.Count} / {_snackbarService.HistoryCapacity}";
    }

    private View CreateHistoryEntry(SnackbarMessage message)
    {
        var border = new Border
        {
            StrokeThickness = 0,
            Padding = new Thickness(12),
            StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle
            {
                CornerRadius = new CornerRadius(14)
            }
        };

        var accent = new BoxView
        {
            WidthRequest = 5,
            CornerRadius = 3,
            VerticalOptions = LayoutOptions.Fill
        };

        var levelLabel = new Label
        {
            FontAttributes = FontAttributes.Bold,
            FontSize = 12
        };

        var categoryLabel = new Label
        {
            FontSize = 12,
            LineBreakMode = LineBreakMode.TailTruncation
        };

        var timeLabel = new Label
        {
            FontSize = 11
        };

        var messageLabel = new Label
        {
            FontSize = 13,
            LineBreakMode = LineBreakMode.WordWrap
        };

        var detailLabel = new Label
        {
            FontSize = 11,
            LineBreakMode = LineBreakMode.WordWrap,
            IsVisible = !string.IsNullOrWhiteSpace(message.ExceptionMessage)
        };

        var timeFormat = GetPreferredTimeFormat();
        levelLabel.Text = message.LevelLabel;
        categoryLabel.Text = message.Category;
        timeLabel.Text = message.Timestamp.ToLocalTime().ToString($"MM/dd {timeFormat}");
        messageLabel.Text = message.Message;
        detailLabel.Text = message.ExceptionMessage;

        ApplySnackbarTheme(
            message.Level,
            border,
            accent,
            levelLabel,
            categoryLabel,
            timeLabel,
            messageLabel,
            detailLabel);

        var headerGrid = new Grid
        {
            ColumnDefinitions =
            {
                new ColumnDefinition(GridLength.Auto),
                new ColumnDefinition(GridLength.Star),
                new ColumnDefinition(GridLength.Auto)
            },
            ColumnSpacing = 10
        };
        headerGrid.Children.Add(levelLabel);
        headerGrid.Children.Add(categoryLabel);
        headerGrid.Children.Add(timeLabel);
        Grid.SetColumn(categoryLabel, 1);
        Grid.SetColumn(timeLabel, 2);

        var contentGrid = new Grid
        {
            RowDefinitions =
            {
                new RowDefinition(GridLength.Auto),
                new RowDefinition(GridLength.Auto),
                new RowDefinition(GridLength.Auto)
            },
            RowSpacing = 2
        };
        contentGrid.Children.Add(headerGrid);
        contentGrid.Children.Add(messageLabel);
        contentGrid.Children.Add(detailLabel);
        Grid.SetRow(messageLabel, 1);
        Grid.SetRow(detailLabel, 2);

        var layoutGrid = new Grid
        {
            ColumnDefinitions =
            {
                new ColumnDefinition(GridLength.Auto),
                new ColumnDefinition(GridLength.Star)
            },
            ColumnSpacing = 12
        };
        layoutGrid.Children.Add(accent);
        layoutGrid.Children.Add(contentGrid);
        Grid.SetColumn(contentGrid, 1);

        border.Content = layoutGrid;
        return border;
    }

    private void ApplySnackbarTheme(
        SnackbarMessageLevel level,
        Border container,
        BoxView accent,
        Label levelLabel,
        Label categoryLabel,
        Label timeLabel,
        Label messageLabel,
        Label? detailLabel = null,
        Button? dismissButton = null)
    {
        var palette = GetPalette(level);

        container.SetAppThemeColor(VisualElement.BackgroundColorProperty, palette.LightBackground, palette.DarkBackground);
        accent.SetAppThemeColor(BoxView.ColorProperty, palette.LightAccent, palette.DarkAccent);
        levelLabel.SetAppThemeColor(Label.TextColorProperty, palette.LightAccent, palette.DarkAccent);
        categoryLabel.SetAppThemeColor(Label.TextColorProperty, palette.LightSubtleText, palette.DarkSubtleText);
        timeLabel.SetAppThemeColor(Label.TextColorProperty, palette.LightSubtleText, palette.DarkSubtleText);
        messageLabel.SetAppThemeColor(Label.TextColorProperty, palette.LightText, palette.DarkText);

        if (detailLabel is not null)
        {
            detailLabel.SetAppThemeColor(Label.TextColorProperty, palette.LightSubtleText, palette.DarkSubtleText);
        }

        if (dismissButton is not null)
        {
            dismissButton.SetAppThemeColor(Button.BackgroundColorProperty, palette.LightAccent, palette.DarkAccent);
            dismissButton.SetAppThemeColor(Button.TextColorProperty, palette.LightBackground, palette.DarkBackground);
        }
    }

    private void RefreshHistoryPaneState()
    {
        LogsDock.HistoryCountLabelControl.Text = $"{_snackbarService.History.Count} / {_snackbarService.HistoryCapacity}";
        LogsDock.HistoryStateLabelControl.Text = _logsDockController.CurrentHeight > 0.5
            ? "Hide log history"
            : "Open log history";
    }

    private string GetPreferredTimeFormat()
    {
        return BindingContext is MainViewModel vm ? vm.Preferences.PreferredTimeFormat : "h:mm:ss tt";
    }

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
            {
                return defaultRegion;
            }

            return UIPointerRegion.Create(view.Bounds, _identifier);
        }

        public override UIPointerStyle? GetStyleForRegion(UIPointerInteraction interaction, UIPointerRegion region)
        {
            if (!_viewReference.TryGetTarget(out var view))
            {
                return UIPointerStyle.CreateSystemPointerStyle();
            }

            var preferredLength = _axis == UIAxis.Horizontal
                ? Math.Max(18, view.Bounds.Height)
                : Math.Max(18, view.Bounds.Width);

            var shape = UIPointerShape.CreateBeam((nfloat)preferredLength, _axis);
            return UIPointerStyle.Create(shape, _axis);
        }
    }
#endif

    private static SnackbarPalette GetPalette(SnackbarMessageLevel level)
    {
        return level switch
        {
            SnackbarMessageLevel.Debug => new SnackbarPalette(
                AppColors.SnackDebugBgLight,
                AppColors.SnackDebugBgDark,
                AppColors.SnackDebugAccentLight,
                AppColors.SnackDebugAccentDark,
                AppColors.SnackDebugTextLight,
                AppColors.SnackDebugTextDark,
                AppColors.SnackDebugSubLight,
                AppColors.SnackDebugSubDark),
            SnackbarMessageLevel.Info => new SnackbarPalette(
                AppColors.SnackInfoBgLight,
                AppColors.SnackInfoBgDark,
                AppColors.SnackInfoAccentLight,
                AppColors.SnackInfoAccentDark,
                AppColors.SnackInfoTextLight,
                AppColors.SnackInfoTextDark,
                AppColors.SnackInfoSubLight,
                AppColors.SnackInfoSubDark),
            SnackbarMessageLevel.Warning => new SnackbarPalette(
                AppColors.SnackWarnBgLight,
                AppColors.SnackWarnBgDark,
                AppColors.SnackWarnAccentLight,
                AppColors.SnackWarnAccentDark,
                AppColors.SnackWarnTextLight,
                AppColors.SnackWarnTextDark,
                AppColors.SnackWarnSubLight,
                AppColors.SnackWarnSubDark),
            _ => new SnackbarPalette(
                AppColors.SnackErrorBgLight,
                AppColors.SnackErrorBgDark,
                AppColors.SnackErrorAccentLight,
                AppColors.SnackErrorAccentDark,
                AppColors.SnackErrorTextLight,
                AppColors.SnackErrorTextDark,
                AppColors.SnackErrorSubLight,
                AppColors.SnackErrorSubDark)
        };
    }

    private sealed record SnackbarPalette(
        Color LightBackground,
        Color DarkBackground,
        Color LightAccent,
        Color DarkAccent,
        Color LightText,
        Color DarkText,
        Color LightSubtleText,
        Color DarkSubtleText);

    private async void OnWorkspaceRenameCompleted(object? sender, WorkspaceRenameEntryEventArgs e)
    {
        if (BindingContext is not MainViewModel vm)
        {
            return;
        }

        await vm.CommitWorkspaceRenameAsync(e.Item);
    }

    private async void OnWorkspaceRenameUnfocused(object? sender, WorkspaceRenameEntryEventArgs e)
    {
        if (BindingContext is not MainViewModel vm)
        {
            return;
        }

        await vm.CommitWorkspaceRenameAsync(e.Item);
    }

    private void OnWorkspaceRenameEntryLoaded(object? sender, WorkspaceRenameEntryEventArgs e)
    {
        if (BindingContext is not MainViewModel vm)
        {
            return;
        }

        if (!ReferenceEquals(vm.PendingRenameItem, e.Item))
        {
            return;
        }

        MainThread.BeginInvokeOnMainThread(async () =>
        {
            e.Entry.Focus();
            // Give the native first-responder cycle time to settle before
            // applying the selection; without this Mac Catalyst resets it.
            await Task.Delay(80);
            e.Entry.CursorPosition = 0;
            e.Entry.SelectionLength = e.Entry.Text?.Length ?? 0;
        });
    }
}
