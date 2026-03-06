using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace SubtitleGuardian.Mac.Views;

public partial class SrtToTxtPage : UserControl
{
    public SrtToTxtPage()
    {
        InitializeComponent();

        // Enable Drag and Drop
        InputFilePath.AddHandler(DragDrop.DragOverEvent, OnDragOver);
        InputFilePath.AddHandler(DragDrop.DropEvent, OnDrop);
    }

    private void OnDragOver(object? sender, DragEventArgs e)
    {
        // Only accept files
        if (e.Data.Contains(DataFormats.Files))
        {
            e.DragEffects = DragDropEffects.Copy;
        }
        else
        {
            e.DragEffects = DragDropEffects.None;
        }
    }

    private async void OnDrop(object? sender, DragEventArgs e)
    {
        var files = e.Data.GetFiles();
        if (files == null) return;
        
        var item = files.FirstOrDefault();
        if (item == null) return;
        
        // Ensure it is a file
        if (item is not IStorageFile file) return;
        
        // Check extension (SRT only as requested)
        if (!file.Name.EndsWith(".srt", StringComparison.OrdinalIgnoreCase))
        {
             return;
        }

        InputFilePath.Text = file.Path.LocalPath;
        
        try
        {
            using var stream = await file.OpenReadAsync();
            using var reader = new StreamReader(stream);
            SrtInputBox.Text = await reader.ReadToEndAsync();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error reading file: {ex.Message}");
        }
    }

    private async void OnSelectFile(object? sender, RoutedEventArgs e)
    {
        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel == null) return;

        var files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "選取 SRT 檔案",
            AllowMultiple = false,
            FileTypeFilter = new[] { new FilePickerFileType("SRT Subtitles") { Patterns = new[] { "*.srt" } } }
        });

        if (files.Count >= 1)
        {
            var file = files[0];
            InputFilePath.Text = file.Path.LocalPath; // Use LocalPath for display

            try
            {
                using var stream = await file.OpenReadAsync();
                using var reader = new StreamReader(stream);
                SrtInputBox.Text = await reader.ReadToEndAsync();
            }
            catch (Exception ex)
            {
                // Show error (for now just console/debug, maybe message box later if needed)
                Console.WriteLine($"Error reading file: {ex.Message}");
            }
        }
    }

    private void OnClear(object? sender, RoutedEventArgs e)
    {
        InputFilePath.Text = string.Empty;
        SrtInputBox.Text = string.Empty;
        TxtOutputBox.Text = string.Empty;
    }

    private void OnConvert(object? sender, RoutedEventArgs e)
    {
        var input = SrtInputBox.Text;
        if (string.IsNullOrWhiteSpace(input))
        {
            TxtOutputBox.Text = string.Empty;
            return;
        }

        TxtOutputBox.Text = ConvertSrtToTxt(input);
    }

    private string ConvertSrtToTxt(string srtContent)
    {
        var sb = new StringBuilder();
        var lines = srtContent.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None);
        
        // Regex for timestamp line: 00:00:00,000 --> 00:00:00,000
        var timestampRegex = new Regex(@"\d{2}:\d{2}:\d{2}[,.]\d{3}\s*-->\s*\d{2}:\d{2}:\d{2}[,.]\d{3}");
        
        bool lastLineWasEmpty = true;

        for (int i = 0; i < lines.Length; i++)
        {
            string line = lines[i].Trim();

            // 1. Skip empty lines (but we might want to preserve paragraph breaks if multiple empty lines?)
            // For now, let's just skip them to compact the text, or handle them smartly.
            // Usually SRT has one empty line between cues.
            if (string.IsNullOrWhiteSpace(line))
            {
                if (!lastLineWasEmpty)
                {
                    // sb.AppendLine(); // Optional: add newline between cues?
                    // User usually wants continuous text or one line per cue.
                    // Let's add a space if we are merging, or newline.
                    // Common request: keep lines, just remove timestamps.
                    // Let's preserve line breaks from original text, but join cues with newline.
                }
                lastLineWasEmpty = true;
                continue;
            }

            // 2. Skip Index (only digits)
            // But be careful: text might be "2023".
            // Heuristic: If it's a number AND the next line is a timestamp, it's an index.
            // Or if previous line was empty (or start of file) AND this is a number.
            bool isIndex = false;
            if (int.TryParse(line, out _) && (lastLineWasEmpty || i == 0))
            {
                // Look ahead for timestamp
                if (i + 1 < lines.Length && timestampRegex.IsMatch(lines[i + 1]))
                {
                    isIndex = true;
                }
            }

            if (isIndex)
            {
                lastLineWasEmpty = false;
                continue;
            }

            // 3. Skip Timestamp
            if (timestampRegex.IsMatch(line))
            {
                lastLineWasEmpty = false;
                continue;
            }

            // 4. It's Text
            sb.AppendLine(line);
            lastLineWasEmpty = false;
        }

        return sb.ToString().Trim();
    }

    private async void OnCopyResult(object? sender, RoutedEventArgs e)
    {
        var text = TxtOutputBox.Text;
        if (string.IsNullOrEmpty(text)) return;

        var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
        if (clipboard != null)
        {
            await clipboard.SetTextAsync(text);
        }
    }

    private async void OnSaveTxt(object? sender, RoutedEventArgs e)
    {
        var text = TxtOutputBox.Text;
        if (string.IsNullOrEmpty(text)) return;

        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel == null) return;

        var file = await topLevel.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "儲存 TXT 檔案",
            DefaultExtension = "txt",
            FileTypeChoices = new[] { new FilePickerFileType("Text Files") { Patterns = new[] { "*.txt" } } }
        });

        if (file != null)
        {
            try
            {
                using var stream = await file.OpenWriteAsync();
                using var writer = new StreamWriter(stream);
                await writer.WriteAsync(text);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error saving file: {ex.Message}");
            }
        }
    }
}
