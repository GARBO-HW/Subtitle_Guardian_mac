#!/bin/bash
set -e

APP_NAME="Subtitle Guardian"
PROJECT_NAME="SubtitleGuardian.Mac"
CONFIGURATION="Release"
FRAMEWORK="net10.0"

SCRIPT_DIR="$( cd "$( dirname "${BASH_SOURCE[0]}" )" &> /dev/null && pwd )"
PROJECT_ROOT="$SCRIPT_DIR/.."
PUBLISH_DIR="$PROJECT_ROOT/src/$PROJECT_NAME/bin/$CONFIGURATION/$FRAMEWORK/osx-arm64/publish"
OUTPUT_DIR="$PROJECT_ROOT/artifacts/mac"
APP_BUNDLE="$OUTPUT_DIR/$APP_NAME.app"

echo "=== Packaging $APP_NAME for Mac ==="

# 0. Generate App Icon
echo "Checking for AppIcon.iconset..."
if [ -d "AppIcon.iconset" ]; then
    echo "Converting AppIcon.iconset to icns..."
    iconutil -c icns "AppIcon.iconset" -o "$PROJECT_ROOT/src/$PROJECT_NAME/Assets/AppIcon.icns"
    echo "AppIcon.icns updated."
elif [ -f "icon_512x512@2x.png" ]; then
    rm -rf "AppIcon.iconset"
    mkdir -p "AppIcon.iconset"
    mv icon_*.png "AppIcon.iconset/"
    iconutil -c icns "AppIcon.iconset" -o "$PROJECT_ROOT/src/$PROJECT_NAME/Assets/AppIcon.icns"
    echo "AppIcon.icns generated."
else
    echo "Warning: Icon files not found."
fi

# 1. Publish
echo "Publishing..."
dotnet publish "$PROJECT_ROOT/src/$PROJECT_NAME/$PROJECT_NAME.csproj" \
    -c "$CONFIGURATION" \
    -f "$FRAMEWORK" \
    -r osx-arm64 \
    --self-contained true \
    -o "$PUBLISH_DIR"

# 2. Create App Bundle
echo "Creating App Bundle..."
if [ -d "$APP_BUNDLE" ]; then
    rm -rf "$APP_BUNDLE"
fi
mkdir -p "$APP_BUNDLE/Contents/MacOS"
mkdir -p "$APP_BUNDLE/Contents/Resources"

# 3. Copy Executable and Assets
cp -a "$PUBLISH_DIR/." "$APP_BUNDLE/Contents/MacOS/"
mv "$APP_BUNDLE/Contents/MacOS/$PROJECT_NAME" "$APP_BUNDLE/Contents/MacOS/$APP_NAME"
chmod +x "$APP_BUNDLE/Contents/MacOS/$APP_NAME"

# 4. Create Info.plist
echo "Creating Info.plist..."
cat > "$APP_BUNDLE/Contents/Info.plist" <<EOF
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0">
<dict>
    <key>CFBundleExecutable</key>
    <string>$APP_NAME</string>
    <key>CFBundleIconFile</key>
    <string>AppIcon</string>
    <key>CFBundleIdentifier</key>
    <string>com.subtitleguardian.mac</string>
    <key>CFBundleName</key>
    <string>$APP_NAME</string>
    <key>CFBundlePackageType</key>
    <string>APPL</string>
    <key>CFBundleShortVersionString</key>
    <string>1.0</string>
    <key>CFBundleVersion</key>
    <string>1</string>
    <key>LSMinimumSystemVersion</key>
    <string>10.13</string>
    <key>NSHighResolutionCapable</key>
    <true/>
</dict>
</plist>
EOF

# 5. Copy Icon (if exists)
if [ -f "$PROJECT_ROOT/src/$PROJECT_NAME/Assets/AppIcon.icns" ]; then
    cp "$PROJECT_ROOT/src/$PROJECT_NAME/Assets/AppIcon.icns" "$APP_BUNDLE/Contents/Resources/"
fi

# 6. Setup Dependencies (Portable Mode)
echo "Setting up dependencies..."
# Use Contents/Resources/runtime for dependencies instead of MacOS/.subtitleguardian
# Apple recommends putting auxiliary executables in Contents/MacOS or Contents/Helpers
# But putting them in a hidden folder inside MacOS is often flagged as suspicious or invalid structure.
# Let's try putting them in Contents/Resources and symlinking or just updating the path in app logic.
# However, to avoid changing C# code right now, let's keep it in MacOS but not hidden?
# Or maybe the hidden folder IS the problem for codesign --deep.

DEST_PORTABLE="$APP_BUNDLE/Contents/MacOS/subtitleguardian_libs"
mkdir -p "$DEST_PORTABLE"

# Copy runtime (ffmpeg, whispercpp)
if [ -d "$PROJECT_ROOT/runtime" ]; then
    cp -R "$PROJECT_ROOT/runtime" "$DEST_PORTABLE/"
else
    echo "WARNING: 'runtime' folder not found in project root. Run setup_mac.sh first."
fi

# Copy models (if exist)
if [ -d "$PROJECT_ROOT/models" ]; then
    cp -R "$PROJECT_ROOT/models" "$DEST_PORTABLE/"
fi

# 6.5. Code Sign (Ad-hoc)
echo "Code Signing (Ad-hoc)..."

