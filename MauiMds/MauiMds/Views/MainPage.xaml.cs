using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using MauiMds.Models;
using MauiMds.ViewModels;
using Microsoft.Extensions.Logging;

namespace MauiMds.Views;

public partial class MainPage : ContentPage
{
    private readonly ILogger<MainPage> _logger;

    public MainPage(MainViewModel vm, ILogger<MainPage> logger)
    {
        _logger = logger;
        _logger.LogInformation("Constructing MainPage.");

        try
        {
            InitializeComponent();
            BindingContext = vm;
            vm.PropertyChanged += OnViewModelPropertyChanged;
            vm.DocumentApplied += OnDocumentApplied;
            vm.ParsedBlocks.CollectionChanged += OnParsedBlocksChanged;
            Loaded += OnLoaded;
            _logger.LogInformation("MainPage initialized successfully.");
        }
        catch (Exception ex)
        {
            _logger.LogCritical(ex, "MainPage initialization failed.");
            throw;
        }
    }

    private async void OnLoaded(object? sender, EventArgs e)
    {
        _logger.LogInformation("MainPage loaded.");

        if (BindingContext is MainViewModel vm)
        {
            await vm.InitializeAsync();
            RefreshHeader(vm);
            RenderMarkdown(vm.ParsedBlocks);
        }
        else
        {
            _logger.LogWarning("MainPage loaded without a MainViewModel binding context.");
        }
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (BindingContext is not MainViewModel vm)
        {
            return;
        }

        if (e.PropertyName is nameof(MainViewModel.FilePath) or nameof(MainViewModel.FileName))
        {
            RefreshHeader(vm);
        }
    }

    private void OnDocumentApplied(object? sender, MarkdownDocument document)
    {
        if (BindingContext is not MainViewModel vm)
        {
            return;
        }

        RefreshHeader(vm);
        RenderMarkdown(vm.ParsedBlocks);
        _logger.LogInformation(
            "Document applied notification received. FileName: {FileName}, DisplayedFilePath: {DisplayedFilePath}, ContentChildren: {ContentChildren}",
            document.FileName ?? vm.FileName,
            vm.FilePath,
            ContentStack.Children.Count);
    }

    private void OnParsedBlocksChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (BindingContext is MainViewModel vm)
        {
            RenderMarkdown(vm.ParsedBlocks);
        }
    }

    private void RenderMarkdown(ObservableCollection<MarkdownBlock> blocks)
    {
        _logger.LogInformation("Rendering markdown blocks. BlockCount: {BlockCount}", blocks.Count);
        ContentStack.Children.Clear();

        foreach (var block in blocks)
        {
            var label = new Label
            {
                TextColor = Colors.Black,
                Margin = new Thickness(0, 0, 0, 8)
            };

            label.SetAppThemeColor(Label.TextColorProperty, Color.FromArgb("#161616"), Color.FromArgb("#F3EDE2"));

            switch (block.Type)
            {
                case BlockType.Header:
                    label.Text = block.Content;
                    label.FontSize = block.HeaderLevel == 1 ? 32 : 24;
                    label.FontAttributes = FontAttributes.Bold;
                    label.Margin = new Thickness(0, block.HeaderLevel == 1 ? 4 : 16, 0, 8);
                    break;

                case BlockType.Paragraph:
                    label.Text = block.Content;
                    label.FontSize = 18;
                    break;

                case BlockType.BulletListItem:
                    label.Text = $"• {block.Content}";
                    label.FontSize = 18;
                    label.Margin = new Thickness(20, 0, 0, 4);
                    break;
            }

            ContentStack.Children.Add(label);
        }

        _logger.LogInformation(
            "Rendered markdown blocks to the UI. ChildCount: {ChildCount}, FirstBlockPreview: {FirstBlockPreview}",
            ContentStack.Children.Count,
            blocks.FirstOrDefault()?.Content);
    }

    private void RefreshHeader(MainViewModel vm)
    {
        FileNameLabel.Text = vm.FileName;
        FilePathLabel.Text = vm.FilePath;

        _logger.LogInformation(
            "Header refreshed. FileName: {FileName}, FilePath: {FilePath}",
            FileNameLabel.Text,
            FilePathLabel.Text);
    }
}
