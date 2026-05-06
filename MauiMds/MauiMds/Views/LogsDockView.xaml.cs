using MauiMds;
using System.Collections.Specialized;
using System.ComponentModel;
using MauiMds.Models;
using MauiMds.Services;

namespace MauiMds.Views;

public partial class LogsDockView : ContentView
{
    public event EventHandler<TappedEventArgs>? SnackbarTapped;
    public event EventHandler<PanUpdatedEventArgs>? ResizePanUpdated;
    public event EventHandler<PointerEventArgs>? ResizePointerEntered;
    public event EventHandler<PointerEventArgs>? ResizePointerExited;

    private ISnackbarService? _snackbarService;
    private Func<string>? _getTimeFormat;
    private Func<double>? _getHistoryHeight;

    public LogsDockView()
    {
        InitializeComponent();
    }

    public VisualElement ResizeHandleElement => HistoryResizeHandle;

    public Task ScrollHistoryToBottomAsync()
        => HistoryScrollView.ScrollToAsync(0, double.MaxValue, animated: false);

    public void ApplyHistoryPaneState(double height, bool historyVisible, bool resizeVisible)
    {
        HistoryTrayBorder.HeightRequest = height;
        HistoryTrayBorder.IsVisible = historyVisible;
        HistoryResizeHandle.IsVisible = resizeVisible;
    }

    public void BindToSnackbar(ISnackbarService snackbarService, Func<string> getTimeFormat, Func<double> getHistoryHeight)
    {
        _snackbarService = snackbarService;
        _getTimeFormat = getTimeFormat;
        _getHistoryHeight = getHistoryHeight;

        snackbarService.PropertyChanged += OnSnackbarPropertyChanged;
        snackbarService.History.CollectionChanged += OnSnackbarHistoryChanged;
    }

    public void UnbindSnackbar()
    {
        if (_snackbarService is null) return;
        _snackbarService.PropertyChanged -= OnSnackbarPropertyChanged;
        _snackbarService.History.CollectionChanged -= OnSnackbarHistoryChanged;
    }

    public void RenderInitial()
    {
        RefreshSnackbar();
        RenderSnackbarHistory();
        RefreshState();
    }

    public void RefreshState()
    {
        if (_snackbarService is null) return;
        HistoryCountLabel.Text = $"{_snackbarService.History.Count} / {_snackbarService.HistoryCapacity}";
        HistoryStateLabel.Text = (_getHistoryHeight?.Invoke() ?? 0) > 0.5
            ? "Hide log history"
            : "Open log history";
    }

    private void OnSnackbarPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (App.IsTerminating) return;
        if (e.PropertyName is nameof(SnackbarService.CurrentMessage) or nameof(SnackbarService.HasCurrentMessage))
            RefreshSnackbar();
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
                SnackbarHistoryStack.Children.Add(CreateHistoryEntry((SnackbarMessage)e.NewItems[0]!));
                HistoryCountLabel.Text = $"{_snackbarService!.History.Count} / {_snackbarService.HistoryCapacity}";
                RefreshState();
                RefreshSnackbar();
                break;

            case NotifyCollectionChangedAction.Remove when e.OldItems?.Count == 1:
                if (e.OldStartingIndex >= 0 && e.OldStartingIndex < SnackbarHistoryStack.Children.Count)
                    SnackbarHistoryStack.Children.RemoveAt(e.OldStartingIndex);
                HistoryCountLabel.Text = $"{_snackbarService!.History.Count} / {_snackbarService.HistoryCapacity}";
                break;

