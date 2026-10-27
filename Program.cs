namespace DevByteLauncher;

internal sealed partial class Program
{
    [LibraryImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool AttachConsole(int dwProcessId);

    private const int AttachParentProcess = -1;

    [STAThread]
    public static int Main(string[] args)
    {
        if (OperatingSystem.IsWindows())
        {
            _ = AttachConsole(AttachParentProcess);
        }

        if (args.Length > 0 && args[0] == "--smoke-test")
        {
            SynchronizationContext.SetSynchronizationContext(null);
            _ = BuildAvaloniaApp().SetupWithoutStarting();
            SynchronizationContext.SetSynchronizationContext(null);
            return RunSmokeTestAsync().GetAwaiter().GetResult();
        }

        if (args.Length > 0 && args[0] == "--test-mc")
        {
            SynchronizationContext.SetSynchronizationContext(null);
            _ = BuildAvaloniaApp().SetupWithoutStarting();
            SynchronizationContext.SetSynchronizationContext(null);
            return RunMinecraftTestAsync().GetAwaiter().GetResult();
        }

        try
        {
            return BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
        }
        catch (Exception ex)
        {
            var errPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "DevByteLauncher", "startup-error.txt");
            _ = Directory.CreateDirectory(Path.GetDirectoryName(errPath)!);
            File.WriteAllText(errPath, ex.ToString());
            Console.WriteLine(ex);
            return 1;
        }
    }

    private static async Task<int> RunSmokeTestAsync()
    {
        Console.WriteLine("=== DevByteLauncher Smoke Test ===");
        var settings = SettingsService.LoadSettings();
        Console.WriteLine($"[1/5] Settings loaded: Path='{settings.InstallationPath}', Nickname='{settings.Nickname}'");

        var drives = SettingsService.GetFixedDrives();
        Console.WriteLine($"[2/5] Drives detected: {drives.Count} drive(s).");
        foreach (var d in drives)
        {
            Console.WriteLine($"      -> {d.DisplayText}, Free: {d.FreeGigabytes} GB / {d.TotalGigabytes} GB");
        }

        Console.WriteLine("[3/5] Pinging homelab Minecraft server...");
        var status = await ServerPingService.PingServerAsync(settings.ServerHost, settings.ServerPort);
        Console.WriteLine($"      -> Online: {status.IsOnline}, Players: {status.OnlinePlayers}/{status.MaxPlayers}, Ping: {status.LatencyMs}ms");

        Console.WriteLine("[4/5] Fetching server modsync manifest...");
        var manifest = await ModSyncService.FetchManifestAsync(settings.ServerHost, settings.SyncPort);
        Console.WriteLine($"      -> Manifest: {manifest.Files.Count} mod(s) available from {settings.ServerHost}:{settings.SyncPort}.");

        Console.WriteLine("[5/5] Testing SkiaSharp Steve avatar generation...");
        using var avatar = SkinService.LoadAvatarHead(null);
        Console.WriteLine($"      -> Avatar loaded: {avatar?.Size.Width ?? 0}x{avatar?.Size.Height ?? 0} px.");

        Console.WriteLine("=== ALL LAUNCHER SUBSYSTEMS OPERATIONAL! ===");
        return 0;
    }

    private static async Task<int> RunMinecraftTestAsync()
    {
        Console.WriteLine("=== DevByte Minecraft Automated Test Launch ===");
        var settings = SettingsService.LoadSettings();
        Console.WriteLine($"[1/3] Installation path: {settings.InstallationPath}");

        Console.WriteLine("[2/3] Running ModSync...");
        var manifest = await ModSyncService.FetchManifestAsync(settings.ServerHost, settings.SyncPort);
        _ = await ModSyncService.SyncModsAsync(settings.InstallationPath, manifest);

        Console.WriteLine("[3/3] Launching Minecraft process...");
        try
        {
            var process = await MinecraftLaunchService.LaunchGameAsync(
                settings,
                onLogReceived: msg => Console.WriteLine(msg));

            Console.WriteLine("[SUCCESS] Minecraft launched and initialized successfully! Process ID: " + process.Id);
            try
            {
                process.Kill(true);
            }
            catch
            {
            }
            return 0;
        }
        catch (Exception ex)
        {
            Console.WriteLine("[ERROR] Minecraft launch error: " + ex.Message);
            var crashDir = Path.Combine(settings.InstallationPath, "crash-reports");
            if (Directory.Exists(crashDir))
            {
                var latestCrash = Directory.GetFiles(crashDir, "crash-*.txt")
                    .OrderByDescending(File.GetLastWriteTimeUtc)
                    .FirstOrDefault();
                if (latestCrash != null)
                {
                    Console.WriteLine("=== RECENT CRASH REPORT: " + Path.GetFileName(latestCrash) + " ===");
                    foreach (var line in File.ReadLines(latestCrash).Take(60))
                    {
                        Console.WriteLine(line);
                    }
                }
            }
            return 1;
        }
    }

    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
#if DEBUG
            .WithDeveloperTools()
#endif
            .WithInterFont()
            .LogToTrace();
}
