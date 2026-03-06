using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using SubtitleGuardian.Application.Formatting;
using SubtitleGuardian.Application.Jobs;
using SubtitleGuardian.Application.Media;
using SubtitleGuardian.Application.Transcription;
using SubtitleGuardian.Domain.Contracts;
using SubtitleGuardian.Domain.Media;
using SubtitleGuardian.Engines.Asr;
using SubtitleGuardian.Infrastructure.Storage;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;

namespace SubtitleGuardian.Mac.Views
{
    public partial class AsrPage : UserControl
    {
        private readonly JobScheduler? _scheduler;
        private readonly MediaApplicationService? _media;
        private readonly TranscriptionUseCase? _transcription;
        private readonly ObservableCollection<AudioTrackRow> _audioTracks;
        private readonly ObservableCollection<AsrEngineRow> _asrEngines;
        private readonly ObservableCollection<ModelSizeRow> _modelSizes;
        private readonly ObservableCollection<ProcessingDeviceRow> _devices;
        private readonly ObservableCollection<OutputFormatRow> _outputFormats;
        private readonly ObservableCollection<LanguageRow> _languages;
        private readonly string _settingsPath;

        private string? _sourcePath;
        private string? _standardWavPath;
        private string? _exportDir;
        private bool _isTranscribing;
        private bool _isProbing;
        private OutputFormat _outputFormat;
        private JobHandle? _currentTranscribeHandle;
        private string? _lastExportPath;

        public AsrPage()
        {
            InitializeComponent();
            _audioTracks = new ObservableCollection<AudioTrackRow>();
            _asrEngines = new ObservableCollection<AsrEngineRow>();
            _modelSizes = new ObservableCollection<ModelSizeRow>();
            _devices = new ObservableCollection<ProcessingDeviceRow>();
            _outputFormats = new ObservableCollection<OutputFormatRow>();
            _languages = new ObservableCollection<LanguageRow>();
            _settingsPath = ResolveUiSettingsPath(out _);
        }

        public AsrPage(JobScheduler scheduler, MediaApplicationService media, TranscriptionUseCase transcription) : this()
        {
            _scheduler = scheduler;
            _media = media;
            _transcription = transcription;

            InitializeCollections();
            InitializeUi();

            AddHandler(DragDrop.DropEvent, OnMediaDrop);
            AddHandler(DragDrop.DragOverEvent, OnDragOver);
        }

        private void InitializeCollections()
        {
            _asrEngines.Add(new AsrEngineRow(AsrEngineId.Whisper, "Whisper"));

            _modelSizes.Add(new ModelSizeRow(TranscriptionQuality.Tiny, "Tiny"));
            _modelSizes.Add(new ModelSizeRow(TranscriptionQuality.Base, "Base"));
            _modelSizes.Add(new ModelSizeRow(TranscriptionQuality.Small, "Small"));
            _modelSizes.Add(new ModelSizeRow(TranscriptionQuality.Medium, "Medium"));
            _modelSizes.Add(new ModelSizeRow(TranscriptionQuality.Large, "Large"));

            _devices.Add(new ProcessingDeviceRow(ProcessingDevice.GpuWithFallback, "GPU (自動判定)"));
            _devices.Add(new ProcessingDeviceRow(ProcessingDevice.CpuOnly, "CPU (強制)"));

            _outputFormats.Add(new OutputFormatRow(OutputFormat.None, "不匯出"));
            _outputFormats.Add(new OutputFormatRow(OutputFormat.Srt, "SRT"));
            _outputFormats.Add(new OutputFormatRow(OutputFormat.Txt, "TXT"));

            _languages.Add(new LanguageRow("auto", "Auto"));
            _languages.Add(new LanguageRow("zh", "中文"));
            _languages.Add(new LanguageRow("zh-TW", "中文（繁體）"));
            _languages.Add(new LanguageRow("zh-CN", "中文（簡體）"));
            _languages.Add(new LanguageRow("en", "English"));
            _languages.Add(new LanguageRow("ja", "日本語"));
            _languages.Add(new LanguageRow("ko", "한국어"));
        }