            default:
                RenderSnackbarHistory();
                RefreshState();
                RefreshSnackbar();
                break;
        }
    }

    private void RefreshSnackbar()
    {
        if (_snackbarService is null) return;

        var message = _snackbarService.History.Count > 0
            ? _snackbarService.History[_snackbarService.History.Count - 1]
            : null;

        var timeFormat = _getTimeFormat?.Invoke() ?? "h:mm:ss tt";

        SnackbarLevelLabel.Text = "Logs";
        SnackbarCategoryLabel.Text = _snackbarService.History.Count == 0
            ? "No recent events"
            : $"{_snackbarService.History.Count} message{(_snackbarService.History.Count == 1 ? string.Empty : "s")} in history";
        SnackbarTimeLabel.Text = message?.Timestamp.ToLocalTime().ToString(timeFormat) ?? string.Empty;
        HistoryStateLabel.Text = (_getHistoryHeight?.Invoke() ?? 0) > 0.5 ? "Hide log history" : "Open log history";

        ApplySnackbarTheme(
            message?.Level ?? SnackbarMessageLevel.Info,
            SnackbarBorder,
            SnackbarAccent,
            SnackbarLevelLabel,
            SnackbarCategoryLabel,
            SnackbarTimeLabel,
            HistoryStateLabel);
    }

    private void RenderSnackbarHistory()
    {
        if (_snackbarService is null) return;

        SnackbarHistoryStack.Children.Clear();
        foreach (var message in _snackbarService.History)
            SnackbarHistoryStack.Children.Add(CreateHistoryEntry(message));
        HistoryCountLabel.Text = $"{_snackbarService.History.Count} / {_snackbarService.HistoryCapacity}";
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

        var levelLabel = new Label { FontAttributes = FontAttributes.Bold, FontSize = 12 };
        var categoryLabel = new Label { FontSize = 12, LineBreakMode = LineBreakMode.TailTruncation };
        var timeLabel = new Label { FontSize = 11 };
        var messageLabel = new Label { FontSize = 13, LineBreakMode = LineBreakMode.WordWrap };
        var detailLabel = new Label
        {
            FontSize = 11,
            LineBreakMode = LineBreakMode.WordWrap,
            IsVisible = !string.IsNullOrWhiteSpace(message.ExceptionMessage)
        };

        var timeFormat = _getTimeFormat?.Invoke() ?? "h:mm:ss tt";
        levelLabel.Text = message.LevelLabel;
        categoryLabel.Text = message.Category;
        timeLabel.Text = message.Timestamp.ToLocalTime().ToString($"MM/dd {timeFormat}");
        messageLabel.Text = message.Message;
        detailLabel.Text = message.ExceptionMessage;

        ApplySnackbarTheme(message.Level, border, accent, levelLabel, categoryLabel, timeLabel, messageLabel, detailLabel);

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

    private static void ApplySnackbarTheme(
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
            detailLabel.SetAppThemeColor(Label.TextColorProperty, palette.LightSubtleText, palette.DarkSubtleText);

        if (dismissButton is not null)
        {
            dismissButton.SetAppThemeColor(Button.BackgroundColorProperty, palette.LightAccent, palette.DarkAccent);
            dismissButton.SetAppThemeColor(Button.TextColorProperty, palette.LightBackground, palette.DarkBackground);
        }
    }

    private static SnackbarPalette GetPalette(SnackbarMessageLevel level) =>
        level switch
        {
            SnackbarMessageLevel.Debug => new SnackbarPalette(
                AppColors.SnackDebugBgLight, AppColors.SnackDebugBgDark,
                AppColors.SnackDebugAccentLight, AppColors.SnackDebugAccentDark,
                AppColors.SnackDebugTextLight, AppColors.SnackDebugTextDark,
                AppColors.SnackDebugSubLight, AppColors.SnackDebugSubDark),
            SnackbarMessageLevel.Info => new SnackbarPalette(
                AppColors.SnackInfoBgLight, AppColors.SnackInfoBgDark,
                AppColors.SnackInfoAccentLight, AppColors.SnackInfoAccentDark,
                AppColors.SnackInfoTextLight, AppColors.SnackInfoTextDark,
                AppColors.SnackInfoSubLight, AppColors.SnackInfoSubDark),
            SnackbarMessageLevel.Warning => new SnackbarPalette(
                AppColors.SnackWarnBgLight, AppColors.SnackWarnBgDark,
                AppColors.SnackWarnAccentLight, AppColors.SnackWarnAccentDark,
                AppColors.SnackWarnTextLight, AppColors.SnackWarnTextDark,
                AppColors.SnackWarnSubLight, AppColors.SnackWarnSubDark),
            _ => new SnackbarPalette(
                AppColors.SnackErrorBgLight, AppColors.SnackErrorBgDark,
                AppColors.SnackErrorAccentLight, AppColors.SnackErrorAccentDark,
                AppColors.SnackErrorTextLight, AppColors.SnackErrorTextDark,
                AppColors.SnackErrorSubLight, AppColors.SnackErrorSubDark)
        };

    private sealed record SnackbarPalette(
        Color LightBackground,
        Color DarkBackground,
        Color LightAccent,
        Color DarkAccent,
        Color LightText,
        Color DarkText,
        Color LightSubtleText,
        Color DarkSubtleText);

    private void OnSnackbarTapped(object? sender, TappedEventArgs e) => SnackbarTapped?.Invoke(this, e);
    private void OnResizePanUpdated(object? sender, PanUpdatedEventArgs e) => ResizePanUpdated?.Invoke(this, e);
    private void OnResizePointerEntered(object? sender, PointerEventArgs e) => ResizePointerEntered?.Invoke(HistoryResizeHandle, e);
    private void OnResizePointerExited(object? sender, PointerEventArgs e) => ResizePointerExited?.Invoke(HistoryResizeHandle, e);
}
