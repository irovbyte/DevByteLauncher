namespace DevByteLauncher.Services;

public static class UpdateService
{
    private static readonly HttpClient s_httpClient = new()
    {
        Timeout = TimeSpan.FromMinutes(5),
        DefaultRequestHeaders =
        {
            { "User-Agent", "DevByteLauncher" },
            { "Accept", "application/vnd.github.v3+json" }
        }
    };

    private static readonly HttpClient s_noRedirectClient = new(new SocketsHttpHandler
    {
        AllowAutoRedirect = false,
        PooledConnectionLifetime = TimeSpan.FromMinutes(2)
    })
    {
        Timeout = TimeSpan.FromSeconds(15)
    };

    public static string CurrentVersion { get; } =
        typeof(UpdateService).Assembly.GetName().Version is { } v
            ? $"{v.Major}.{v.Minor}.{v.Build}"
            : "1.0.0";
    public const string RepositoryOwner = "irovbyte";
    public const string RepositoryName = "DevByteLauncher";

    public static void CleanPreviousBinary()
    {
        try
        {
            var currentExe = Environment.ProcessPath;
            if (string.IsNullOrEmpty(currentExe))
            {
                return;
            }

            var oldExe = currentExe + ".old";
            if (File.Exists(oldExe))
            {
                File.Delete(oldExe);
            }
        }
        catch
        {
        }
    }

    public static async Task<UpdateInfo> CheckForUpdatesAsync(CancellationToken cancellationToken = default)
    {
        var info = new UpdateInfo
        {
            CurrentVersion = CurrentVersion,
            IsAvailable = false
        };

        // Channel 1: Query GitHub Releases redirect (Completely immune to GitHub REST API rate limits)
        try
        {
            var latestUrl = $"https://github.com/{RepositoryOwner}/{RepositoryName}/releases/latest";
            using var request = new HttpRequestMessage(HttpMethod.Get, latestUrl);
            request.Headers.Add("User-Agent", "DevByteLauncher");
            using var response = await s_noRedirectClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);

            var location = response.Headers.Location?.ToString();
            if (!string.IsNullOrEmpty(location))
            {
                var rawTag = location.Split('/').LastOrDefault() ?? string.Empty;
                var cleanTag = rawTag.TrimStart('v', 'V');
                if (Version.TryParse(cleanTag, out var remoteVer) && Version.TryParse(CurrentVersion, out var localVer))
                {
                    if (remoteVer > localVer)
                    {
                        var assetName = OperatingSystem.IsWindows()
                            ? "DevByteLauncher.exe"
                            : "DevByteLauncher-linux-x64";
                        info.LatestVersion = cleanTag;
                        info.ReleaseName = $"Обновление v{cleanTag}";
                        info.DownloadUrl = $"https://github.com/{RepositoryOwner}/{RepositoryName}/releases/download/v{cleanTag}/{assetName}";
                        info.IsAvailable = true;
                        return info;
                    }
                    else
                    {
                        return info;
                    }
                }
            }
        }
        catch
        {
        }

        // Channel 2: Fallback to GitHub REST API if redirect was blocked or failed
        try
        {
            var url = $"https://api.github.com/repos/{RepositoryOwner}/{RepositoryName}/releases/latest";
            using var response = await s_httpClient.GetAsync(url, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                return info;
            }

            var json = await response.Content.ReadAsStringAsync(cancellationToken);
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            if (!root.TryGetProperty("tag_name", out var tagEl))
            {
                return info;
            }

            var rawTag = tagEl.GetString() ?? string.Empty;
            var cleanTag = rawTag.TrimStart('v', 'V');

            if (!Version.TryParse(cleanTag, out var remoteVer) || !Version.TryParse(CurrentVersion, out var localVer))
            {
                return info;
            }

            if (remoteVer <= localVer)
            {
                return info;
            }

            info.LatestVersion = cleanTag;
            if (root.TryGetProperty("name", out var nameEl))
            {
                info.ReleaseName = nameEl.GetString() ?? $"Обновление {rawTag}";
            }

            if (root.TryGetProperty("body", out var bodyEl))
            {
                info.ReleaseNotes = bodyEl.GetString() ?? string.Empty;
            }

            if (root.TryGetProperty("published_at", out var pubEl) && pubEl.TryGetDateTime(out var dt))
            {
                info.PublishedAt = dt;
            }

            if (root.TryGetProperty("assets", out var assetsEl) && assetsEl.ValueKind == JsonValueKind.Array)
            {
                foreach (var asset in assetsEl.EnumerateArray())
                {
                    if (!asset.TryGetProperty("name", out var assetNameEl) || !asset.TryGetProperty("browser_download_url", out var dlEl))
                    {
                        continue;
                    }

                    var assetName = assetNameEl.GetString() ?? string.Empty;
                    var isMatch = OperatingSystem.IsWindows()
                        ? assetName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
                        : (assetName.Contains("linux-x64", StringComparison.OrdinalIgnoreCase) && !assetName.EndsWith(".tar.gz", StringComparison.OrdinalIgnoreCase));

                    if (isMatch)
                    {
                        info.DownloadUrl = dlEl.GetString() ?? string.Empty;
                        if (asset.TryGetProperty("size", out var sizeEl))
                        {
                            info.AssetSize = sizeEl.GetInt64();
                        }
                        info.IsAvailable = true;
                        break;
                    }
                }
            }
        }
        catch
        {
            info.IsAvailable = false;
        }

