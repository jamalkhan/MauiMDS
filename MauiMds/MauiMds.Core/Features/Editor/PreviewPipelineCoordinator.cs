using System.Threading;
using System.Threading.Tasks;
using MauiMds.Models;
using MauiMds.Services;
using Microsoft.Extensions.Logging;

namespace MauiMds.Features.Editor;

public sealed class PreviewPipelineCoordinator : IPreviewPipelineCoordinator
{
    private readonly IDocumentWorkflowService _documentWorkflowController;
    private readonly IDelayScheduler _delayScheduler;
    private readonly IClock _clock;
    private readonly ILogger<PreviewPipelineCoordinator> _logger;

    private CancellationTokenSource? _previewDelayCancellationSource;
    private CancellationTokenSource? _externalReloadCancellationSource;
    private long _previewGeneration;
    private DateTimeOffset _lastSaveUtc;

    public PreviewPipelineCoordinator(
        IDocumentWorkflowService documentWorkflowController,
        IDelayScheduler delayScheduler,
        IClock clock,
        ILogger<PreviewPipelineCoordinator> logger)
    {
        _documentWorkflowController = documentWorkflowController;
        _delayScheduler = delayScheduler;
        _clock = clock;
        _logger = logger;
    }

    public void MarkSaved()
    {
        _lastSaveUtc = _clock.UtcNow;
    }

    public bool ShouldSuppressExternalReload(bool isSavingDocument, TimeSpan suppressionWindow)
    {
        return isSavingDocument || _clock.UtcNow - _lastSaveUtc < suppressionWindow;
    }

    public Task SchedulePreviewAsync(
        MarkdownDocument snapshot,
        EditorViewMode currentViewMode,
        TimeSpan delay,
        Func<MarkdownDocument, DocumentPreviewResult, TimeSpan, Task> applyPreviewAsync)
    {
        var token = ResetPreviewCancellationToken();
        var generation = Interlocked.Increment(ref _previewGeneration);

        return SchedulePreviewInternalAsync(snapshot, currentViewMode, delay, generation, token, applyPreviewAsync);
    }

    public Task ScheduleExternalReloadAsync(TimeSpan delay, Func<Task> reloadAsync)
    {
        var token = ResetExternalReloadCancellationToken();
        return ScheduleExternalReloadInternalAsync(delay, token, reloadAsync);
    }

    private async Task SchedulePreviewInternalAsync(
        MarkdownDocument snapshot,
        EditorViewMode currentViewMode,
        TimeSpan delay,
        long generation,
        CancellationToken token,
        Func<MarkdownDocument, DocumentPreviewResult, TimeSpan, Task> applyPreviewAsync)
    {
        try
        {
            await _delayScheduler.DelayAsync(delay, token);
            token.ThrowIfCancellationRequested();

            if (generation != Interlocked.Read(ref _previewGeneration))
            {
                return;
            }

            var parseStartedUtc = _clock.UtcNow;
            var preview = _documentWorkflowController.PreparePreview(snapshot, currentViewMode);
            var parseElapsed = _clock.UtcNow - parseStartedUtc;
            if (generation != Interlocked.Read(ref _previewGeneration))
            {
                return;
            }

            await applyPreviewAsync(snapshot, preview, parseElapsed);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Preview pipeline execution failed for {FilePath}", snapshot.FilePath);
            throw;
        }
    }

    private async Task ScheduleExternalReloadInternalAsync(TimeSpan delay, CancellationToken token, Func<Task> reloadAsync)
    {
        try
        {
            await _delayScheduler.DelayAsync(delay, token);
            token.ThrowIfCancellationRequested();
            await reloadAsync();
        }
        catch (OperationCanceledException)
        {
        }
    }

    public void CancelPreview()
    {
        _previewDelayCancellationSource?.Cancel();
        _previewDelayCancellationSource?.Dispose();
        _previewDelayCancellationSource = null;
    }

    public void CancelExternalReload()
    {
        _externalReloadCancellationSource?.Cancel();
        _externalReloadCancellationSource?.Dispose();
        _externalReloadCancellationSource = null;
    }

    public void Dispose()
    {
        CancelPreview();
        CancelExternalReload();
    }

    private CancellationToken ResetPreviewCancellationToken()
    {
        CancelPreview();
        _previewDelayCancellationSource = new CancellationTokenSource();
        return _previewDelayCancellationSource.Token;
    }

    private CancellationToken ResetExternalReloadCancellationToken()
    {
        CancelExternalReload();
        _externalReloadCancellationSource = new CancellationTokenSource();
        return _externalReloadCancellationSource.Token;
    }
}
