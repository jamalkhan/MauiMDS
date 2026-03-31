namespace MauiMds.Views;

public partial class LogsDockView : ContentView
{
    public event EventHandler<TappedEventArgs>? SnackbarTapped;
    public event EventHandler<PanUpdatedEventArgs>? ResizePanUpdated;
    public event EventHandler<PointerEventArgs>? ResizePointerEntered;
    public event EventHandler<PointerEventArgs>? ResizePointerExited;

    public LogsDockView()
    {
        InitializeComponent();
    }

    public Border SnackbarBorderControl => SnackbarBorder;
    public BoxView SnackbarAccentControl => SnackbarAccent;
    public Label SnackbarLevelLabelControl => SnackbarLevelLabel;
    public Label SnackbarCategoryLabelControl => SnackbarCategoryLabel;
    public Label SnackbarTimeLabelControl => SnackbarTimeLabel;
    public Label HistoryStateLabelControl => HistoryStateLabel;
    public Label HistoryCountLabelControl => HistoryCountLabel;
    public VerticalStackLayout HistoryStack => SnackbarHistoryStack;
    public VisualElement ResizeHandleElement => HistoryResizeHandle;

    public void ApplyHistoryPaneState(double height, bool historyVisible, bool resizeVisible)
    {
        HistoryTrayBorder.HeightRequest = height;
        HistoryTrayBorder.IsVisible = historyVisible;
        HistoryResizeHandle.IsVisible = resizeVisible;
    }

    private void OnSnackbarTapped(object? sender, TappedEventArgs e)
    {
        SnackbarTapped?.Invoke(this, e);
    }

    private void OnResizePanUpdated(object? sender, PanUpdatedEventArgs e)
    {
        ResizePanUpdated?.Invoke(this, e);
    }

    private void OnResizePointerEntered(object? sender, PointerEventArgs e)
    {
        ResizePointerEntered?.Invoke(HistoryResizeHandle, e);
    }

    private void OnResizePointerExited(object? sender, PointerEventArgs e)
    {
        ResizePointerExited?.Invoke(HistoryResizeHandle, e);
    }
}
