using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;

namespace SubtitleGuardian.App.Views;

public partial class SrtToTxtPage : UserControl
{
    public SrtToTxtPage()
    {
        InitializeComponent();
    }

    private void OnPickSrt(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog
        {
            Filter = "SRT Files (*.srt)|*.srt|All Files (*.*)|*.*"
        };

        if (dlg.ShowDialog() == true)
        {
            LoadSrtFile(dlg.FileName);
        }
    }

    private void OnDragOver(object sender, DragEventArgs e)
    {
        if (e.Data.GetDataPresent(DataFormats.FileDrop))
        {
            e.Effects = DragDropEffects.Copy;
        }
        else
        {
            e.Effects = DragDropEffects.None;
        }
        e.Handled = true;
    }

    private void OnSrtDrop(object sender, DragEventArgs e)
    {
        if (e.Data.GetDataPresent(DataFormats.FileDrop))
        {
            string[] files = (string[])e.Data.GetData(DataFormats.FileDrop);
            if (files != null && files.Length > 0)
            {
                LoadSrtFile(files[0]);
            }
        }
    }

    private void LoadSrtFile(string path)
    {
        SrtPathText.Text = path;
        try
        {
            string content = File.ReadAllText(path, Encoding.UTF8);
            SrtContentText.Text = content;
            StatusText.Text = "已載入 SRT 檔案";
            ConvertSrtToTxt(); // Auto convert
        }
        catch (Exception ex)
        {
            MessageBox.Show($"無法讀取檔案: {ex.Message}", "錯誤", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void OnClear(object sender, RoutedEventArgs e)
    {
        SrtPathText.Text = string.Empty;
        SrtContentText.Text = string.Empty;
        TxtContentText.Text = string.Empty;
        StatusText.Text = string.Empty;
    }

    private void OnSrtContentChanged(object sender, TextChangedEventArgs e)
    {
        // Optional: Implement auto-convert or debounce if needed
    }

    private void OnConvertClick(object sender, RoutedEventArgs e)
    {
        ConvertSrtToTxt();
    }

    private void ConvertSrtToTxt()
    {
        string srt = SrtContentText.Text;
        if (string.IsNullOrWhiteSpace(srt))
        {
            TxtContentText.Text = string.Empty;
            return;
        }

        try
        {
            string txt = ParseSrtToTxt(srt);
            TxtContentText.Text = txt;
            StatusText.Text = "轉換完成";
        }
        catch (Exception ex)
        {
            StatusText.Text = $"轉換錯誤: {ex.Message}";
        }
    }

    private string ParseSrtToTxt(string srtContent)
    {
        // Normalize line endings
        string normalized = srtContent.Replace("\r\n", "\n").Replace("\r", "\n");
        string[] lines = normalized.Split('\n');
        
        var sb = new StringBuilder();
        
        // Regex for timestamp line: 00:00:01,000 --> 00:00:04,000
        // Relaxed regex to catch potential variations
        var timestampRegex = new Regex(@"\d{1,2}:\d{2}:\d{2}[,.]\d{3}\s+-->\s+\d{1,2}:\d{2}:\d{2}[,.]\d{3}");
        var indexRegex = new Regex(@"^\d+$");

        for (int i = 0; i < lines.Length; i++)
        {
            string line = lines[i].Trim();
            
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            // Check if it's an index
            if (indexRegex.IsMatch(line))
            {
                // Look ahead for timestamp to confirm it's an index
                bool isIndex = false;
                for (int j = i + 1; j < lines.Length; j++)
                {
                    string nextLine = lines[j].Trim();
                    if (string.IsNullOrWhiteSpace(nextLine)) continue;
                    
                    if (timestampRegex.IsMatch(nextLine))
                    {
                        isIndex = true;
                    }
                    break;
                }
                
                if (isIndex) continue;
            }

            // Check if it's a timestamp
            if (timestampRegex.IsMatch(line))
            {
                continue;
            }

            // It's text
            sb.AppendLine(line);
        }

        return sb.ToString().Trim(); // Trim trailing newline
    }

    private void OnCopyResult(object sender, RoutedEventArgs e)
    {
        if (!string.IsNullOrEmpty(TxtContentText.Text))
        {
            Clipboard.SetText(TxtContentText.Text);
            StatusText.Text = "已複製到剪貼簿";
        }
    }

    private void OnExportTxt(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrEmpty(TxtContentText.Text)) return;

        string initialName = "export.txt";
        if (!string.IsNullOrEmpty(SrtPathText.Text))
        {
            initialName = Path.GetFileNameWithoutExtension(SrtPathText.Text) + ".txt";
        }

        var dlg = new SaveFileDialog
        {
            Filter = "Text Files (*.txt)|*.txt|All Files (*.*)|*.*",
            FileName = initialName
        };

        if (dlg.ShowDialog() == true)
        {
            try
            {
                File.WriteAllText(dlg.FileName, TxtContentText.Text, Encoding.UTF8);
                StatusText.Text = $"已儲存至 {dlg.FileName}";
            }
            catch (Exception ex)
            {
                MessageBox.Show($"儲存失敗: {ex.Message}", "錯誤", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }
}