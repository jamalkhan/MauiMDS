using MauiMds.Controls;

namespace MauiMds.Views;

public partial class ViewerHostView : ContentView
{
    public ViewerHostView()
    {
        InitializeComponent();
    }

    public IEditorSurface TextEditorSurface => TextEditor;
    public IEditorSurface VisualEditorSurface => VisualEditor;
}