        private void InitializeUi()
        {
            AudioTracksCombo.ItemsSource = _audioTracks;
            AsrEngineCombo.ItemsSource = _asrEngines;
            AsrEngineCombo.SelectedValue = AsrEngineId.Whisper;
            ModelSizeCombo.ItemsSource = _modelSizes;
            ModelSizeCombo.SelectedValue = TranscriptionQuality.Medium;
            DeviceCombo.ItemsSource = _devices;
            DeviceCombo.SelectedValue = ProcessingDevice.GpuWithFallback;
            LanguageCombo.ItemsSource = _languages;
            LanguageCombo.SelectedValue = "zh-TW";
            OutputFormatCombo.ItemsSource = _outputFormats;

            UiSettings s = LoadSettingsOrDefault(_settingsPath, out _);
            _exportDir = s.ExportDirectory;
            ExportDirText.Text = _exportDir ?? string.Empty;
            _outputFormat = ParseOutputFormat(s.OutputFormat);
            OutputFormatCombo.SelectedValue = _outputFormat;

            UpdateUiState();
        }

        private void OnMediaDrop(object? sender, DragEventArgs e)
        {
            var files = e.Data.GetFiles();
            if (files != null)
            {
                var file = files.FirstOrDefault();
                if (file != null)
                {
                    string path = file.Path.LocalPath;
                    string ext = Path.GetExtension(path).ToLowerInvariant();
                    if (ext == ".mp3" || ext == ".wav" || ext == ".mp4" || ext == ".mkv" ||
                        ext == ".m4a" || ext == ".aac" || ext == ".flac" || ext == ".webm")
                    {
                        LoadMediaFile(path);
                    }
                }
            }
        }

        private void OnDragOver(object? sender, DragEventArgs e)
        {
            if (e.Data.Contains(DataFormats.Files))
            {
                e.DragEffects = DragDropEffects.Copy;
                e.Handled = true;
            }
            else
            {
                e.DragEffects = DragDropEffects.None;
            }
        }

        private async void OnPickMedia(object? sender, RoutedEventArgs e)
        {
            var topLevel = TopLevel.GetTopLevel(this);
            if (topLevel == null) return;

            var files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = "選擇音檔或影片",
                AllowMultiple = false,
                FileTypeFilter = new[]
                {
                    new FilePickerFileType("Media Files")
                    {
                        Patterns = new[] { "*.mp3", "*.wav", "*.mp4", "*.mkv", "*.m4a", "*.aac", "*.flac", "*.webm" }
                    },
                    new FilePickerFileType("All Files")
                    {
                        Patterns = new[] { "*.*" }
                    }
                }
            });

