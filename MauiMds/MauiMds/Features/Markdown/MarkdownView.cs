using System.Diagnostics;
using MauiMds.Models;
using Microsoft.Extensions.Logging;

namespace MauiMds.Features.Markdown;

public sealed class MarkdownView : ContentView
{
    private const int IncrementalRenderBatchSize = 4;

    public static readonly BindableProperty BlocksProperty = BindableProperty.Create(
        nameof(Blocks),
        typeof(IReadOnlyList<MarkdownBlock>),
        typeof(MarkdownView),
        default(IReadOnlyList<MarkdownBlock>),
        propertyChanged: OnBlocksChanged);

    public static readonly BindableProperty SourceFilePathProperty = BindableProperty.Create(
        nameof(SourceFilePath),
        typeof(string),
        typeof(MarkdownView),
        string.Empty,
        propertyChanged: OnSourceFilePathChanged);

    public static readonly BindableProperty IsRenderingEnabledProperty = BindableProperty.Create(
        nameof(IsRenderingEnabled),
        typeof(bool),
        typeof(MarkdownView),
        true,
        propertyChanged: OnIsRenderingEnabledChanged);

    public static readonly BindableProperty InitialRenderLineCountProperty = BindableProperty.Create(
        nameof(InitialRenderLineCount),
        typeof(int),
        typeof(MarkdownView),
        20,
        propertyChanged: OnInitialRenderLineCountChanged);

    private readonly VerticalStackLayout _contentStack;
    private readonly MarkdownRenderer _renderer;
    private readonly MarkdownInlineFormatter _inlineFormatter;
    private ILogger<MarkdownView>? _logger;
    private CancellationTokenSource? _renderCancellationSource;
    private bool _hasPendingRender;
    private int _renderVersion;

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

    public IReadOnlyList<MarkdownBlock>? Blocks
    {
        get => (IReadOnlyList<MarkdownBlock>?)GetValue(BlocksProperty);
        set => SetValue(BlocksProperty, value);
    }

    public string SourceFilePath
    {
        get => (string)GetValue(SourceFilePathProperty);
        set => SetValue(SourceFilePathProperty, value);
    }

    public bool IsRenderingEnabled
    {
        get => (bool)GetValue(IsRenderingEnabledProperty);
        set => SetValue(IsRenderingEnabledProperty, value);
    }

    public int InitialRenderLineCount
    {
        get => (int)GetValue(InitialRenderLineCountProperty);
        set => SetValue(InitialRenderLineCountProperty, value);
    }

    private static void OnBlocksChanged(BindableObject bindable, object? oldValue, object? newValue)
    {
        var view = (MarkdownView)bindable;

        view.RequestRender("blocks property changed");
    }

    private static void OnSourceFilePathChanged(BindableObject bindable, object? oldValue, object? newValue)
    {
        ((MarkdownView)bindable).RequestRender("source file path changed");
    }

    private static void OnIsRenderingEnabledChanged(BindableObject bindable, object? oldValue, object? newValue)
    {
        var view = (MarkdownView)bindable;
        var isEnabled = (bool)(newValue ?? true);

        if (isEnabled)
        {
            view._logger?.LogInformation(
                "MarkdownView rendering enabled. PendingRender: {PendingRender}, BlockCount: {BlockCount}, SourceFilePath: {SourceFilePath}",
                view._hasPendingRender,
                view.Blocks?.Count ?? 0,
                view.SourceFilePath);

            view.RequestRender("rendering enabled");
            return;
        }

        view._logger?.LogInformation(
            "MarkdownView rendering disabled. BlockCount: {BlockCount}, SourceFilePath: {SourceFilePath}",
            view.Blocks?.Count ?? 0,
            view.SourceFilePath);
    }

    private static void OnInitialRenderLineCountChanged(BindableObject bindable, object? oldValue, object? newValue)
    {
        ((MarkdownView)bindable).RequestRender("initial render line count changed");
    }

