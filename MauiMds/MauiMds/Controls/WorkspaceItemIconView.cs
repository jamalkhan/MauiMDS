using MauiMds.Models;

namespace MauiMds.Controls;

public sealed class WorkspaceItemIconView : ContentView
{
    public static readonly BindableProperty KindProperty = BindableProperty.Create(
        nameof(Kind),
        typeof(WorkspaceItemIconKind),
        typeof(WorkspaceItemIconView),
        WorkspaceItemIconKind.Markdown,
        propertyChanged: OnKindChanged);

    private readonly Border _documentBody;
    private readonly Border _foldCorner;
    private readonly Border _folderBody;
    private readonly Border _folderTab;
    private readonly Border _redFolderBody;
    private readonly Border _redFolderTab;
    private readonly Border _audioBody;
    private readonly Border _audioTranscribedBody;
    private readonly Border _audioQueuedBody;
    private readonly Label _hashOverlay;
    private readonly Label _audioNote;
    private readonly Label _audioTranscribedNote;
    private readonly Label _audioQueuedNote;

    public WorkspaceItemIconView()
    {
        WidthRequest = 36;
        HeightRequest = 36;

        _documentBody = new Border
        {
            WidthRequest = 28,
            HeightRequest = 32,
            StrokeThickness = 1,
            StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = new CornerRadius(8) },
            HorizontalOptions = LayoutOptions.Center,
            VerticalOptions = LayoutOptions.Center,
            BackgroundColor = Color.FromArgb("#FFFDFC"),
            Stroke = Color.FromArgb("#8D867C")
        };

