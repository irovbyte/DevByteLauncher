namespace DevByteLauncher.Services;

public static class ShaderService
{
    private static readonly HttpClient t_httpClient = new()
    {
        Timeout = TimeSpan.FromSeconds(30)
    };

    public const string DefaultShader = "photon_v1.4.zip";
    public const string BslShader = "BSL_v10.1.8.zip";
    public const string ComplementaryShader = "ComplementaryReimagined_r5.9.3.zip";

    private const string PhotonDownloadUrl = "https://cdn.modrinth.com/data/lLqFfGNs/versions/BnGklH5y/photon_v1.4.zip";
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

        return (isEnabled, string.IsNullOrWhiteSpace(selectedPack) ? DefaultShader : selectedPack);
    }

    public static void SaveShaderState(string installationPath, bool isEnabled, string? selectedPack)
    {
        try
        {
            var configDir = Path.Combine(installationPath, "config");
            _ = Directory.CreateDirectory(configDir);
            var configPath = Path.Combine(configDir, "iris.properties");

            var packToSave = !string.IsNullOrWhiteSpace(selectedPack)
                ? selectedPack
                : DefaultShader;

            var existingProperties = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["enableShaders"] = isEnabled.ToString().ToLowerInvariant(),
                ["shaderPack"] = packToSave
            };

            if (File.Exists(configPath))
            {
                foreach (var line in File.ReadAllLines(configPath))
                {
                    var trimmed = line.Trim();
                    if (string.IsNullOrWhiteSpace(trimmed) || trimmed.StartsWith('#'))
                    {
                        continue;
                    }
                    var eq = trimmed.IndexOf('=');
                    if (eq > 0)
                    {
                        var key = trimmed[..eq].Trim();
                        var val = trimmed[(eq + 1)..].Trim();
                        if (!existingProperties.ContainsKey(key))
                        {
                            existingProperties[key] = val;
                        }
                    }
                }
            }

            var lines = new List<string>
            {
                "#This file stores configuration options for Iris, such as the currently active shaderpack"
            };
            foreach (var kvp in existingProperties)
            {
                lines.Add($"{kvp.Key}={kvp.Value}");
            }

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
                var name = Path.GetFileName(file);
                if (!string.IsNullOrWhiteSpace(name))
                {
                    result.Add(name);
                }
            }
        }

        if (!result.Contains(DefaultShader, StringComparer.OrdinalIgnoreCase))
        {
            result.Insert(0, DefaultShader);
        }

        if (!result.Contains(BslShader, StringComparer.OrdinalIgnoreCase))
        {
            result.Add(BslShader);
        }

        if (!result.Contains(ComplementaryShader, StringComparer.OrdinalIgnoreCase))
        {
            result.Add(ComplementaryShader);
        }

        return result;
    }

    public static async Task EnsureShadersAndModsAsync(
        string installationPath,
        IProgress<(string message, double percentage)>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var shaderpacksDir = Path.Combine(installationPath, "shaderpacks");
        _ = Directory.CreateDirectory(shaderpacksDir);

        var photonShaderPath = Path.Combine(shaderpacksDir, DefaultShader);
        if (!File.Exists(photonShaderPath))
        {
            progress?.Report(("Загрузка шейдеров Photon...", 60));
            await DownloadFileAsync(PhotonDownloadUrl, photonShaderPath, cancellationToken);
        }

        var bslShaderPath = Path.Combine(shaderpacksDir, BslShader);
        if (!File.Exists(bslShaderPath))
        {
            progress?.Report(("Загрузка шейдеров BSL...", 70));
            await DownloadFileAsync(BslDownloadUrl, bslShaderPath, cancellationToken);
        }

        var compShaderPath = Path.Combine(shaderpacksDir, ComplementaryShader);
        if (!File.Exists(compShaderPath))
        {
            progress?.Report(("Загрузка шейдеров Complementary Reimagined...", 80));
            await DownloadFileAsync(ComplementaryDownloadUrl, compShaderPath, cancellationToken);
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
