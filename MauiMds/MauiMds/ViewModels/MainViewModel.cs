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

    private readonly IMarkdownDocumentService _documentService;
    private readonly ILogger<MainViewModel> _logger;
    private readonly MdsParser _parser;
    private bool _isInitialized;
    private bool _isOpeningDocument;
    private string _filePath = string.Empty;

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
                ApplyDocument(document);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load the initial markdown document.");
            FilePath = "Unable to load example.mds";
            ParsedBlocks.Clear();
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
                return;
            }

            ApplyDocument(document);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to open the selected markdown document.");
        }
        finally
        {
            _isOpeningDocument = false;
            (OpenFileCommand as Command)?.ChangeCanExecute();
        }
    }

    private void ApplyDocument(MarkdownDocument document)
    {
        _logger.LogInformation("Applying markdown document from {FilePath}. CharacterCount: {CharacterCount}", document.FilePath, document.Content.Length);
        var blocks = _parser.Parse(document.Content);
        ParsedBlocks.Clear();
        foreach (var block in blocks)
        {
            ParsedBlocks.Add(block);
        }

        FilePath = document.FilePath;
        _logger.LogInformation("Parsed markdown document into {BlockCount} blocks.", ParsedBlocks.Count);
    }

    protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        _logger.LogDebug("Property changed: {PropertyName}", propertyName);
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
