using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using SubtitleGuardian.Domain.Contracts;

namespace SubtitleGuardian.Engines.Asr;

public sealed class WhisperAsrEngine : IAsrEngine
{
    public AsrEngineId Id => AsrEngineId.Whisper;

    public AsrEngineCapabilities GetCapabilities()
    {
        return new AsrEngineCapabilities(
            SupportsLanguageSelection: true,
            SupportsWordTimestamps: true
        );
    }

    public async Task<IReadOnlyList<Segment>> TranscribeAsync(
        string audioPath,
        TranscribeOptions options,
        IProgress<AsrProgress>? progress,
        CancellationToken cancellationToken)
    {
        string modelPath = ResolveModelPath(options);
        string executablePath = ResolveExecutablePath();

        // Ensure executable permissions on Unix-like systems
        if (!OperatingSystem.IsWindows())
        {
            try
            {
                File.SetUnixFileMode(executablePath, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute | 
                                                     UnixFileMode.GroupRead | UnixFileMode.GroupExecute | 
                                                     UnixFileMode.OtherRead | UnixFileMode.OtherExecute);
            }
            catch { /* Ignore if fails, maybe permission denied or not supported */ }
        }

        // Generate a temporary file path for JSON output
        string tempBase = Path.Combine(Path.GetTempPath(), $"sg_whisper_{Guid.NewGuid():N}");
        string tempJsonPath = tempBase + ".json";

        // Prepare arguments
        var argsBuilder = new StringBuilder();
        
        // Base arguments
        argsBuilder.Append($"--model \"{modelPath}\" ");
        argsBuilder.Append($"--file \"{audioPath}\" ");
        
        // Output format: JSON to temp file
        argsBuilder.Append($"--output-json --output-file \"{tempBase}\" ");
        
        // Enable progress printing to stderr
        argsBuilder.Append("--print-progress ");

        // Max Segment Length (characters) -> Tokens approximation
        if (options.MaxSegmentLength > 0)
        {
            int maxTokens = (int)(options.MaxSegmentLength * 2.32);
            if (maxTokens < 1) maxTokens = 1;
            argsBuilder.Append($"--max-len {maxTokens} ");
        }

        // Language
        if (!string.IsNullOrEmpty(options.Language))
        {
            string lang = options.Language.ToLowerInvariant();
            string prompt = "";
            
            if (lang == "zh-tw")
            {
                lang = "zh";
                prompt = "繁體中文";
            }
            else if (lang == "zh-cn")
            {
                lang = "zh";
                prompt = "简体中文";
            }
            
            argsBuilder.Append($"--language {lang} ");
            
            if (!string.IsNullOrEmpty(prompt))
            {
                 argsBuilder.Append($"--prompt \"{prompt}\" ");
            }
        }

        // GPU / Device arguments
        // On Mac (Core ML / Metal), usually handled by build flags.
        // We just pass --threads
        
        // Threading
        var threads = Math.Max(1, Environment.ProcessorCount - 2);
        argsBuilder.Append($"--threads {threads} ");

        // Process setup
        var startInfo = new ProcessStartInfo
        {
            FileName = executablePath,
            Arguments = argsBuilder.ToString(),
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };

        var segments = new List<Segment>();
        var errorLog = new StringBuilder();

        using var process = new Process { StartInfo = startInfo };

        process.ErrorDataReceived += (sender, e) =>
        {
            if (!string.IsNullOrEmpty(e.Data))
            {
                errorLog.AppendLine(e.Data);
                if (e.Data.Contains('%'))
                {
                    var match = Regex.Match(e.Data, @"(\d{1,3})%");
                    if (match.Success && int.TryParse(match.Groups[1].Value, out int pct))
                    {
                        progress?.Report(new AsrProgress(pct, "Transcribing..."));
                    }
                }
            }
        };

        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        try
        {
            await process.WaitForExitAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            process.Kill();
            CleanupTempFiles(tempBase);
            throw;
        }

        if (process.ExitCode != 0)
        {
            CleanupTempFiles(tempBase);
            var errorMsg = errorLog.ToString();
            throw new Exception($"Whisper process failed with exit code {process.ExitCode}.\nDetails:\n{errorMsg}");
        }

        // Parse JSON output
        if (File.Exists(tempJsonPath))
        {
            try
            {
                string json = await File.ReadAllTextAsync(tempJsonPath, cancellationToken);
                segments.AddRange(ParseJson(json));
            }
            finally
            {
                CleanupTempFiles(tempBase);
            }
        }
        else
        {
            var errorMsg = errorLog.ToString();
            throw new FileNotFoundException($"Whisper output file not found at {tempJsonPath}.\nLogs:\n{errorMsg}");
        }

        return segments;
    }

