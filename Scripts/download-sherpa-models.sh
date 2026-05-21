#!/usr/bin/env bash
# Downloads the two Sherpa-ONNX model files required for speaker diarization and places
# them in Resources/Raw/Models/Sherpa/ so MAUI bundles them into the app at build time.
#
# Run once before building:
#   bash Scripts/download-sherpa-models.sh
#
# Models (~43 MB total):
#   segmentation.onnx  — pyannote segmentation 3.0  (~17 MB)
#   embedding.onnx     — WeSpeaker VoxCeleb ResNet34 (~26 MB)

set -euo pipefail

DEST="$(cd "$(dirname "$0")/.." && pwd)/Rizedown/Rizedown/Resources/Raw/Models/Sherpa"
mkdir -p "$DEST"

# ── Segmentation model ────────────────────────────────────────────────────────
SEG_FILE="$DEST/segmentation.onnx"
if [[ -f "$SEG_FILE" ]]; then
    echo "✓ segmentation.onnx already present, skipping."
else
    echo "↓ Downloading pyannote segmentation model…"
    TMP=$(mktemp -d)
    curl -fL --progress-bar \
        "https://github.com/k2-fsa/sherpa-onnx/releases/download/speaker-segmentation-models/sherpa-onnx-pyannote-segmentation-3-0.tar.bz2" \
        -o "$TMP/seg.tar.bz2"
    tar -xjf "$TMP/seg.tar.bz2" -C "$TMP"
    cp "$TMP/sherpa-onnx-pyannote-segmentation-3-0/model.onnx" "$SEG_FILE"
    rm -rf "$TMP"
    echo "✓ segmentation.onnx saved ($(du -sh "$SEG_FILE" | cut -f1))."
fi

# ── Embedding model ───────────────────────────────────────────────────────────
EMB_FILE="$DEST/embedding.onnx"
if [[ -f "$EMB_FILE" ]]; then
    echo "✓ embedding.onnx already present, skipping."
else
    echo "↓ Downloading WeSpeaker VoxCeleb ResNet34 embedding model…"
    curl -fL --progress-bar \
        "https://github.com/k2-fsa/sherpa-onnx/releases/download/speaker-recongition-models/wespeaker_en_voxceleb_resnet34_LM.onnx" \
        -o "$EMB_FILE"
    echo "✓ embedding.onnx saved ($(du -sh "$EMB_FILE" | cut -f1))."
fi

echo ""
echo "Sherpa-ONNX models ready in: $DEST"
echo "Build the app and they will be bundled automatically."
