using System.Diagnostics;
using MauiMds.Models;
using Microsoft.Extensions.Logging;

namespace MauiMds.Features.Markdown;

public sealed class MarkdownView : ContentView
{
    private const int IncrementalRenderBatchLineBudget = 10;
    private const int MinimumInitialRenderLineCount = 12;
    private const int MinimumEstimatedViewportLines = 16;
    private static readonly TimeSpan IncrementalRenderBatchDelay = TimeSpan.FromMilliseconds(12);

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
    private int _requestedRenderVersion;

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
        var view = (MarkdownView)bindable;
        view._logger?.LogInformation(
            "MarkdownView source file path updated without rerender. SourceFilePath: {SourceFilePath}, BlockCount: {BlockCount}",
            view.SourceFilePath,
            view.Blocks?.Count ?? 0);
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
        var requestVersion = Interlocked.Increment(ref _requestedRenderVersion);

        if (!IsRenderingEnabled)
        {
            _hasPendingRender = true;
            _logger?.LogInformation(
                "MarkdownView render deferred. Reason: {Reason}, RequestVersion: {RequestVersion}, BlockCount: {BlockCount}, SourceFilePath: {SourceFilePath}",
                reason,
                requestVersion,
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
                await MainThread.InvokeOnMainThreadAsync(async () => await RenderMarkdownCoreAsync(token, requestVersion));
            }
            catch (OperationCanceledException)
            {
            }
        }, token);
    }

    private async Task RenderMarkdownCoreAsync(CancellationToken cancellationToken, int requestVersion)
    {
        if (requestVersion != _requestedRenderVersion)
        {
            _logger?.LogInformation(
                "MarkdownView render skipped before start because a newer request exists. RequestVersion: {RequestVersion}, CurrentRequestVersion: {CurrentRequestVersion}, SourceFilePath: {SourceFilePath}",
                requestVersion,
                _requestedRenderVersion,
                SourceFilePath);
            return;
        }

        var stopwatch = Stopwatch.StartNew();
        var renderVersion = Interlocked.Increment(ref _renderVersion);
        _logger?.LogInformation(
            "MarkdownView rendering started. RequestVersion: {RequestVersion}, BlockCount: {BlockCount}, SourceFilePath: {SourceFilePath}",
            requestVersion,
            Blocks?.Count ?? 0,
            SourceFilePath);

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

        if (requestVersion != _requestedRenderVersion)
        {
            _logger?.LogInformation(
                "MarkdownView render canceled before clearing old content. RequestVersion: {RequestVersion}, CurrentRequestVersion: {CurrentRequestVersion}, SourceFilePath: {SourceFilePath}",
                requestVersion,
                _requestedRenderVersion,
                SourceFilePath);
            return;
        }

        _contentStack.Children.Clear();

        var blocks = Blocks;
        var context = new MarkdownRenderContext
        {
            SourceFilePath = SourceFilePath,
            InlineFormatter = _inlineFormatter
        };

        var initialBatchEnd = CalculateBatchEnd(
            blocks,
            0,
            CalculateInitialRenderTargetLineCount());

        _logger?.LogDebug(
            "MarkdownView initial render batch prepared. RequestVersion: {RequestVersion}, InitialBatchBlockCount: {InitialBatchBlockCount}, TotalBlockCount: {TotalBlockCount}, InitialTargetLines: {InitialTargetLines}, SourceFilePath: {SourceFilePath}",
            requestVersion,
            initialBatchEnd,
            blocks.Count,
            CalculateInitialRenderTargetLineCount(),
            SourceFilePath);

        RenderRange(blocks, 0, initialBatchEnd, context);
        _contentStack.InvalidateMeasure();
        InvalidateMeasure();

        var index = initialBatchEnd;
        if (index < blocks.Count)
        {
            _ = RenderRemainingBatchesAsync(
                blocks,
                context,
                index,
                renderVersion,
                requestVersion,
                stopwatch,
                cancellationToken);
            return;
        }

        CompleteRender(stopwatch);
    }

    private async Task RenderRemainingBatchesAsync(
        IReadOnlyList<MarkdownBlock> blocks,
        MarkdownRenderContext context,
        int startIndex,
        int renderVersion,
        int requestVersion,
        Stopwatch stopwatch,
        CancellationToken cancellationToken)
    {
        var index = startIndex;

        while (index < blocks.Count)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await Task.Delay(IncrementalRenderBatchDelay, cancellationToken);

            var batchStart = index;
            var batchEnd = CalculateBatchEnd(blocks, batchStart, IncrementalRenderBatchLineBudget);
            var batchStopwatch = Stopwatch.StartNew();

            await MainThread.InvokeOnMainThreadAsync(() =>
            {
                if (renderVersion != _renderVersion || requestVersion != _requestedRenderVersion || !IsRenderingEnabled)
                {
                    _logger?.LogInformation(
                        "MarkdownView incremental render canceled. RenderVersion: {RenderVersion}, CurrentVersion: {CurrentVersion}, RequestVersion: {RequestVersion}, CurrentRequestVersion: {CurrentRequestVersion}, SourceFilePath: {SourceFilePath}",
                        renderVersion,
                        _renderVersion,
                        requestVersion,
                        _requestedRenderVersion,
                        SourceFilePath);
                    return;
                }

                RenderRange(blocks, batchStart, batchEnd, context);
                _contentStack.InvalidateMeasure();
                InvalidateMeasure();
            });

            batchStopwatch.Stop();
            _logger?.LogTrace(
                "MarkdownView incremental batch rendered. BatchStart: {BatchStart}, BatchEnd: {BatchEnd}, BatchBlockCount: {BatchBlockCount}, ElapsedMs: {ElapsedMs}, SourceFilePath: {SourceFilePath}",
                batchStart,
                batchEnd,
                batchEnd - batchStart,
                batchStopwatch.ElapsedMilliseconds,
                SourceFilePath);

            if (renderVersion != _renderVersion || requestVersion != _requestedRenderVersion || !IsRenderingEnabled)
            {
                return;
            }

            index = batchEnd;
        }

        await MainThread.InvokeOnMainThreadAsync(() => CompleteRender(stopwatch));
    }

    private void RenderRange(IReadOnlyList<MarkdownBlock> blocks, int startIndex, int endIndex, MarkdownRenderContext context)
    {
        var rangeStopwatch = Stopwatch.StartNew();
        for (var index = startIndex; index < endIndex; index++)
        {
            var block = blocks[index];

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

        rangeStopwatch.Stop();
        _logger?.LogTrace(
            "MarkdownView rendered block range. StartIndex: {StartIndex}, EndIndex: {EndIndex}, Count: {Count}, ElapsedMs: {ElapsedMs}, SourceFilePath: {SourceFilePath}",
            startIndex,
            endIndex,
            endIndex - startIndex,
            rangeStopwatch.ElapsedMilliseconds,
            SourceFilePath);
    }

    private int CalculateInitialRenderTargetLineCount()
    {
        var preferenceTarget = Math.Max(MinimumInitialRenderLineCount, InitialRenderLineCount);
        var viewportTarget = Height > 0
            ? Math.Max(MinimumEstimatedViewportLines, (int)Math.Ceiling(Height / 26d))
            : MinimumEstimatedViewportLines;

        return Math.Max(preferenceTarget, viewportTarget);
    }

    private static int CalculateBatchEnd(IReadOnlyList<MarkdownBlock> blocks, int startIndex, int targetLineCount)
    {
        if (blocks.Count == 0 || startIndex >= blocks.Count)
        {
            return startIndex;
        }

        var lineCount = 0;
        for (var index = startIndex; index < blocks.Count; index++)
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
        _inlineFormatter.AttachLogger(Handler?.MauiContext?.Services.GetService<ILogger<MarkdownInlineFormatter>>());
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
