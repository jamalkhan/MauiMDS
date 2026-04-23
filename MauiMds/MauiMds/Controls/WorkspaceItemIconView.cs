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
    private readonly Label _hashOverlay;
    private readonly Label _audioNote;

    public WorkspaceItemIconView()
    {
        WidthRequest = 18;
        HeightRequest = 18;

        _documentBody = new Border
        {
            WidthRequest = 14,
            HeightRequest = 16,
            StrokeThickness = 1,
            StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = new CornerRadius(4) },
            HorizontalOptions = LayoutOptions.Center,
            VerticalOptions = LayoutOptions.Center,
            BackgroundColor = Color.FromArgb("#FFFDFC"),
            Stroke = Color.FromArgb("#8D867C")
        };

        _foldCorner = new Border
        {
            WidthRequest = 5,
            HeightRequest = 5,
            StrokeThickness = 1,
            HorizontalOptions = LayoutOptions.End,
            VerticalOptions = LayoutOptions.Start,
            Margin = new Thickness(0, 1, 1, 0),
            BackgroundColor = Color.FromArgb("#F3ECE2"),
            Stroke = Color.FromArgb("#8D867C"),
            StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = new CornerRadius(0, 3, 0, 3) }
        };

        _folderBody = new Border
        {
            WidthRequest = 15,
            HeightRequest = 11,
            StrokeThickness = 1,
            HorizontalOptions = LayoutOptions.Center,
            VerticalOptions = LayoutOptions.End,
            Margin = new Thickness(0, 0, 0, 1),
            BackgroundColor = Color.FromArgb("#D8C08C"),
            Stroke = Color.FromArgb("#8B7347"),
            StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = new CornerRadius(4) }
        };

        _folderTab = new Border
        {
            WidthRequest = 7,
            HeightRequest = 4,
            StrokeThickness = 1,
            HorizontalOptions = LayoutOptions.Start,
            VerticalOptions = LayoutOptions.Start,
            Margin = new Thickness(2, 2, 0, 0),
            BackgroundColor = Color.FromArgb("#E4CD9D"),
            Stroke = Color.FromArgb("#8B7347"),
            StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = new CornerRadius(3, 3, 1, 1) }
        };

        _redFolderBody = new Border
        {
            WidthRequest = 15,
            HeightRequest = 11,
            StrokeThickness = 1,
            HorizontalOptions = LayoutOptions.Center,
            VerticalOptions = LayoutOptions.End,
            Margin = new Thickness(0, 0, 0, 1),
            BackgroundColor = Color.FromArgb("#D44040"),
            Stroke = Color.FromArgb("#952222"),
            StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = new CornerRadius(4) }
        };

        _redFolderTab = new Border
        {
            WidthRequest = 7,
            HeightRequest = 4,
            StrokeThickness = 1,
            HorizontalOptions = LayoutOptions.Start,
            VerticalOptions = LayoutOptions.Start,
            Margin = new Thickness(2, 2, 0, 0),
            BackgroundColor = Color.FromArgb("#E86060"),
            Stroke = Color.FromArgb("#952222"),
            StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = new CornerRadius(3, 3, 1, 1) }
        };

        _audioBody = new Border
        {
            WidthRequest = 14,
            HeightRequest = 14,
            StrokeThickness = 1,
            StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = new CornerRadius(7) },
            HorizontalOptions = LayoutOptions.Center,
            VerticalOptions = LayoutOptions.Center,
            BackgroundColor = Color.FromArgb("#3A7DD4"),
            Stroke = Color.FromArgb("#1A5CA0")
        };

        _hashOverlay = new Label
        {
            Text = "#",
            FontSize = 9,
            FontAttributes = FontAttributes.Bold,
            HorizontalTextAlignment = TextAlignment.Center,
            VerticalTextAlignment = TextAlignment.Center,
            Margin = new Thickness(0, 6, 0, 0),
            TextColor = Color.FromArgb("#C9651A"),
            IsVisible = false
        };

        _audioNote = new Label
        {
            Text = "♪",
            FontSize = 9,
            FontAttributes = FontAttributes.Bold,
            HorizontalTextAlignment = TextAlignment.Center,
            VerticalTextAlignment = TextAlignment.Center,
            TextColor = Colors.White,
            IsVisible = false
        };

        Content = new Grid
        {
            WidthRequest = 18,
            HeightRequest = 18,
            Children =
            {
                _redFolderBody,
                _redFolderTab,
                _folderBody,
                _folderTab,
                _audioBody,
                _documentBody,
                _foldCorner,
                _hashOverlay,
                _audioNote
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
        var isMarkdownSharp = Kind == WorkspaceItemIconKind.MarkdownSharp;
        var isDocument = !isFolder && !isRecordingsFolder && !isAudio;

        _folderBody.IsVisible = isFolder;
        _folderTab.IsVisible = isFolder;
        _redFolderBody.IsVisible = isRecordingsFolder;
        _redFolderTab.IsVisible = isRecordingsFolder;
        _audioBody.IsVisible = isAudio;
        _audioNote.IsVisible = isAudio;
        _documentBody.IsVisible = isDocument;
        _foldCorner.IsVisible = isDocument;
        _hashOverlay.IsVisible = isMarkdownSharp;
    }
}