        _foldCorner = new Border
        {
            WidthRequest = 10,
            HeightRequest = 10,
            StrokeThickness = 1,
            HorizontalOptions = LayoutOptions.End,
            VerticalOptions = LayoutOptions.Start,
            Margin = new Thickness(0, 2, 2, 0),
            BackgroundColor = Color.FromArgb("#F3ECE2"),
            Stroke = Color.FromArgb("#8D867C"),
            StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = new CornerRadius(0, 6, 0, 6) }
        };

        _folderBody = new Border
        {
            WidthRequest = 30,
            HeightRequest = 22,
            StrokeThickness = 1,
            HorizontalOptions = LayoutOptions.Center,
            VerticalOptions = LayoutOptions.End,
            Margin = new Thickness(0, 0, 0, 2),
            BackgroundColor = Color.FromArgb("#D8C08C"),
            Stroke = Color.FromArgb("#8B7347"),
            StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = new CornerRadius(8) }
        };

        _folderTab = new Border
        {
            WidthRequest = 14,
            HeightRequest = 8,
            StrokeThickness = 1,
            HorizontalOptions = LayoutOptions.Start,
            VerticalOptions = LayoutOptions.Start,
            Margin = new Thickness(4, 4, 0, 0),
            BackgroundColor = Color.FromArgb("#E4CD9D"),
            Stroke = Color.FromArgb("#8B7347"),
            StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = new CornerRadius(6, 6, 2, 2) }
        };

        _redFolderBody = new Border
        {
            WidthRequest = 30,
            HeightRequest = 22,
            StrokeThickness = 1,
            HorizontalOptions = LayoutOptions.Center,
            VerticalOptions = LayoutOptions.End,
            Margin = new Thickness(0, 0, 0, 2),
            BackgroundColor = Color.FromArgb("#D44040"),
            Stroke = Color.FromArgb("#952222"),
            StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = new CornerRadius(8) }
        };

        _redFolderTab = new Border
        {
            WidthRequest = 14,
            HeightRequest = 8,
            StrokeThickness = 1,
            HorizontalOptions = LayoutOptions.Start,
            VerticalOptions = LayoutOptions.Start,
            Margin = new Thickness(4, 4, 0, 0),
            BackgroundColor = Color.FromArgb("#E86060"),
            Stroke = Color.FromArgb("#952222"),
            StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = new CornerRadius(6, 6, 2, 2) }
        };

        _audioBody = new Border
        {
            WidthRequest = 28,
            HeightRequest = 28,
            StrokeThickness = 1,
            StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = new CornerRadius(14) },
            HorizontalOptions = LayoutOptions.Center,
            VerticalOptions = LayoutOptions.Center,
            BackgroundColor = Color.FromArgb("#3A7DD4"),
            Stroke = Color.FromArgb("#1A5CA0")
        };

        _audioTranscribedBody = new Border
        {
            WidthRequest = 28,
            HeightRequest = 28,
            StrokeThickness = 1,
            StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = new CornerRadius(14) },
            HorizontalOptions = LayoutOptions.Center,
            VerticalOptions = LayoutOptions.Center,
            BackgroundColor = Color.FromArgb("#3A9B62"),
            Stroke = Color.FromArgb("#1E7340")
        };

        _audioQueuedBody = new Border
        {
            WidthRequest = 28,
            HeightRequest = 28,
            StrokeThickness = 1,
            StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = new CornerRadius(14) },
            HorizontalOptions = LayoutOptions.Center,
            VerticalOptions = LayoutOptions.Center,
            BackgroundColor = Color.FromArgb("#7BBFEA"),
            Stroke = Color.FromArgb("#4A8FBF")
        };

        _hashOverlay = new Label
        {
            Text = "#",
            FontSize = 18,
            FontAttributes = FontAttributes.Bold,
            HorizontalTextAlignment = TextAlignment.Center,
            VerticalTextAlignment = TextAlignment.Center,
            Margin = new Thickness(0, 12, 0, 0),
            TextColor = Color.FromArgb("#C9651A"),
            IsVisible = false
        };

        _audioNote = new Label
        {
            Text = "♪",
            FontSize = 18,
            FontAttributes = FontAttributes.Bold,
            HorizontalTextAlignment = TextAlignment.Center,
            VerticalTextAlignment = TextAlignment.Center,
            TextColor = Colors.White,
            IsVisible = false
        };

        _audioTranscribedNote = new Label
        {
            Text = "♪",
            FontSize = 18,
            FontAttributes = FontAttributes.Bold,
            HorizontalTextAlignment = TextAlignment.Center,
            VerticalTextAlignment = TextAlignment.Center,
            TextColor = Colors.White,
            IsVisible = false
        };

        _audioQueuedNote = new Label
        {
            Text = "♪",
            FontSize = 18,
            FontAttributes = FontAttributes.Bold,
            HorizontalTextAlignment = TextAlignment.Center,
            VerticalTextAlignment = TextAlignment.Center,
            TextColor = Colors.White,
            IsVisible = false
        };

        Content = new Grid
        {
            WidthRequest = 36,
            HeightRequest = 36,
            Children =
            {
                _redFolderBody,
                _redFolderTab,
                _folderBody,
                _folderTab,
                _audioBody,
                _audioTranscribedBody,
                _audioQueuedBody,
                _documentBody,
                _foldCorner,
                _hashOverlay,
                _audioNote,
                _audioTranscribedNote,
                _audioQueuedNote
            }
        };

        ApplyKind();
    }

    public WorkspaceItemIconKind Kind
    {
        get => (WorkspaceItemIconKind)GetValue(KindProperty);
        set => SetValue(KindProperty, value);
    }

    private static void OnKindChanged(BindableObject bindable, object? oldValue, object? newValue)
    {
        ((WorkspaceItemIconView)bindable).ApplyKind();
    }

    private void ApplyKind()
    {
        var isFolder = Kind == WorkspaceItemIconKind.Folder;
        var isRecordingsFolder = Kind == WorkspaceItemIconKind.RecordingsFolder;
        var isAudio = Kind == WorkspaceItemIconKind.Audio;
        var isAudioTranscribed = Kind == WorkspaceItemIconKind.AudioTranscribed;
        var isAudioQueued = Kind == WorkspaceItemIconKind.AudioQueued;
        var isMarkdownSharp = Kind == WorkspaceItemIconKind.MarkdownSharp;
        var isDocument = !isFolder && !isRecordingsFolder && !isAudio && !isAudioTranscribed && !isAudioQueued;

        _folderBody.IsVisible = isFolder;
        _folderTab.IsVisible = isFolder;
        _redFolderBody.IsVisible = isRecordingsFolder;
        _redFolderTab.IsVisible = isRecordingsFolder;
        _audioBody.IsVisible = isAudio;
        _audioNote.IsVisible = isAudio;
        _audioTranscribedBody.IsVisible = isAudioTranscribed;
        _audioTranscribedNote.IsVisible = isAudioTranscribed;
        _audioQueuedBody.IsVisible = isAudioQueued;
        _audioQueuedNote.IsVisible = isAudioQueued;
        _documentBody.IsVisible = isDocument;
        _foldCorner.IsVisible = isDocument;
        _hashOverlay.IsVisible = isMarkdownSharp;
    }
}
