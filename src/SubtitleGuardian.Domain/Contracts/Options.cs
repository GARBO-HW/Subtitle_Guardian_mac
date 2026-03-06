namespace SubtitleGuardian.Domain.Contracts;

public enum TranscriptionQuality
{
    Tiny = 0,
    Base = 1,
    Small = 2,
    Medium = 3,
    Large = 4
}

public enum ProcessingDevice
{
    GpuWithFallback = 0, // Default: Try GPU, fallback to CPU
    CpuOnly = 1          // Force CPU
}

public sealed record TranscribeOptions(
    string? Language = null,
    TranscriptionQuality Quality = TranscriptionQuality.Medium,
    bool EnableWordTimestamps = false,
    ProcessingDevice Device = ProcessingDevice.GpuWithFallback,
    int MaxSegmentLength = 0 // 0 means default/unlimited
);

public sealed record AlignOptions(
    string? Language = null,
    int MaxShiftMs = 2000
);

