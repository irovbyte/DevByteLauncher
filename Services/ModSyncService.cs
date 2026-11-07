using System.Collections.Concurrent;

namespace DevByteLauncher.Services;

public static class ModSyncService
{
    private static readonly SocketsHttpHandler s_handler = new()
    {
        ConnectTimeout = TimeSpan.FromSeconds(15),
        PooledConnectionLifetime = TimeSpan.FromMinutes(2),
        PooledConnectionIdleTimeout = TimeSpan.FromSeconds(30),
        MaxConnectionsPerServer = 32
    };

    private static readonly HttpClient s_httpClient = new(s_handler)
    {
        Timeout = TimeSpan.FromMinutes(10)
    };

    public static async Task<ModManifest> FetchManifestAsync(string host, int port, CancellationToken cancellationToken = default)
    {
        if (port <= 0)
        {
            return new ModManifest { Files = [] };
        }

        var url = $"http://{host}:{port}/manifest.json";
        for (var attempt = 1; attempt <= 2; attempt++)
        {
            try
            {
                using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                cts.CancelAfter(TimeSpan.FromSeconds(15));

                var response = await s_httpClient.GetStringAsync(url, cts.Token);
                var manifest = JsonSerializer.Deserialize<ModManifest>(response);
                if (manifest != null && (manifest.Files.Count > 0 || manifest.ResourcePacks.Count > 0 || manifest.ShaderPacks.Count > 0))
                {
                    return manifest;
                }
            }
            catch when (attempt < 2 && !cancellationToken.IsCancellationRequested)
            {
                await Task.Delay(1000, cancellationToken);
            }
            catch
            {
            }
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

        progress?.Report(("Синхронизация списка модов...", 1));

        var toDownload = new List<(ModManifestItem item, string targetPath)>();
        var processed = 0;

        progress?.Report(($"Проверка целостности модов ({totalFiles} шт)...", 3));

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
            progress?.Report(($"Подготовка к загрузке {toDownload.Count} файлов сборки...", 5));

            var parallelOptions = new ParallelOptions
            {
                MaxDegreeOfParallelism = 4,
                CancellationToken = cancellationToken
            };

            var failedMods = new ConcurrentBag<string>();

            await Parallel.ForEachAsync(toDownload, parallelOptions, async (entry, ct) =>
            {
                var fileProgress = new Progress<(long bytesRead, long totalBytes)>(p =>
                {
                    var readMb = p.bytesRead / (1024.0 * 1024.0);
                    var totalMb = p.totalBytes / (1024.0 * 1024.0);
                    var pct = totalFiles > 0 ? (double)processed / totalFiles * 100.0 : 0.0;
                    if (totalMb >= 3.0)
                    {
                        progress?.Report(($"Загрузка ({processed + 1}/{totalFiles}): {entry.item.Name} ({readMb:F1}/{totalMb:F1} МБ)...", pct));
                    }
                    else
                    {
                        progress?.Report(($"Загрузка ({processed + 1}/{totalFiles}): {entry.item.Name}...", pct));
                    }
                });

                try
                {
                    await DownloadWithRetryAsync(entry.item.Url, entry.targetPath, entry.item.Size, entry.item.Sha1, 5, ct, fileProgress);
                }
                catch (Exception)
                {
                    failedMods.Add(entry.item.Name);
                }
                finally
                {
                    var current = Interlocked.Increment(ref processed);
                    var progressPercent = (double)current / totalFiles * 100.0;
                    progress?.Report(($"Загрузка: {entry.item.Name} ({current}/{totalFiles})...", progressPercent));
                }
            });

            if (!failedMods.IsEmpty)
            {
                var failedSample = string.Join(", ", failedMods.Take(3));
                progress?.Report(($"Не удалось загрузить {failedMods.Count} модов ({failedSample}). Проверьте соединение.", 100));
                return false;
            }
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
                MaxDegreeOfParallelism = 3,
                CancellationToken = cancellationToken
            };

            await Parallel.ForEachAsync(toDownload, parallelOptions, async (entry, ct) =>
            {
                try
                {
                    await DownloadWithRetryAsync(entry.item.Url, entry.targetPath, entry.item.Size, entry.item.Sha1, 5, ct);
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
                MaxDegreeOfParallelism = 3,
                CancellationToken = cancellationToken
            };

            await Parallel.ForEachAsync(toDownload, parallelOptions, async (entry, ct) =>
            {
                try
                {
                    await DownloadWithRetryAsync(entry.item.Url, entry.targetPath, entry.item.Size, entry.item.Sha1, 5, ct);
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

    public static async Task<bool> SyncConfigsAsync(
        string installationPath,
        ModManifest manifest,
        IProgress<(string message, double percentage)>? progress = null,
        CancellationToken cancellationToken = default)
    {
        if (manifest.Configs.Count == 0)
        {
            return true;
        }

        foreach (var item in manifest.Configs)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var relativePath = item.Path.Replace('\\', '/').TrimStart('/');
            var targetPath = Path.Combine(installationPath, relativePath.Replace('/', Path.DirectorySeparatorChar));
            var targetDir = Path.GetDirectoryName(targetPath);
            if (!string.IsNullOrEmpty(targetDir))
            {
                _ = Directory.CreateDirectory(targetDir);
            }

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
                progress?.Report(($"Синхронизация конфигурации: {item.Name}...", 95));
                await DownloadWithRetryAsync(item.Url, targetPath, item.Size, item.Sha1, 5, cancellationToken);
            }

            if (item.Name.Equals("keybindings.txt", StringComparison.OrdinalIgnoreCase) && File.Exists(targetPath))
            {
                ApplyKeybindingsToOptions(installationPath, targetPath);
            }
        }

        progress?.Report(("Конфигурации успешно синхронизированы!", 100));
        return true;
    }

    public static void ApplyKeybindingsToOptions(string installationPath, string keybindingsFilePath)
    {
        try
        {
            if (!File.Exists(keybindingsFilePath))
            {
                return;
            }

            var optionsPath = Path.Combine(installationPath, "options.txt");
            var keyLines = File.ReadAllLines(keybindingsFilePath)
                .Where(line => line.StartsWith("key_", StringComparison.OrdinalIgnoreCase))
                .ToList();

            if (keyLines.Count == 0)
            {
                return;
            }

            if (!File.Exists(optionsPath))
            {
                File.WriteAllLines(optionsPath, keyLines);
                return;
            }

            var optionsLines = File.ReadAllLines(optionsPath);
            var optionsMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var otherLines = new List<string>();

            foreach (var line in optionsLines)
            {
                var separatorIndex = line.IndexOf(':');
                if (separatorIndex > 0 && line.StartsWith("key_", StringComparison.OrdinalIgnoreCase))
                {
                    var key = line[..separatorIndex];
                    var value = line[(separatorIndex + 1)..];
                    optionsMap[key] = value;
                }
                else
                {
                    otherLines.Add(line);
                }
            }

            foreach (var line in keyLines)
            {
                var separatorIndex = line.IndexOf(':');
                if (separatorIndex > 0)
                {
                    var key = line[..separatorIndex];
                    var value = line[(separatorIndex + 1)..];
                    optionsMap[key] = value;
                }
            }

            var resultLines = new List<string>(otherLines);
            foreach (var kvp in optionsMap.OrderBy(k => k.Key, StringComparer.OrdinalIgnoreCase))
            {
                resultLines.Add($"{kvp.Key}:{kvp.Value}");
            }

            File.WriteAllLines(optionsPath, resultLines);
        }
        catch
        {
        }
    }

    public static async Task<bool> SyncAllAsync(
        string installationPath,
        ModManifest manifest,
        IProgress<(string message, double percentage)>? progress = null,
        CancellationToken cancellationToken = default)
    {
        progress?.Report(("Синхронизация модов сборки...", 10));
        var modsOk = await SyncModsAsync(installationPath, manifest, progress, cancellationToken);

        progress?.Report(("Синхронизация ресурспаков...", 40));
        var rpOk = await SyncResourcePacksAsync(installationPath, manifest, progress, cancellationToken);

        progress?.Report(("Синхронизация шейдерпаков...", 70));
        var spOk = await SyncShaderPacksAsync(installationPath, manifest, progress, cancellationToken);

        progress?.Report(("Синхронизация конфигураций...", 90));
        var cfgOk = await SyncConfigsAsync(installationPath, manifest, progress, cancellationToken);

        progress?.Report(("Синхронизация сборки успешно завершена!", 100));
        return modsOk && rpOk && spOk && cfgOk;
    }

    public static async Task DownloadWithRetryAsync(
        string url,
        string destinationPath,
        long expectedSize,
        string? expectedSha1,
        int maxRetries,
        CancellationToken cancellationToken,
        IProgress<(long bytesRead, long totalBytes)>? fileProgress = null)
    {
        for (var attempt = 1; attempt <= maxRetries; attempt++)
        {
            try
            {
                await DownloadFileAsync(url, destinationPath, expectedSize, expectedSha1, cancellationToken, fileProgress);
                return;
            }
            catch (Exception) when (attempt < maxRetries && !cancellationToken.IsCancellationRequested)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(500 * attempt), cancellationToken);
            }
        }
    }

    public static Task DownloadFileAsync(string url, string destinationPath, CancellationToken cancellationToken)
        => DownloadFileAsync(url, destinationPath, 0, null, cancellationToken, null);

    public static async Task DownloadFileAsync(
        string url,
        string destinationPath,
        long expectedSize,
        string? expectedSha1,
        CancellationToken cancellationToken,
        IProgress<(long bytesRead, long totalBytes)>? fileProgress = null)
    {
        var tempFile = destinationPath + ".tmp";
        try
        {
            var existingLength = File.Exists(tempFile) ? new FileInfo(tempFile).Length : 0L;
            HttpResponseMessage response;
            FileStream streamToWriteTo;
            long startOffset = 0;

            if (existingLength > 0 && expectedSize > 0 && existingLength < expectedSize)
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, url);
                request.Headers.Range = new System.Net.Http.Headers.RangeHeaderValue(existingLength, null);
                response = await s_httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);

                if (response.StatusCode == System.Net.HttpStatusCode.PartialContent)
                {
                    startOffset = existingLength;
                    streamToWriteTo = File.Open(tempFile, FileMode.Append, FileAccess.Write, FileShare.None);
                }
                else
                {
                    response.Dispose();
                    response = await s_httpClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
                    _ = response.EnsureSuccessStatusCode();
                    streamToWriteTo = File.Open(tempFile, FileMode.Create, FileAccess.Write, FileShare.None);
                }
            }
            else
            {
                response = await s_httpClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
                _ = response.EnsureSuccessStatusCode();
                streamToWriteTo = File.Open(tempFile, FileMode.Create, FileAccess.Write, FileShare.None);
            }

            using (response)
            await using (streamToWriteTo)
            await using (var streamToReadFrom = await response.Content.ReadAsStreamAsync(cancellationToken))
            {
                var totalBytes = expectedSize > 0 ? expectedSize : (response.Content.Headers.ContentLength ?? 0L) + startOffset;
                var buffer = new byte[131072]; // 128 KB
                var totalRead = startOffset;
                var lastReportTimestamp = Stopwatch.GetTimestamp();

                while (true)
                {
                    using var readCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                    readCts.CancelAfter(TimeSpan.FromSeconds(15));

                    int bytesRead;
                    try
                    {
                        bytesRead = await streamToReadFrom.ReadAsync(buffer.AsMemory(0, buffer.Length), readCts.Token);
                    }
                    catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
                    {
                        throw new TimeoutException($"Таймаут передачи данных: поток не отвечает более 15 секунд при загрузке {Path.GetFileName(destinationPath)}");
                    }

                    if (bytesRead == 0)
                    {
                        break;
                    }

                    await streamToWriteTo.WriteAsync(buffer.AsMemory(0, bytesRead), cancellationToken);
                    totalRead += bytesRead;

                    if (fileProgress != null && Stopwatch.GetElapsedTime(lastReportTimestamp).TotalMilliseconds >= 250)
                    {
                        lastReportTimestamp = Stopwatch.GetTimestamp();
                        fileProgress.Report((totalRead, totalBytes));
                    }
                }

                fileProgress?.Report((totalRead, totalBytes));
            }

            var tempInfo = new FileInfo(tempFile);
            if (expectedSize > 0 && tempInfo.Length != expectedSize)
            {
                throw new IOException($"Файл поврежден (размер не совпадает): ожидалось {expectedSize}, получено {tempInfo.Length}");
            }

            if (!string.IsNullOrEmpty(expectedSha1))
            {
                var localSha1 = CalculateSha1(tempFile);
                if (!string.Equals(localSha1, expectedSha1, StringComparison.OrdinalIgnoreCase))
                {
                    throw new IOException($"Файл поврежден (хэш SHA-1 не совпадает): ожидалось {expectedSha1}, получено {localSha1}");
                }
            }

            try
            {
                if (File.Exists(destinationPath))
                {
                    File.Delete(destinationPath);
                }
            }
            catch
            {
            }

            File.Move(tempFile, destinationPath, overwrite: true);
        }
        catch
        {
            if (File.Exists(tempFile))
            {
                var len = new FileInfo(tempFile).Length;
                if (expectedSize > 0 && len > expectedSize)
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
            throw;
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
