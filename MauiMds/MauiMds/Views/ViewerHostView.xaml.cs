using MauiMds.Controls;

namespace MauiMds.Views;

public partial class ViewerHostView : ContentView
{
    public ViewerHostView()
    {
        InitializeComponent();
    }

    public IEditorSurface MarkdownEditor => MarkdownTextEditor;
    public IEditorSurface RichTextEditorSurface => RichTextEditor;
}
