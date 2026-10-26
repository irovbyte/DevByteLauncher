namespace DevByteLauncher.Services;

public static class UpdateService
{
    private static readonly HttpClient t_httpClient = new()
    {
        Timeout = TimeSpan.FromSeconds(15),
        DefaultRequestHeaders =
        {
            { "User-Agent", "DevByteLauncher" },
            { "Accept", "application/vnd.github.v3+json" }
        }
    };

    public const string CurrentVersion = "1.0.0";
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

        try
        {
            var url = $"https://api.github.com/repos/{RepositoryOwner}/{RepositoryName}/releases/latest";
            using var response = await t_httpClient.GetAsync(url, cancellationToken);
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
                    if (assetName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
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

        using (var response = await t_httpClient.GetAsync(update.DownloadUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken))
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

        if (File.Exists(oldExe))
        {
            File.Delete(oldExe);
        }

        File.Move(currentExe, oldExe);
        File.Move(tempNewExe, currentExe);

        _ = Process.Start(new ProcessStartInfo
        {
            FileName = currentExe,
            UseShellExecute = true
        });

        Environment.Exit(0);
    }
}
