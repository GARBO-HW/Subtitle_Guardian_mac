#!/bin/bash
set -e

# Directory setup
SCRIPT_DIR="$( cd "$( dirname "${BASH_SOURCE[0]}" )" &> /dev/null && pwd )"
PROJECT_ROOT="$SCRIPT_DIR/.."
RUNTIME_DIR="$PROJECT_ROOT/runtime"

mkdir -p "$RUNTIME_DIR/ffmpeg/bin"
mkdir -p "$RUNTIME_DIR/whispercpp/bin"

echo "=== Setting up Mac dependencies in $RUNTIME_DIR ==="

# 1. FFmpeg
echo "Checking FFmpeg..."
if [ ! -f "$RUNTIME_DIR/ffmpeg/bin/ffmpeg" ]; then
    echo "Downloading FFmpeg (Universal/6.1.1)..."
    # Using evermeet.cx generic link or specific version
    curl -L -o ffmpeg.zip https://evermeet.cx/ffmpeg/ffmpeg-6.1.1.zip
    unzip -o ffmpeg.zip
    mv ffmpeg "$RUNTIME_DIR/ffmpeg/bin/"
    chmod +x "$RUNTIME_DIR/ffmpeg/bin/ffmpeg"
    rm ffmpeg.zip
    echo "FFmpeg installed."
else
    echo "FFmpeg already exists."
fi

if [ ! -f "$RUNTIME_DIR/ffmpeg/bin/ffprobe" ]; then
    echo "Downloading FFprobe..."
    curl -L -o ffprobe.zip https://evermeet.cx/ffmpeg/ffprobe-6.1.1.zip
    unzip -o ffprobe.zip
    mv ffprobe "$RUNTIME_DIR/ffmpeg/bin/"
    chmod +x "$RUNTIME_DIR/ffmpeg/bin/ffprobe"
    rm ffprobe.zip
    echo "FFprobe installed."
else
    echo "FFprobe already exists."
fi

# 2. Whisper.cpp
echo "Checking Whisper.cpp..."
if [ ! -f "$RUNTIME_DIR/whispercpp/bin/whisper-cli" ]; then
    echo "It is recommended to build whisper.cpp from source for best Mac performance (Metal support)."
    read -p "Do you want to clone and build whisper.cpp now? (y/n) " -n 1 -r
    echo
    if [[ $REPLY =~ ^[Yy]$ ]]; then
        echo "Cloning whisper.cpp..."
        if [ -d "whisper.cpp" ]; then
            rm -rf whisper.cpp
        fi
        git clone https://github.com/ggerganov/whisper.cpp.git
        cd whisper.cpp
        
        echo "Building..."
        make -j4
        
        echo "Copying binaries..."
        cp main "$RUNTIME_DIR/whispercpp/bin/whisper-cli"
        # Also copy libwhisper.dylib if it exists/needed, though static linking is default for main usually?
        # main usually links everything.
        
        cd ..
        rm -rf whisper.cpp
        echo "Whisper.cpp built and installed."
    else
        echo "Downloading pre-built whisper.cpp (Intel x64, might run via Rosetta)..."
        # Fallback to a release binary if user declines build
        # Note: This URL might need updating
        curl -L -o whisper-bin.zip https://github.com/ggerganov/whisper.cpp/releases/download/v1.5.4/whisper-bin-x64.zip
        unzip -o whisper-bin.zip -d whisper_tmp
        mv whisper_tmp/main "$RUNTIME_DIR/whispercpp/bin/whisper-cli"
        chmod +x "$RUNTIME_DIR/whispercpp/bin/whisper-cli"
        rm -rf whisper_tmp whisper-bin.zip
        echo "Whisper.cpp installed."
    fi
else
    echo "Whisper.cpp already exists."
fi

# 3. Models (Optional)
echo "Checking for base model..."
MODEL_DIR="$PROJECT_ROOT/models/whisper"
mkdir -p "$MODEL_DIR"
if [ ! -f "$MODEL_DIR/ggml-base.bin" ]; then
    echo "Downloading base model..."
    curl -L -o "$MODEL_DIR/ggml-base.bin" https://huggingface.co/ggerganov/whisper.cpp/resolve/main/ggml-base.bin
    echo "Base model downloaded."
else
    echo "Base model exists."
fi

echo "=== Setup Complete ==="
echo "Dependencies are in $RUNTIME_DIR"
