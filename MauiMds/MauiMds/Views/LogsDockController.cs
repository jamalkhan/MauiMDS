namespace MauiMds.Views;

internal sealed class LogsDockController
{
    private const string AnimationName = "SnackbarHistoryPane";

    private readonly VisualElement _animationOwner;
    private readonly Action<double, bool, bool> _applyHeight;
    private readonly Action _refreshLabels;

    private double _currentHeight;
    private double _maxHeight;
    private double _resizeStartHeight;

    public LogsDockController(
        VisualElement animationOwner,
        Action<double, bool, bool> applyHeight,
        Action refreshLabels)
    {
        _animationOwner = animationOwner;
        _applyHeight = applyHeight;
        _refreshLabels = refreshLabels;
    }

    public double CurrentHeight => _currentHeight;

    public void UpdateMaxHeight(double pageHeight)
    {
        _maxHeight = Math.Max(160, pageHeight * 0.4);
        if (_currentHeight > _maxHeight)
        {
            SetHeight(_maxHeight);
        }
    }

    public Task ToggleAsync(double pageHeight)
    {
        UpdateMaxHeight(pageHeight);
        var targetHeight = _currentHeight > 0.5
            ? 0
            : Math.Min(_maxHeight, Math.Max(180, pageHeight * 0.24));

        return AnimateToAsync(targetHeight);
    }

    public void HandleResizePan(PanUpdatedEventArgs e)
    {
        switch (e.StatusType)
        {
            case GestureStatus.Started:
                _animationOwner.AbortAnimation(AnimationName);
                _resizeStartHeight = _currentHeight;
                break;
            case GestureStatus.Running:
                SetHeight(_resizeStartHeight - e.TotalY);
                break;
            case GestureStatus.Canceled:
            case GestureStatus.Completed:
                var targetHeight = _currentHeight >= _maxHeight * 0.2 ? _currentHeight : 0;
                _ = AnimateToAsync(targetHeight);
                break;
        }
    }

    private async Task AnimateToAsync(double targetHeight)
    {
        targetHeight = Math.Clamp(targetHeight, 0, _maxHeight);
        var startingHeight = _currentHeight;

        if (Math.Abs(startingHeight - targetHeight) < 0.5)
        {
            SetHeight(targetHeight);
            return;
        }

        var completion = new TaskCompletionSource();
        var animation = new Animation(value => SetHeight(value), startingHeight, targetHeight, Easing.CubicOut);
        animation.Commit(
            _animationOwner,
            AnimationName,
            16,
            220,
            Easing.CubicOut,
            (_, _) => completion.TrySetResult());

        await completion.Task;
    }

    private void SetHeight(double requestedHeight)
    {
        _currentHeight = Math.Clamp(requestedHeight, 0, _maxHeight);
        var isOpen = _currentHeight > 0.5;
        _applyHeight(_currentHeight, isOpen, isOpen);
        _refreshLabels();
    }
}
