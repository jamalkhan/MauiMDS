using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using MauiMds.Models;
using MauiMds.Services;
using MauiMds.ViewModels;
using Microsoft.Maui.Controls.Shapes;
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
            vm.DocumentApplied += OnDocumentApplied;
            vm.ParsedBlocks.CollectionChanged += OnParsedBlocksChanged;
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
            RenderMarkdown(vm.ParsedBlocks);
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

    private void OnDocumentApplied(object? sender, MarkdownDocument document)
    {
        if (BindingContext is not MainViewModel vm)
        {
            return;
        }

        RefreshHeader(vm);
        RenderMarkdown(vm.ParsedBlocks);
        _logger.LogDebug(
            "Document applied notification received. FileName: {FileName}, DisplayedFilePath: {DisplayedFilePath}, ContentChildren: {ContentChildren}",
            document.FileName ?? vm.FileName,
            vm.FilePath,
            ContentStack.Children.Count);
    }

    private void OnParsedBlocksChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (BindingContext is MainViewModel vm)
        {
            RenderMarkdown(vm.ParsedBlocks);
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

    private void RenderMarkdown(ObservableCollection<MarkdownBlock> blocks)
    {
        _logger.LogDebug("Rendering markdown blocks. BlockCount: {BlockCount}", blocks.Count);
        ContentStack.Children.Clear();

        foreach (var block in blocks)
        {
            ContentStack.Children.Add(CreateMarkdownView(block));
        }

        _logger.LogDebug(
            "Rendered markdown blocks to the UI. ChildCount: {ChildCount}, FirstBlockPreview: {FirstBlockPreview}",
            ContentStack.Children.Count,
            blocks.FirstOrDefault()?.Content);
    }

    private View CreateMarkdownView(MarkdownBlock block)
    {
        return block.Type switch
        {
            BlockType.Header => CreateHeaderView(block),
            BlockType.Paragraph => CreateParagraphView(block.Content),
            BlockType.BulletListItem => CreateBulletView(block.Content),
            BlockType.BlockQuote => CreateBlockQuoteView(block.Content),
            BlockType.CodeBlock => CreateCodeBlockView(block),
            BlockType.Table => CreateTableView(block),
            _ => CreateParagraphView(block.Content)
        };
    }

    private Label CreateHeaderView(MarkdownBlock block)
    {
        var label = CreateBaseLabel();
        label.Text = block.Content;
        label.FontSize = block.HeaderLevel == 1 ? 32 : 24;
        label.FontAttributes = FontAttributes.Bold;
        label.Margin = new Thickness(0, block.HeaderLevel == 1 ? 4 : 16, 0, 8);
        return label;
    }

    private Label CreateParagraphView(string content)
    {
        var label = CreateBaseLabel();
        label.Text = content;
        label.FontSize = 18;
        return label;
    }

    private Label CreateBulletView(string content)
    {
        var label = CreateBaseLabel();
        label.Text = $"• {content}";
        label.FontSize = 18;
        label.Margin = new Thickness(20, 0, 0, 4);
        return label;
    }

    private View CreateBlockQuoteView(string content)
    {
        var quoteLabel = CreateBaseLabel();
        quoteLabel.Text = content;
        quoteLabel.FontSize = 17;
        quoteLabel.Margin = new Thickness(0);
        quoteLabel.LineBreakMode = LineBreakMode.WordWrap;

        var accent = new BoxView
        {
            WidthRequest = 4,
            CornerRadius = 2,
            VerticalOptions = LayoutOptions.Fill
        };
        accent.SetAppThemeColor(BoxView.ColorProperty, Color.FromArgb("#A08E71"), Color.FromArgb("#C8B79D"));

        var layout = new Grid
        {
            ColumnDefinitions =
            {
                new ColumnDefinition(GridLength.Auto),
                new ColumnDefinition(GridLength.Star)
            },
            ColumnSpacing = 14
        };
        layout.Children.Add(accent);
        layout.Children.Add(quoteLabel);
        Grid.SetColumn(quoteLabel, 1);

        var border = new Border
        {
            Padding = new Thickness(16, 14),
            Margin = new Thickness(0, 4, 0, 10),
            StrokeThickness = 0,
            Content = layout,
            StrokeShape = new RoundRectangle
            {
                CornerRadius = new CornerRadius(16)
            }
        };
        border.SetAppThemeColor(BackgroundColorProperty, Color.FromArgb("#EFE7D8"), Color.FromArgb("#343432"));

        return border;
    }

    private View CreateCodeBlockView(MarkdownBlock block)
    {
        var codeLabel = new Label
        {
            Text = block.Content,
            FontFamily = "Courier New",
            FontSize = 15,
            LineBreakMode = LineBreakMode.NoWrap,
            Margin = new Thickness(0),
            Padding = new Thickness(0)
        };
        codeLabel.SetAppThemeColor(Label.TextColorProperty, Color.FromArgb("#1E1E1E"), Color.FromArgb("#F5F1E8"));

        var stack = new VerticalStackLayout
        {
            Spacing = 8
        };

        if (!string.IsNullOrWhiteSpace(block.CodeLanguage))
        {
            var languageLabel = new Label
            {
                Text = block.CodeLanguage,
                FontSize = 11,
                FontAttributes = FontAttributes.Bold,
                Margin = new Thickness(0)
            };
            languageLabel.SetAppThemeColor(Label.TextColorProperty, Color.FromArgb("#7B735F"), Color.FromArgb("#CDBEA3"));
            stack.Children.Add(languageLabel);
        }

        stack.Children.Add(new ScrollView
        {
            Orientation = ScrollOrientation.Both,
            Content = codeLabel
        });

        var border = new Border
        {
            Padding = new Thickness(16, 14),
            Margin = new Thickness(0, 4, 0, 12),
            StrokeThickness = 1,
            Content = stack,
            StrokeShape = new RoundRectangle
            {
                CornerRadius = new CornerRadius(16)
            }
        };
        border.SetAppThemeColor(BackgroundColorProperty, Color.FromArgb("#EAE3D6"), Color.FromArgb("#1E1F21"));
        border.SetAppThemeColor(Border.StrokeProperty, Color.FromArgb("#CFC3AE"), Color.FromArgb("#4A4C52"));

        return border;
    }

    private View CreateTableView(MarkdownBlock block)
    {
        var columnCount = Math.Max(
            block.TableHeaders.Count,
            block.TableRows.Count == 0 ? 0 : block.TableRows.Max(row => row.Count));

        if (columnCount == 0)
        {
            return CreateParagraphView(block.Content);
        }

        var grid = new Grid
        {
            ColumnSpacing = 0,
            RowSpacing = 0
        };

        for (var columnIndex = 0; columnIndex < columnCount; columnIndex++)
        {
            grid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
        }

        grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
        for (var rowIndex = 0; rowIndex < block.TableRows.Count; rowIndex++)
        {
            grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
        }

        for (var columnIndex = 0; columnIndex < columnCount; columnIndex++)
        {
            var headerCell = CreateTableCell(
                columnIndex < block.TableHeaders.Count ? block.TableHeaders[columnIndex] : string.Empty,
                isHeader: true,
                isLastColumn: columnIndex == columnCount - 1,
                isLastRow: block.TableRows.Count == 0);
            grid.Children.Add(headerCell);
            Grid.SetColumn(headerCell, columnIndex);
            Grid.SetRow(headerCell, 0);
        }

        for (var rowIndex = 0; rowIndex < block.TableRows.Count; rowIndex++)
        {
            var row = block.TableRows[rowIndex];
            for (var columnIndex = 0; columnIndex < columnCount; columnIndex++)
            {
                var dataCell = CreateTableCell(
                    columnIndex < row.Count ? row[columnIndex] : string.Empty,
                    isHeader: false,
                    isLastColumn: columnIndex == columnCount - 1,
                    isLastRow: rowIndex == block.TableRows.Count - 1);
                grid.Children.Add(dataCell);
                Grid.SetColumn(dataCell, columnIndex);
                Grid.SetRow(dataCell, rowIndex + 1);
            }
        }

        var tableBorder = new Border
        {
            Padding = new Thickness(0),
            StrokeThickness = 1,
            Content = grid,
            StrokeShape = new RoundRectangle
            {
                CornerRadius = new CornerRadius(14)
            }
        };
        tableBorder.SetAppThemeColor(BackgroundColorProperty, Color.FromArgb("#F8F3E8"), Color.FromArgb("#2A2B2D"));
        tableBorder.SetAppThemeColor(Border.StrokeProperty, Color.FromArgb("#D8CEBB"), Color.FromArgb("#4A4B50"));

        return new ScrollView
        {
            Orientation = ScrollOrientation.Horizontal,
            Margin = new Thickness(0, 4, 0, 12),
            Content = tableBorder
        };
    }

    private Border CreateTableCell(string text, bool isHeader, bool isLastColumn, bool isLastRow)
    {
        var label = new Label
        {
            Text = text,
            FontSize = isHeader ? 14 : 13,
            FontAttributes = isHeader ? FontAttributes.Bold : FontAttributes.None,
            LineBreakMode = LineBreakMode.WordWrap,
            Margin = new Thickness(0)
        };
        label.SetAppThemeColor(Label.TextColorProperty, Color.FromArgb("#1A1A1A"), Color.FromArgb("#F3EDE2"));

        var border = new Border
        {
            Padding = new Thickness(12, 10),
            Content = label,
            StrokeShape = new Rectangle(),
            StrokeThickness = 0
        };

        border.SetAppThemeColor(BackgroundColorProperty,
            isHeader ? Color.FromArgb("#EDE4D4") : Color.FromArgb("#F8F3E8"),
            isHeader ? Color.FromArgb("#35363A") : Color.FromArgb("#2A2B2D"));

        border.StrokeThickness = 0;
        border.Stroke = Brush.Transparent;

        if (!isLastColumn || !isLastRow)
        {
            border.StrokeThickness = 1;
            border.SetAppThemeColor(Border.StrokeProperty, Color.FromArgb("#D8CEBB"), Color.FromArgb("#4A4B50"));
        }

        return border;
    }

    private Label CreateBaseLabel()
    {
        var label = new Label
        {
            TextColor = Colors.Black,
            Margin = new Thickness(0, 0, 0, 8)
        };

        label.SetAppThemeColor(Label.TextColorProperty, Color.FromArgb("#161616"), Color.FromArgb("#F3EDE2"));
        return label;
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
            StrokeShape = new RoundRectangle
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

        container.SetAppThemeColor(BackgroundColorProperty, palette.LightBackground, palette.DarkBackground);
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
