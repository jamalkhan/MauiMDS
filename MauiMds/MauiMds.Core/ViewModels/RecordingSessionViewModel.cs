using MauiMds.AudioCapture;
using MauiMds.Features.Workspace;
using MauiMds.Models;
using MauiMds.Services;
using Microsoft.Extensions.Logging;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;

namespace MauiMds.ViewModels;

public sealed class RecordingSessionViewModel : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;
    public event EventHandler<RecordingGroup>? RecordingStarted;
    public event EventHandler<RecordingStoppedEventArgs>? RecordingStopped;

    private readonly IAudioCaptureService _audioCaptureService;
    private readonly IAudioPlayerService _audioPlayerService;
    private readonly IClock _clock;
    private readonly ILogger<RecordingSessionViewModel> _logger;
    private readonly IMainThreadDispatcher _mainThreadDispatcher;
    private readonly IApplicationLifetime _applicationLifetime;
    private readonly Func<RecordingFormat> _getRecordingFormat;
    private readonly Func<int> _getLiveChunkIntervalSeconds;
    private readonly Func<string> _getWorkspaceRootPath;
    private readonly Func<string, Exception?, string, Task> _reportError;

    private bool _isRecording;
    private bool _isRecordingTransitioning;
    private RecordingGroup? _selectedRecordingGroup;
    private string? _activeRecordingBaseName;

    private readonly RelayCommand _toggleRecordingCommand;

    public RecordingSessionViewModel(
        IAudioCaptureService audioCaptureService,
        IAudioPlayerService audioPlayerService,
        IClock clock,
        ILogger<RecordingSessionViewModel> logger,
        IMainThreadDispatcher mainThreadDispatcher,
        IApplicationLifetime applicationLifetime,
        Func<RecordingFormat> getRecordingFormat,
        Func<int> getLiveChunkIntervalSeconds,
        Func<string> getWorkspaceRootPath,
        Func<string, Exception?, string, Task> reportError)
    {
        _audioCaptureService = audioCaptureService;
        _audioPlayerService = audioPlayerService;
        _clock = clock;
        _logger = logger;
        _mainThreadDispatcher = mainThreadDispatcher;
        _applicationLifetime = applicationLifetime;
        _getRecordingFormat = getRecordingFormat;
        _getLiveChunkIntervalSeconds = getLiveChunkIntervalSeconds;
        _getWorkspaceRootPath = getWorkspaceRootPath;
        _reportError = reportError;

        _audioCaptureService.StateChanged += OnAudioCaptureStateChanged;
        _audioPlayerService.PlaybackStateChanged += (_, _) =>
        {
            OnPropertyChanged(nameof(CurrentlyPlayingAudioPath));
            OnPropertyChanged(nameof(PlaybackDuration));
            OnPropertyChanged(nameof(PlaybackPosition));
        };
        _audioPlayerService.PlaybackPositionChanged += (_, _) => OnPropertyChanged(nameof(PlaybackPosition));

        _toggleRecordingCommand = new RelayCommand(async () => await ToggleRecordingAsync(), () => !_isRecordingTransitioning);
        ToggleRecordingCommand = _toggleRecordingCommand;
        PlayAudioCommand = new RelayCommand<string>(async path => await _audioPlayerService.PlayAsync(path ?? string.Empty));
        PauseAudioCommand = new RelayCommand(() => _audioPlayerService.Pause());
        RewindCommand = new RelayCommand(() => _audioPlayerService.Seek(
            TimeSpan.FromSeconds(Math.Max(0, _audioPlayerService.Position.TotalSeconds - 15))));
        FastForwardCommand = new RelayCommand(() => _audioPlayerService.Seek(
            TimeSpan.FromSeconds(Math.Min(_audioPlayerService.Duration.TotalSeconds, _audioPlayerService.Position.TotalSeconds + 15))));
        SeekCommand = new RelayCommand<object>(param =>
        {
            if (param is double seconds)
                _audioPlayerService.Seek(TimeSpan.FromSeconds(seconds));
        });
    }

    public ICommand ToggleRecordingCommand { get; }
    public ICommand PlayAudioCommand { get; }
    public ICommand PauseAudioCommand { get; }
    public ICommand RewindCommand { get; }
    public ICommand FastForwardCommand { get; }
    public ICommand SeekCommand { get; }  // execute with a boxed double (seconds)

    public bool IsRecording
    {
        get => _isRecording;
        private set
        {
            if (_isRecording == value) return;
            _isRecording = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(RecordButtonLabel));
        }
    }

    public string RecordButtonLabel => IsRecording ? "Stop Recording..." : "Record";

    public RecordingGroup? SelectedRecordingGroup
    {
        get => _selectedRecordingGroup;
        set
        {
            if (ReferenceEquals(_selectedRecordingGroup, value)) return;
            _selectedRecordingGroup = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsRecordingGroupSelected));
        }
    }

    public bool IsRecordingGroupSelected => _selectedRecordingGroup is not null;

    public string? CurrentlyPlayingAudioPath => _audioPlayerService.CurrentlyPlayingPath;
    public TimeSpan PlaybackPosition => _audioPlayerService.Position;
    public TimeSpan PlaybackDuration => _audioPlayerService.Duration;

    public void StopPlayback() => _audioPlayerService.Stop();
    public Task PlayAudioAsync(string path) => _audioPlayerService.PlayAsync(path);

    public Task RequestMicrophonePermissionAsync()
        => _audioCaptureService.RequestMicrophonePermissionAsync();

    public async Task StopRecordingAsync()
    {
        if (!_isRecording) return;
        var result = await _audioCaptureService.StopAsync();
        if (!result.Success)
            _logger.LogWarning("StopRecordingAsync failed: {Error}", result.ErrorMessage);
    }

    public void ApplyHighlights(IEnumerable<WorkspaceTreeItem> items)
    {
        foreach (var item in items)
        {
            item.IsActivelyRecording =
                !string.IsNullOrEmpty(_activeRecordingBaseName) &&
                item.IsRecordingGroup &&
                string.Equals(item.RecordingGroup!.BaseName, _activeRecordingBaseName, StringComparison.Ordinal);
        }
    }

    private void SetActiveRecordingBaseName(string? baseName)
        => _activeRecordingBaseName = baseName;

    private async Task ToggleRecordingAsync()
    {
        if (_isRecordingTransitioning) return;

        if (_isRecording)
        {
            _isRecordingTransitioning = true;
            _toggleRecordingCommand.ChangeCanExecute();
            try
            {
                var stoppedBaseName = _activeRecordingBaseName;
                var result = await _audioCaptureService.StopAsync();
                SetActiveRecordingBaseName(null);
                if (!result.Success)
                {
                    _logger.LogWarning("Recording stop failed: {Error}", result.ErrorMessage);
                    await _reportError("Recording stop failed.", null,
                        result.ErrorMessage ?? "The recording could not be stopped.");
                }
                else
                {
                    _logger.LogInformation("Recording saved: {Path}, Duration: {Duration:g}",
                        result.FilePath, result.Duration);
                    RecordingStopped?.Invoke(this, new RecordingStoppedEventArgs(stoppedBaseName, result));
                }
            }
            catch (Exception ex)
            {
                await _reportError("Recording stop failed.", ex, "The recording could not be stopped.");
            }
            finally
            {
                _isRecordingTransitioning = false;
                _toggleRecordingCommand.ChangeCanExecute();
            }
        }
        else
        {
            _isRecordingTransitioning = true;
            _toggleRecordingCommand.ChangeCanExecute();
            try
            {
                var permission = await _audioCaptureService.CheckMicrophonePermissionAsync();
                if (permission == AudioPermissionStatus.Denied)
                {
                    await _reportError("Microphone permission denied.", null,
                        "Microphone access is required for recording. Please grant permission in System Settings.");
                    return;
                }

                if (permission == AudioPermissionStatus.NotDetermined)
                {
                    var granted = await _audioCaptureService.RequestMicrophonePermissionAsync();
                    if (granted == AudioPermissionStatus.Denied)
                    {
                        await _reportError("Microphone permission denied.", null,
                            "Microphone access is required for recording.");
                        return;
                    }
                }

                var workspaceRoot = _getWorkspaceRootPath();
                var baseFolder = !string.IsNullOrWhiteSpace(workspaceRoot)
                    ? workspaceRoot
                    : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "MauiMds");

                var now = _clock.UtcNow.ToLocalTime();
                var ext = _getRecordingFormat() switch
                {
                    RecordingFormat.MP3  => ".mp3",
                    RecordingFormat.FLAC => ".flac",
                    _                   => ".m4a"
                };
                var micPath = RecordingPathBuilder.BuildMic(baseFolder, now, ext);
                var sysPath = RecordingPathBuilder.BuildSys(baseFolder, now);
                var options = new AudioCaptureOptions
                {
                    OutputPath = micPath,
                    SysOutputPath = sysPath,
                    EnableLiveChunks = true,
                    LiveChunkInterval = TimeSpan.FromSeconds(Math.Max(5, _getLiveChunkIntervalSeconds()))
                };

                await _audioCaptureService.StartAsync(options);
                RecordingPathBuilder.TryParseGroupFile(Path.GetFileName(micPath), out var activeBaseName, out _);
                SetActiveRecordingBaseName(activeBaseName);
                _logger.LogInformation("Recording started: mic={Mic}, sys={Sys}", micPath, sysPath);

                var prelimGroup = new RecordingGroup
                {
                    BaseName = activeBaseName ?? string.Empty,
                    DirectoryPath = Path.GetDirectoryName(micPath) ?? baseFolder,
                    MicFilePath = micPath,
                    SysFilePath = sysPath
                };
                RecordingStarted?.Invoke(this, prelimGroup);

                if (_audioCaptureService.LastStartWarning == "screen_recording_denied")
                {
                    await _reportError(
                        "Recording started without system audio: Screen Recording permission denied.",
                        null,
                        "System audio unavailable — grant Screen Recording permission in System Settings → Privacy & Security → Screen Recording, then restart.");
                }
            }
            catch (Exception ex)
            {
                await _reportError("Recording could not start.", ex,
                    "The recording could not be started. Check permissions and try again.");
            }
            finally
            {
                _isRecordingTransitioning = false;
                _toggleRecordingCommand.ChangeCanExecute();
            }
        }
    }

    private void OnAudioCaptureStateChanged(object? sender, AudioCaptureState state)
    {
        _mainThreadDispatcher.BeginInvokeOnMainThread(() => IsRecording = state == AudioCaptureState.Recording);
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        if (_applicationLifetime.IsTerminating) return;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}

public sealed class RecordingStoppedEventArgs : EventArgs
{
    public string? BaseName { get; }
    public AudioCaptureResult Result { get; }

    public RecordingStoppedEventArgs(string? baseName, AudioCaptureResult result)
    {
        BaseName = baseName;
        Result = result;
    }
}
