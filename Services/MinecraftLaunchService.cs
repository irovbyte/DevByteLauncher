namespace DevByteLauncher.Services;

public static class MinecraftLaunchService
{
    private static readonly HttpClient t_httpClient = new();

    public static async Task<Process> LaunchGameAsync(
        AppSettings settings,
        IProgress<(string stage, double percentage)>? progress = null,
        Action<string>? onLogReceived = null,
        CancellationToken cancellationToken = default)
    {
        var gameDir = settings.InstallationPath;
        _ = Directory.CreateDirectory(gameDir);

        var logsDir = Path.Combine(gameDir, "logs");
        _ = Directory.CreateDirectory(logsDir);

        progress?.Report(("Инициализация окружения Minecraft...", 5));
        onLogReceived?.Invoke($"[{DateTime.Now:HH:mm:ss}] Инициализация окружения Minecraft...");

        var mcPath = new MinecraftPath(gameDir);
        var launcher = new MinecraftLauncher(mcPath);

        const string mcVersion = "1.21.1";
        const string fabricVersion = "0.16.9";
        var versionName = $"fabric-loader-{fabricVersion}-{mcVersion}";

        launcher.FileProgressChanged += (_, e) =>
        {
            if (e.TotalTasks > 0)
            {
                var percent = ((double)e.ProgressedTasks / e.TotalTasks * 50.0) + 15.0;
                progress?.Report(($"Загрузка файлов игры: {e.Name} ({e.ProgressedTasks}/{e.TotalTasks})", Math.Min(percent, 75.0)));
            }
        };

        progress?.Report(("Проверка базовой версии Minecraft 1.21.1...", 10));
        await launcher.InstallAsync(mcVersion, cancellationToken);

        progress?.Report(("Проверка профиля Fabric Loader...", 75));
        await EnsureFabricProfileAsync(mcPath, mcVersion, fabricVersion, cancellationToken);
        await launcher.InstallAsync(versionName, cancellationToken);

        progress?.Report(("Подготовка сессии игрока...", 90));
        var session = await AuthService.AuthenticateAsync(settings);

        var launchOption = new MLaunchOption
        {
            Session = session,
            MaximumRamMb = settings.AllocatedRamMb > 0 ? settings.AllocatedRamMb : 6144,
            ServerIp = settings.AutoConnectServer ? settings.ServerHost : null,
            ServerPort = settings.AutoConnectServer ? settings.ServerPort : 0
        };

        if (!string.IsNullOrWhiteSpace(settings.CustomJavaPath) && File.Exists(settings.CustomJavaPath))
        {
            launchOption.JavaPath = settings.CustomJavaPath;
        }

        launchOption.JvmArgumentOverrides =
        [
            new MArgument("-XX:+UseG1GC"),
            new MArgument("-XX:+ParallelRefProcEnabled"),
            new MArgument("-XX:MaxGCPauseMillis=200"),
            new MArgument("-XX:+UnlockExperimentalVMOptions"),
            new MArgument("-XX:+DisableExplicitGC"),
            new MArgument("-XX:+AlwaysPreTouch")
        ];

        progress?.Report(("Запуск процесса Minecraft Fabric...", 98));
        var process = await launcher.BuildProcessAsync(versionName, launchOption, cancellationToken);

        var clientLogPath = Path.Combine(logsDir, "launcher-client.log");
        var logWriter = new StreamWriter(new FileStream(clientLogPath, FileMode.Create, FileAccess.Write, FileShare.ReadWrite))
        {
            AutoFlush = true
        };

        process.StartInfo.RedirectStandardOutput = true;
        process.StartInfo.RedirectStandardError = true;
        process.StartInfo.UseShellExecute = false;

        process.OutputDataReceived += (_, args) =>
        {
            if (args.Data != null)
            {
                logWriter.WriteLine(args.Data);
                onLogReceived?.Invoke(args.Data);
            }
        };

        process.ErrorDataReceived += (_, args) =>
        {
            if (args.Data != null)
            {
                logWriter.WriteLine("[ERROR] " + args.Data);
                onLogReceived?.Invoke("[ERROR] " + args.Data);
            }
        };

        _ = process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        process.EnableRaisingEvents = true;
        process.Exited += (_, _) =>
        {
            logWriter.Flush();
            logWriter.Dispose();
        };

        progress?.Report(("Minecraft успешно запущен! Приятной игры.", 100));
        return process;
    }

    private static async Task EnsureFabricProfileAsync(
        MinecraftPath mcPath,
        string mcVersion,
        string fabricVersion,
        CancellationToken cancellationToken)
    {
        var versionName = $"fabric-loader-{fabricVersion}-{mcVersion}";
        var versionDir = Path.Combine(mcPath.BasePath, "versions", versionName);
        var jsonFile = Path.Combine(versionDir, $"{versionName}.json");

        if (File.Exists(jsonFile))
        {
            return;
        }

        _ = Directory.CreateDirectory(versionDir);
        var fabricMetaUrl = $"https://meta.fabricmc.net/v2/versions/loader/{mcVersion}/{fabricVersion}/profile/json";

        var jsonContent = await t_httpClient.GetStringAsync(fabricMetaUrl, cancellationToken);
        await File.WriteAllTextAsync(jsonFile, jsonContent, cancellationToken);
    }
}
