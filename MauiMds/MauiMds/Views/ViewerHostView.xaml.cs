using MauiMds.Controls;

namespace MauiMds.Views;

public partial class ViewerHostView : ContentView
{
    public ViewerHostView()
    {
        InitializeComponent();
    }

    public MarkdownSyntaxEditorView MarkdownEditor => MarkdownTextEditor;
}
