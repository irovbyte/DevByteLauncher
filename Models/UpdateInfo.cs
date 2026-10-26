namespace DevByteLauncher.Models;

public class UpdateInfo
{
    public bool IsAvailable { get; set; }
    public string CurrentVersion { get; set; } = UpdateService.CurrentVersion;
    public string LatestVersion { get; set; } = string.Empty;
    public string ReleaseName { get; set; } = string.Empty;
    public string ReleaseNotes { get; set; } = string.Empty;
    public string DownloadUrl { get; set; } = string.Empty;
    public long AssetSize { get; set; }
    public DateTime? PublishedAt { get; set; }

    public string AssetSizeFormatted => AssetSize > 0
        ? $"{Math.Round((double)AssetSize / (1024 * 1024), 1)} МБ"
        : string.Empty;
}
