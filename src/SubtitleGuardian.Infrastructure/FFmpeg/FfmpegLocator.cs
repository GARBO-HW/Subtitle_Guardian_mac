namespace SubtitleGuardian.Infrastructure.FFmpeg;

public sealed class FfmpegLocator
{
    private readonly string? _ffmpegPath;
    private readonly string? _ffprobePath;

    public FfmpegLocator(string? ffmpegPath, string? ffprobePath)
    {
        _ffmpegPath = ffmpegPath;
        _ffprobePath = ffprobePath;
    }

    public static FfmpegLocator FromConventionalLocations(string runtimeRoot)
    {
        // Windows
        string ffmpegExe = Path.Combine(runtimeRoot, "ffmpeg", "bin", "ffmpeg.exe");
        string ffprobeExe = Path.Combine(runtimeRoot, "ffmpeg", "bin", "ffprobe.exe");

        // Mac/Linux
        string ffmpegBin = Path.Combine(runtimeRoot, "ffmpeg", "bin", "ffmpeg");
        string ffprobeBin = Path.Combine(runtimeRoot, "ffmpeg", "bin", "ffprobe");

        string? foundFfmpeg = File.Exists(ffmpegExe) ? ffmpegExe : (File.Exists(ffmpegBin) ? ffmpegBin : null);
        string? foundFfprobe = File.Exists(ffprobeExe) ? ffprobeExe : (File.Exists(ffprobeBin) ? ffprobeBin : null);

        return new FfmpegLocator(foundFfmpeg, foundFfprobe);
    }

    public string ResolveFfmpeg()
    {
        return _ffmpegPath ?? "ffmpeg";
    }

    public string ResolveFfprobe()
    {
        return _ffprobePath ?? "ffprobe";
    }
}
