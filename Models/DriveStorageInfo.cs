namespace DevByteLauncher.Models;

public class DriveStorageInfo
{
    public string Name { get; set; } = string.Empty;
    public string VolumeLabel { get; set; } = string.Empty;
    public double TotalGigabytes { get; set; }
    public double FreeGigabytes { get; set; }
    public double UsedGigabytes => TotalGigabytes - FreeGigabytes;
    public double PercentUsed => TotalGigabytes > 0 ? UsedGigabytes / TotalGigabytes * 100 : 0;
    public bool IsSystemDrive { get; set; }

    public string DisplayText => $"{Name} ({VolumeLabel}) — {FreeGigabytes:F1} ГБ свободно из {TotalGigabytes:F1} ГБ";
}
