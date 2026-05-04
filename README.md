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
