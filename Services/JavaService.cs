namespace DevByteLauncher.Services;

public static class JavaService
{
    private static readonly HttpClient s_httpClient = new()
    {
        Timeout = TimeSpan.FromMinutes(5)
    };

    public static async Task<string> ResolveJava21Async(
        AppSettings settings,
        IProgress<(string stage, double percentage)>? progress = null,
        Action<string>? onLogReceived = null,
        CancellationToken cancellationToken = default)
    {
        if (!string.IsNullOrWhiteSpace(settings.CustomJavaPath) && File.Exists(settings.CustomJavaPath))
        {
            return settings.CustomJavaPath;
        }

        var localCandidate = Path.Combine(settings.InstallationPath, "runtime", "java-runtime-delta", "bin", OperatingSystem.IsWindows() ? "javaw.exe" : "java");
        if (File.Exists(localCandidate))
        {
            return localCandidate;
        }

        var detected = FindExistingJava21();
        if (!string.IsNullOrEmpty(detected))
        {
            return detected;
        }

        progress?.Report(("Загрузка Java 21 LTS (Microsoft OpenJDK)...", 10));
        onLogReceived?.Invoke($"[{DateTime.Now:HH:mm:ss}] Локальная Java 21 не найдена. Начинается автоматическая установка Microsoft OpenJDK 21 LTS...");

        var installedPath = await DownloadAndInstallJava21Async(settings.InstallationPath, progress, onLogReceived, cancellationToken);
        return installedPath;
    }

    private static string? FindExistingJava21()
    {
        var candidates = new List<string>();

        var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        candidates.Add(Path.Combine(userProfile, "Games", "Minecraft", "jre", "java-runtime-delta", "bin", OperatingSystem.IsWindows() ? "javaw.exe" : "java"));
        candidates.Add(@"C:\Program Files\Java\jdk-21.0.11\bin\javaw.exe");

        if (OperatingSystem.IsWindows())
        {
            var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
            string[] javaDirs =
            [
                Path.Combine(programFiles, "Java"),
                Path.Combine(programFiles, "Eclipse Adoptium"),
                Path.Combine(programFiles, "Microsoft")
            ];

            foreach (var jDir in javaDirs)
            {
                if (Directory.Exists(jDir))
                {
                    try
                    {
                        foreach (var subDir in Directory.GetDirectories(jDir, "*21*"))
                        {
                            var binJava = Path.Combine(subDir, "bin", "javaw.exe");
                            if (File.Exists(binJava))
                            {
                                candidates.Add(binJava);
                            }
                        }
                    }
                    catch
                    {
                    }
                }
            }
        }

        foreach (var c in candidates)
        {
            if (File.Exists(c))
            {
                return c;
            }
        }

        return null;
    }

    private static async Task<string> DownloadAndInstallJava21Async(
        string installationPath,
        IProgress<(string stage, double percentage)>? progress,
        Action<string>? onLogReceived,
        CancellationToken cancellationToken)
    {
        var runtimeDir = Path.Combine(installationPath, "runtime");
        var javaDeltaDir = Path.Combine(runtimeDir, "java-runtime-delta");
        _ = Directory.CreateDirectory(runtimeDir);

        var javaExe = Path.Combine(javaDeltaDir, "bin", OperatingSystem.IsWindows() ? "javaw.exe" : "java");
        if (File.Exists(javaExe))
        {
            return javaExe;
        }

        const string winUrl = "https://aka.ms/download-jdk/microsoft-jdk-21.0.7-windows-x64.zip";
        const string linuxUrl = "https://aka.ms/download-jdk/microsoft-jdk-21.0.7-linux-x64.tar.gz";

        var url = OperatingSystem.IsWindows() ? winUrl : linuxUrl;
        var tempArchive = Path.Combine(runtimeDir, "java21_temp.zip");

        try
        {
            using (var response = await s_httpClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken))
            {
                _ = response.EnsureSuccessStatusCode();
                var totalBytes = response.Content.Headers.ContentLength ?? -1L;

                await using var contentStream = await response.Content.ReadAsStreamAsync(cancellationToken);
                await using var fileStream = new FileStream(tempArchive, FileMode.Create, FileAccess.Write, FileShare.None, 81920, true);

                var buffer = new byte[81920];
                long totalRead = 0;
                int read;

                while ((read = await contentStream.ReadAsync(buffer, cancellationToken)) > 0)
                {
                    await fileStream.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                    totalRead += read;

                    if (totalBytes > 0)
                    {
                        var pct = (double)totalRead / totalBytes * 100.0;
                        progress?.Report(($"Загрузка Java 21 LTS: {pct:F0}% ({totalRead / 1048576} MB / {totalBytes / 1048576} MB)", pct));
                    }
                }
            }

            progress?.Report(("Распаковка Java 21 LTS...", 95));
            onLogReceived?.Invoke($"[{DateTime.Now:HH:mm:ss}] Распаковка Java 21 в {javaDeltaDir}...");

            var extractTemp = Path.Combine(runtimeDir, "extract_temp");
            if (Directory.Exists(extractTemp))
            {
                Directory.Delete(extractTemp, true);
            }
            _ = Directory.CreateDirectory(extractTemp);

            await Task.Run(() => ZipFile.ExtractToDirectory(tempArchive, extractTemp), cancellationToken);

            var rootSubdir = Directory.GetDirectories(extractTemp).FirstOrDefault() ?? extractTemp;
            if (Directory.Exists(javaDeltaDir))
            {
                Directory.Delete(javaDeltaDir, true);
            }

            Directory.Move(rootSubdir, javaDeltaDir);

            if (Directory.Exists(extractTemp))
            {
                try
                {
                    Directory.Delete(extractTemp, true);
                }
                catch
                {
                }
            }

            onLogReceived?.Invoke($"[{DateTime.Now:HH:mm:ss}] Java 21 LTS успешно установлена.");
            return javaExe;
        }
        finally
        {
            if (File.Exists(tempArchive))
            {
                try
                {
                    File.Delete(tempArchive);
                }
                catch
                {
                }
            }
        }
    }
}
