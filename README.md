# MauiMds

A Mac Catalyst markdown editor with audio recording and AI transcription.

[![CI / Release](https://github.com/jamalkhan/MauiMDS/actions/workflows/ci.yml/badge.svg)](https://github.com/jamalkhan/MauiMDS/actions/workflows/ci.yml)
[![Latest Release](https://img.shields.io/github/v/release/jamalkhan/MauiMDS?label=download)](https://github.com/jamalkhan/MauiMDS/releases/latest)

## Download

→ **[Latest release](https://github.com/jamalkhan/MauiMDS/releases/latest)**

Requires **Apple Silicon** (M1 / M2 / M3 / M4), macOS 15 Sequoia or later.

**First launch:** right-click the app → **Open**, then click **Open** in the dialog (Gatekeeper will warn about an unidentified developer — this is expected for unsigned builds).

## Features

- Markdown editor with live preview
- Audio recording (microphone + system audio via ScreenCaptureKit)
- Automatic transcription via Whisper.cpp or Apple Speech
- Speaker diarization via pyannote.audio
- Workspace file explorer with recording group management

## Building from source

```bash
git clone https://github.com/jamalkhan/MauiMDS.git
cd MauiMDS/MauiMds
dotnet build MauiMds.sln
```

Requires .NET 10 and Xcode 16.
