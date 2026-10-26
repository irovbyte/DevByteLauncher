namespace DevByteLauncher.Services;

public static class ShaderService
{
    private static readonly HttpClient t_httpClient = new()
    {
        Timeout = TimeSpan.FromSeconds(30)
    };

    public const string DefaultShader = "ComplementaryReimagined_r5.9.3.zip";
    public const string AlternativeShader = "BSL_v10.1.8.zip";

    private const string IrisDownloadUrl = "https://cdn.modrinth.com/data/YL57xq9U/versions/bAo1Qhte/iris-fabric-1.8.14-beta.1%2Bmc1.21.1.jar";
    private const string SodiumDownloadUrl = "https://cdn.modrinth.com/data/AANobbMI/versions/s7adptIg/sodium-fabric-0.8.12-beta.1%2Bmc1.21.1.jar";
    private const string ComplementaryDownloadUrl = "https://cdn.modrinth.com/data/HVnmMxH1/versions/Bqen1mJX/ComplementaryReimagined_r5.9.3.zip";
    private const string BslDownloadUrl = "https://cdn.modrinth.com/data/Q1vvjJYV/versions/Y68yiql9/BSL_v10.1.8.zip";

    public static (bool IsEnabled, string SelectedPack) LoadShaderState(string installationPath)
    {
        var configPath = Path.Combine(installationPath, "config", "iris.properties");
        if (!File.Exists(configPath))
        {
            return (false, DefaultShader);
        }

        var isEnabled = false;
        var selectedPack = DefaultShader;

        try
        {
            var lines = File.ReadAllLines(configPath);
            foreach (var line in lines)
            {
                var trimmed = line.Trim();
                if (trimmed.StartsWith("enableShaders=", StringComparison.OrdinalIgnoreCase))
                {
                    var val = trimmed["enableShaders=".Length..].Trim();
                    isEnabled = bool.TryParse(val, out var parsed) && parsed;
                }
                else if (trimmed.StartsWith("shaderPack=", StringComparison.OrdinalIgnoreCase))
                {
                    var val = trimmed["shaderPack=".Length..].Trim();
                    if (!string.IsNullOrWhiteSpace(val))
                    {
                        selectedPack = val;
                    }
                }
            }
        }
        catch
        {
        }

        return (isEnabled, selectedPack);
    }

    public static void SaveShaderState(string installationPath, bool isEnabled, string selectedPack)
    {
        try
        {
            var configDir = Path.Combine(installationPath, "config");
            _ = Directory.CreateDirectory(configDir);
            var configPath = Path.Combine(configDir, "iris.properties");

            var lines = new List<string>
            {
                $"enableShaders={isEnabled.ToString().ToLowerInvariant()}",
                $"shaderPack={selectedPack}"
            };

            File.WriteAllLines(configPath, lines);
        }
        catch
        {
        }
    }

    public static List<string> GetAvailableShaderPacks(string installationPath)
    {
        var result = new List<string>();
        var shaderpacksDir = Path.Combine(installationPath, "shaderpacks");
        if (Directory.Exists(shaderpacksDir))
        {
            var files = Directory.GetFiles(shaderpacksDir, "*.zip");
            foreach (var file in files)
            {
                result.Add(Path.GetFileName(file));
            }
        }

        if (!result.Contains(DefaultShader, StringComparer.OrdinalIgnoreCase))
        {
            result.Insert(0, DefaultShader);
        }

        if (!result.Contains(AlternativeShader, StringComparer.OrdinalIgnoreCase))
        {
            result.Add(AlternativeShader);
        }

        return result;
    }

    public static async Task EnsureShadersAndModsAsync(
        string installationPath,
        IProgress<(string message, double percentage)>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var modsDir = Path.Combine(installationPath, "mods");
        _ = Directory.CreateDirectory(modsDir);

        var shaderpacksDir = Path.Combine(installationPath, "shaderpacks");
        _ = Directory.CreateDirectory(shaderpacksDir);

        var irisPath = Path.Combine(modsDir, "iris.jar");
        if (!File.Exists(irisPath))
        {
            progress?.Report(("Загрузка шейдер-движка Iris...", 40));
            await DownloadFileAsync(IrisDownloadUrl, irisPath, cancellationToken);
        }

        var sodiumPath = Path.Combine(modsDir, "sodium.jar");
        if (!File.Exists(sodiumPath))
        {
            progress?.Report(("Загрузка модуля оптимизации Sodium...", 50));
            await DownloadFileAsync(SodiumDownloadUrl, sodiumPath, cancellationToken);
        }

        var compShaderPath = Path.Combine(shaderpacksDir, DefaultShader);
        if (!File.Exists(compShaderPath))
        {
            progress?.Report(("Загрузка шейдеров Complementary Reimagined...", 60));
            await DownloadFileAsync(ComplementaryDownloadUrl, compShaderPath, cancellationToken);
        }

        var bslShaderPath = Path.Combine(shaderpacksDir, AlternativeShader);
        if (!File.Exists(bslShaderPath))
        {
            progress?.Report(("Загрузка шейдеров BSL...", 70));
            await DownloadFileAsync(BslDownloadUrl, bslShaderPath, cancellationToken);
        }
    }

    private static async Task DownloadFileAsync(string url, string destinationPath, CancellationToken cancellationToken)
    {
        var tempPath = destinationPath + ".tmp";
        if (File.Exists(tempPath))
        {
            File.Delete(tempPath);
        }

        using var response = await t_httpClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        _ = response.EnsureSuccessStatusCode();

        await using (var sourceStream = await response.Content.ReadAsStreamAsync(cancellationToken))
        await using (var fileStream = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None))
        {
            await sourceStream.CopyToAsync(fileStream, cancellationToken);
        }

        if (File.Exists(destinationPath))
        {
            File.Delete(destinationPath);
        }

        File.Move(tempPath, destinationPath);
    }
}
