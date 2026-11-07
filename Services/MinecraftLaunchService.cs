namespace DevByteLauncher.Services;

public static class MinecraftLaunchService
{
    private static readonly HttpClient s_httpClient = new();

    [SuppressMessage("Reliability", "CA2000:Dispose objects before losing scope", Justification = "Disposed on process.Exited.")]
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

        var optionsPath = Path.Combine(gameDir, "options.txt");
        if (!File.Exists(optionsPath))
        {
            await File.WriteAllTextAsync(optionsPath, "lang:ru_ru\nenableVsync:true\nmaxFps:260\ndirectionalAudio:true\nsoundCategory_master:0.8\nsoundCategory_music:0.15\nautoJump:false\npauseOnLostFocus:false\nadvancedItemTooltips:true\noperatorItemsTab:true\nattackIndicator:1\ngamma:1.0\n", cancellationToken);
        }
        else
        {
            var text = await File.ReadAllTextAsync(optionsPath, cancellationToken);
            if (text.Contains("lang:en_US", StringComparison.OrdinalIgnoreCase))
            {
                await File.WriteAllTextAsync(optionsPath, text.Replace("lang:en_US", "lang:ru_ru", StringComparison.OrdinalIgnoreCase), cancellationToken);
            }
        }

        progress?.Report(("Инициализация окружения Minecraft...", 5));
        onLogReceived?.Invoke($"[{DateTime.Now:HH:mm:ss}] Инициализация окружения Minecraft...");

        var serverDisplayAddress = !string.IsNullOrWhiteSpace(settings.ServerHost)
            ? (settings.ServerPort is > 0 and not 25565 ? $"{settings.ServerHost}:{settings.ServerPort}" : settings.ServerHost)
            : "mc.obxodka.one";
        EnsureServerListEntry(gameDir, "DevByte Homelab", serverDisplayAddress);

        var mcPath = new MinecraftPath(gameDir);
        var launcher = new MinecraftLauncher(mcPath);

        var mcVersion = !string.IsNullOrWhiteSpace(settings.MinecraftVersion) ? settings.MinecraftVersion : "1.20.1";
        var fabricVersion = !string.IsNullOrWhiteSpace(settings.FabricVersion) ? settings.FabricVersion : "0.20.1";
        var versionName = $"fabric-loader-{fabricVersion}-{mcVersion}";

        launcher.FileProgressChanged += (_, e) =>
        {
            if (e.TotalTasks > 0)
            {
                var percent = ((double)e.ProgressedTasks / e.TotalTasks * 50.0) + 15.0;
                progress?.Report(($"Загрузка файлов игры: {e.Name} ({e.ProgressedTasks}/{e.TotalTasks})", Math.Min(percent, 75.0)));
            }
        };

        progress?.Report(($"Проверка базовой версии Minecraft {mcVersion}...", 10));
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

        progress?.Report(("Проверка среды исполнения Java 21 LTS...", 92));
        var javaPath = await JavaService.ResolveJava21Async(settings, progress, onLogReceived, cancellationToken);
        launchOption.JavaPath = javaPath;
        onLogReceived?.Invoke($"[{DateTime.Now:HH:mm:ss}] Использована среда Java 21: {javaPath}");

        launchOption.JvmArgumentOverrides =
        [
            new MArgument("-XX:+UseG1GC"),
            new MArgument("-XX:+ParallelRefProcEnabled"),
            new MArgument("-XX:MaxGCPauseMillis=200"),
            new MArgument("-XX:+UnlockExperimentalVMOptions"),
            new MArgument("-XX:+DisableExplicitGC"),
            new MArgument("-XX:+AlwaysPreTouch"),
            new MArgument($"-Dfabric.client.title=DevByte Minecraft • Fabric {mcVersion}"),
            new MArgument("-Ddevbyte.homelab=mc.obxodka.one")
        ];

        progress?.Report(("Синхронизация шейдеров и графических оптимизаций...", 95));
        await ShaderService.EnsureShadersAndModsAsync(settings.InstallationPath, progress, cancellationToken);
        var activePack = !string.IsNullOrWhiteSpace(settings.SelectedShaderPack)
            ? settings.SelectedShaderPack
            : ShaderService.DefaultShader;
        ShaderService.SaveShaderState(settings.InstallationPath, settings.ShadersEnabled, activePack);

        ResourcePackService.EnsureResourcePackEnabled(gameDir, ResourcePackService.DefaultFreshAnimationsPack);
        ResourcePackService.EnsureResourcePackEnabled(gameDir, ResourcePackService.DefaultStayTruePack);

        progress?.Report(("Запуск процесса Minecraft Fabric...", 98));
        var process = await launcher.BuildProcessAsync(versionName, launchOption, cancellationToken);

        var clientLogPath = Path.Combine(logsDir, "launcher-client.log");
        var logWriter = new StreamWriter(new FileStream(clientLogPath, FileMode.Create, FileAccess.Write, FileShare.ReadWrite))
        {
            AutoFlush = true
        };

