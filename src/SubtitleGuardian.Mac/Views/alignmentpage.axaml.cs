using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using SubtitleGuardian.Application.Alignment;
using SubtitleGuardian.Application.Formatting;
using SubtitleGuardian.Application.Jobs;
using SubtitleGuardian.Application.Media;
using SubtitleGuardian.Application.Text;
using SubtitleGuardian.Application.Transcription;
using SubtitleGuardian.Domain.Contracts;
using SubtitleGuardian.Domain.Text;
using SubtitleGuardian.Engines.Asr;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace SubtitleGuardian.Mac.Views
{
    public partial class AlignmentPage : UserControl
    {
        private readonly JobScheduler? _scheduler;
        private readonly MediaApplicationService? _media;
        private readonly TranscriptionUseCase? _transcription;
        private readonly ObservableCollection<LanguageRow> _languages;

        private string? _audioPath;
        private string? _textPath;
        private string? _exportDir;
        private bool _isAligning;

        public AlignmentPage()
        {
            InitializeComponent();
            _languages = new ObservableCollection<LanguageRow>();
            TranscriptInput.TextChanged += OnTranscriptTextChanged;
        }

        public AlignmentPage(JobScheduler scheduler, MediaApplicationService media, TranscriptionUseCase transcription) : this()
        {
            _scheduler = scheduler;
            _media = media;
            _transcription = transcription;

            _languages.Add(new LanguageRow("zh", "中文"));
            _languages.Add(new LanguageRow("zh-TW", "中文（繁體）"));
            _languages.Add(new LanguageRow("zh-CN", "中文（簡體）"));
            _languages.Add(new LanguageRow("en", "English"));
            _languages.Add(new LanguageRow("ja", "日本語"));
            _languages.Add(new LanguageRow("ko", "한국어"));

            LanguageCombo.ItemsSource = _languages;
            LanguageCombo.SelectedValue = "zh-TW";

            AddHandler(DragDrop.DropEvent, OnDrop);
            AddHandler(DragDrop.DragOverEvent, OnDragOver);

            UpdateUiState();
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

        private void OnDrop(object? sender, DragEventArgs e)
        {
            var files = e.Data.GetFiles();
            if (files == null) return;

            var file = files.FirstOrDefault();
            if (file == null) return;

            string path = file.Path.LocalPath;
            string ext = Path.GetExtension(path).ToLowerInvariant();

            // Check if dropped on Audio Source or Text Source area
            // Avalonia's drag drop is bubbled. We can check the source or just infer from extension.
            // But since we use a global handler for simplicity in this port:
            
            bool isAudio = ext == ".mp3" || ext == ".wav" || ext == ".mp4" || ext == ".mkv" ||
                           ext == ".m4a" || ext == ".aac" || ext == ".flac" || ext == ".webm";
            
            bool isText = ext == ".txt" || ext == ".srt" || ext == ".md" || ext == ".csv";

            if (isAudio)
            {
                LoadAudioFile(path);
            }
            else if (isText)
            {
                LoadTextFile(path);
            }
        }

        private async void OnPickAudio(object? sender, RoutedEventArgs e)
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
                LoadAudioFile(files[0].Path.LocalPath);
            }
        }

        private void LoadAudioFile(string path)
        {
            _audioPath = path;
            AudioSourceText.Text = _audioPath;

            // Auto-set export dir if not set or empty
            if (string.IsNullOrWhiteSpace(_exportDir) || string.IsNullOrWhiteSpace(ExportDirText.Text))
            {
                _exportDir = Path.GetDirectoryName(_audioPath);
                ExportDirText.Text = _exportDir;
            }

            UpdateUiState();
        }

        private async void OnPickTextFile(object? sender, RoutedEventArgs e)
        {
            var topLevel = TopLevel.GetTopLevel(this);
            if (topLevel == null) return;

            var files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = "選擇文字稿",
                AllowMultiple = false,
                FileTypeFilter = new[]
                {
                    new FilePickerFileType("Text Files")
                    {
                        Patterns = new[] { "*.txt", "*.srt", "*.md", "*.csv" }
                    },
                    new FilePickerFileType("All Files")
                    {
                        Patterns = new[] { "*.*" }
                    }
                }
            });

            if (files.Count > 0)
            {
                LoadTextFile(files[0].Path.LocalPath);
            }
        }

        private void LoadTextFile(string path)
        {
            _textPath = path;
            TextSourcePathText.Text = _textPath;
            try
            {
                TranscriptInput.Text = File.ReadAllText(_textPath);
            }
            catch (Exception ex)
            {
                // In Avalonia we don't have MessageBox easily available without extra packages.
                // We'll just ignore or log to console for now.
                Console.WriteLine($"Error reading text file: {ex.Message}");
            }

            UpdateUiState();
        }

        private void OnTranscriptTextChanged(object? sender, TextChangedEventArgs e)
        {
            UpdateUiState();
        }

        private async void OnBrowseExport(object? sender, RoutedEventArgs e)
        {
            var topLevel = TopLevel.GetTopLevel(this);
            if (topLevel == null) return;

            var folders = await topLevel.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
            {
                Title = "選擇輸出資料夾",
                AllowMultiple = false
            });

            if (folders.Count > 0)
            {
                _exportDir = folders[0].Path.LocalPath;
                ExportDirText.Text = _exportDir;
                UpdateUiState();
            }
        }

        private void OnStartAlignment(object? sender, RoutedEventArgs e)
        {
            if (_scheduler == null || _media == null || _transcription == null || 
                string.IsNullOrWhiteSpace(_audioPath) || string.IsNullOrWhiteSpace(TranscriptInput.Text))
            {
                return;
            }

            string audioPath = _audioPath;
            string transcript = TranscriptInput.Text;
            string? exportDir = _exportDir;
            string? lang = LanguageCombo.SelectedValue as string;

            if (!int.TryParse(MaxCharsText.Text, out int maxChars))
            {
                maxChars = 40;
            }

            _isAligning = true;
            UpdateUiState();

            var handle = _scheduler.Enqueue("文稿匹配 (Alignment)", async ctx =>
            {
                ctx.Progress.Report(new JobProgress(0, "初始化..."));

                // 1. ASR
                ctx.Progress.Report(new JobProgress(10, "執行語音轉錄 (ASR)..."));

                // Transcode to standard WAV for better compatibility
                ctx.Progress.Report(new JobProgress(15, "轉碼至標準音訊格式..."));
                var resolvedAudio = await _media.ResolveToStandardWavAsync(audioPath, null, ctx.CancellationToken).ConfigureAwait(false);
                string finalAudioPath = resolvedAudio.StandardWavPath;

                var req = new TranscriptionRequest(
                    AsrEngineId.Whisper,
                    finalAudioPath,
                    new TranscribeOptions(Language: lang, Quality: TranscriptionQuality.Medium, EnableWordTimestamps: true)
                );

                IReadOnlyList<Segment> asrSegments;
                try
                {
                    asrSegments = await _transcription.ExecuteAsync(req, null, ctx.CancellationToken).ConfigureAwait(false);
                }
                catch (Exception ex) when (ex is NotSupportedException || ex.Message.Contains("not implemented"))
                {
                    ctx.Progress.Report(new JobProgress(15, $"ASR 引擎未就緒 ({ex.Message})，使用測試資料模擬..."));
                    await Task.Delay(1000, ctx.CancellationToken); // Simulate delay
                    asrSegments = GenerateMockAsrSegments(transcript);
                }

                if (ctx.CancellationToken.IsCancellationRequested) return false;

                // 2. Alignment
                ctx.Progress.Report(new JobProgress(40, $"取得 {asrSegments.Count} 個語音片段，開始匹配..."));

                var aligner = new TextAligner();
                var splitOptions = new SentenceSplitOptions
                {
                    MaxSentenceLength = maxChars,
                    KeepPunctuation = false // Cleaner matching
                };

                // Run alignment
                var alignedSegments = aligner.Align(asrSegments, transcript, splitOptions);

                ctx.Progress.Report(new JobProgress(80, $"匹配完成，共 {alignedSegments.Count} 句"));

                // 3. Export
                if (!string.IsNullOrWhiteSpace(exportDir))
                {
                    try
                    {
                        Directory.CreateDirectory(exportDir);
                        string fileName = Path.GetFileNameWithoutExtension(audioPath) + "_aligned.srt";
                        string outFile = Path.Combine(exportDir, fileName);

                        ctx.Progress.Report(new JobProgress(90, $"匯出至 {fileName}..."));

                        new SrtFormatter().WriteToFile(outFile, alignedSegments);

                        ctx.Progress.Report(new JobProgress(100, $"已匯出：{outFile}"));
                    }
                    catch (Exception ex)
                    {
                        ctx.Progress.Report(new JobProgress(95, $"匯出失敗：{ex.Message}"));
                    }
                }
                else
                {
                    ctx.Progress.Report(new JobProgress(100, "完成 (未設定匯出目錄)"));
                }

                return true;
            });

            _ = handle.Completion.ContinueWith(_ =>
            {
                Dispatcher.UIThread.InvokeAsync(() =>
                {
                    _isAligning = false;
                    UpdateUiState();
                });
            }, TaskScheduler.Default);
        }

        private List<Segment> GenerateMockAsrSegments(string userText)
        {
            // Simulate rough ASR output from the user text for testing purposes
            var splitter = new SentenceSplitter();
            var sentences = splitter.Split(userText);
            var result = new List<Segment>();
            long time = 0;

            foreach (var s in sentences)
            {
                string asrText = s;
                if (s.Length > 5) asrText = s.Substring(0, s.Length - 1);

                long duration = Math.Max(1000, s.Length * 200);
                result.Add(new Segment(time, time + duration, asrText));
                time += duration + 200;
            }
            return result;
        }

        private void UpdateUiState()
        {
            bool canAlign = !_isAligning &&
                            !string.IsNullOrWhiteSpace(_audioPath) &&
                            !string.IsNullOrWhiteSpace(TranscriptInput.Text);

            AlignButton.IsEnabled = canAlign;
            AlignButton.Content = _isAligning ? "匹配中..." : "開始匹配 (Align)";

            PickAudioButton.IsEnabled = !_isAligning;
            PickTextButton.IsEnabled = !_isAligning;
            TranscriptInput.IsReadOnly = _isAligning;
            BrowseExportButton.IsEnabled = !_isAligning;
        }

        private sealed record LanguageRow(string Code, string Label);
    }
}
