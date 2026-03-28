using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using MauiMds.Models;
using MauiMds.Processors;
using MauiMds.Services;
using Microsoft.Extensions.Logging;
using System.Windows.Input;

namespace MauiMds.ViewModels;

public class MainViewModel : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;
    public event EventHandler<MarkdownDocument>? DocumentApplied;

    private readonly IMarkdownDocumentService _documentService;
    private readonly ILogger<MainViewModel> _logger;
    private readonly MdsParser _parser;
    private bool _isInitialized;
    private bool _isOpeningDocument;
    private string _filePath = string.Empty;
    private string _inlineErrorMessage = string.Empty;

    public ObservableCollection<MarkdownBlock> ParsedBlocks { get; } = new();

    public ICommand OpenFileCommand { get; }

    public string FilePath
    {
        get => _filePath;
        private set
        {
            if (_filePath == value)
            {
                return;
            }

            _filePath = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(FileName));
        }
    }

    public string FileName => string.IsNullOrWhiteSpace(FilePath) ? "No file loaded" : Path.GetFileName(FilePath);

    public string InlineErrorMessage
    {
        get => _inlineErrorMessage;
        private set
        {
            if (_inlineErrorMessage == value)
            {
                return;
            }

            _inlineErrorMessage = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(HasInlineError));
        }
    }

    public bool HasInlineError => !string.IsNullOrWhiteSpace(InlineErrorMessage);

    public MainViewModel(
        MdsParser parser,
        IMarkdownDocumentService documentService,
        ILogger<MainViewModel> logger)
    {
        _parser = parser;
        _documentService = documentService;
        _logger = logger;
        _logger.LogInformation("MainViewModel created.");
        OpenFileCommand = new Command(async () => await OpenFileAsync(), () => !_isOpeningDocument);
    }

    public async Task InitializeAsync()
    {
        if (_isInitialized)
        {
            return;
        }

        _isInitialized = true;

        try
        {
            var document = await _documentService.LoadInitialDocumentAsync();
            if (document is not null)
            {
                await ApplyDocumentAsync(document);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load the initial markdown document.");
            await MainThread.InvokeOnMainThreadAsync(() =>
            {
                FilePath = "Unable to load example.mds";
                ParsedBlocks.Clear();
                InlineErrorMessage = "The bundled example document could not be loaded.";
            });
        }
    }

    private async Task OpenFileAsync()
    {
        if (_isOpeningDocument)
        {
            return;
        }

        _isOpeningDocument = true;
        (OpenFileCommand as Command)?.ChangeCanExecute();

        try
        {
            var document = await _documentService.PickDocumentAsync();
            if (document is null)
            {
                await ClearInlineError();
                return;
            }

            await ApplyDocumentAsync(document);
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogWarning(ex, "The selected file could not be opened because it is not a supported markdown format.");
            await MainThread.InvokeOnMainThreadAsync(() =>
            {
                InlineErrorMessage = ex.Message;
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to open the selected markdown document.");
            await MainThread.InvokeOnMainThreadAsync(() =>
            {
                InlineErrorMessage = "The selected file could not be opened.";
            });
        }
        finally
        {
            _isOpeningDocument = false;
            (OpenFileCommand as Command)?.ChangeCanExecute();
        }
    }

    private async Task ApplyDocumentAsync(MarkdownDocument document)
    {
        _logger.LogDebug(
            "Applying markdown document. FileName: {FileName}, FilePath: {FilePath}, SizeKB: {SizeKb:F2}, LastModified: {LastModified}, CharacterCount: {CharacterCount}",
            document.FileName ?? Path.GetFileName(document.FilePath),
            document.FilePath,
            (document.FileSizeBytes ?? 0) / 1024d,
            document.LastModified,
            document.Content.Length);

        var blocks = _parser.Parse(document.Content);

        await MainThread.InvokeOnMainThreadAsync(() =>
        {
            InlineErrorMessage = string.Empty;
            ParsedBlocks.Clear();
            foreach (var block in blocks)
            {
                ParsedBlocks.Add(block);
            }

            FilePath = document.FilePath;
            _logger.LogDebug(
                "Applied markdown document to the UI. DisplayedFilePath: {DisplayedFilePath}, BlockCount: {BlockCount}",
                FilePath,
                ParsedBlocks.Count);
            DocumentApplied?.Invoke(this, document);
        });

        _logger.LogInformation(
            "Loaded markdown document successfully. FileName: {FileName}, BlockCount: {BlockCount}",
            document.FileName ?? Path.GetFileName(document.FilePath),
            blocks.Count);
    }

    protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        _logger.LogDebug("Property changed: {PropertyName}", propertyName);
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    private Task ClearInlineError()
    {
        return MainThread.InvokeOnMainThreadAsync(() =>
        {
            InlineErrorMessage = string.Empty;
        });
    }
}
