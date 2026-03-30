using System.Collections.ObjectModel;
using System.Collections.Specialized;
using MauiMds.Models;
using Microsoft.Extensions.Logging;

namespace MauiMds.Markdown;

public sealed class MarkdownView : ContentView
{
    public static readonly BindableProperty BlocksProperty = BindableProperty.Create(
        nameof(Blocks),
        typeof(ObservableCollection<MarkdownBlock>),
        typeof(MarkdownView),
        default(ObservableCollection<MarkdownBlock>),
        propertyChanged: OnBlocksChanged);

    public static readonly BindableProperty SourceFilePathProperty = BindableProperty.Create(
        nameof(SourceFilePath),
        typeof(string),
        typeof(MarkdownView),
        string.Empty,
        propertyChanged: OnSourceFilePathChanged);

    private readonly VerticalStackLayout _contentStack;
    private readonly MarkdownRenderer _renderer;
    private readonly MarkdownInlineFormatter _inlineFormatter;
    private ILogger<MarkdownView>? _logger;

    public MarkdownView()
    {
        _inlineFormatter = new MarkdownInlineFormatter();
        _renderer = new MarkdownRenderer(
        [
            new FrontMatterBlockRenderer(),
            new HeaderBlockRenderer(),
            new ParagraphBlockRenderer(),
            new ListBlockRenderer(),
            new BlockQuoteBlockRenderer(),
            new CodeBlockRenderer(),
            new TableBlockRenderer(),
            new HorizontalRuleBlockRenderer(),
            new ImageBlockRenderer(),
            new FootnoteBlockRenderer()
        ]);

        _contentStack = new VerticalStackLayout
        {
            Spacing = 12
        };

        Content = new ScrollView
        {
            Content = _contentStack
        };

        HandlerChanged += OnHandlerChanged;
    }

    public ObservableCollection<MarkdownBlock>? Blocks
    {
        get => (ObservableCollection<MarkdownBlock>?)GetValue(BlocksProperty);
        set => SetValue(BlocksProperty, value);
    }

    public string SourceFilePath
    {
        get => (string)GetValue(SourceFilePathProperty);
        set => SetValue(SourceFilePathProperty, value);
    }

    private static void OnBlocksChanged(BindableObject bindable, object? oldValue, object? newValue)
    {
        var view = (MarkdownView)bindable;

        if (oldValue is ObservableCollection<MarkdownBlock> oldCollection)
        {
            oldCollection.CollectionChanged -= view.OnBlocksCollectionChanged;
        }

        if (newValue is ObservableCollection<MarkdownBlock> newCollection)
        {
            newCollection.CollectionChanged += view.OnBlocksCollectionChanged;
        }

        view.RenderMarkdown();
    }

    private static void OnSourceFilePathChanged(BindableObject bindable, object? oldValue, object? newValue)
    {
        ((MarkdownView)bindable).RenderMarkdown();
    }

    private void OnBlocksCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        _logger?.LogInformation(
            "MarkdownView observed block collection change. Action: {Action}, BlockCount: {BlockCount}, SourceFilePath: {SourceFilePath}",
            e.Action,
            Blocks?.Count ?? 0,
            SourceFilePath);

        RenderMarkdown();
    }

    private void RenderMarkdown()
    {
        _logger?.LogInformation(
            "MarkdownView rendering started. BlockCount: {BlockCount}, SourceFilePath: {SourceFilePath}",
            Blocks?.Count ?? 0,
            SourceFilePath);

        _contentStack.Children.Clear();

        if (Blocks is null)
        {
            _logger?.LogWarning("MarkdownView render skipped because Blocks is null.");
            return;
        }

        var context = new MarkdownRenderContext
        {
            SourceFilePath = SourceFilePath,
            InlineFormatter = _inlineFormatter
        };

        for (var index = 0; index < Blocks.Count; index++)
        {
            var block = Blocks[index];

            try
            {
                var view = _renderer.RenderBlock(block, context);
                if (view is not null)
                {
                    _contentStack.Children.Add(view);
                }
            }
            catch (Exception ex)
            {
                _logger?.LogError(
                    ex,
                    "Failed to render markdown block. Index: {BlockIndex}, Type: {BlockType}, ContentPreview: {ContentPreview}",
                    index,
                    block.Type,
                    BuildContentPreview(block));

                _contentStack.Children.Add(CreateFallbackBlockView(block, index));
            }
        }

        _contentStack.InvalidateMeasure();
        InvalidateMeasure();

        _logger?.LogInformation(
            "MarkdownView rendering completed. RenderedChildCount: {RenderedChildCount}, SourceFilePath: {SourceFilePath}",
            _contentStack.Children.Count,
            SourceFilePath);
    }

    private void OnHandlerChanged(object? sender, EventArgs e)
    {
        _logger = Handler?.MauiContext?.Services.GetService<ILogger<MarkdownView>>();
    }

    private static string BuildContentPreview(MarkdownBlock block)
    {
        var text = string.IsNullOrWhiteSpace(block.Content) ? block.ImageSource : block.Content;
        if (string.IsNullOrWhiteSpace(text))
        {
            return "(empty)";
        }

        return text.Length <= 120 ? text : $"{text[..117]}...";
    }

    private View CreateFallbackBlockView(MarkdownBlock block, int index)
    {
        var message = MarkdownViewFactory.CreateBaseLabel();
        message.FontSize = 13;
        message.FontAttributes = FontAttributes.Italic;
        message.Text = $"Unable to render block {index + 1} ({block.Type}).";
        message.Margin = new Thickness(0);

        var details = MarkdownViewFactory.CreateBaseLabel();
        details.FontSize = 14;
        details.Text = BuildContentPreview(block);
        details.Margin = new Thickness(0, 8, 0, 0);

        var stack = new VerticalStackLayout
        {
            Spacing = 0,
            Children =
            {
                message,
                details
            }
        };

        var border = MarkdownViewFactory.CreateThemedBorder(stack, new Thickness(14, 12), new Thickness(0, 4, 0, 10));
        border.SetAppThemeColor(VisualElement.BackgroundColorProperty, Color.FromArgb("#FBE0DD"), Color.FromArgb("#432524"));
        border.SetAppThemeColor(Border.StrokeProperty, Color.FromArgb("#D27A72"), Color.FromArgb("#B65A54"));
        return border;
    }
}
