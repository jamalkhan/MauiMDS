namespace MauiMds.Views;

public partial class DocumentHeaderView : ContentView
{
    public DocumentHeaderView()
    {
        InitializeComponent();
    }

    public void ApplyHeaderState(string fileName, string filePath, string statusText, bool hasInlineError, string inlineErrorMessage)
    {
        FileNameLabel.Text = fileName;
        FilePathLabel.Text = filePath;
        StatusLabel.Text = statusText;
        InlineErrorBorder.IsVisible = hasInlineError;
        InlineErrorLabel.Text = inlineErrorMessage;
    }
}
