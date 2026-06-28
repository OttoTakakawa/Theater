using System.IO;
using System.Windows;
using Theater.Models;
using Theater.Services;

namespace Theater;

public partial class ImportSingleBookConfirmDialog : Window
{
    private readonly BatchImportCandidate _candidate;

    public string EditedTitle { get; private set; } = "";

    public ImportSingleBookConfirmDialog(BatchImportCandidate candidate)
    {
        InitializeComponent();
        _candidate = candidate;
        FolderPathText.Text = candidate.FolderPath;
        TitleBox.Text = candidate.Title;
        SummaryText.Text = BuildSummary(candidate);
        UpdateConfirmButton();
    }

    private static string BuildSummary(BatchImportCandidate candidate)
    {
        var parts = new List<string>();
        if (candidate.VideoCount > 0)
        {
            parts.Add($"{candidate.VideoCount} 个视频");
        }
        if (candidate.ImageSetPaths.Count > 0)
        {
            parts.Add($"{candidate.ImageSetPaths.Count} 个图集");
        }
        if (candidate.PageCount > 0)
        {
            parts.Add($"{candidate.PageCount} 张图片");
        }

        var totalBytes = ImageLoader.SumFileBytes(candidate.Pages)
            + SumVideoBytes(candidate.VideoPaths)
            + SumImageSetBytes(candidate.ImageSetPaths);
        if (totalBytes > 0)
        {
            parts.Add(FormatSize(totalBytes));
        }

        return parts.Count > 0 ? string.Join(" · ", parts) : "未识别到内容";
    }

    private static long SumVideoBytes(IReadOnlyList<string> paths)
    {
        long sum = 0;
        foreach (var path in paths)
        {
            try { sum += new FileInfo(path).Length; } catch { }
        }
        return sum;
    }

    private static long SumImageSetBytes(IReadOnlyList<string> setPaths)
    {
        long sum = 0;
        foreach (var setPath in setPaths)
        {
            try
            {
                foreach (var file in Directory.EnumerateFiles(setPath))
                {
                    if (ImageLoader.IsSupportedImage(file))
                    {
                        try { sum += new FileInfo(file).Length; } catch { }
                    }
                }
            }
            catch { }
        }
        return sum;
    }

    private static string FormatSize(long bytes)
    {
        if (bytes >= 1L << 30) return $"{bytes / (double)(1L << 30):F2} GB";
        if (bytes >= 1L << 20) return $"{bytes / (double)(1L << 20):F1} MB";
        if (bytes >= 1L << 10) return $"{bytes / (double)(1L << 10):F0} KB";
        return $"{bytes} B";
    }

    private void TitleBox_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
    {
        EditedTitle = TitleBox.Text.Trim();
        UpdateConfirmButton();
    }

    private void UpdateConfirmButton()
    {
        ConfirmButton.IsEnabled = !string.IsNullOrWhiteSpace(TitleBox.Text);
    }

    private void Confirm_Click(object sender, RoutedEventArgs e)
    {
        EditedTitle = TitleBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(EditedTitle))
        {
            HintText.Text = "标题不能为空。";
            return;
        }
        DialogResult = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }
}