# Sign internal binaries first
find "$DEST_PORTABLE" -type f \( -name "ffmpeg" -o -name "ffprobe" -o -name "whisper-cli" \) -exec codesign --force --sign - {} \;

# Sign the main app bundle
codesign --force --deep --sign - "$APP_BUNDLE"
echo "Code Signing Complete."

# 7. Create DMG
echo "Creating DMG..."
DMG_NAME="$APP_NAME.dmg"
DMG_PATH="$OUTPUT_DIR/$DMG_NAME"
VOL_NAME="$APP_NAME"
TMP_DMG_DIR="$OUTPUT_DIR/dmg_tmp"
DMG_BACKGROUND="$PROJECT_ROOT/artifacts/mac/dmg_background.png"

# Cleanup
rm -rf "$TMP_DMG_DIR" "$DMG_PATH" "$DMG_PATH.tmp.dmg"
mkdir -p "$TMP_DMG_DIR"

# Copy App to tmp dir
echo "Copying app to DMG staging..."
cp -R "$APP_BUNDLE" "$TMP_DMG_DIR/"

# Create Alias to Applications folder (instead of symlink to support custom icon)
echo "Creating Applications alias..."
# Use Swift script to create alias
swift "$SCRIPT_DIR/create_alias.swift" "/Applications" "$TMP_DMG_DIR/Applications"

# Set custom icon for Applications alias
APP_ICON_SRC="$PROJECT_ROOT/artifacts/mac/Applications.icns"
if [ -f "$APP_ICON_SRC" ]; then
    echo "Setting custom icon for Applications alias..."
    swift "$SCRIPT_DIR/set_icon.swift" "$APP_ICON_SRC" "$TMP_DMG_DIR/Applications"
else
    echo "Warning: Applications.icns not found at $APP_ICON_SRC"
fi

# Copy background image if exists
if [ -f "$DMG_BACKGROUND" ]; then
    echo "Found background image: $DMG_BACKGROUND"
    mkdir -p "$TMP_DMG_DIR/.background"
    cp "$DMG_BACKGROUND" "$TMP_DMG_DIR/.background/background.png"
else
    echo "Warning: Background image not found at $DMG_BACKGROUND"
fi

# Create temporary read-write DMG from folder
echo "Creating temporary DMG..."
hdiutil create -volname "$VOL_NAME" -srcfolder "$TMP_DMG_DIR" -ov -format UDRW "$DMG_PATH.tmp.dmg"

# Mount the temporary DMG
echo "Mounting DMG..."
# Attach and get the device node (e.g. /dev/disk4s1)
device=$(hdiutil attach -readwrite -noverify -noautoopen "$DMG_PATH.tmp.dmg" | grep Apple_HFS | awk '{print $1}')
echo "Mounted at device: $device"

# Wait for mount
sleep 5

# Hide background folder and system files
echo "Hiding system files..."
if [ -d "/Volumes/$VOL_NAME/.background" ]; then
    chflags hidden "/Volumes/$VOL_NAME/.background"
fi
if [ -d "/Volumes/$VOL_NAME/.fseventsd" ]; then
    chflags hidden "/Volumes/$VOL_NAME/.fseventsd"
fi
if [ -d "/Volumes/$VOL_NAME/.Trashes" ]; then
    chflags hidden "/Volumes/$VOL_NAME/.Trashes"
fi

# Apply layout using AppleScript
echo "Applying DMG layout..."
# We need to make sure we are targeting the right volume.
# Volume path is /Volumes/$VOL_NAME
osascript <<EOF
tell application "Finder"
    tell disk "$VOL_NAME"
        open
        
        -- Wait for window to open
        delay 2
        
        -- Try to set view options
        try
           set current view of container window to icon view
           set toolbar visible of container window to false
           set statusbar visible of container window to false
        on error errMsg
           log "Error setting view: " & errMsg
        end try
        
        -- Set window size and position (optional)
        -- bounds: {left, top, right, bottom}
        -- Width: 750 (1150-400), Height: 500 (600-100) to ensure title bar fits
        set the bounds of container window to {400, 100, 1150, 600}
        
        set theViewOptions to the icon view options of container window
        set arrangement of theViewOptions to not arranged
        set icon size of theViewOptions to 128
        set text size of theViewOptions to 14
        
        -- Set background picture
        try
            set background picture of theViewOptions to file ".background:background.png"
        on error errMsg
            log "Error setting background: " & errMsg
        end try
        
        -- Set positions
        -- Window is 750x450
        -- Center Y is ~225
        -- App at left: 180, 225
        -- Apps at right: 570, 225
        try
            set position of item "$APP_NAME.app" of container window to {180, 225}
            set position of item "Applications" of container window to {570, 225}
        on error errMsg
            log "Error setting positions: " & errMsg
        end try
        
        -- Force update
        update without registering applications
        delay 2
        
        close
    end tell
end tell
EOF


# Unmount
echo "Unmounting DMG..."
hdiutil detach "$device" || hdiutil detach "/Volumes/$VOL_NAME" -force

# Convert to compressed DMG
echo "Compressing DMG..."
hdiutil convert "$DMG_PATH.tmp.dmg" -format UDZO -o "$DMG_PATH"

# Cleanup tmp
rm "$DMG_PATH.tmp.dmg"
rm -rf "$TMP_DMG_DIR"

echo "=== Package Complete ==="
echo "App Bundle: $APP_BUNDLE"
echo "DMG File: $DMG_PATH"
echo "You can distribute the DMG file now."
