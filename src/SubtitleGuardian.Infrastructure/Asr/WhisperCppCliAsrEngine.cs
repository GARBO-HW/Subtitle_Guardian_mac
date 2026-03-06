using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using SubtitleGuardian.Domain.Contracts;
using SubtitleGuardian.Engines.Asr;
using SubtitleGuardian.Infrastructure.Storage;

namespace SubtitleGuardian.Infrastructure.Asr;

public class WhisperCppCliAsrEngine : IAsrEngine
{
    private readonly AppPaths _paths;
    private readonly WhisperCppModelFinder _modelFinder;

    public WhisperCppCliAsrEngine()
    {
        // Infrastructure should ideally be injected, but for now we self-initialize
        _paths = new AppPaths("SubtitleGuardian");
        _modelFinder = new WhisperCppModelFinder(_paths);
    }

    public AsrEngineId Id => AsrEngineId.Whisper;

    public AsrEngineCapabilities GetCapabilities()
    {
        return new AsrEngineCapabilities(
            SupportsLanguageSelection: true,
            SupportsWordTimestamps: false // CLI parsing usually gives segment timestamps
        );
    }

    public async Task<IReadOnlyList<Segment>> TranscribeAsync(
        string audioPath,
        TranscribeOptions options,
        IProgress<AsrProgress>? progress,
        CancellationToken cancellationToken)
    {
        string modelPath = _modelFinder.ResolveModelFile(options);
        var runPlan = await ResolveRunPlanAsync(options);
        var executablePath = runPlan.ExecutablePath;
        var useGpu = runPlan.UseGpu;

        // Generate a temporary file path for JSON output
        string tempBase = Path.Combine(Path.GetTempPath(), $"sg_whisper_{Guid.NewGuid():N}");
        string tempJsonPath = tempBase + ".json";

        // Prepare arguments
        var argsBuilder = new StringBuilder();
        
        // Base arguments
        argsBuilder.Append($"--model \"{modelPath}\" ");
        argsBuilder.Append($"--file \"{audioPath}\" ");
        
        // Output format: JSON to temp file
        // We use JSON output because stdout parsing can be unreliable with different CLI versions/builds
        argsBuilder.Append($"--output-json --output-file \"{tempBase}\" ");
        
        // Enable progress printing to stderr
        argsBuilder.Append("--print-progress ");

        // Max Segment Length (characters)
        if (options.MaxSegmentLength > 0)
        {
            // The --max-len parameter in whisper.cpp is for tokens, not characters.
            // Based on the user-provided formula/heuristic:
            // 65 tokens roughly corresponds to 28 Chinese characters (mixed content).
            // Ratio: 65 / 28 ≈ 2.32
            // We apply this multiplier to convert the user's "Character Count" target into "Token Count".
            int maxTokens = (int)(options.MaxSegmentLength * 2.32);
            // Ensure a reasonable minimum (e.g. at least 1 token) though user input > 0
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
        if (useGpu)
        {
             // For this build version (likely OpenVINO or specific CUDA build), 
             // GPU is enabled by default if available in the binary.
             // The --gpu-layers argument is NOT supported by this CLI.
             // So we do nothing here to enable it.
        }
        else
        {
            // Explicitly disable GPU
            // Both bin_cuda and bin_blas support --no-gpu based on help output
            argsBuilder.Append("--no-gpu ");
        }

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
        var errorLog = new StringBuilder(); // Capture full stderr

        using var process = new Process { StartInfo = startInfo };

        // We can still listen to stdout/stderr for logging or progress
        process.OutputDataReceived += (sender, e) =>
        {
            // Stdout might be empty if using -oj, or might contain other info
            if (!string.IsNullOrEmpty(e.Data))
            {
                // Optional: Parse progress from stdout if available
            }
        };

        process.ErrorDataReceived += (sender, e) =>
        {
            if (!string.IsNullOrEmpty(e.Data))
            {
                errorLog.AppendLine(e.Data); // Capture log

                // Parse progress from stderr
                // Format usually: "whisper_print_progress_callback: 10%" or similar
                // But CLI output varies. Simple heuristic: look for "%"
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
            try { if (File.Exists(tempJsonPath)) File.Delete(tempJsonPath); } catch { }
            throw;
        }

        if (process.ExitCode != 0)
        {
            try { if (File.Exists(tempJsonPath)) File.Delete(tempJsonPath); } catch { }
            var errorMsg = errorLog.ToString();
            throw new Exception($"Whisper process failed with exit code {process.ExitCode}.\nDetails:\n{errorMsg}");
        }

        // Parse JSON output
        if (File.Exists(tempJsonPath))
        {
            try
            {
                string json = await File.ReadAllTextAsync(tempJsonPath, cancellationToken);
                
                // Log the first 500 chars of JSON for debugging
                // Debug.WriteLine($"[Whisper] JSON output (first 500): {json.Substring(0, Math.Min(json.Length, 500))}");
                
                var jsonOptions = new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                };
                
                var result = JsonSerializer.Deserialize<WhisperJsonResult>(json, jsonOptions);
                
                if (result?.transcription != null)
                {
                    foreach (var item in result.transcription)
                    {
                        // Use offsets (ms) if available, otherwise parse timestamps
                        long startMs = item.offsets?.from ?? ParseTimestamp(item.timestamps?.from ?? "00:00:00.000");
                        long endMs = item.offsets?.to ?? ParseTimestamp(item.timestamps?.to ?? "00:00:00.000");
                        string text = item.text?.Trim() ?? string.Empty;
                        
                        segments.Add(new Segment(startMs, endMs, text));
                    }
                }
            }
            finally
            {
                // Keep the file if debugging is needed, otherwise delete
                File.Delete(tempJsonPath);
            }
        }
        else
        {
            // Fallback: if JSON file missing, maybe it wrote to stdout?
            // But we didn't capture stdout in a list.
            // Assuming failure if no JSON.
            var errorMsg = errorLog.ToString();
            throw new FileNotFoundException($"Whisper output file not found at {tempJsonPath}. The process might have failed silently.\nLogs:\n{errorMsg}");
        }

        return segments;
    }

    // JSON models
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

    private static Segment? ParseSegment(string line)
    {
        // Example: [00:00:00.000 --> 00:00:05.000]  Some text here
        // Regex: \[(\d{2}:\d{2}:\d{2}\.\d{3}) --> (\d{2}:\d{2}:\d{2}\.\d{3})\]\s+(.*)
        
        var match = Regex.Match(line, @"\[(\d{2}:\d{2}:\d{2}\.\d{3}) --> (\d{2}:\d{2}:\d{2}\.\d{3})\]\s+(.*)");
        if (match.Success)
        {
            var startStr = match.Groups[1].Value;
            var endStr = match.Groups[2].Value;
            var text = match.Groups[3].Value.Trim();

            long startMs = ParseTimestamp(startStr);
            long endMs = ParseTimestamp(endStr);

            return new Segment(startMs, endMs, text);
        }
        return null;
    }

    private static long ParseTimestamp(string ts)
    {
        // Normalize comma to dot (00:00:00,000 -> 00:00:00.000)
        ts = ts.Replace(',', '.');
        
        if (TimeSpan.TryParse(ts, out var t))
        {
            return (long)t.TotalMilliseconds;
        }
        return 0;
    }

    private async Task<RunPlan> ResolveRunPlanAsync(TranscribeOptions options)
    {
        var baseDir = AppContext.BaseDirectory;
        var runtimeDir = Path.Combine(baseDir, ".subtitleguardian", "runtime", "whispercpp");
        
        // Mac/Linux Support
        if (!OperatingSystem.IsWindows())
        {
            var binDir = Path.Combine(runtimeDir, "bin");
            var candidates = new[] 
            { 
                Path.Combine(binDir, "whisper-cli"), 
                Path.Combine(binDir, "main") 
            };
             
            foreach(var c in candidates)
            {
                if(File.Exists(c)) 
                {
                    // Ensure +x permissions
                    try 
                    { 
                        File.SetUnixFileMode(c, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute | 
                                                UnixFileMode.GroupRead | UnixFileMode.GroupExecute | 
                                                UnixFileMode.OtherRead | UnixFileMode.OtherExecute); 
                    } 
                    catch { }

                    // On Mac (Core ML / Metal), GPU usage is usually automatic or handled by build flags.
                    // We assume it's enabled if available.
                    return new RunPlan { ExecutablePath = c, UseGpu = true }; 
                }
            }
            
            throw new FileNotFoundException($"Could not find whisper-cli or main executable in {binDir}. Please run setup scripts.");
        }

        // Windows Logic
        // Paths to potential binaries
        var binBlas = Path.Combine(runtimeDir, "bin_blas", "whisper-cli.exe");
        var binCuda = Path.Combine(runtimeDir, "bin_cuda", "whisper-cli.exe"); 
        var binCpu = Path.Combine(runtimeDir, "bin_cpu", "whisper-cli.exe"); 
        // Fallback for unified bin structure
        var binUnified = Path.Combine(runtimeDir, "bin", "whisper-cli.exe");

        bool hasBlas = File.Exists(binBlas);
        bool hasCuda = File.Exists(binCuda);
        bool hasCpu = File.Exists(binCpu);
        bool hasUnified = File.Exists(binUnified);

        if (options.Device == ProcessingDevice.GpuWithFallback)
        {
            if (hasCuda) return new RunPlan { ExecutablePath = binCuda, UseGpu = true };
            if (hasBlas) return new RunPlan { ExecutablePath = binBlas, UseGpu = true };
        }
        
        if (hasBlas) return new RunPlan { ExecutablePath = binBlas, UseGpu = false };
        if (hasCpu) return new RunPlan { ExecutablePath = binCpu, UseGpu = false };
        if (hasCuda) return new RunPlan { ExecutablePath = binCuda, UseGpu = false };
        if (hasUnified) return new RunPlan { ExecutablePath = binUnified, UseGpu = false };

        throw new FileNotFoundException("No whisper-cli executable found.");
    }

    private class RunPlan
    {
        public string ExecutablePath { get; set; } = string.Empty;
        public bool UseGpu { get; set; }
    }
}
