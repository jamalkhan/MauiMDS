using MauiMds.Models;
using MauiMds.Services;
using Windows.Storage;
using Windows.Storage.Pickers;
using WinRT.Interop;

namespace MauiMds.Services;

internal sealed class DocumentPickerPlatformService : IDocumentPickerPlatformService
{
    public async Task<string?> PickDocumentPathAsync()
    {
        var options = new PickOptions
        {
            PickerTitle = "Open a Markdown or MDS file",
            FileTypes = new FilePickerFileType(new Dictionary<DevicePlatform, IEnumerable<string>>
            {
                { DevicePlatform.WinUI, [".md", ".mds"] }
            })
        };

        var result = await FilePicker.Default.PickAsync(options);
        return result?.FullPath;
    }

    public async Task<SaveDocumentResult?> SaveAsAsync(EditorDocumentState document, string suggestedFileName, CancellationToken ct = default)
    {
        var picker = new FileSavePicker
        {
            SuggestedFileName = Path.GetFileNameWithoutExtension(suggestedFileName)
        };

        picker.FileTypeChoices.Add("Markdown files", [".md", ".mds"]);
        picker.SuggestedStartLocation = PickerLocationId.DocumentsLibrary;
        InitializeWithWindow.Initialize(picker, GetWindowHandle());

        var file = await picker.PickSaveFileAsync();
        if (file is null)
        {
            return null;
        }

        await FileIO.WriteTextAsync(file, MarkdownFileConventions.NormalizeNewLines(document.Content, document.NewLine));
        var properties = await file.GetBasicPropertiesAsync();

        return new SaveDocumentResult
        {
            FilePath = file.Path,
            FileName = file.Name,
            FileSizeBytes = (long)properties.Size,
            LastModified = file.DateCreated
        };
    }

    private static nint GetWindowHandle()
    {
        var window = Application.Current?.Windows.FirstOrDefault()
            ?? throw new InvalidOperationException("Unable to locate the current window.");
        var mauiWindow = window.Handler?.PlatformView as Microsoft.UI.Xaml.Window
            ?? throw new InvalidOperationException("Unable to locate the native window.");
        return WindowNative.GetWindowHandle(mauiWindow);
    }
}