    private void CleanupTempFiles(string tempBase)
    {
        try { if (File.Exists(tempBase + ".json")) File.Delete(tempBase + ".json"); } catch { }
        try { if (File.Exists(tempBase + ".txt")) File.Delete(tempBase + ".txt"); } catch { }
        try { if (File.Exists(tempBase + ".srt")) File.Delete(tempBase + ".srt"); } catch { }
        try { if (File.Exists(tempBase + ".vtt")) File.Delete(tempBase + ".vtt"); } catch { }
    }

    private string ResolveExecutablePath()
    {
        var baseDir = AppContext.BaseDirectory;
        var runtimeDir = Path.Combine(baseDir, ".subtitleguardian", "runtime", "whispercpp", "bin");

        // Check for 'whisper-cli' (Mac/Linux) or 'whisper-cli.exe' (Windows)
        // Also check 'main' as fallback
        var candidates = new[]
        {
            Path.Combine(runtimeDir, "whisper-cli"),
            Path.Combine(runtimeDir, "whisper-cli.exe"),
            Path.Combine(runtimeDir, "main"),
            Path.Combine(runtimeDir, "main.exe")
        };

        foreach (var path in candidates)
        {
            if (File.Exists(path)) return path;
        }

        throw new FileNotFoundException($"Could not find whisper-cli executable in {runtimeDir}. Please run setup scripts.");
    }

    private string ResolveModelPath(TranscribeOptions options)
    {
        var baseDir = AppContext.BaseDirectory;
        var modelRoot = Path.Combine(baseDir, ".subtitleguardian", "models", "whisper");

        if (!Directory.Exists(modelRoot))
        {
             throw new DirectoryNotFoundException($"Model directory not found: {modelRoot}");
        }

        var candidates = Directory.EnumerateFiles(modelRoot, "*.bin", SearchOption.AllDirectories)
            .Where(p => Path.GetFileName(p).Contains("ggml-", StringComparison.OrdinalIgnoreCase))
            .ToArray();

        if (candidates.Length == 0)
        {
            throw new FileNotFoundException($"No GGML models found in {modelRoot}");
        }

        // Simple selection logic: prioritize medium/large if quality is high, else base/small
        // For now, just pick the largest file as "best" or first one found
        // Better logic can be added later matching TranscriptionQuality enum
        
        string key = options.Quality switch
        {
            TranscriptionQuality.Tiny => "tiny",
            TranscriptionQuality.Base => "base",
            TranscriptionQuality.Small => "small",
            TranscriptionQuality.Medium => "medium",
            TranscriptionQuality.Large => "large",
            _ => "medium"
        };

        var best = candidates
            .OrderByDescending(p => Path.GetFileName(p).Contains(key) ? 100 : 0) // Exact match priority
            .ThenByDescending(p => new FileInfo(p).Length) // Size priority
            .First();

        return best;
    }

    private IEnumerable<Segment> ParseJson(string json)
    {
        var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        var result = JsonSerializer.Deserialize<WhisperJsonResult>(json, options);
        
        var segments = new List<Segment>();
        if (result?.transcription != null)
        {
            foreach (var item in result.transcription)
            {
                long startMs = item.offsets?.from ?? ParseTimestamp(item.timestamps?.from);
                long endMs = item.offsets?.to ?? ParseTimestamp(item.timestamps?.to);
                string text = item.text?.Trim() ?? string.Empty;
                segments.Add(new Segment(startMs, endMs, text));
            }
        }
        return segments;
    }

    private long ParseTimestamp(string? ts)
    {
        if (string.IsNullOrEmpty(ts)) return 0;
        ts = ts.Replace(',', '.');
        if (TimeSpan.TryParse(ts, out var t)) return (long)t.TotalMilliseconds;
        return 0;
    }

    private class WhisperJsonResult
    {
        public List<WhisperJsonSegment>? transcription { get; set; }
    }

    private class WhisperJsonSegment
    {
        public WhisperJsonTimestamps? timestamps { get; set; }
        public WhisperJsonOffsets? offsets { get; set; }
        public string? text { get; set; }
    }

    private class WhisperJsonTimestamps
    {
        public string? from { get; set; }
        public string? to { get; set; }
    }

    private class WhisperJsonOffsets
    {
        public long from { get; set; }
        public long to { get; set; }
    }
}
