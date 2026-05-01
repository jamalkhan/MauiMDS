using System.ComponentModel;
using MauiMds.ViewModels;

namespace MauiMds.Views;

public partial class RecordingGroupView : ContentView
{
    private MainViewModel? _vm;

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
                _vm.PropertyChanged -= OnVmPropertyChanged;
                _vm = null;
            }
        }
    }

    private void OnBindingContextChanged(object? sender, EventArgs e)
    {
        if (App.IsTerminating) return;

        if (_vm is not null)
            _vm.PropertyChanged -= OnVmPropertyChanged;

        _vm = BindingContext as MainViewModel;

        if (_vm is not null)
            _vm.PropertyChanged += OnVmPropertyChanged;

        Rebuild();
    }

    private void OnVmPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (App.IsTerminating) return;

        if (e.PropertyName is nameof(MainViewModel.SelectedRecordingGroup)
                           or nameof(MainViewModel.CurrentlyPlayingAudioPath))
        {
            MainThread.BeginInvokeOnMainThread(Rebuild);
        }
    }

    private void Rebuild()
    {
        if (App.IsTerminating) return;

        try
        {
            ChipsContainer.Children.Clear();

            var group = _vm?.SelectedRecordingGroup;
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

    private View BuildReTranscribeButton()
    {
        var isDark = Application.Current?.RequestedTheme == AppTheme.Dark;
        var btn = new Button
        {
            Text = "Re-transcribe",
            FontSize = 12,
            Padding = new Thickness(12, 6),
            HorizontalOptions = LayoutOptions.Start,
            BackgroundColor = isDark ? Color.FromArgb("#3A3835") : Color.FromArgb("#DDD3BF"),
            TextColor = isDark ? Color.FromArgb("#C8B89A") : Color.FromArgb("#5A4E42")
        };
        btn.Clicked += (_, _) => _vm?.ReTranscribeGroupCommand.Execute(null);
        return btn;
    }

    private View BuildChip(string filePath)
    {
        var fileName = Path.GetFileName(filePath);
        var isPlaying = string.Equals(filePath, _vm?.CurrentlyPlayingAudioPath, StringComparison.Ordinal);

        var chipBg = Application.Current?.RequestedTheme == AppTheme.Dark
            ? Color.FromArgb("#3A3835")
            : Color.FromArgb("#E8E0CF");
        var textColor = Application.Current?.RequestedTheme == AppTheme.Dark
            ? Color.FromArgb("#F3EDE2")
            : Color.FromArgb("#1A1A1A");

        var iconLabel = new Label
        {
            Text = "♪",
            FontSize = 15,
            VerticalTextAlignment = TextAlignment.Center,
            TextColor = textColor,
            Margin = new Thickness(0, 0, 8, 0)
        };

        var nameLabel = new Label
        {
            Text = fileName,
            FontSize = 13,
            VerticalTextAlignment = TextAlignment.Center,
            TextColor = textColor,
            HorizontalOptions = LayoutOptions.Fill
        };

        var playPauseButton = new Button
        {
            Text = isPlaying ? "⏸" : "▶",
            FontSize = 13,
            Padding = new Thickness(10, 6),
            BackgroundColor = isPlaying
                ? Color.FromArgb("#CC4444")
                : (Application.Current?.RequestedTheme == AppTheme.Dark
                    ? Color.FromArgb("#4A4340")
                    : Color.FromArgb("#5A4E42")),
            TextColor = Colors.White,
            VerticalOptions = LayoutOptions.Center
        };

        var capturedPath = filePath;
        if (isPlaying)
            playPauseButton.Clicked += (_, _) => _vm?.PauseAudioCommand.Execute(null);
        else
            playPauseButton.Clicked += (_, _) => _vm?.PlayAudioCommand.Execute(capturedPath);

        var row = new Grid
        {
            ColumnDefinitions =
            [
                new ColumnDefinition { Width = GridLength.Auto },
                new ColumnDefinition { Width = GridLength.Star },
                new ColumnDefinition { Width = GridLength.Auto }
            ],
            ColumnSpacing = 8,
            Padding = new Thickness(10, 6)
        };
        row.Add(iconLabel, 0, 0);
        row.Add(nameLabel, 1, 0);
        row.Add(playPauseButton, 2, 0);

        return new Border
        {
            BackgroundColor = chipBg,
            StrokeThickness = 0,
            StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = new CornerRadius(8) },
            Content = row
        };
    }
}