            if (files.Count > 0)
            {
                LoadMediaFile(files[0].Path.LocalPath);
            }
        }

        private void LoadMediaFile(string path)
        {
            _sourcePath = path;
            SourcePathText.Text = _sourcePath;
            _standardWavPath = null;
            _audioTracks.Clear();
            AudioTracksCombo.SelectedIndex = -1;

            _isProbing = true;
            UpdateUiState();

            if (_scheduler == null || _media == null) return;

            JobHandle handle = _scheduler.Enqueue("讀取媒體資訊", async ctx =>
            {
                ctx.Progress.Report(new JobProgress(0, "ffprobe..."));
                MediaInfo info = await _media.ProbeAsync(path, ctx.CancellationToken).ConfigureAwait(false);

                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    _audioTracks.Clear();
                    foreach (AudioStreamInfo s in info.AudioStreams)
                    {
                        _audioTracks.Add(AudioTrackRow.From(s));
                    }

                    if (_audioTracks.Count > 0)
                    {
                        AudioTracksCombo.SelectedIndex = 0;
                    }
                });

                ctx.Progress.Report(new JobProgress(100, $"audio={info.AudioStreams.Count}"));
                return info;
            });

            _ = handle.Completion.ContinueWith(_ =>
            {
                Dispatcher.UIThread.InvokeAsync(() =>
                {
                    _isProbing = false;
                    UpdateUiState();
                });
            }, TaskScheduler.Default);
        }

        private async void OnBrowseExportDir(object? sender, RoutedEventArgs e)
        {
            var topLevel = TopLevel.GetTopLevel(this);
            if (topLevel == null) return;

            string start = _exportDir ?? string.Empty;
            if (string.IsNullOrWhiteSpace(start) || !Directory.Exists(start))
            {
                start = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
            }

            var folders = await topLevel.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
            {
                Title = "選擇輸出資料夾",
                AllowMultiple = false,
                SuggestedStartLocation = await topLevel.StorageProvider.TryGetFolderFromPathAsync(start)
            });

            if (folders.Count > 0)
            {
                _exportDir = folders[0].Path.LocalPath;
                ExportDirText.Text = _exportDir;
                SaveSettings();
                UpdateUiState();
            }
        }

        private void OnClearExportDir(object? sender, RoutedEventArgs e)
        {
            _exportDir = null;
            ExportDirText.Text = string.Empty;
            SaveSettings();
            UpdateUiState();
        }

        private void OnExportDirLostFocus(object? sender, RoutedEventArgs e)
        {
            _exportDir = string.IsNullOrWhiteSpace(ExportDirText.Text) ? null : ExportDirText.Text.Trim();
            SaveSettings();
            UpdateUiState();
        }

        private void OnOutputFormatSelectionChanged(object? sender, SelectionChangedEventArgs e)
        {
            _outputFormat = OutputFormatCombo.SelectedValue is OutputFormat f ? f : OutputFormat.Srt;
            SaveSettings();
            UpdateUiState();
        }

        private void OnTranscribeAsr(object? sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(_sourcePath)) return;
            if (_isProbing) return;

            if (_isTranscribing)
            {
                _currentTranscribeHandle?.Cancel();
                return;
            }

            if (_scheduler == null || _transcription == null || _media == null) return;

            AsrEngineId engineId = AsrEngineCombo.SelectedValue is AsrEngineId id ? id : AsrEngineId.Whisper;
            TranscriptionQuality quality = ModelSizeCombo.SelectedValue is TranscriptionQuality q ? q : TranscriptionQuality.Medium;
            string? lang = LanguageCombo.SelectedValue as string;
            if (string.Equals(lang, "auto", StringComparison.OrdinalIgnoreCase))
            {
                lang = "auto";
            }
            int? audioIndex = AudioTracksCombo.SelectedValue is int i ? i : null;
            string sourcePath = _sourcePath;
            string? exportDir = _exportDir;
            OutputFormat outputFormat = _outputFormat;
            ProcessingDevice device = DeviceCombo.SelectedValue is ProcessingDevice d ? d : ProcessingDevice.GpuWithFallback;

            int maxSegmentLength = 0;
            if (int.TryParse(MaxSegmentLengthText.Text, out int parsedMax) && parsedMax > 0)
            {
                maxSegmentLength = parsedMax;
            }

            _isTranscribing = true;
            UpdateUiState();

            JobHandle handle = _scheduler.Enqueue($"ASR 轉錄 ({engineId})", async ctx =>
            {
                var asrProgress = new Progress<AsrProgress>(p =>
                {
                    ctx.Progress.Report(new JobProgress(p.Percent, p.Message));
                });

                string wav = _standardWavPath is not null && File.Exists(_standardWavPath)
                    ? _standardWavPath
                    : string.Empty;

                if (string.IsNullOrWhiteSpace(wav))
                {
                    ctx.Progress.Report(new JobProgress(0, "Probe + 抽取標準音訊..."));
                    var resolved = await _media.ResolveToStandardWavAsync(sourcePath, audioIndex, ctx.CancellationToken).ConfigureAwait(false);

                    await Dispatcher.UIThread.InvokeAsync(() =>
                    {
                        _audioTracks.Clear();
                        foreach (AudioStreamInfo s in resolved.MediaInfo.AudioStreams)
                        {
                            _audioTracks.Add(AudioTrackRow.From(s));
                        }

                        if (_audioTracks.Count > 0)
                        {
                            AudioTracksCombo.SelectedValue = resolved.AudioStreamIndex ?? 0;
                        }

                        _standardWavPath = resolved.StandardWavPath;
                    });

                    wav = resolved.StandardWavPath;
                }

                ctx.Progress.Report(new JobProgress(0, "初始化語音轉錄引擎..."));

                var req = new TranscriptionRequest(
                    engineId,
                    wav,
                    new TranscribeOptions(Language: lang, Quality: quality, EnableWordTimestamps: true, Device: device, MaxSegmentLength: maxSegmentLength)
                );

                IReadOnlyList<Segment> segments = await _transcription.ExecuteAsync(req, asrProgress, ctx.CancellationToken).ConfigureAwait(false);

                if (outputFormat == OutputFormat.None)
                {
                    ctx.Progress.Report(new JobProgress(98, "輸出格式設為不匯出，略過輸出"));
                }
                else if (!string.IsNullOrWhiteSpace(exportDir))
                {
                    try
                    {
                        Directory.CreateDirectory(exportDir);
                        string ext = outputFormat == OutputFormat.Txt ? ".txt" : ".srt";
                        string outFile = ResolveAutoOutputPath(exportDir, sourcePath, ext);
                        string label = outputFormat == OutputFormat.Txt ? "TXT" : "SRT";
                        ctx.Progress.Report(new JobProgress(95, $"匯出 {label}..."));

                        if (outputFormat == OutputFormat.Txt)
                        {
                            new TxtFormatter().WriteToFile(outFile, segments);
                        }
                        else
                        {
                            new SrtFormatter().WriteToFile(outFile, segments);
                        }

                        await Dispatcher.UIThread.InvokeAsync(() =>
                        {
                            _lastExportPath = outFile;
                            UpdateUiState();
                        });

                        ctx.Progress.Report(new JobProgress(98, $"已匯出：{outFile}"));
                    }
                    catch (Exception ex)
                    {
                        ctx.Progress.Report(new JobProgress(98, $"匯出失敗：{ex.Message}"));
                    }
                }
                else
                {
                    ctx.Progress.Report(new JobProgress(98, "未設定輸出目錄，略過匯出"));
                }

                ctx.Progress.Report(new JobProgress(100, $"segments={segments.Count}"));
                return segments;
            });

            _currentTranscribeHandle = handle;
            _ = handle.Completion.ContinueWith(_ =>
            {
                Dispatcher.UIThread.InvokeAsync(() =>
                {
                    _isTranscribing = false;
                    _currentTranscribeHandle = null;
                    UpdateUiState();
                });
            }, TaskScheduler.Default);
        }

        private void OnOpenExportDir(object? sender, RoutedEventArgs e)
        {
            try
            {
                string? file = _lastExportPath;
                if (!string.IsNullOrWhiteSpace(file) && File.Exists(file))
                {
                    // Mac uses 'open -R' to reveal file in Finder
                    Process.Start("open", $"-R \"{file}\"");
                    return;
                }

                string? dir = _exportDir;
                if (!string.IsNullOrWhiteSpace(dir) && Directory.Exists(dir))
                {
                    // Mac uses 'open' to open directory
                    Process.Start("open", $"\"{dir}\"");
                }
            }
            catch
            {
            }
        }

        private void OnAudioTrackSelectionChanged(object? sender, SelectionChangedEventArgs e)
        {
            _standardWavPath = null;
        }

        private void UpdateUiState()
        {
            bool canTranscribe = !_isTranscribing && !_isProbing && !string.IsNullOrWhiteSpace(_sourcePath);
            TranscribeButton.IsEnabled = _isTranscribing || canTranscribe;
            TranscribeButton.Content = _isTranscribing ? "取消轉錄" : "轉錄（ASR）";
            PickMediaButton.IsEnabled = !_isTranscribing && !_isProbing;
            ExportDirText.IsEnabled = !_isTranscribing;
            OutputFormatCombo.IsEnabled = !_isTranscribing;
            BrowseExportDirButton.IsEnabled = !_isTranscribing;
            ClearExportDirButton.IsEnabled = !_isTranscribing && !string.IsNullOrWhiteSpace(_exportDir);
            OpenExportDirButton.IsEnabled = !_isTranscribing && !string.IsNullOrWhiteSpace(_exportDir) && Directory.Exists(_exportDir);
            
            MaxSegmentLengthText.IsEnabled = !_isTranscribing;
        }

        // Records and Enums
        private sealed record AudioTrackRow(int AudioIndex, string Label)
        {
            public static AudioTrackRow From(AudioStreamInfo s)
            {
                string codec = s.Codec ?? "unknown";
                string ch = s.Channels is null ? "?" : s.Channels.Value.ToString();
                string sr = s.SampleRate is null ? "?" : s.SampleRate.Value.ToString();
                string lang = string.IsNullOrWhiteSpace(s.Language) ? string.Empty : $" {s.Language}";
                string title = string.IsNullOrWhiteSpace(s.Title) ? string.Empty : $" {s.Title}";

                return new AudioTrackRow(s.AudioIndex, $"#{s.AudioIndex} ({codec} {ch}ch {sr}Hz){lang}{title}");
            }
        }

        private sealed record AsrEngineRow(AsrEngineId Id, string Label);
        private sealed record ModelSizeRow(TranscriptionQuality Value, string Label);
        private sealed record ProcessingDeviceRow(ProcessingDevice Value, string Label);
        private enum OutputFormat
        {
            None = 0,
            Srt = 1,
            Txt = 2
        }

        private sealed record OutputFormatRow(OutputFormat Id, string Label);
        private sealed record LanguageRow(string Code, string Label);

        private sealed record UiSettings(string? ExportDirectory, string? OutputFormat, string? ExportSrtDirectory);

        private static UiSettings LoadSettingsOrDefault(string settingsPath, out string root)
        {
            var (installRoot, userRoot) = AppPaths.ResolveRoots("SubtitleGuardian");
            root = userRoot;
            try
            {
                if (File.Exists(settingsPath))
                {
                    string json = File.ReadAllText(settingsPath);
                    UiSettings? s = JsonSerializer.Deserialize<UiSettings>(json);
                    if (s is not null)
                    {
                        string? exportDir = string.IsNullOrWhiteSpace(s.ExportDirectory) ? null : s.ExportDirectory.Trim();
                        if (string.IsNullOrWhiteSpace(exportDir) && !string.IsNullOrWhiteSpace(s.ExportSrtDirectory))
                        {
                            exportDir = s.ExportSrtDirectory.Trim();
                        }

                        string? fmt = string.IsNullOrWhiteSpace(s.OutputFormat) ? null : s.OutputFormat.Trim().ToLowerInvariant();
                        return new UiSettings(exportDir, fmt, s.ExportSrtDirectory);
                    }
                }
            }
            catch
            {
            }

            return new UiSettings(null, null, null);
        }

        private void SaveSettings()
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(_settingsPath)!);
                string fmt = _outputFormat switch
                {
                    OutputFormat.Txt => "txt",
                    OutputFormat.None => "none",
                    _ => "srt"
                };
                var s = new UiSettings(_exportDir, fmt, null);
                string json = JsonSerializer.Serialize(s);
                File.WriteAllText(_settingsPath, json);
            }
            catch
            {
            }
        }

        private static OutputFormat ParseOutputFormat(string? value)
        {
            if (value is null) return OutputFormat.Srt;
            if (value.Equals("none", StringComparison.OrdinalIgnoreCase)) return OutputFormat.None;
            if (value.Equals("txt", StringComparison.OrdinalIgnoreCase)) return OutputFormat.Txt;
            return OutputFormat.Srt;
        }

        private static string ResolveUiSettingsPath(out string root)
        {
            var (installRoot, userRoot) = AppPaths.ResolveRoots("SubtitleGuardian");
            root = userRoot;
            return Path.Combine(root, "ui-settings.json");
        }

        private static string ResolveAutoOutputPath(string exportDir, string sourcePath, string extensionWithDot)
        {
            string baseName = Path.GetFileNameWithoutExtension(sourcePath);
            if (string.IsNullOrWhiteSpace(baseName)) baseName = "output";

            string candidate = Path.Combine(exportDir, baseName + extensionWithDot);
            if (!File.Exists(candidate)) return candidate;

            for (int i = 1; i <= 9999; i++)
            {
                string c = Path.Combine(exportDir, $"{baseName} ({i}){extensionWithDot}");
                if (!File.Exists(c)) return c;
            }

            return Path.Combine(exportDir, $"{baseName} ({Guid.NewGuid():N}){extensionWithDot}");
        }
    }
}
