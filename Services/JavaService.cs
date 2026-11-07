using System.Formats.Tar;
using System.IO.Compression;

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
            EnsureExecutable(settings.CustomJavaPath);
            return settings.CustomJavaPath;
        }

        var localCandidate = Path.Combine(settings.InstallationPath, "runtime", "java-runtime-delta", "bin", OperatingSystem.IsWindows() ? "javaw.exe" : "java");
        if (File.Exists(localCandidate))
        {
            EnsureExecutable(localCandidate);
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

        var javaHome = Environment.GetEnvironmentVariable("JAVA_HOME");
        if (!string.IsNullOrWhiteSpace(javaHome))
        {
            var javaHomeBin = Path.Combine(javaHome, "bin", OperatingSystem.IsWindows() ? "javaw.exe" : "java");
            if (File.Exists(javaHomeBin) && (IsJava21Directory(javaHome) || IsJava21Binary(javaHomeBin)))
            {
                candidates.Add(javaHomeBin);
            }
        }

        if (OperatingSystem.IsWindows())
        {
            candidates.Add(@"C:\Program Files\Java\jdk-21.0.11\bin\javaw.exe");
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
        else
        {
            // Linux & macOS standard JVM search paths
            string[] jvmRoots =
            [
                "/usr/lib/jvm",
                "/usr/java",
                "/opt/java",
                "/opt/jdk",
                "/usr/local/java",
                Path.Combine(userProfile, ".jdks"),
                Path.Combine(userProfile, ".sdkman", "candidates", "java")
            ];

            foreach (var root in jvmRoots)
            {
                if (!Directory.Exists(root))
                {
                    continue;
                }

                try
                {
                    foreach (var subDir in Directory.GetDirectories(root, "*21*"))
                    {
                        var binJava = Path.Combine(subDir, "bin", "java");
                        if (File.Exists(binJava))
                        {
                            candidates.Add(binJava);
                        }
                    }

                    var defaultJavaDir = Path.Combine(root, "default-java");
                    var defaultJavaBin = Path.Combine(defaultJavaDir, "bin", "java");
                    if (File.Exists(defaultJavaBin) && IsJava21Directory(defaultJavaDir))
                    {
                        candidates.Add(defaultJavaBin);
                    }
                }
                catch
                {
                }
            }

            const string usrBinJava = "/usr/bin/java";
            if (File.Exists(usrBinJava) && IsJava21Binary(usrBinJava))
            {
                candidates.Add(usrBinJava);
            }
        }

        foreach (var c in candidates)
        {
            if (File.Exists(c))
            {
                EnsureExecutable(c);
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
            EnsureExecutable(javaExe);
            return javaExe;
        }

        const string winUrl = "https://aka.ms/download-jdk/microsoft-jdk-21.0.7-windows-x64.zip";
        const string linuxUrl = "https://aka.ms/download-jdk/microsoft-jdk-21.0.7-linux-x64.tar.gz";

        var url = OperatingSystem.IsWindows() ? winUrl : linuxUrl;
        var tempArchive = Path.Combine(runtimeDir, OperatingSystem.IsWindows() ? "java21_temp.zip" : "java21_temp.tar.gz");

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

            if (OperatingSystem.IsWindows())
            {
                await Task.Run(() => ZipFile.ExtractToDirectory(tempArchive, extractTemp), cancellationToken);
            }
            else
            {
                await using var fs = File.OpenRead(tempArchive);
                await using var gz = new GZipStream(fs, CompressionMode.Decompress);
                await TarFile.ExtractToDirectoryAsync(gz, extractTemp, overwriteFiles: true, cancellationToken);
            }

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

            EnsureExecutable(javaExe);
            if (!OperatingSystem.IsWindows())
            {
                var binDir = Path.Combine(javaDeltaDir, "bin");
                if (Directory.Exists(binDir))
                {
                    foreach (var file in Directory.EnumerateFiles(binDir))
                    {
                        EnsureExecutable(file);
                    }
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

    private static void EnsureExecutable(string filePath)
    {
        if (!OperatingSystem.IsWindows() && File.Exists(filePath))
        {
            try
            {
                const UnixFileMode execPerms = UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute |
                                               UnixFileMode.GroupRead | UnixFileMode.GroupExecute |
                                               UnixFileMode.OtherRead | UnixFileMode.OtherExecute;
                File.SetUnixFileMode(filePath, execPerms);
            }
            catch
            {
            }
        }
    }

    private static bool IsJava21Directory(string jdkDir)
    {
        try
        {
            var releaseFile = Path.Combine(jdkDir, "release");
            if (File.Exists(releaseFile))
            {
                var content = File.ReadAllText(releaseFile);
                if (content.Contains("JAVA_VERSION=\"21", StringComparison.OrdinalIgnoreCase) ||
                    content.Contains("JAVA_RUNTIME_VERSION=\"21", StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
        }
        catch
        {
        }
        return false;
    }

    private static bool IsJava21Binary(string javaPath)
    {
        try
        {
            var binDir = Path.GetDirectoryName(javaPath);
            if (!string.IsNullOrEmpty(binDir))
            {
                var parentDir = Path.GetDirectoryName(binDir);
                if (!string.IsNullOrEmpty(parentDir) && IsJava21Directory(parentDir))
                {
                    return true;
                }
            }

            using var proc = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = javaPath,
                    Arguments = "-version",
                    RedirectStandardError = true,
                    RedirectStandardOutput = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                }
            };
            _ = proc.Start();
            var output = proc.StandardError.ReadToEnd() + " " + proc.StandardOutput.ReadToEnd();
            _ = proc.WaitForExit(2000);
            return output.Contains("\"21.", StringComparison.OrdinalIgnoreCase) ||
                   output.Contains(" 21.", StringComparison.OrdinalIgnoreCase) ||
                   output.Contains("version 21", StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }
}
