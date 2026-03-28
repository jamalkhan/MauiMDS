using System.Collections.Specialized;
using System.ComponentModel;
using MauiMds.Models;
using MauiMds.Services;
using MauiMds.ViewModels;
using Microsoft.Extensions.Logging;

namespace MauiMds.Views;

public partial class MainPage : ContentPage
{
    private const string TrayAnimationName = "SnackbarTray";

    private readonly ILogger<MainPage> _logger;
    private readonly SnackbarService _snackbarService;
    private double _trayCurrentHeight;
    private double _trayMaxHeight;
    private double _trayPanStartHeight;

    public MainPage(MainViewModel vm, SnackbarService snackbarService, ILogger<MainPage> logger)
    {
        _logger = logger;
        _snackbarService = snackbarService;
        _logger.LogInformation("Constructing MainPage.");

        try
        {
            InitializeComponent();
            BindingContext = vm;
            vm.PropertyChanged += OnViewModelPropertyChanged;
            _snackbarService.PropertyChanged += OnSnackbarPropertyChanged;
            _snackbarService.History.CollectionChanged += OnSnackbarHistoryChanged;
            Loaded += OnLoaded;
            SizeChanged += OnPageSizeChanged;
            _logger.LogInformation("MainPage initialized successfully.");
        }
        catch (Exception ex)
        {
            _logger.LogCritical(ex, "MainPage initialization failed.");
            throw;
        }
    }

    private async void OnLoaded(object? sender, EventArgs e)
    {
        _logger.LogInformation("MainPage loaded.");

        if (BindingContext is MainViewModel vm)
        {
            await vm.InitializeAsync();
            RefreshHeader(vm);
        }
        else
        {
            _logger.LogWarning("MainPage loaded without a MainViewModel binding context.");
        }

        RefreshTrayState();
        RefreshSnackbar();
        RenderSnackbarHistory();
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (BindingContext is not MainViewModel vm)
        {
            return;
        }

        if (e.PropertyName is nameof(MainViewModel.FilePath) or nameof(MainViewModel.FileName))
        {
            RefreshHeader(vm);
        }
    }

