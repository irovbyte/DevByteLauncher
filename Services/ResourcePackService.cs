namespace DevByteLauncher.Services;

public static class ResourcePackService
{
    private static readonly SocketsHttpHandler s_handler = new()
    {
        PooledConnectionLifetime = TimeSpan.FromMinutes(2),
        MaxConnectionsPerServer = 16
    };

    private static readonly HttpClient s_httpClient = new(s_handler)
    {
        Timeout = TimeSpan.FromSeconds(60)
    };

    private static readonly JsonSerializerOptions s_compactJsonOptions = new() { WriteIndented = false };

    public const string DefaultFreshAnimationsPack = "FreshAnimations_v1.11.2_1.20+.zip";
    public const string DefaultStayTruePack = "Stay_True_1.20.zip";

    private const string FreshAnimationsUrl = "https://cdn.modrinth.com/data/50dA9Sha/versions/HC1XRKGD/FreshAnimations_v1.11.2_1.20%2B.zip";
    private const string FreshAnimationsSha1 = "c0f564f3b9c12762923015eefad53edbdbbd3d96";
    private const long FreshAnimationsSize = 742790L;

    private const string StayTrueUrl = "https://cdn.modrinth.com/data/1Ktxmpyr/versions/3W5XfL34/Stay_True_1.20.zip";
    private const string StayTrueSha1 = "";
    private const long StayTrueSize = 0L;

    public static IReadOnlyList<ModManifestItem> GetRecommendedPacks() =>
    [
        new ModManifestItem
        {
            Name = DefaultFreshAnimationsPack,
            Path = Path.Combine("resourcepacks", DefaultFreshAnimationsPack),
            Url = FreshAnimationsUrl,
            Sha1 = FreshAnimationsSha1,
            Size = FreshAnimationsSize
        },
        new ModManifestItem
        {
            Name = DefaultStayTruePack,
            Path = Path.Combine("resourcepacks", DefaultStayTruePack),
            Url = StayTrueUrl,
            Sha1 = StayTrueSha1,
            Size = StayTrueSize
        }
    ];

    public static List<string> GetAvailableResourcePacks(string installationPath)
    {
        var result = new List<string>();
        var resourcepacksDir = Path.Combine(installationPath, "resourcepacks");
        if (Directory.Exists(resourcepacksDir))
        {
            var files = Directory.GetFiles(resourcepacksDir, "*.zip");
            foreach (var file in files)
            {
                var name = Path.GetFileName(file);
                if (!string.IsNullOrWhiteSpace(name))
                {
                    result.Add(name);
                }
            }

            foreach (var dir in Directory.GetDirectories(resourcepacksDir))
            {
                if (File.Exists(Path.Combine(dir, "pack.mcmeta")))
                {
                    var dirName = Path.GetFileName(dir);
                    if (!string.IsNullOrWhiteSpace(dirName))
                    {
                        result.Add(dirName);
                    }
                }
            }
        }

        return result;
    }

    public static List<string> GetActiveResourcePacks(string installationPath)
    {
        var result = new List<string>();
        var optionsPath = Path.Combine(installationPath, "options.txt");
        if (!File.Exists(optionsPath))
        {
            return result;
        }

        try
        {
            var lines = File.ReadAllLines(optionsPath);
            foreach (var line in lines)
            {
                var trimmed = line.Trim();
                if (trimmed.StartsWith("resourcePacks:", StringComparison.OrdinalIgnoreCase))
                {
                    var json = trimmed["resourcePacks:".Length..].Trim();
                    if (!string.IsNullOrWhiteSpace(json))
                    {
                        var parsed = JsonSerializer.Deserialize<List<string>>(json);
                        if (parsed != null)
                        {
                            result.AddRange(parsed);
                        }
                    }
                    break;
                }
            }
        }
        catch
        {
        }

        return result;
    }

