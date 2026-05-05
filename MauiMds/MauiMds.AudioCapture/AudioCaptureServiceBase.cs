using Microsoft.Extensions.Logging;

namespace MauiMds.AudioCapture;

/// <summary>
/// Owns the Idle → Starting → Recording → Stopping → Idle state machine and the
/// concurrency lock that protects it. Subclasses supply platform-specific capture
/// start/stop/cleanup logic via the abstract methods below.
/// </summary>
public abstract class AudioCaptureServiceBase : IAudioCaptureService, IDisposable
{
    protected readonly ILogger Logger;

    private readonly SemaphoreSlim _stateLock = new(1, 1);
    private AudioCaptureState _state = AudioCaptureState.Idle;

    public AudioCaptureState State => _state;
    public string? LastStartWarning { get; protected set; }
    public event EventHandler<AudioCaptureState>? StateChanged;

    protected AudioCaptureServiceBase(ILogger logger) => Logger = logger;

    // ── Permission (platform-specific behaviour) ──────────────────────────────

    public abstract Task<AudioPermissionStatus> CheckMicrophonePermissionAsync();
    public abstract Task<AudioPermissionStatus> RequestMicrophonePermissionAsync();

    // ── State machine ─────────────────────────────────────────────────────────

    public async Task StartAsync(AudioCaptureOptions options, CancellationToken cancellationToken = default)
    {
        if (!options.CaptureMicrophone && !options.CaptureSystemAudio)
            throw new ArgumentException("At least one audio source must be enabled.", nameof(options));

        await _stateLock.WaitAsync(cancellationToken);
        try
        {
            if (_state != AudioCaptureState.Idle)
                throw new InvalidOperationException($"Cannot start capture from state {_state}.");

            SetState(AudioCaptureState.Starting);
            LastStartWarning = null;
            await StartCaptureAsync(options, cancellationToken);
            SetState(AudioCaptureState.Recording);
        }
        catch
        {
            await CleanupAfterFailedStartAsync();
            SetState(AudioCaptureState.Idle);
            throw;
        }
        finally
        {
            _stateLock.Release();
        }
    }

    public async Task<AudioCaptureResult> StopAsync()
    {
        // Hold the lock only for the state check and transition; encoding / file
        // finalisation can take seconds and must happen outside it.
        await _stateLock.WaitAsync();
        try
        {
            if (_state != AudioCaptureState.Recording)
                return new AudioCaptureResult
                {
                    Success = false,
                    ErrorMessage = $"Cannot stop — current state is {_state}."
                };
            SetState(AudioCaptureState.Stopping);
        }
        finally
        {
            _stateLock.Release();
        }

        try
        {
            return await StopCaptureAsync();
        }
        finally
        {
            SetState(AudioCaptureState.Idle);
        }
    }

    // ── Platform hooks ────────────────────────────────────────────────────────

    /// <summary>Start platform audio sources. Called inside the state lock after transitioning to Starting.</summary>
    protected abstract Task StartCaptureAsync(AudioCaptureOptions options, CancellationToken cancellationToken);

    /// <summary>Release any resources partially acquired during a failed StartCaptureAsync. State will be reset to Idle by the base.</summary>
    protected abstract Task CleanupAfterFailedStartAsync();

    /// <summary>Stop platform audio sources, finalise files, and return the result. Called outside the state lock.</summary>
    protected abstract Task<AudioCaptureResult> StopCaptureAsync();

    /// <summary>Release platform-specific resources. Called from Dispose().</summary>
    protected abstract void DisposeResources();

    // ── Shared helpers ────────────────────────────────────────────────────────

    protected void SetState(AudioCaptureState newState)
    {
        _state = newState;
        StateChanged?.Invoke(this, newState);
    }

    protected static void EnsureDirectory(string filePath)
    {
        var dir = Path.GetDirectoryName(filePath);
        if (!string.IsNullOrWhiteSpace(dir))
            Directory.CreateDirectory(dir);
    }

    protected static void TryDeleteFile(string? path)
    {
        if (path is not null)
            try { File.Delete(path); } catch { /* best-effort */ }
    }

    public void Dispose()
    {
        DisposeResources();
        _stateLock.Dispose();
    }
}