        return info;
    }

    public static async Task ApplyUpdateAsync(UpdateInfo update, IProgress<double>? progress = null, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(update.DownloadUrl))
        {
            throw new InvalidOperationException("Download URL is empty");
        }

        var currentExe = Environment.ProcessPath;
        if (string.IsNullOrEmpty(currentExe) || !File.Exists(currentExe))
        {
            throw new FileNotFoundException("Cannot determine current process path");
        }

        var tempNewExe = currentExe + ".update";
        var oldExe = currentExe + ".old";

        if (File.Exists(tempNewExe))
        {
            File.Delete(tempNewExe);
        }

        using (var response = await s_httpClient.GetAsync(update.DownloadUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken))
        {
            _ = response.EnsureSuccessStatusCode();

            var totalBytes = response.Content.Headers.ContentLength ?? update.AssetSize;
            await using var sourceStream = await response.Content.ReadAsStreamAsync(cancellationToken);
            await using var fileStream = new FileStream(tempNewExe, FileMode.Create, FileAccess.Write, FileShare.None);

            var buffer = new byte[65536];
            long totalRead = 0;
            int bytesRead;

            while ((bytesRead = await sourceStream.ReadAsync(buffer, cancellationToken)) > 0)
            {
                await fileStream.WriteAsync(buffer.AsMemory(0, bytesRead), cancellationToken);
                totalRead += bytesRead;
                if (totalBytes > 0)
                {
                    progress?.Report(Math.Round((double)totalRead / totalBytes * 100.0, 1));
                }
            }
        }

        // Pre-warm Process.Start and IPC pipe machinery on Linux/macOS BEFORE moving binary.
        // In .NET Single-File bundles, System.IO.Pipes is loaded lazily from the host binary.
        // If currentExe is moved or replaced beforehand, Process.StartCore fails with FileNotFoundException.
        if (OperatingSystem.IsLinux() || OperatingSystem.IsMacOS())
        {
            try
            {
                _ = typeof(System.IO.Pipes.PipeStream).Assembly;
                using var warmProc = Process.Start(new ProcessStartInfo
                {
                    FileName = "true",
                    UseShellExecute = false,
                    CreateNoWindow = true
                });
                if (warmProc != null)
                {
                    await warmProc.WaitForExitAsync(cancellationToken);
                }
            }
            catch
            {
            }
        }

        await EnsureExecutablePermissionAsync(tempNewExe, cancellationToken);

        try
        {
            if (File.Exists(oldExe))
            {
                File.Delete(oldExe);
            }
        }
        catch
        {
        }

        File.Move(currentExe, oldExe, overwrite: true);
        File.Move(tempNewExe, currentExe, overwrite: true);

        await EnsureExecutablePermissionAsync(currentExe, cancellationToken);

        var workingDir = Path.GetDirectoryName(currentExe);
        if (string.IsNullOrEmpty(workingDir) || !Directory.Exists(workingDir))
        {
            workingDir = AppContext.BaseDirectory;
        }

        _ = Process.Start(new ProcessStartInfo
        {
            FileName = currentExe,
            WorkingDirectory = workingDir,
            UseShellExecute = OperatingSystem.IsWindows()
        });

        Environment.Exit(0);
    }

    private static async Task EnsureExecutablePermissionAsync(string filePath, CancellationToken cancellationToken = default)
    {
        if (OperatingSystem.IsLinux() || OperatingSystem.IsMacOS())
        {
            try
            {
                File.SetUnixFileMode(filePath,
                    UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute |
                    UnixFileMode.GroupRead | UnixFileMode.GroupExecute |
                    UnixFileMode.OtherRead | UnixFileMode.OtherExecute);
            }
            catch
            {
                try
                {
                    using var chmod = Process.Start(new ProcessStartInfo
                    {
                        FileName = "chmod",
                        Arguments = $"+x \"{filePath}\"",
                        UseShellExecute = false,
                        CreateNoWindow = true
                    });
                    if (chmod != null)
                    {
                        await chmod.WaitForExitAsync(cancellationToken);
                    }
                }
                catch
                {
                }
            }
        }
    }
}