    public static void SetActiveResourcePacks(string installationPath, IEnumerable<string> activePacks)
    {
        try
        {
            _ = Directory.CreateDirectory(installationPath);
            var optionsPath = Path.Combine(installationPath, "options.txt");

            var packsList = new List<string>();
            foreach (var pack in activePacks)
            {
                if (!string.IsNullOrWhiteSpace(pack) && !packsList.Contains(pack, StringComparer.OrdinalIgnoreCase))
                {
                    packsList.Add(pack);
                }
            }

            if (!packsList.Contains("vanilla", StringComparer.OrdinalIgnoreCase))
            {
                packsList.Insert(0, "vanilla");
            }

            var jsonArray = JsonSerializer.Serialize(packsList, s_compactJsonOptions);
            var resourcePackLine = $"resourcePacks:{jsonArray}";

            if (File.Exists(optionsPath))
            {
                var lines = File.ReadAllLines(optionsPath).ToList();
                var found = false;
                for (var i = 0; i < lines.Count; i++)
                {
                    if (lines[i].TrimStart().StartsWith("resourcePacks:", StringComparison.OrdinalIgnoreCase))
                    {
                        lines[i] = resourcePackLine;
                        found = true;
                        break;
                    }
                }

                if (!found)
                {
                    lines.Add(resourcePackLine);
                }

                File.WriteAllLines(optionsPath, lines);
            }
            else
            {
                File.WriteAllLines(optionsPath, [resourcePackLine]);
            }
        }
        catch
        {
        }
    }

    public static void EnsureResourcePackEnabled(string installationPath, string packFileName)
    {
        if (string.IsNullOrWhiteSpace(packFileName))
        {
            return;
        }

        var entry = packFileName.StartsWith("file/", StringComparison.OrdinalIgnoreCase) || packFileName == "vanilla" || packFileName == "fabric"
            ? packFileName
            : $"file/{packFileName}";

        var currentPacks = GetActiveResourcePacks(installationPath);
        if (!currentPacks.Contains(entry, StringComparer.OrdinalIgnoreCase))
        {
            currentPacks.Add(entry);
            SetActiveResourcePacks(installationPath, currentPacks);
        }
    }

    public static void DisableResourcePack(string installationPath, string packFileName)
    {
        if (string.IsNullOrWhiteSpace(packFileName))
        {
            return;
        }

        var entry = packFileName.StartsWith("file/", StringComparison.OrdinalIgnoreCase)
            ? packFileName
            : $"file/{packFileName}";

        var rawName = packFileName.StartsWith("file/", StringComparison.OrdinalIgnoreCase)
            ? packFileName["file/".Length..]
            : packFileName;

        var currentPacks = GetActiveResourcePacks(installationPath);
        var removed = currentPacks.RemoveAll(p =>
            string.Equals(p, entry, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(p, rawName, StringComparison.OrdinalIgnoreCase));

        if (removed > 0)
        {
            SetActiveResourcePacks(installationPath, currentPacks);
        }
    }

    public static async Task<bool> EnsureRecommendedPacksAsync(
        string installationPath,
        IProgress<(string message, double percentage)>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var resourcepacksDir = Path.Combine(installationPath, "resourcepacks");
        _ = Directory.CreateDirectory(resourcepacksDir);

        var packs = GetRecommendedPacks();
        var total = packs.Count;
        var current = 0;

        foreach (var pack in packs)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var targetPath = Path.Combine(resourcepacksDir, pack.Name);
            var needsDownload = false;

            if (!File.Exists(targetPath))
            {
                needsDownload = true;
            }
            else
            {
                var fileInfo = new FileInfo(targetPath);
                if (pack.Size > 0 && fileInfo.Length != pack.Size)
                {
                    needsDownload = true;
                }
                else if (!string.IsNullOrEmpty(pack.Sha1))
                {
                    var localSha1 = CalculateSha1(targetPath);
                    if (!string.Equals(localSha1, pack.Sha1, StringComparison.OrdinalIgnoreCase))
                    {
                        needsDownload = true;
                    }
                }
            }

            if (needsDownload)
            {
                current++;
                var pct = (double)current / total * 100.0;
                progress?.Report(($"Загрузка ресурспака: {pack.Name}...", pct));
                try
                {
                    await DownloadFileAsync(pack.Url, targetPath, cancellationToken);
                }
                catch
                {
                    // Fallback gracefully if external CDN URL is unreachable
                }
            }
        }

        EnsureResourcePackEnabled(installationPath, DefaultFreshAnimationsPack);
        EnsureResourcePackEnabled(installationPath, DefaultStayTruePack);
        return true;
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

    [SuppressMessage("Security", "CA5350:Do Not Use Weak Cryptographic Algorithms", Justification = "SHA1 is required by Minecraft manifest standard.")]
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
