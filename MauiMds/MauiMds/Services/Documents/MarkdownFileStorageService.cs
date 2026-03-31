using System.Text;
using MauiMds.Models;
using Microsoft.Extensions.Logging;

namespace MauiMds.Services;

public sealed class MarkdownFileStorageService : IMarkdownFileStorageService
{
    private readonly IMarkdownFileAccessService _fileAccessService;
    private readonly ILogger<MarkdownFileStorageService> _logger;

    public MarkdownFileStorageService(IMarkdownFileAccessService fileAccessService, ILogger<MarkdownFileStorageService> logger)
    {
        _fileAccessService = fileAccessService;
        _logger = logger;
    }

    public async Task<MarkdownDocument?> LoadInitialDocumentAsync()
    {
        _logger.LogInformation("Loading initial markdown document from the app package: {DocumentName}", MarkdownFileConventions.ExampleDocumentName);

        await using var stream = await FileSystem.Current.OpenAppPackageFileAsync(MarkdownFileConventions.ExampleDocumentName);
        using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        var content = await reader.ReadToEndAsync();

        return new MarkdownDocument
        {
            FilePath = MarkdownFileConventions.ExampleDocumentName,
            FileName = MarkdownFileConventions.ExampleDocumentName,
            FileSizeBytes = Encoding.UTF8.GetByteCount(content),
            Content = content,
            IsUntitled = false,
            EncodingName = reader.CurrentEncoding.WebName,
            NewLine = MarkdownFileConventions.DetectNewLine(content)
        };
    }

    public async Task<MarkdownDocument> LoadDocumentAsync(string filePath, CancellationToken cancellationToken = default)
    {
        MarkdownFileConventions.ValidateExtension(filePath);

        var fileInfo = new FileInfo(filePath);
        if (!fileInfo.Exists)
        {
            throw new FileNotFoundException("The markdown document could not be found.", filePath);
        }

        using var access = _fileAccessService.CreateAccessScope(filePath);

        byte[] bytes;
        try
        {
            bytes = await File.ReadAllBytesAsync(filePath, cancellationToken);
        }
        catch (UnauthorizedAccessException ex)
        {
            _logger.LogError(ex, "Access denied while reading file {FilePath}", filePath);
            throw;
        }

        var encoding = MarkdownFileConventions.DetectEncoding(bytes);
        var content = encoding.GetString(bytes);

        _logger.LogInformation(
            "Loaded markdown document. FileName: {FileName}, FilePath: {FilePath}, SizeKB: {SizeKb:F2}, LastModified: {LastModified}, Encoding: {Encoding}",
            fileInfo.Name,
            fileInfo.FullName,
            fileInfo.Length / 1024d,
            fileInfo.LastWriteTimeUtc,
            encoding.WebName);

        return new MarkdownDocument
        {
            FilePath = fileInfo.FullName,
            FileName = fileInfo.Name,
            FileSizeBytes = fileInfo.Length,
            LastModified = fileInfo.LastWriteTimeUtc,
            Content = content,
            IsUntitled = false,
            EncodingName = encoding.WebName,
            NewLine = MarkdownFileConventions.DetectNewLine(content)
        };
    }

    public Task<MarkdownDocument> CreateUntitledDocumentAsync(string? suggestedName = null)
    {
        var fileName = MarkdownFileConventions.EnsureValidFileName(suggestedName, allowEmpty: true);
        if (string.IsNullOrWhiteSpace(fileName))
        {
            fileName = "Untitled.mds";
        }

        fileName = MarkdownFileConventions.EnsureMarkdownExtension(fileName);

        return Task.FromResult(new MarkdownDocument
        {
            FilePath = string.Empty,
            FileName = fileName,
            Content = string.Empty,
            FileSizeBytes = 0,
            LastModified = null,
            IsUntitled = true,
            EncodingName = Encoding.UTF8.WebName,
            NewLine = Environment.NewLine
        });
    }

    public async Task<SaveDocumentResult> SaveAsync(EditorDocumentState document, CancellationToken cancellationToken = default)
    {
        if (document.IsUntitled || string.IsNullOrWhiteSpace(document.FilePath))
        {
            throw new InvalidOperationException("Save requires a concrete file path.");
        }

        MarkdownFileConventions.ValidateExtension(document.FilePath);

        using var access = _fileAccessService.CreateAccessScope(document.FilePath);

        try
        {
            await File.WriteAllTextAsync(
                document.FilePath,
                MarkdownFileConventions.NormalizeNewLines(document.Content, document.NewLine),
                MarkdownFileConventions.ResolveEncoding(document.EncodingName),
                cancellationToken);
        }
        catch (UnauthorizedAccessException ex)
        {
            _logger.LogError(ex, "Save failed because access was denied. FilePath: {FilePath}", document.FilePath);
            throw;
        }
        catch (IOException ex)
        {
            _logger.LogError(ex, "Save failed because the file is locked or unavailable. FilePath: {FilePath}", document.FilePath);
            throw;
        }

        var fileInfo = new FileInfo(document.FilePath);
        _logger.LogInformation(
            "Saved markdown document. FileName: {FileName}, FilePath: {FilePath}, SizeKB: {SizeKb:F2}, LastModified: {LastModified}",
            fileInfo.Name,
            fileInfo.FullName,
            fileInfo.Length / 1024d,
            fileInfo.LastWriteTimeUtc);

        return new SaveDocumentResult
        {
            FilePath = fileInfo.FullName,
            FileName = fileInfo.Name,
            FileSizeBytes = fileInfo.Length,
            LastModified = fileInfo.LastWriteTimeUtc
        };
    }
}