    private void OnSnackbarPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(SnackbarService.CurrentMessage) or nameof(SnackbarService.HasCurrentMessage))
        {
            RefreshSnackbar();
        }
    }

    private void OnSnackbarHistoryChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        RenderSnackbarHistory();
        RefreshTrayState();
    }

    private async void OnTrayHandleTapped(object? sender, TappedEventArgs e)
    {
        await ToggleTrayAsync();
    }

    private async void OnSnackbarTapped(object? sender, TappedEventArgs e)
    {
        await OpenTrayAsync();
    }

    private void OnSnackbarDismissClicked(object? sender, EventArgs e)
    {
        _snackbarService.DismissVisibleAndPendingMessages();
    }

    private void OnTrayPanUpdated(object? sender, PanUpdatedEventArgs e)
    {
        switch (e.StatusType)
        {
            case GestureStatus.Started:
                this.AbortAnimation(TrayAnimationName);
                _trayPanStartHeight = _trayCurrentHeight;
                break;
            case GestureStatus.Running:
                SetTrayHeight(_trayPanStartHeight - e.TotalY);
                break;
            case GestureStatus.Canceled:
            case GestureStatus.Completed:
                var targetHeight = _trayCurrentHeight >= _trayMaxHeight * 0.35 ? _trayMaxHeight : 0;
                _ = AnimateTrayToAsync(targetHeight);
                break;
        }
    }

    private void OnPageSizeChanged(object? sender, EventArgs e)
    {
        _trayMaxHeight = Height * 0.3;
        if (_trayCurrentHeight > _trayMaxHeight)
        {
            SetTrayHeight(_trayMaxHeight);
        }
    }

    private void RefreshHeader(MainViewModel vm)
    {
        FileNameLabel.Text = vm.FileName;
        FilePathLabel.Text = vm.FilePath;

        _logger.LogDebug(
            "Header refreshed. FileName: {FileName}, FilePath: {FilePath}",
            FileNameLabel.Text,
            FilePathLabel.Text);
    }

    private void RefreshSnackbar()
    {
        var message = _snackbarService.CurrentMessage;
        SnackbarBorder.IsVisible = message is not null;

        if (message is null)
        {
            return;
        }

        SnackbarLevelLabel.Text = message.LevelLabel;
        SnackbarCategoryLabel.Text = message.Category;
        SnackbarTimeLabel.Text = message.Timestamp.ToLocalTime().ToString("h:mm:ss tt");
        SnackbarMessageLabel.Text = message.DisplayMessage;

        ApplySnackbarTheme(
            message.Level,
            SnackbarBorder,
            SnackbarAccent,
            SnackbarLevelLabel,
            SnackbarCategoryLabel,
            SnackbarTimeLabel,
            SnackbarMessageLabel,
            dismissButton: SnackbarDismissButton);
    }

    private void RenderSnackbarHistory()
    {
        SnackbarHistoryStack.Children.Clear();

        foreach (var message in _snackbarService.History)
        {
            SnackbarHistoryStack.Children.Add(CreateHistoryEntry(message));
        }

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

        levelLabel.Text = message.LevelLabel;
        categoryLabel.Text = message.Category;
        timeLabel.Text = message.Timestamp.ToLocalTime().ToString("MM/dd h:mm:ss tt");
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

    private async Task ToggleTrayAsync()
    {
        await AnimateTrayToAsync(_trayCurrentHeight > 0 ? 0 : _trayMaxHeight);
    }

    private async Task OpenTrayAsync()
    {
        await AnimateTrayToAsync(_trayMaxHeight);
    }

    private async Task AnimateTrayToAsync(double targetHeight)
    {
        targetHeight = Math.Clamp(targetHeight, 0, _trayMaxHeight);
        var startingHeight = _trayCurrentHeight;

        if (Math.Abs(startingHeight - targetHeight) < 0.5)
        {
            SetTrayHeight(targetHeight);
            return;
        }

        var completion = new TaskCompletionSource();
        var animation = new Animation(value => SetTrayHeight(value), startingHeight, targetHeight, Easing.CubicOut);
        animation.Commit(
            this,
            TrayAnimationName,
            16,
            220,
            Easing.CubicOut,
            (_, _) => completion.TrySetResult());

        await completion.Task;
    }

    private void SetTrayHeight(double requestedHeight)
    {
        _trayCurrentHeight = Math.Clamp(requestedHeight, 0, _trayMaxHeight);
        HistoryTrayBorder.HeightRequest = _trayCurrentHeight;
        HistoryTrayBorder.IsVisible = _trayCurrentHeight > 0.5;
        RefreshTrayState();
    }

    private void RefreshTrayState()
    {
        TrayStateLabel.Text = _trayCurrentHeight > 0.5
            ? "Slide down to hide"
            : "Slide up for history";

        HistoryCountLabel.Text = $"{_snackbarService.History.Count} / {_snackbarService.HistoryCapacity}";
    }

    private static SnackbarPalette GetPalette(SnackbarMessageLevel level)
    {
        return level switch
        {
            SnackbarMessageLevel.Debug => new SnackbarPalette(
                Color.FromArgb("#EEE8DD"),
                Color.FromArgb("#313234"),
                Color.FromArgb("#756B5B"),
                Color.FromArgb("#9B9388"),
                Color.FromArgb("#1C1A17"),
                Color.FromArgb("#EEE5D9"),
                Color.FromArgb("#645C53"),
                Color.FromArgb("#B8B1A6")),
            SnackbarMessageLevel.Info => new SnackbarPalette(
                Color.FromArgb("#F7F2E8"),
                Color.FromArgb("#2D2E30"),
                Color.FromArgb("#8D7F67"),
                Color.FromArgb("#CBBEA6"),
                Color.FromArgb("#161616"),
                Color.FromArgb("#F3EDE2"),
                Color.FromArgb("#5F584F"),
                Color.FromArgb("#BEB7AC")),
            SnackbarMessageLevel.Warning => new SnackbarPalette(
                Color.FromArgb("#FFF3C9"),
                Color.FromArgb("#43381B"),
                Color.FromArgb("#C79000"),
                Color.FromArgb("#FFD45C"),
                Color.FromArgb("#342600"),
                Color.FromArgb("#FFF4D2"),
                Color.FromArgb("#705B1F"),
                Color.FromArgb("#E8D39D")),
            _ => new SnackbarPalette(
                Color.FromArgb("#FBE0DD"),
                Color.FromArgb("#432524"),
                Color.FromArgb("#B42318"),
                Color.FromArgb("#FF8A7A"),
                Color.FromArgb("#3F0D07"),
                Color.FromArgb("#FFE7E4"),
                Color.FromArgb("#7D2E28"),
                Color.FromArgb("#F1B5AE"))
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
}