        var windowReadyTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        process.StartInfo.RedirectStandardOutput = true;
        process.StartInfo.RedirectStandardError = true;
        process.StartInfo.UseShellExecute = false;

        process.OutputDataReceived += (_, args) =>
        {
            if (args.Data != null)
            {
                logWriter.WriteLine(args.Data);
                onLogReceived?.Invoke(args.Data);

                if (args.Data.Contains("Narrator library for", StringComparison.OrdinalIgnoreCase) ||
                    args.Data.Contains("OpenAL initialized", StringComparison.OrdinalIgnoreCase) ||
                    args.Data.Contains("Sound engine started", StringComparison.OrdinalIgnoreCase) ||
                    args.Data.Contains("Backend library: LWJGL", StringComparison.OrdinalIgnoreCase) ||
                    args.Data.Contains("LWJGL Version:", StringComparison.OrdinalIgnoreCase) ||
                    args.Data.Contains("Created: 1024x", StringComparison.OrdinalIgnoreCase) ||
                    args.Data.Contains("Connecting to", StringComparison.OrdinalIgnoreCase))
                {
                    _ = windowReadyTcs.TrySetResult(true);
                }
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

        process.EnableRaisingEvents = true;
        process.Exited += (_, _) =>
        {
            _ = windowReadyTcs.TrySetResult(false);
            try
            {
                logWriter.Flush();
                logWriter.Dispose();
            }
            catch
            {
            }
        };

        _ = process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        progress?.Report(("Ожидание открытия окна Minecraft...", 99));

        await WaitForWindowReadyAsync(process, windowReadyTcs, cancellationToken);

        if (process.HasExited)
        {
            throw new InvalidOperationException($"Процесс Minecraft неожиданно завершился (код: {process.ExitCode}). Проверьте лог запуска.");
        }

        progress?.Report(("Minecraft успешно запущен! Приятной игры.", 100));
        return process;
    }

    private static async Task WaitForWindowReadyAsync(
        Process process,
        TaskCompletionSource<bool> windowReadyTcs,
        CancellationToken cancellationToken)
    {
        using var timeoutCts = new CancellationTokenSource(TimeSpan.FromMinutes(5));
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);

        try
        {
            while (!process.HasExited && !linkedCts.Token.IsCancellationRequested)
            {
                if (windowReadyTcs.Task.IsCompleted)
                {
                    return;
                }

                try
                {
                    process.Refresh();
                    if (process.MainWindowHandle != IntPtr.Zero && !string.IsNullOrWhiteSpace(process.MainWindowTitle))
                    {
                        _ = windowReadyTcs.TrySetResult(true);
                        return;
                    }
                }
                catch
                {
                }

                await Task.Delay(250, linkedCts.Token);
            }
        }
        catch (OperationCanceledException)
        {
        }
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

        var jsonContent = await s_httpClient.GetStringAsync(fabricMetaUrl, cancellationToken);
        await File.WriteAllTextAsync(jsonFile, jsonContent, cancellationToken);
    }

    public static void EnsureServerListEntry(string gameDir, string serverName, string serverAddress)
    {
        try
        {
            var serversFile = Path.Combine(gameDir, "servers.dat");
            if (File.Exists(serversFile))
            {
                var bytes = File.ReadAllBytes(serversFile);
                var addressBytes = Encoding.UTF8.GetBytes(serverAddress);
                if (bytes.AsSpan().IndexOf(addressBytes) >= 0)
                {
                    return;
                }
            }

            using var ms = new MemoryStream();

            // Root TAG_Compound (id 10), empty name
            ms.WriteByte(10);
            WriteNbtString(ms, string.Empty);

            // TAG_List (id 9), name "servers"
            ms.WriteByte(9);
            WriteNbtString(ms, "servers");
            ms.WriteByte(10); // Element type: TAG_Compound

            // Count = 1 (Int32 BigEndian)
            Span<byte> intBuf = stackalloc byte[4];
            System.Buffers.Binary.BinaryPrimitives.WriteInt32BigEndian(intBuf, 1);
            ms.Write(intBuf);

            // Item compound
            // TAG_String "name"
            ms.WriteByte(8);
            WriteNbtString(ms, "name");
            WriteNbtString(ms, serverName);

            // TAG_String "ip"
            ms.WriteByte(8);
            WriteNbtString(ms, "ip");
            WriteNbtString(ms, serverAddress);

            // TAG_Byte "hidden" = 0
            ms.WriteByte(1);
            WriteNbtString(ms, "hidden");
            ms.WriteByte(0);

            // TAG_End (item)
            ms.WriteByte(0);

            // TAG_End (root)
            ms.WriteByte(0);

            File.WriteAllBytes(serversFile, ms.ToArray());
        }
        catch
        {
        }
    }

    private static void WriteNbtString(Stream stream, string value)
    {
        var bytes = Encoding.UTF8.GetBytes(value);
        Span<byte> lenBuf = stackalloc byte[2];
        System.Buffers.Binary.BinaryPrimitives.WriteInt16BigEndian(lenBuf, (short)bytes.Length);
        stream.Write(lenBuf);
        stream.Write(bytes);
    }
}