    private void RequestRender(string reason)
    {
        if (!IsRenderingEnabled)
        {
            _hasPendingRender = true;
            _logger?.LogInformation(
                "MarkdownView render deferred. Reason: {Reason}, BlockCount: {BlockCount}, SourceFilePath: {SourceFilePath}",
                reason,
                Blocks?.Count ?? 0,
                SourceFilePath);
            return;
        }

        _hasPendingRender = false;
        _renderCancellationSource?.Cancel();
        _renderCancellationSource?.Dispose();
        _renderCancellationSource = new CancellationTokenSource();
        var token = _renderCancellationSource.Token;

        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(1, token);
                token.ThrowIfCancellationRequested();
                await MainThread.InvokeOnMainThreadAsync(async () => await RenderMarkdownCoreAsync(token));
            }
            catch (OperationCanceledException)
            {
            }
        }, token);
    }

    private async Task RenderMarkdownCoreAsync(CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        var renderVersion = Interlocked.Increment(ref _renderVersion);
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

        if (Blocks.Count == 0)
        {
            CompleteRender(stopwatch);
            return;
        }

        var context = new MarkdownRenderContext
        {
            SourceFilePath = SourceFilePath,
            InlineFormatter = _inlineFormatter
        };

        var initialBatchEnd = CalculateInitialBatchEnd(Blocks, Math.Max(5, InitialRenderLineCount));
        RenderRange(0, initialBatchEnd, context);
        _contentStack.InvalidateMeasure();
        InvalidateMeasure();

        var index = initialBatchEnd;
        while (index < Blocks.Count)
        {
            cancellationToken.ThrowIfCancellationRequested();

            await Task.Yield();

            if (renderVersion != _renderVersion || !IsRenderingEnabled)
            {
                _logger?.LogInformation(
                    "MarkdownView incremental render canceled. RenderVersion: {RenderVersion}, CurrentVersion: {CurrentVersion}, SourceFilePath: {SourceFilePath}",
                    renderVersion,
                    _renderVersion,
                    SourceFilePath);
                return;
            }

            var batchEnd = Math.Min(Blocks.Count, index + IncrementalRenderBatchSize);
            RenderRange(index, batchEnd, context);
            _contentStack.InvalidateMeasure();
            InvalidateMeasure();
            index = batchEnd;
        }

        CompleteRender(stopwatch);
    }

    private void RenderRange(int startIndex, int endIndex, MarkdownRenderContext context)
    {
        if (Blocks is null)
        {
            return;
        }

        for (var index = startIndex; index < endIndex; index++)
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
    }

    private static int CalculateInitialBatchEnd(IReadOnlyList<MarkdownBlock> blocks, int targetLineCount)
    {
        if (blocks.Count == 0)
        {
            return 0;
        }

        var lineCount = 0;
        for (var index = 0; index < blocks.Count; index++)
        {
            lineCount += EstimateBlockLineCount(blocks[index]);
            if (lineCount >= targetLineCount)
            {
                return index + 1;
            }
        }

        return blocks.Count;
    }

    private static int EstimateBlockLineCount(MarkdownBlock block)
    {
        return block.Type switch
        {
            BlockType.Table => Math.Max(2, 1 + block.TableRows.Count),
            BlockType.HorizontalRule => 1,
            BlockType.Image => 1,
            _ => CountLines(block.Content)
        };
    }

    private static int CountLines(string? content)
    {
        if (string.IsNullOrEmpty(content))
        {
            return 1;
        }

        var lines = 1;
        foreach (var character in content)
        {
            if (character == '\n')
            {
                lines++;
            }
        }

        return lines;
    }

    private void CompleteRender(Stopwatch stopwatch)
    {
        _logger?.LogInformation(
            "MarkdownView rendering completed. RenderedChildCount: {RenderedChildCount}, SourceFilePath: {SourceFilePath}, ElapsedMs: {ElapsedMs}",
            _contentStack.Children.Count,
            SourceFilePath,
            stopwatch.ElapsedMilliseconds);
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
