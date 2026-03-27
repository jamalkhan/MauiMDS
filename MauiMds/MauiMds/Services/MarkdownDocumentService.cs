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

            return new MarkdownDocument
            {
                FilePath = ExampleDocumentName,
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

        var result = await FilePicker.Default.PickAsync(new PickOptions
        {
            PickerTitle = "Open a Markdown or MDS file"
        });

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

        _logger.LogInformation("Reading selected markdown document from {FullPath}", result.FullPath);
        await using var stream = await result.OpenReadAsync();
        using var reader = new StreamReader(stream);
        var content = await reader.ReadToEndAsync();

        return new MarkdownDocument
        {
            FilePath = result.FullPath,
            Content = content
        };
    }

}
