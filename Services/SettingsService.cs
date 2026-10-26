namespace DevByteLauncher.Services;

public static class SettingsService
{
    private static readonly string t_configDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "DevByteLauncher"
    );
    private static readonly string t_configFilePath = Path.Combine(t_configDir, "settings.json");

    private static readonly JsonSerializerOptions t_jsonOptions = new()
    {
        WriteIndented = true
    };

    public static AppSettings LoadSettings()
    {
        try
        {
            if (File.Exists(t_configFilePath))
            {
                var json = File.ReadAllText(t_configFilePath);
                var settings = JsonSerializer.Deserialize<AppSettings>(json);
                if (settings != null)
                {
                    if (string.IsNullOrWhiteSpace(settings.InstallationPath))
                    {
                        settings.InstallationPath = GetDefaultInstallationPath();
                    }
                    return settings;
                }
            }
        }
        catch
        {
        }

        var defaultSettings = new AppSettings
        {
            InstallationPath = GetDefaultInstallationPath()
        };
        SaveSettings(defaultSettings);
        return defaultSettings;
    }

    public static void SaveSettings(AppSettings settings)
    {
        try
        {
            _ = Directory.CreateDirectory(t_configDir);
            var json = JsonSerializer.Serialize(settings, t_jsonOptions);
            File.WriteAllText(t_configFilePath, json);
        }
        catch
        {
        }
    }

    public static string GetDefaultInstallationPath()
    {
        if (OperatingSystem.IsLinux() || OperatingSystem.IsMacOS())
        {
            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            return Path.Combine(home, ".local", "share", "DevByteMC");
        }

        var drives = GetFixedDrives();

        var nonSystemDrive = drives
            .Where(d => !d.IsSystemDrive && d.FreeGigabytes >= 10.0)
            .OrderByDescending(d => d.FreeGigabytes)
            .FirstOrDefault();

        if (nonSystemDrive != null)
        {
            return Path.Combine(nonSystemDrive.Name, "Games", "DevByteMC");
        }

        var largestDrive = drives.OrderByDescending(d => d.FreeGigabytes).FirstOrDefault();
        return largestDrive != null
            ? Path.Combine(largestDrive.Name, "Games", "DevByteMC")
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), ".devbyte-minecraft");
    }

    public static List<DriveStorageInfo> GetFixedDrives()
    {
        var result = new List<DriveStorageInfo>();
        try
        {
            foreach (var drive in DriveInfo.GetDrives())
            {
                if (!drive.IsReady || drive.DriveType != DriveType.Fixed || drive.VolumeLabel.Contains("Google", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var totalGb = Math.Round((double)drive.TotalSize / (1024 * 1024 * 1024), 1);
                var freeGb = Math.Round((double)drive.AvailableFreeSpace / (1024 * 1024 * 1024), 1);
                var isSystem = string.Equals(drive.Name, Path.GetPathRoot(Environment.SystemDirectory), StringComparison.OrdinalIgnoreCase);

                result.Add(new DriveStorageInfo
                {
                    Name = drive.Name,
                    VolumeLabel = string.IsNullOrEmpty(drive.VolumeLabel) ? "Локальный диск" : drive.VolumeLabel,
                    TotalGigabytes = totalGb,
                    FreeGigabytes = freeGb,
                    IsSystemDrive = isSystem
                });
            }
        }
        catch
        {
        }

        return result;
    }

    public static DriveStorageInfo? GetDriveForPath(string path)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return null;
            }

            var root = Path.GetPathRoot(Path.GetFullPath(path));
            if (string.IsNullOrEmpty(root))
            {
                return null;
            }

            var drive = new DriveInfo(root);
            if (!drive.IsReady)
            {
                return null;
            }

            var totalGb = Math.Round((double)drive.TotalSize / (1024 * 1024 * 1024), 1);
            var freeGb = Math.Round((double)drive.AvailableFreeSpace / (1024 * 1024 * 1024), 1);
            var isSystem = string.Equals(drive.Name, Path.GetPathRoot(Environment.SystemDirectory), StringComparison.OrdinalIgnoreCase);

            return new DriveStorageInfo
            {
                Name = drive.Name,
                VolumeLabel = string.IsNullOrEmpty(drive.VolumeLabel) ? "Локальный диск" : drive.VolumeLabel,
                TotalGigabytes = totalGb,
                FreeGigabytes = freeGb,
                IsSystemDrive = isSystem
            };
        }
        catch
        {
            return null;
        }
    }
}
