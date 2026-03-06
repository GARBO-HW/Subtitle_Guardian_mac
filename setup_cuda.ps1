$ErrorActionPreference = "Stop"

# Use v1.6.0 (known to work well)
$url = "https://github.com/ggml-org/whisper.cpp/releases/download/v1.6.0/whisper-cublas-11.8.0-bin-x64.zip"
$destDir = "c:\Users\a1130\Desktop\Subtitle_Guardian\.subtitleguardian\runtime\whispercpp\bin_cuda"
$zipPath = Join-Path $destDir "whisper-cuda.zip"

# Ensure directory exists
if (-not (Test-Path $destDir)) {
    New-Item -ItemType Directory -Path $destDir -Force | Out-Null
}

# Download if not exists or if we want to force update (let's assume we download if zip missing)
if (-not (Test-Path $zipPath)) {
    Write-Host "Downloading from $url..."
    try {
        [Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
        Invoke-WebRequest -Uri $url -OutFile $zipPath
        Write-Host "Download complete."
    } catch {
        Write-Error "Download failed: $_"
        exit 1
    }
} else {
    Write-Host "Zip file already exists. Skipping download."
}

# Extract using .NET API for better control (skipping locked files)
Write-Host "Extracting files..."
Add-Type -AssemblyName System.IO.Compression.FileSystem

try {
    $zip = [System.IO.Compression.ZipFile]::OpenRead($zipPath)
    
    foreach ($entry in $zip.Entries) {
        if ($entry.FullName.EndsWith("/")) { continue }
        
        $fileName = Split-Path $entry.FullName -Leaf
        $targetPath = $null
        
        # Mapping logic
        if ($fileName -eq "main.exe") {
            $targetPath = Join-Path $destDir "whisper-cli.exe"
            Write-Host "Extracting main.exe -> whisper-cli.exe"
        }
        elseif ($fileName.EndsWith(".dll")) {
            $targetPath = Join-Path $destDir $fileName
            Write-Host "Extracting $fileName"
        }
        
        if ($targetPath) {
            try {
                [System.IO.Compression.ZipFileExtensions]::ExtractToFile($entry, $targetPath, $true)
            } catch {
                Write-Warning "Failed to extract $fileName : $_"
            }
        }
    }
    $zip.Dispose()
} catch {
    Write-Error "Failed to open zip file: $_"
}

# Copy VC runtimes from System32 (safest source for system dependencies)
$system32 = "C:\Windows\System32"
$runtimes = @("msvcp140.dll", "msvcp140_1.dll", "msvcp140_2.dll", "vcruntime140.dll", "vcruntime140_1.dll", "vcomp140.dll", "concrt140.dll")

foreach ($dll in $runtimes) {
    $srcPath = Join-Path $system32 $dll
    if (Test-Path $srcPath) {
        Copy-Item $srcPath $destDir -Force -ErrorAction SilentlyContinue
        Write-Host "Copied $dll from System32."
    } else {
        # Fallback to bin_blas if available
        $blasPath = Join-Path $blasDir $dll
        if (Test-Path $blasPath) {
            Copy-Item $blasPath $destDir -Force -ErrorAction SilentlyContinue
            Write-Host "Copied $dll from bin_blas."
        }
    }
}

# Copy missing BLAS DLLs (libopenblas, ggml) if not in zip
$blasDir = "c:\Users\a1130\Desktop\Subtitle_Guardian\.subtitleguardian\runtime\whispercpp\bin_blas"
if (Test-Path $blasDir) {
    Get-ChildItem $blasDir -Filter "ggml*.dll" | Copy-Item -Destination $destDir -Force -ErrorAction SilentlyContinue
    Get-ChildItem $blasDir -Filter "libopenblas.dll" | Copy-Item -Destination $destDir -Force -ErrorAction SilentlyContinue
    Write-Host "Copied BLAS dependencies from bin_blas."
}

Write-Host "Setup complete."
