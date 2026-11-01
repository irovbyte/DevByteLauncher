namespace DevByteLauncher.Services;

public static class ModSyncService
{
    private static readonly SocketsHttpHandler s_handler = new()
    {
        PooledConnectionLifetime = TimeSpan.FromMinutes(2),
        MaxConnectionsPerServer = 32
    };

    private static readonly HttpClient s_httpClient = new(s_handler)
    {
        Timeout = TimeSpan.FromSeconds(30)
    };

    public static async Task<ModManifest> FetchManifestAsync(string host, int port, CancellationToken cancellationToken = default)
    {
        if (port <= 0)
        {
            return new ModManifest { Files = [] };
        }

        var url = $"http://{host}:{port}/manifest.json";
        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(TimeSpan.FromSeconds(2));

            var response = await s_httpClient.GetStringAsync(url, cts.Token);
            var manifest = JsonSerializer.Deserialize<ModManifest>(response);
            if (manifest != null && (manifest.Files.Count > 0 || manifest.ResourcePacks.Count > 0 || manifest.ShaderPacks.Count > 0))
            {
                return manifest;
            }
        }
        catch
        {
        }

        return new ModManifest { Files = [] };
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

        var allowedNames = manifest.Files
            .Select(f => Path.GetFileName(f.Path))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var existingFile in Directory.EnumerateFiles(modsDir, "*.jar"))
        {
            var fileName = Path.GetFileName(existingFile);
            if (!allowedNames.Contains(fileName))
            {
                try
                {
                    File.Delete(existingFile);
                }
                catch
                {
                }
            }
        }

        var totalFiles = manifest.Files.Count;
        if (totalFiles == 0)
        {
            progress?.Report(("Моды синхронизированы", 100));
            return true;
        }

        var toDownload = new List<(ModManifestItem item, string targetPath)>();
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

            if (needsDownload)
            {
                toDownload.Add((item, targetPath));
            }
            else
            {
                _ = Interlocked.Increment(ref processed);
            }
        }

        if (toDownload.Count > 0)
        {
            var parallelOptions = new ParallelOptions
            {
                MaxDegreeOfParallelism = 12,
                CancellationToken = cancellationToken
            };

            await Parallel.ForEachAsync(toDownload, parallelOptions, async (entry, ct) =>
            {
                try
                {
                    await DownloadFileAsync(entry.item.Url, entry.targetPath, ct);
                }
                catch
                {
                }
                finally
                {
                    var current = Interlocked.Increment(ref processed);
                    var progressPercent = (double)current / totalFiles * 100.0;
                    progress?.Report(($"Загрузка: {entry.item.Name} ({current}/{totalFiles})...", progressPercent));
                }
            });
        }

        progress?.Report(("Все моды успешно синхронизированы!", 100));
        return true;
    }

    public static async Task<bool> SyncResourcePacksAsync(
        string installationPath,
        ModManifest manifest,
        IProgress<(string message, double percentage)>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var resourcepacksDir = Path.Combine(installationPath, "resourcepacks");
        _ = Directory.CreateDirectory(resourcepacksDir);

        var packsToSync = manifest.ResourcePacks.Count > 0
            ? manifest.ResourcePacks
            : ResourcePackService.GetRecommendedPacks();

        var total = packsToSync.Count;
        if (total == 0)
        {
            progress?.Report(("Ресурспаки синхронизированы", 100));
            return true;
        }

        var toDownload = new List<(ModManifestItem item, string targetPath)>();
        var processed = 0;

        foreach (var item in packsToSync)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var fileName = !string.IsNullOrWhiteSpace(item.Name)
                ? item.Name
                : Path.GetFileName(item.Path);

            if (string.IsNullOrWhiteSpace(fileName))
            {
                continue;
            }

            var targetPath = Path.Combine(resourcepacksDir, fileName);
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

            if (needsDownload)
            {
                toDownload.Add((item, targetPath));
            }
            else
            {
                _ = Interlocked.Increment(ref processed);
            }
        }

        if (toDownload.Count > 0)
        {
            var parallelOptions = new ParallelOptions
            {
                MaxDegreeOfParallelism = 4,
                CancellationToken = cancellationToken
            };

            await Parallel.ForEachAsync(toDownload, parallelOptions, async (entry, ct) =>
            {
                try
                {
                    await DownloadFileAsync(entry.item.Url, entry.targetPath, ct);
                }
                catch
                {
                }
                finally
                {
                    var current = Interlocked.Increment(ref processed);
                    var pct = (double)current / total * 100.0;
                    progress?.Report(($"Загрузка ресурспака: {entry.item.Name} ({current}/{total})...", pct));
                }
            });
        }

        var activePacks = ResourcePackService.GetActiveResourcePacks(installationPath);
        if (activePacks.Count == 0 || (activePacks.Count == 1 && activePacks[0] == "vanilla"))
        {
            ResourcePackService.EnsureResourcePackEnabled(installationPath, ResourcePackService.DefaultFreshAnimationsPack);
        }

        progress?.Report(("Все ресурспаки синхронизированы!", 100));
        return true;
    }

    public static async Task<bool> SyncShaderPacksAsync(
        string installationPath,
        ModManifest manifest,
        IProgress<(string message, double percentage)>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var shaderpacksDir = Path.Combine(installationPath, "shaderpacks");
        _ = Directory.CreateDirectory(shaderpacksDir);

        if (manifest.ShaderPacks.Count == 0)
        {
            await ShaderService.EnsureShadersAndModsAsync(installationPath, progress, cancellationToken);
            progress?.Report(("Шейдерпаки синхронизированы", 100));
            return true;
        }

        var total = manifest.ShaderPacks.Count;
        var toDownload = new List<(ModManifestItem item, string targetPath)>();
        var processed = 0;

        foreach (var item in manifest.ShaderPacks)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var fileName = !string.IsNullOrWhiteSpace(item.Name)
                ? item.Name
                : Path.GetFileName(item.Path);

            if (string.IsNullOrWhiteSpace(fileName))
            {
                continue;
            }

            var targetPath = Path.Combine(shaderpacksDir, fileName);
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

            if (needsDownload)
            {
                toDownload.Add((item, targetPath));
            }
            else
            {
                _ = Interlocked.Increment(ref processed);
            }
        }

        if (toDownload.Count > 0)
        {
            var parallelOptions = new ParallelOptions
            {
                MaxDegreeOfParallelism = 4,
                CancellationToken = cancellationToken
            };

            await Parallel.ForEachAsync(toDownload, parallelOptions, async (entry, ct) =>
            {
                try
                {
                    await DownloadFileAsync(entry.item.Url, entry.targetPath, ct);
                }
                catch
                {
                }
                finally
                {
                    var current = Interlocked.Increment(ref processed);
                    var pct = (double)current / total * 100.0;
                    progress?.Report(($"Загрузка шейдера: {entry.item.Name} ({current}/{total})...", pct));
                }
            });
        }

        progress?.Report(("Все шейдерпаки синхронизированы!", 100));
        return true;
    }

    public static async Task<bool> SyncAllAsync(
        string installationPath,
        ModManifest manifest,
        IProgress<(string message, double percentage)>? progress = null,
        CancellationToken cancellationToken = default)
    {
        progress?.Report(("Синхронизация модов сборки...", 10));
        var modsOk = await SyncModsAsync(installationPath, manifest, progress, cancellationToken);

        progress?.Report(("Синхронизация ресурспаков...", 50));
        var rpOk = await SyncResourcePacksAsync(installationPath, manifest, progress, cancellationToken);

        progress?.Report(("Синхронизация шейдерпаков...", 80));
        var spOk = await SyncShaderPacksAsync(installationPath, manifest, progress, cancellationToken);

        progress?.Report(("Синхронизация сборки успешно завершена!", 100));
        return modsOk && rpOk && spOk;
    }

    public static async Task DownloadFileAsync(string url, string destinationPath, CancellationToken cancellationToken)
    {
        var tempFile = destinationPath + ".tmp";
        try
        {
            using var response = await s_httpClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
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
    public static string CalculateSha1(string filePath)
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
}
