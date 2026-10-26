namespace DevByteLauncher.Services;

public static class ModSyncService
{
    private static readonly HttpClient t_httpClient = new()
    {
        Timeout = TimeSpan.FromSeconds(15)
    };

    public static async Task<ModManifest> FetchManifestAsync(string host, int port, CancellationToken cancellationToken = default)
    {
        var url = $"http://{host}:{port}/manifest.json";
        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(TimeSpan.FromSeconds(5));

            var response = await t_httpClient.GetStringAsync(url, cts.Token);
            var manifest = JsonSerializer.Deserialize<ModManifest>(response);
            if (manifest != null && manifest.Files.Count > 0)
            {
                return manifest;
            }
        }
        catch
        {
        }

        return CreateFallbackManifest(host);
    }

    public static async Task<bool> SyncModsAsync(
        string installationPath,
        ModManifest manifest,
        IProgress<(string message, double percentage)>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var modsDir = Path.Combine(installationPath, "mods");
        _ = Directory.CreateDirectory(modsDir);

        var serverOnlyBlacklist = new[] { "c2me.jar", "chunky.jar", "easyauth.jar" };
        foreach (var badMod in serverOnlyBlacklist)
        {
            var badModPath = Path.Combine(modsDir, badMod);
            if (File.Exists(badModPath))
            {
                File.Delete(badModPath);
            }
        }

        var totalFiles = manifest.Files.Count;
        if (totalFiles == 0)
        {
            progress?.Report(("Моды синхронизированы", 100));
            return true;
        }

        var processed = 0;

        foreach (var item in manifest.Files)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var targetPath = Path.Combine(modsDir, Path.GetFileName(item.Path));
            var needsDownload = false;

            if (!File.Exists(targetPath))
            {
                needsDownload = true;
            }
            else
            {
                var fileInfo = new FileInfo(targetPath);
                if (item.Size > 0 && fileInfo.Length != item.Size)
                {
                    needsDownload = true;
                }
                else if (!string.IsNullOrEmpty(item.Sha1))
                {
                    var localSha1 = CalculateSha1(targetPath);
                    if (!string.Equals(localSha1, item.Sha1, StringComparison.OrdinalIgnoreCase))
                    {
                        needsDownload = true;
                    }
                }
            }

            var progressPercent = (double)processed / totalFiles * 100.0;

            if (needsDownload)
            {
                progress?.Report(($"Загрузка: {item.Name} ({processed + 1}/{totalFiles})...", progressPercent));
                await DownloadFileAsync(item.Url, targetPath, cancellationToken);
            }
            else
            {
                progress?.Report(($"Проверен: {item.Name} ({processed + 1}/{totalFiles})", progressPercent));
            }

            processed++;
        }

        progress?.Report(("Все моды успешно синхронизированы!", 100));
        return true;
    }

    private static async Task DownloadFileAsync(string url, string destinationPath, CancellationToken cancellationToken)
    {
        var tempFile = destinationPath + ".tmp";
        try
        {
            using var response = await t_httpClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            _ = response.EnsureSuccessStatusCode();

            await using (var streamToReadFrom = await response.Content.ReadAsStreamAsync(cancellationToken))
            await using (var streamToWriteTo = File.Open(tempFile, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                await streamToReadFrom.CopyToAsync(streamToWriteTo, cancellationToken);
            }

            if (File.Exists(destinationPath))
            {
                File.Delete(destinationPath);
            }

            File.Move(tempFile, destinationPath);
        }
        finally
        {
            if (File.Exists(tempFile))
            {
                try
                {
                    File.Delete(tempFile);
                }
                catch
                {
                }
            }
        }
    }

    [SuppressMessage("Security", "CA5350:Do Not Use Weak Cryptographic Algorithms", Justification = "SHA1 is required by Minecraft and Fabric manifest standard.")]
    private static string CalculateSha1(string filePath)
    {
        try
        {
            using var sha1 = SHA1.Create();
            using var stream = File.OpenRead(filePath);
            var hash = sha1.ComputeHash(stream);
            return Convert.ToHexStringLower(hash);
        }
        catch
        {
            return string.Empty;
        }
    }

    private static ModManifest CreateFallbackManifest(string host)
    {
        return new ModManifest
        {
            Version = "1.21.1",
            FabricLoaderVersion = "0.16.9",
            ServerHost = host,
            ServerPort = 25565,
            Files =
            [
                new ModManifestItem
                {
                    Name = "fabric-api.jar",
                    Path = "mods/fabric-api.jar",
                    Url = $"http://{host}:25566/download/fabric-api.jar"
                },
                new ModManifestItem
                {
                    Name = "lithium.jar",
                    Path = "mods/lithium.jar",
                    Url = $"http://{host}:25566/download/lithium.jar"
                },
                new ModManifestItem
                {
                    Name = "ferritecore.jar",
                    Path = "mods/ferritecore.jar",
                    Url = $"http://{host}:25566/download/ferritecore.jar"
                },
                new ModManifestItem
                {
                    Name = "krypton.jar",
                    Path = "mods/krypton.jar",
                    Url = $"http://{host}:25566/download/krypton.jar"
                },
                new ModManifestItem
                {
                    Name = "fabrictailor.jar",
                    Path = "mods/fabrictailor.jar",
                    Url = $"http://{host}:25566/download/fabrictailor.jar"
                }
            ]
        };
    }
}
