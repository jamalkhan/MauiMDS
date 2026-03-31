namespace MauiMds.Views;

internal sealed class WorkspacePaneController
{
    private const string AnimationName = "WorkspacePane";

    private readonly VisualElement _animationOwner;
    private readonly Action<double> _setWidth;
    private readonly Action<bool, double> _setPanelVisibility;

    private double _currentWidth;
    private double _resizeStartWidth;

    public WorkspacePaneController(
        VisualElement animationOwner,
        Action<double> setWidth,
        Action<bool, double> setPanelVisibility)
    {
        _animationOwner = animationOwner;
        _setWidth = setWidth;
        _setPanelVisibility = setPanelVisibility;
    }

    public double CurrentWidth => _currentWidth;

    public void Refresh(bool initial, bool isVisible, double targetWidth)
    {
        if (initial)
        {
            SetWidth(targetWidth);
            _setPanelVisibility(isVisible, isVisible ? 1 : 0);
            return;
        }

        _ = AnimateToAsync(targetWidth, isVisible);
    }

    public void HandleResizePan(PanUpdatedEventArgs e, bool isVisible, double fallbackWidth, Action<double> persistWidth)
    {
        if (!isVisible)
        {
            return;
        }

        switch (e.StatusType)
        {
            case GestureStatus.Started:
                _animationOwner.AbortAnimation(AnimationName);
                _resizeStartWidth = _currentWidth > 0 ? _currentWidth : fallbackWidth;
                break;
            case GestureStatus.Running:
                SetWidth(_resizeStartWidth + e.TotalX);
                break;
            case GestureStatus.Canceled:
            case GestureStatus.Completed:
                persistWidth(_currentWidth);
                break;
        }
    }

    private void SetWidth(double width)
    {
        _currentWidth = Math.Max(0, width);
        _setWidth(_currentWidth);
    }

    private async Task AnimateToAsync(double targetWidth, bool shouldRemainVisible)
    {
        _animationOwner.AbortAnimation(AnimationName);

        if (shouldRemainVisible)
        {
            _setPanelVisibility(true, 1);
        }

        var startWidth = _currentWidth;
        var startOpacity = shouldRemainVisible ? 0 : 1;
        var targetOpacity = shouldRemainVisible ? 1 : 0;
        var animation = new Animation(progress =>
        {
            SetWidth(startWidth + ((targetWidth - startWidth) * progress));
            _setPanelVisibility(true, startOpacity + ((targetOpacity - startOpacity) * progress));
        });

        var tcs = new TaskCompletionSource<bool>();
        animation.Commit(_animationOwner, AnimationName, 16, 180, Easing.CubicOut, (_, canceled) => tcs.TrySetResult(!canceled));
        await tcs.Task;

        SetWidth(targetWidth);
        _setPanelVisibility(shouldRemainVisible, targetOpacity);
    }
}
