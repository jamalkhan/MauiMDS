using MauiMds.Models;
using Microsoft.Extensions.Logging;

namespace MauiMds.Services;

public sealed class MarkdownDocumentService : IMarkdownDocumentService
{
    private static readonly string[] AllowedExtensions = [".mds", ".md"];
    private const string ExampleDocumentName = "example.mds";

    private readonly ILogger<MarkdownDocumentService> _logger;

    public MarkdownDocumentService(ILogger<MarkdownDocumentService> logger)
    {
        _logger = logger;
    }

    public async Task<MarkdownDocument?> LoadInitialDocumentAsync()
    {
        _logger.LogInformation("Loading initial markdown document from the app package: {DocumentName}", ExampleDocumentName);

        try
        {
            await using var stream = await FileSystem.Current.OpenAppPackageFileAsync(ExampleDocumentName);
            using var reader = new StreamReader(stream);
            var content = await reader.ReadToEndAsync();
            var sizeInBytes = System.Text.Encoding.UTF8.GetByteCount(content);

            _logger.LogInformation(
                "Loaded bundled markdown document. FileName: {FileName}, SizeKB: {SizeKb:F2}, ContentLength: {ContentLength}",
                ExampleDocumentName,
                sizeInBytes / 1024d,
                content.Length);

            return new MarkdownDocument
            {
                FilePath = ExampleDocumentName,
                FileName = ExampleDocumentName,
                FileSizeBytes = sizeInBytes,
                Content = content
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to open bundled markdown document {DocumentName}", ExampleDocumentName);
            throw;
        }
    }

    public async Task<MarkdownDocument?> PickDocumentAsync()
    {
        _logger.LogInformation("Opening file picker for markdown documents.");

        var options = new PickOptions
        {
            PickerTitle = "Open a Markdown or MDS file"
        };

        if (DeviceInfo.Current.Platform == DevicePlatform.WinUI)
        {
            options.FileTypes = new FilePickerFileType(new Dictionary<DevicePlatform, IEnumerable<string>>
            {
                { DevicePlatform.WinUI, [".md", ".mds"] }
            });
        }

        var result = await FilePicker.Default.PickAsync(options);

        if (result is null)
        {
            _logger.LogInformation("File picker canceled.");
            return null;
        }

        var extension = Path.GetExtension(result.FileName);
        if (!AllowedExtensions.Contains(extension, StringComparer.OrdinalIgnoreCase))
        {
            _logger.LogWarning("Rejected unsupported file type: {FileName}", result.FileName);
            throw new InvalidOperationException("Please choose a .mds or .md file.");
        }

        FileInfo? fileInfo = null;
        if (!string.IsNullOrWhiteSpace(result.FullPath))
        {
            fileInfo = new FileInfo(result.FullPath);
        }

        _logger.LogInformation(
            "Reading selected markdown document. FileName: {FileName}, FullPath: {FullPath}, SizeKB: {SizeKb:F2}, LastModified: {LastModified}",
            result.FileName,
            result.FullPath,
            (fileInfo?.Length ?? 0) / 1024d,
            fileInfo?.LastWriteTime);

        await using var stream = await result.OpenReadAsync();
        using var reader = new StreamReader(stream);
        var content = await reader.ReadToEndAsync();

        return new MarkdownDocument
        {
            FilePath = result.FullPath,
            FileName = result.FileName,
            FileSizeBytes = fileInfo?.Length,
            LastModified = fileInfo?.LastWriteTime,
            Content = content
        };
    }

}
