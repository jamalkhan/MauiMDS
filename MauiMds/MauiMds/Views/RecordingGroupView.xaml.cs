using MauiMds;
using System.ComponentModel;
using MauiMds.ViewModels;

namespace MauiMds.Views;

public partial class RecordingGroupView : ContentView
{
    private MainViewModel? _vm;

    // Live-update handles for the active chip's playback controls.
    private Slider? _positionSlider;
    private Label? _currentTimeLabel;
    private Label? _remainingTimeLabel;
    private bool _sliderDragging;

    public RecordingGroupView()
    {
        InitializeComponent();
        BindingContextChanged += OnBindingContextChanged;
    }

    protected override void OnHandlerChanging(HandlerChangingEventArgs args)
    {
        base.OnHandlerChanging(args);

        // Unsubscribe from all managed events when the native view is being
        // detached. This prevents event callbacks from firing on a partially
        // torn-down view, which would throw inside _traitCollectionDidChange:
        // and cause SIGABRT on Mac Catalyst.
        if (args.NewHandler is null)
        {
            BindingContextChanged -= OnBindingContextChanged;
            if (_vm is not null)
            {
                _vm.Recording.PropertyChanged -= OnVmPropertyChanged;
                _vm = null;
            }
        }
    }

    private void OnBindingContextChanged(object? sender, EventArgs e)
    {
        if (App.IsTerminating) return;

        if (_vm is not null)
            _vm.Recording.PropertyChanged -= OnVmPropertyChanged;

        _vm = BindingContext as MainViewModel;

        if (_vm is not null)
            _vm.Recording.PropertyChanged += OnVmPropertyChanged;

        Rebuild();
    }

    private void OnVmPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (App.IsTerminating) return;

        if (e.PropertyName == nameof(RecordingSessionViewModel.PlaybackPosition))
        {
            MainThread.BeginInvokeOnMainThread(UpdatePositionDisplay);
            return;
        }

