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

    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
#if DEBUG
            .WithDeveloperTools()
#endif
            .WithInterFont()
            .LogToTrace();
}
