# MauiMds

A markdown editor with audio recording and AI transcription, for Mac and Windows.

[![CI / Release](https://github.com/jamalkhan/MauiMDS/actions/workflows/ci.yml/badge.svg)](https://github.com/jamalkhan/MauiMDS/actions/workflows/ci.yml)
[![Latest Release](https://img.shields.io/github/v/release/jamalkhan/MauiMDS?label=download)](https://github.com/jamalkhan/MauiMDS/releases/latest)

## Download

→ **[Latest release](https://github.com/jamalkhan/MauiMDS/releases/latest)**

**Mac:** requires Apple Silicon (M1 / M2 / M3 / M4), macOS 15 Sequoia or later.

**First launch on Mac:** right-click the app → **Open**, then click **Open** in the dialog (Gatekeeper will warn about an unidentified developer — this is expected for unsigned builds).

**Windows:** requires Windows 10 (build 19041) or later, x64.

## Features

- Markdown editor with live preview
- Audio recording (microphone + system audio via ScreenCaptureKit on Mac)
- Automatic transcription via Whisper.cpp or Apple Speech (Mac only)
- Speaker diarization via pyannote.audio
- Workspace file explorer with recording group management

## Prerequisites

The app works out of the box for basic recording and editing. The following tools are only needed if you intend to use specific features.

### ffmpeg

**Required for:** MP3 recording on Mac; FLAC recording on Windows.

**Mac** — install via Homebrew:
```bash
brew install ffmpeg
```

**Windows** — download from [ffmpeg.org](https://ffmpeg.org/download.html) and add the `bin` folder to your `PATH`. If ffmpeg is not found when FLAC is selected, the recording will be saved as WAV instead.

On Mac, FLAC does not require ffmpeg (it uses the built-in `afconvert`). MP3 on Windows also does not require ffmpeg (it uses a bundled encoder). Only the combinations listed above need it.

### Whisper.cpp

**Required for:** transcription using the Whisper.cpp engine.

Install `whisper-cli` (e.g. via Homebrew on Mac: `brew install whisper-cpp`) and download a model file. Then set the binary and model paths in **Preferences → Transcription**.

### pyannote.audio

**Required for:** speaker diarization (identifying who spoke when in a transcript).

1. Install Python 3.10+ and create a virtual environment, then install the package:
   ```bash
   pip install pyannote.audio
   ```

2. Accept the model terms on Hugging Face (a free account is required):
   - [pyannote/speaker-diarization-3.1](https://huggingface.co/pyannote/speaker-diarization-3.1)

3. Create a Hugging Face access token at [huggingface.co/settings/tokens](https://huggingface.co/settings/tokens).

4. In MauiMds, open **Preferences → Transcription** and set:
   - **Python executable** — path to the Python binary in your virtual environment
   - **Hugging Face token** — the token from step 3

## Building from source

```bash
git clone https://github.com/jamalkhan/MauiMDS.git
cd MauiMDS/MauiMds
dotnet build MauiMds.sln
```

Requires .NET 10. Mac builds also require Xcode 26.3 or later.

---

## Architecture overview

The solution is split into four projects:

```
MauiMds.AudioCapture   Platform audio capture and playback (Mac/Windows)
MauiMds.Core           ViewModels, interfaces, business logic (net10.0, no platform deps)
MauiMds.Transcription  Transcription and diarization engines (platform-specific TFMs)
MauiMds               MAUI app shell, views, platform glue
```

### Recording → Transcript pipeline

```
IAudioCaptureService
  │  produces audio files + WAV live-chunks
  ▼
TranscriptionQueueViewModel
  │  live path: feeds chunks to ILiveTranscriptionSession
  │  batch path: queues groups for ITranscriptionPipeline.RunAsync
  ▼
ITranscriptionPipeline (StandardTranscriptionPipeline)
  ├─ ITranscriptionEngine.TranscribeAsync   → List<TranscriptSegment>
  ├─ IDiarizationEngine.DiarizeAsync        → List<SpeakerSegment>
  └─ ISpeakerMergeStrategy.Merge            → labelled List<TranscriptSegment>
  ▼
ITranscriptFormatter  →  markdown string
ITranscriptStorage    →  .md file on disk
```

### Key design decisions

- **`MauiMds.Core` is platform-agnostic.** ViewModels depend only on interfaces defined in Core; platform implementations are registered in `MauiProgram.cs` via `#if MACCATALYST / WINDOWS`.
- **`ITranscriptionPipelineFactory` is the seam for adding engines.** It owns the engine registry and dispatches on `TranscriptionEngineType` / `DiarizationEngineType` enums; callers never instantiate engines directly.
- **Live and batch transcription share one queue VM.** `TranscriptionQueueViewModel` runs up to three concurrent workstreams (live, batch queue, diarization post-processing) coordinated through a single `CancellationTokenSource`.
- **All UI mutation goes through `IMainThreadDispatcher`.** Background tasks post results back rather than accessing `ObservableCollection` directly, keeping threading explicit and testable.

---

## Adding a new transcription engine

1. **Implement `ITranscriptionEngine`** (and optionally `ILiveTranscriptionSession` for live support):

   ```csharp
   // in MauiMds.Transcription/Engines/MyEngine/
   public sealed class MyTranscriptionEngine : ITranscriptionEngine
   {
       public string Name => "MyEngine";
       public bool IsAvailable => /* check binary/model */ true;

       public async Task<IReadOnlyList<TranscriptSegment>> TranscribeAsync(
           string audioFilePath, IProgress<double>? progress, CancellationToken ct)
       {
           // ... return segments ordered by Start
       }
   }
   ```

2. **Add an enum value** to `TranscriptionEngineType` in `MauiMds.Core/Models/EditorPreferences.cs`.

3. **Register in `TranscriptionPipelineFactory`** (`MauiMds.Transcription/TranscriptionPipelineFactory.cs`):
   - Add an instance of your engine to `_availableEngines`.
   - Add a `case TranscriptionEngineType.MyEngine:` branch in `ResolveEngine()`.

4. **Add live session support** (optional): implement `ILiveTranscriptionSession` and add a `case` in `CreateLiveSession()`.

5. **Add a preference label** in `PreferencesViewModel` so the engine appears in the Preferences dropdown.

No changes to `MauiProgram.cs` or the ViewModels are needed; the factory handles all dispatch.

---

## Troubleshooting

### Live transcription not appearing during recording

- **Check the transcription engine.** Live transcription is only supported by Whisper.cpp and Apple Speech (Mac only). If the batch engine (e.g. pyannote-only or None) is selected, transcription runs after recording stops.
- **Check the log.** Open the log file (path shown in **Preferences → Advanced**) and search for `"Live transcription started"` or `"does not support live transcription"`.
- **Apple Speech (Mac).** Requires the app to have Microphone permission granted in System Settings → Privacy & Security → Microphone. The log records the permission status at each recording attempt.

### Transcript is empty or missing after recording

- The transcript is written when recording stops. If the recording was very short (< 1 second) or captured silence, Whisper may produce no segments.
- Check the log for `"Live transcript saved"` or `"Group transcription complete"`. If absent, look for preceding errors.
- If the workspace did not refresh, try **View → Refresh Workspace** to reload the file tree.

### ffmpeg not found

- **Mac:** `brew install ffmpeg` installs to `/opt/homebrew/bin/ffmpeg` (Apple Silicon) or `/usr/local/bin/ffmpeg` (Intel). The app checks both paths automatically.
- **Windows:** ensure the `ffmpeg/bin` directory is on your `PATH` and restart the app.
- When ffmpeg is unavailable, FLAC recordings fall back to WAV on Windows; MP3 recording on Mac will show an error.

### Whisper.cpp: "binary not found" or no output

- In **Preferences → Transcription**, confirm the **Whisper binary** path points to the `whisper-cli` executable and the **Model** path points to a `.bin` GGML model file.
- Run the binary manually from Terminal to verify it works: `whisper-cli --help`.
- Whisper.cpp stderr is captured and written to the log file. Search for `"WhisperCpp"` entries.

### pyannote diarization not running

- Confirm the **Python executable** path in Preferences points to the interpreter inside your virtual environment (the one where `pyannote.audio` is installed), not the system Python.
- The Hugging Face token must have access to `pyannote/speaker-diarization-3.1`. Tokens can be tested at `huggingface-cli whoami`.
- The diarization script is embedded in the app; check the log for `"Pyannote"` entries.

### Speaker labels showing "Speaker" instead of real names

Speaker diarization assigns opaque labels like `SPEAKER_00`. Renaming speakers is not yet supported in the UI; labels can be edited in the generated `.md` transcript file.

---

## Mac-specific notes (sandbox and permissions)

MauiMds is distributed as an **unsigned, unsandboxed app**. It is not and cannot be submitted to the Mac App Store in its current form because:

- **Screen Recording permission** — capturing system audio via ScreenCaptureKit requires the Screen Recording entitlement. On Mac App Store builds this entitlement is restricted; the current distribution bypasses that restriction by running outside the sandbox.
- **Unrestricted file access** — the workspace browser and transcript storage write to arbitrary user-selected folders. The App Store sandbox limits file access to a narrow set of security-scoped bookmarks; adapting the app would require migrating all file I/O to bookmark-based access (partially implemented in `MacMarkdownFileAccessService` and `FolderPickerPlatformService`, but not complete).
- **External processes** — Whisper.cpp and pyannote run as child processes via `IProcessRunner`. Sandboxed apps may not spawn arbitrary executables.

**Gatekeeper warning on first launch** is expected. Right-click → Open → Open bypasses the unsigned-app check for the current user without disabling Gatekeeper system-wide.