        if (e.PropertyName is nameof(RecordingSessionViewModel.SelectedRecordingGroup)
                           or nameof(RecordingSessionViewModel.CurrentlyPlayingAudioPath))
        {
            MainThread.BeginInvokeOnMainThread(Rebuild);
        }
    }

    private void Rebuild()
    {
        if (App.IsTerminating) return;

        _positionSlider = null;
        _currentTimeLabel = null;
        _remainingTimeLabel = null;

        try
        {
            ChipsContainer.Children.Clear();

            var group = _vm?.Recording.SelectedRecordingGroup;
            if (group is null) return;

            foreach (var filePath in group.AudioFilePaths)
                ChipsContainer.Children.Add(BuildChip(filePath));

            if (group.HasTranscript)
                ChipsContainer.Children.Add(BuildReTranscribeButton());
        }
        catch (Exception)
        {
            // Swallow exceptions thrown during app teardown. UIKit may call
            // _traitCollectionDidChange: on views still in the hierarchy while
            // native objects are being destroyed; any managed exception that
            // escapes the trampoline causes SIGABRT.
        }
    }

    private void UpdatePositionDisplay()
    {
        if (_positionSlider is null || _vm is null) return;

        var pos = _vm.Recording.PlaybackPosition;
        var dur = _vm.Recording.PlaybackDuration;

        if (dur.TotalSeconds > 0 && Math.Abs(_positionSlider.Maximum - dur.TotalSeconds) > 0.5)
            _positionSlider.Maximum = dur.TotalSeconds;

        if (!_sliderDragging && dur.TotalSeconds > 0)
            _positionSlider.Value = pos.TotalSeconds;

        if (_currentTimeLabel is not null)
            _currentTimeLabel.Text = FormatTime(pos);

        if (_remainingTimeLabel is not null)
        {
            var remaining = dur - pos;
            _remainingTimeLabel.Text = $"–{FormatTime(remaining < TimeSpan.Zero ? TimeSpan.Zero : remaining)}";
        }
    }

    private static string FormatTime(TimeSpan t)
    {
        if (t < TimeSpan.Zero) t = TimeSpan.Zero;
        return t.TotalHours >= 1 ? t.ToString(@"h\:mm\:ss") : t.ToString(@"m\:ss");
    }

    private View BuildReTranscribeButton()
    {
        var isDark = Application.Current?.RequestedTheme == AppTheme.Dark;
        var btn = new Button
        {
            Text = "Re-transcribe",
            FontSize = 12,
            Padding = new Thickness(12, 6),
            HorizontalOptions = LayoutOptions.Start,
            BackgroundColor = isDark ? AppColors.RecordingBtnBgDark : AppColors.RecordingBtnBgLight,
            TextColor = isDark ? AppColors.RecordingBtnTextDark : AppColors.RecordingBtnTextLight
        };
        btn.Clicked += (_, _) => _vm?.TranscriptionQueue.ReTranscribeGroupCommand.Execute(null);
        return btn;
    }

    private View BuildChip(string filePath)
    {
        var isPlaying = string.Equals(filePath, _vm?.Recording.CurrentlyPlayingAudioPath, StringComparison.Ordinal);

        var chipBg = Application.Current?.RequestedTheme == AppTheme.Dark
            ? AppColors.AudioChipBgDark
            : AppColors.AudioChipBgLight;
        var textColor = Application.Current?.RequestedTheme == AppTheme.Dark
            ? AppColors.AudioChipTextDark
            : AppColors.AudioChipTextLight;

        var fileName = Path.GetFileName(filePath);
        var capturedPath = filePath;

        var iconLabel = new Label
        {
            Text = "♪",
            FontSize = 15,
            VerticalTextAlignment = TextAlignment.Center,
            TextColor = textColor,
            Margin = new Thickness(0, 0, 4, 0)
        };

        var nameLabel = new Label
        {
            Text = fileName,
            FontSize = 13,
            VerticalTextAlignment = TextAlignment.Center,
            TextColor = textColor,
            HorizontalOptions = LayoutOptions.Fill,
            LineBreakMode = LineBreakMode.TailTruncation
        };

        var playPauseButton = new Button
        {
            Text = isPlaying ? "⏸" : "▶",
            FontSize = 13,
            Padding = new Thickness(10, 6),
            BackgroundColor = isPlaying
                ? AppColors.PlayBtnActive
                : (Application.Current?.RequestedTheme == AppTheme.Dark
                    ? AppColors.PlayBtnBgDark
                    : AppColors.PlayBtnBgLight),
            TextColor = Colors.White,
            VerticalOptions = LayoutOptions.Center
        };

        if (isPlaying)
            playPauseButton.Clicked += (_, _) => _vm?.Recording.PauseAudioCommand.Execute(null);
        else
            playPauseButton.Clicked += (_, _) => _vm?.Recording.PlayAudioCommand.Execute(capturedPath);

        // Top row: icon + filename + transport buttons
        var topRow = new Grid
        {
            ColumnDefinitions =
            [
                new ColumnDefinition { Width = GridLength.Auto },
                new ColumnDefinition { Width = GridLength.Star },
                new ColumnDefinition { Width = GridLength.Auto }
            ],
            ColumnSpacing = 8
        };
        topRow.Add(iconLabel, 0, 0);
        topRow.Add(nameLabel, 1, 0);

        if (isPlaying)
        {
            var rewindBtn = new Button
            {
                Text = "⏮",
                FontSize = 13,
                Padding = new Thickness(8, 6),
                BackgroundColor = Application.Current?.RequestedTheme == AppTheme.Dark
                    ? AppColors.PlayBtnBgDark : AppColors.PlayBtnBgLight,
                TextColor = Colors.White,
                VerticalOptions = LayoutOptions.Center
            };
            rewindBtn.Clicked += (_, _) => _vm?.Recording.RewindCommand.Execute(null);

            var ffBtn = new Button
            {
                Text = "⏭",
                FontSize = 13,
                Padding = new Thickness(8, 6),
                BackgroundColor = Application.Current?.RequestedTheme == AppTheme.Dark
                    ? AppColors.PlayBtnBgDark : AppColors.PlayBtnBgLight,
                TextColor = Colors.White,
                VerticalOptions = LayoutOptions.Center
            };
            ffBtn.Clicked += (_, _) => _vm?.Recording.FastForwardCommand.Execute(null);

            var transportRow = new HorizontalStackLayout
            {
                Spacing = 6,
                VerticalOptions = LayoutOptions.Center
            };
            transportRow.Add(rewindBtn);
            transportRow.Add(playPauseButton);
            transportRow.Add(ffBtn);

            topRow.Add(transportRow, 2, 0);
        }
        else
        {
            topRow.Add(playPauseButton, 2, 0);
        }

        View chipContent;

        if (isPlaying)
        {
            var pos = _vm?.Recording.PlaybackPosition ?? TimeSpan.Zero;
            var dur = _vm?.Recording.PlaybackDuration ?? TimeSpan.Zero;

            var slider = new Slider
            {
                Minimum = 0,
                Maximum = dur.TotalSeconds > 0 ? dur.TotalSeconds : 1,
                Value = pos.TotalSeconds,
                HorizontalOptions = LayoutOptions.Fill,
                Margin = new Thickness(0, 4, 0, 0)
            };
            slider.DragStarted += (_, _) => _sliderDragging = true;
            slider.DragCompleted += (_, _) =>
            {
                _sliderDragging = false;
                _vm?.Recording.SeekCommand.Execute(slider.Value);
            };

            var currentLabel = new Label
            {
                Text = FormatTime(pos),
                FontSize = 11,
                TextColor = textColor,
                HorizontalOptions = LayoutOptions.Start
            };
            var remainingLabel = new Label
            {
                Text = $"–{FormatTime(dur - pos < TimeSpan.Zero ? TimeSpan.Zero : dur - pos)}",
                FontSize = 11,
                TextColor = textColor,
                HorizontalOptions = LayoutOptions.End
            };

            var timeRow = new Grid
            {
                ColumnDefinitions =
                [
                    new ColumnDefinition { Width = GridLength.Star },
                    new ColumnDefinition { Width = GridLength.Star }
                ]
            };
            timeRow.Add(currentLabel, 0, 0);
            timeRow.Add(remainingLabel, 1, 0);

            // Store references for live updates
            _positionSlider = slider;
            _currentTimeLabel = currentLabel;
            _remainingTimeLabel = remainingLabel;

            var stack = new VerticalStackLayout { Spacing = 2 };
            stack.Add(topRow);
            stack.Add(slider);
            stack.Add(timeRow);
            chipContent = stack;
        }
        else
        {
            chipContent = topRow;
        }

        return new Border
        {
            BackgroundColor = chipBg,
            StrokeThickness = 0,
            StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = new CornerRadius(8) },
            Padding = new Thickness(10, 6),
            Content = chipContent
        };
    }
}
