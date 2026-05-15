using Rizedown.Controls;

namespace Rizedown.Views;

public partial class ViewerHostView : ContentView
{
    public ViewerHostView()
    {
        InitializeComponent();
    }

    public IEditorSurface TextEditorSurface => TextEditor;
    public IEditorSurface VisualEditorSurface => VisualEditor;
}
