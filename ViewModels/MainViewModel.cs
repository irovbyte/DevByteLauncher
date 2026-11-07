namespace DevByteLauncher.ViewModels;

public sealed partial class MainViewModel : ViewModelBase, IDisposable
{
    private readonly CancellationTokenSource _cts = new();
    private Process? _gameProcess;

    [ObservableProperty]
    public partial AppSettings Settings { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsTab0Selected))]
    [NotifyPropertyChangedFor(nameof(IsTab1Selected))]
    [NotifyPropertyChangedFor(nameof(IsTab2Selected))]
    [NotifyPropertyChangedFor(nameof(IsTab3Selected))]
    [NotifyPropertyChangedFor(nameof(IsTab4Selected))]
    public partial int SelectedTabIndex { get; set; }

    public bool IsTab0Selected => SelectedTabIndex == 0;
    public bool IsTab1Selected => SelectedTabIndex == 1;
    public bool IsTab2Selected => SelectedTabIndex == 2;
    public bool IsTab3Selected => SelectedTabIndex == 3;
    public bool IsTab4Selected => SelectedTabIndex == 4;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanPlay))]
    [NotifyPropertyChangedFor(nameof(PlayButtonToolTip))]
    [NotifyPropertyChangedFor(nameof(PlayButtonText))]
    [NotifyPropertyChangedFor(nameof(PlayButtonSymbol))]
    public partial bool IsBusy { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanPlay))]
    [NotifyPropertyChangedFor(nameof(PlayButtonToolTip))]
    [NotifyPropertyChangedFor(nameof(PlayButtonText))]
    [NotifyPropertyChangedFor(nameof(PlayButtonSymbol))]
    public partial bool IsGameRunning { get; set; }

    public bool IsNicknameValid => !string.IsNullOrWhiteSpace(Nickname);

    public bool CanPlay => !IsBusy && !IsGameRunning && IsNicknameValid;

    public string DisplayNameText => string.IsNullOrWhiteSpace(Nickname) ? "Игрок не указан" : Nickname;

    public string NicknameBorderBrush => IsNicknameValid ? "#3a3a3a" : "#f87171";

    public string PlayButtonText => IsBusy ? "ЗАГРУЗКА..." : "И Г Р А Т Ь";

    public string PlayButtonSymbol => IsBusy ? "ArrowSync" : "Play";

    public string PlayButtonToolTip => !IsNicknameValid
        ? "Введите никнейм перед запуском игры"
        : (IsBusy ? "Выполняется запуск игры..." : (IsGameRunning ? "Minecraft запущен" : $"Запустить Minecraft Fabric {Settings.MinecraftVersion}"));

    [ObservableProperty]
    public partial bool IsProgressVisible { get; set; }

    [ObservableProperty]
    public partial double ProgressPercentage { get; set; }

    [ObservableProperty]
    public partial string StatusMessage { get; set; } = "Готов к запуску • Сервер mc.obxodka.one";

    [ObservableProperty]
    public partial ServerStatusInfo ServerStatus { get; set; } = new();

    [ObservableProperty]
    public partial string CurrentDriveSummary { get; set; } = string.Empty;

    [ObservableProperty]
    public partial Bitmap? AvatarBitmap { get; set; }

    [ObservableProperty]
    public partial UpdateInfo? AvailableUpdate { get; set; }

    [ObservableProperty]
    public partial bool IsUpdateAvailable { get; set; }

    [ObservableProperty]
    public partial bool IsUpdating { get; set; }

    [ObservableProperty]
    public partial double UpdateProgress { get; set; }

    [ObservableProperty]
    public partial string UpdateStatusText { get; set; } = "Проверка обновлений...";

    [ObservableProperty]
    public partial bool ShadersEnabled { get; set; }

    [ObservableProperty]
    public partial string SelectedShaderPack { get; set; } = "photon_v1.4.zip";

    [ObservableProperty]
    public partial bool CloseOnLaunch { get; set; } = true;

    [ObservableProperty]
    public partial bool IsDeviceCodeDialogVisible { get; set; }

    [ObservableProperty]
    public partial string DeviceCode { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string DeviceVerificationUrl { get; set; } = "https://www.microsoft.com/link";

    [ObservableProperty]
    public partial string ResourcePacksStatusText { get; set; } = "Готовы к игре (Fresh Animations, Stay True)";

    [ObservableProperty]
    public partial string ShadersStatusText { get; set; } = "Готовы к игре (Iris + Sodium)";

    [ObservableProperty]
    public partial bool IsResourcePacksSyncing { get; set; }

    [ObservableProperty]
    public partial bool IsShadersSyncing { get; set; }

    public static string CurrentVersionText => $"v{UpdateService.CurrentVersion}";

    public ObservableCollection<DriveStorageInfo> Drives { get; } = [];
    public ObservableCollection<ModItemViewModel> Mods { get; } = [];
    public ObservableCollection<string> ConsoleLogs { get; } = [];
    public ObservableCollection<string> AvailableShaderPacks { get; } = [];
    public ObservableCollection<string> AvailableResourcePacks { get; } = [];
    public ObservableCollection<string> ActiveResourcePacks { get; } = [];

    public int AllocatedRamGb
    {
        get => Settings.AllocatedRamMb / 1024;
        set
        {
            if (value is >= 2 and <= 32)
            {
                Settings.AllocatedRamMb = value * 1024;
                OnPropertyChanged();
                SaveSettings();
            }
        }
    }

    public string Nickname
    {
        get => Settings.Nickname;
        set
        {
            if (Settings.IsMicrosoftAuth && !string.IsNullOrWhiteSpace(Settings.Nickname) && value != Settings.Nickname)
            {
                return;
            }
            Settings.Nickname = value ?? string.Empty;
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsNicknameValid));
            OnPropertyChanged(nameof(CanPlay));
            OnPropertyChanged(nameof(DisplayNameText));
            OnPropertyChanged(nameof(NicknameBorderBrush));
            OnPropertyChanged(nameof(PlayButtonToolTip));
            OnPropertyChanged(nameof(NicknameToolTip));
            SaveSettings();
        }
    }

    public string NicknameToolTip => Settings.IsMicrosoftAuth
        ? "Никнейм привязан к вашей лицензии Microsoft и защищен от изменений"
        : "Ваш никнейм для игры на сервере";

    public bool IsMicrosoftAuth
    {
        get => Settings.IsMicrosoftAuth;
        set
        {
            Settings.IsMicrosoftAuth = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(AuthModeBadgeText));
            SaveSettings();
        }
    }

    public string AuthModeBadgeText => Settings.IsMicrosoftAuth ? "Microsoft Лицензия" : "Офлайн Аккаунт";

    public string InstallationPath
    {
        get => Settings.InstallationPath;
        set
        {
            Settings.InstallationPath = value;
            OnPropertyChanged();
            UpdateDriveSummary();
            SaveSettings();
            LoadShaderState();
            LoadResourcePacksState();
            _ = LoadModsListAsync();
        }
    }

    public MainViewModel()
    {
        UpdateService.CleanPreviousBinary();
        Settings = SettingsService.LoadSettings();
        CloseOnLaunch = Settings.CloseOnLaunch;
        RefreshDrives();
        UpdateDriveSummary();
        ReloadAvatar();
        LoadShaderState();
        LoadResourcePacksState();
        AddLog($"[{DateTime.Now:HH:mm:ss}] [SYSTEM] DevByte Launcher v{UpdateService.CurrentVersion} инициализирован.");
        AddLog($"[{DateTime.Now:HH:mm:ss}] [SYSTEM] Директория установки: {Settings.InstallationPath}");
        AddLog($"[{DateTime.Now:HH:mm:ss}] [SYSTEM] Сервер: {Settings.ServerHost}:{Settings.ServerPort} (Порт синхронизации: {Settings.SyncPort})");
        AddLog($"[{DateTime.Now:HH:mm:ss}] [SYSTEM] Выделено памяти: {AllocatedRamGb} ГБ, Режим: {AuthModeBadgeText}");
        AddLog($"[{DateTime.Now:HH:mm:ss}] [GRAPHICS] Шейдеры: {(ShadersEnabled ? $"Включены ({SelectedShaderPack})" : "Выключены (Iris + Sodium готовы)")}");
        AddLog($"[{DateTime.Now:HH:mm:ss}] [RESOURCES] Ресурспаки: {ResourcePacksStatusText}");
        _ = RefreshServerStatusAsync();
        _ = LoadModsListAsync();
        _ = StartPeriodicPingLoopAsync();
        _ = CheckForUpdatesAsync();
        if (Settings.IsMicrosoftAuth)
        {
            _ = SyncCloudSkinAsync();
        }
    }

    public void LoadShaderState()
    {
        var (isEnabled, selectedPack) = ShaderService.LoadShaderState(Settings.InstallationPath);
        if (string.IsNullOrWhiteSpace(selectedPack))
        {
            selectedPack = !string.IsNullOrWhiteSpace(Settings.SelectedShaderPack)
                ? Settings.SelectedShaderPack
                : ShaderService.DefaultShader;
        }

        var packs = ShaderService.GetAvailableShaderPacks(Settings.InstallationPath);
        if (!packs.Contains(selectedPack, StringComparer.OrdinalIgnoreCase))
        {
            packs.Insert(0, selectedPack);
        }

        AvailableShaderPacks.Clear();
        foreach (var pack in packs)
        {
            AvailableShaderPacks.Add(pack);
        }

        ShadersEnabled = isEnabled;
        SelectedShaderPack = selectedPack;
        Settings.SelectedShaderPack = selectedPack;
        Settings.ShadersEnabled = isEnabled;
        ShaderService.SaveShaderState(Settings.InstallationPath, isEnabled, selectedPack);
    }

    public void LoadResourcePacksState()
    {
        var available = ResourcePackService.GetAvailableResourcePacks(Settings.InstallationPath);
        var active = ResourcePackService.GetActiveResourcePacks(Settings.InstallationPath);

        AvailableResourcePacks.Clear();
        foreach (var pack in available)
        {
            AvailableResourcePacks.Add(pack);
        }

        ActiveResourcePacks.Clear();
        foreach (var pack in active)
        {
            ActiveResourcePacks.Add(pack);
        }

        var activeCount = active.Count(p => p is not "vanilla" and not "fabric");
        ResourcePacksStatusText = activeCount > 0
            ? $"Активно наборов: {activeCount} (Fresh Animations, Stay True)"
            : "Рекомендованные (Fresh Animations, Stay True)";
    }

    [RelayCommand]
    public void OpenResourcepacksFolder()
    {
        try
        {
            var rpDir = Path.Combine(Settings.InstallationPath, "resourcepacks");
            _ = Directory.CreateDirectory(rpDir);
            _ = Process.Start(new ProcessStartInfo
            {
                FileName = rpDir,
                UseShellExecute = true
            });
        }
        catch
        {
        }
    }

    [RelayCommand]
    public async Task SyncResourcePacksAsync()
    {
        if (IsResourcePacksSyncing)
        {
            return;
        }

        try
        {
            IsResourcePacksSyncing = true;
            ResourcePacksStatusText = "Синхронизация ресурспаков...";
            AddLog($"[{DateTime.Now:HH:mm:ss}] [RESOURCES] Синхронизация наборов текстур (Fresh Animations, Stay True)...");
            var manifest = await ModSyncService.FetchManifestAsync(Settings.ServerHost, Settings.SyncPort, _cts.Token);
            if (!string.IsNullOrWhiteSpace(manifest.FabricLoaderVersion))
            {
                Settings.FabricVersion = manifest.FabricLoaderVersion;
            }
            if (!string.IsNullOrWhiteSpace(manifest.Version))
            {
                Settings.MinecraftVersion = manifest.Version;
            }
            SaveSettings();
            _ = await ModSyncService.SyncResourcePacksAsync(Settings.InstallationPath, manifest, null, _cts.Token);
            LoadResourcePacksState();
            AddLog($"[{DateTime.Now:HH:mm:ss}] [RESOURCES] Синхронизация ресурспаков успешно завершена.");
        }
        catch (Exception ex)
        {
            ResourcePacksStatusText = "Ошибка синхронизации";
            AddLog($"[{DateTime.Now:HH:mm:ss}] [ERROR] Ошибка синхронизации ресурспаков: {ex.Message}");
        }
        finally
        {
            IsResourcePacksSyncing = false;
        }
    }

    partial void OnShadersEnabledChanged(bool value)
    {
        Settings.ShadersEnabled = value;
        var pack = !string.IsNullOrWhiteSpace(SelectedShaderPack) ? SelectedShaderPack : ShaderService.DefaultShader;
        ShaderService.SaveShaderState(Settings.InstallationPath, value, pack);
        SaveSettings();
    }

    partial void OnSelectedShaderPackChanged(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return;
        }

        Settings.SelectedShaderPack = value;
        ShaderService.SaveShaderState(Settings.InstallationPath, ShadersEnabled, value);
        SaveSettings();
    }

    partial void OnCloseOnLaunchChanged(bool value)
    {
        Settings.CloseOnLaunch = value;
        SaveSettings();
    }

    public void ReloadAvatar() => AvatarBitmap = SkinService.LoadAvatarHead(Settings.SelectedSkinPath);

    public async Task SyncCloudSkinAsync(string? explicitToken = null)
    {
        if (Settings.IsMicrosoftAuth)
        {
            try
            {
                var token = explicitToken;
                if (string.IsNullOrWhiteSpace(token))
                {
                    var session = await AuthService.AuthenticateAsync(Settings);
                    token = session.AccessToken;
                }

                if (string.IsNullOrWhiteSpace(token) || token == "access_token")
                {
                    return;
                }

                var (skinUrl, _) = await AuthService.GetActiveSkinAsync(token);
                if (!string.IsNullOrWhiteSpace(skinUrl))
                {
                    var skinsDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "DevByteLauncher", "skins");
                    _ = Directory.CreateDirectory(skinsDir);
                    var skinPath = Path.Combine(skinsDir, $"{Settings.Nickname}.png");
                    var downloaded = await AuthService.DownloadSkinAsync(skinUrl, skinPath);
                    if (downloaded)
                    {
                        Settings.SelectedSkinPath = skinPath;
                        SaveSettings();

                        if (!string.IsNullOrWhiteSpace(Settings.InstallationPath) && Directory.Exists(Settings.InstallationPath))
                        {
                            var ftSkin = Path.Combine(Settings.InstallationPath, "devbyte-skin.png");
                            try
                            {
                                File.Copy(skinPath, ftSkin, true);
                            }
                            catch
                            {
                            }
                        }

                        Dispatcher.UIThread.Post(() =>
                        {
                            ReloadAvatar();
                            OnPropertyChanged(nameof(Settings));
                        });
                        AddLog($"[{DateTime.Now:HH:mm:ss}] [SKIN] Актуальный скин игрока {Settings.Nickname} успешно обновлен из облака Microsoft.");
                    }
                }
            }
            catch (Exception ex)
            {
                AddLog($"[{DateTime.Now:HH:mm:ss}] [ERROR] Ошибка синхронизации скина: {ex.Message}");
            }
        }
        else if (!string.IsNullOrWhiteSpace(Settings.Nickname) && Settings.Nickname != "Player")
        {
            try
            {
                var skinsDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "DevByteLauncher", "skins");
                _ = Directory.CreateDirectory(skinsDir);
                var skinPath = Path.Combine(skinsDir, $"{Settings.Nickname}.png");
                var skinUrl = $"https://minotar.net/skin/{Settings.Nickname}";
                var downloaded = await AuthService.DownloadSkinAsync(skinUrl, skinPath);
                if (downloaded)
                {
                    Settings.SelectedSkinPath = skinPath;
                    SaveSettings();

                    if (!string.IsNullOrWhiteSpace(Settings.InstallationPath) && Directory.Exists(Settings.InstallationPath))
                    {
                        var ftSkin = Path.Combine(Settings.InstallationPath, "devbyte-skin.png");
                        try
                        {
                            File.Copy(skinPath, ftSkin, true);
                        }
                        catch
                        {
                        }
                    }

                    Dispatcher.UIThread.Post(() =>
                    {
                        ReloadAvatar();
                        OnPropertyChanged(nameof(Settings));
                    });
                    AddLog($"[{DateTime.Now:HH:mm:ss}] [SKIN] Скин игрока {Settings.Nickname} успешно синхронизирован.");
                }
            }
            catch
            {
            }
        }
    }

    public async Task ChangeSkinAsync(string newSkinFilePath)
    {
        if (string.IsNullOrWhiteSpace(newSkinFilePath) || !File.Exists(newSkinFilePath))
        {
            return;
        }

        try
        {
            var skinsDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "DevByteLauncher", "skins");
            _ = Directory.CreateDirectory(skinsDir);
            var destPath = Path.Combine(skinsDir, $"{Settings.Nickname}_custom.png");
            File.Copy(newSkinFilePath, destPath, true);

            Settings.SelectedSkinPath = destPath;
            SaveSettings();

            if (!string.IsNullOrWhiteSpace(Settings.InstallationPath) && Directory.Exists(Settings.InstallationPath))
            {
                var ftSkin = Path.Combine(Settings.InstallationPath, "devbyte-skin.png");
                try
                {
                    File.Copy(destPath, ftSkin, true);
                }
                catch
                {
                }
            }

            ReloadAvatar();
            OnPropertyChanged(nameof(Settings));
            AddLog($"[{DateTime.Now:HH:mm:ss}] [SKIN] Скин успешно обновлен локально.");

            if (Settings.IsMicrosoftAuth)
            {
                StatusMessage = "Синхронизация скина с облаком Microsoft...";
                var session = await AuthService.AuthenticateAsync(Settings);
                if (!string.IsNullOrWhiteSpace(session.AccessToken) && session.AccessToken != "access_token")
                {
                    var uploaded = await AuthService.UploadSkinToCloudAsync(session.AccessToken, destPath, "classic");
                    if (uploaded)
                    {
                        StatusMessage = "Скин успешно обновлен локально и в облаке Microsoft!";
                        AddLog($"[{DateTime.Now:HH:mm:ss}] [SKIN] Скин успешно опубликован в облаке Microsoft Mojang.");
                    }
                    else
                    {
                        StatusMessage = "Скин применен локально. Не удалось обновить в облаке Microsoft.";
                        AddLog($"[{DateTime.Now:HH:mm:ss}] [WARN] Не удалось отправить скин в облако Microsoft.");
                    }
                }
            }
            else
            {
                StatusMessage = "Скин успешно обновлен!";
            }
        }
        catch (Exception ex)
        {
            StatusMessage = $"Ошибка смены скина: {ex.Message}";
            AddLog($"[{DateTime.Now:HH:mm:ss}] [ERROR] Ошибка смены скина: {ex.Message}");
        }
    }

    private async Task StartPeriodicPingLoopAsync()
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(25));
        while (!_cts.Token.IsCancellationRequested && await timer.WaitForNextTickAsync(_cts.Token))
        {
            await RefreshServerStatusAsync();
        }
    }

    [RelayCommand]
    public void SelectTab(object? param)
    {
        if (param is int i)
        {
            SelectedTabIndex = i;
        }
        else if (param != null && int.TryParse(param.ToString(), out var parsed))
        {
            SelectedTabIndex = parsed;
        }

        LoadShaderState();
        LoadResourcePacksState();
    }

    [RelayCommand]
    public async Task RefreshServerStatusAsync()
    {
        try
        {
            var status = await ServerPingService.PingServerAsync(Settings.ServerHost, Settings.ServerPort);
            Dispatcher.UIThread.Post(() => ServerStatus = status);
        }
        catch
        {
            Dispatcher.UIThread.Post(() => ServerStatus = new ServerStatusInfo { IsOnline = false });
        }
    }

    [RelayCommand]
    public void RefreshDrives()
    {
        Drives.Clear();
        var fixedDrives = SettingsService.GetFixedDrives();
        foreach (var drive in fixedDrives)
        {
            Drives.Add(drive);
        }
        UpdateDriveSummary();
    }

    private void UpdateDriveSummary()
    {
        var info = SettingsService.GetDriveForPath(Settings.InstallationPath);
        var displayPath = Settings.InstallationPath;
        if (OperatingSystem.IsLinux() || OperatingSystem.IsMacOS())
        {
            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            if (!string.IsNullOrEmpty(home) && displayPath.StartsWith(home, StringComparison.Ordinal))
            {
                displayPath = "~" + displayPath[home.Length..];
            }
        }

        CurrentDriveSummary = info != null
            ? $"{displayPath} ({info.FreeGigabytes:F1} ГБ свободно из {info.TotalGigabytes:F1} ГБ)"
            : (!string.IsNullOrEmpty(displayPath) ? displayPath : "Путь не указан");
    }

    public void SaveSettings() => SettingsService.SaveSettings(Settings);

    [RelayCommand]
    public async Task LoadModsListAsync()
    {
        try
        {
            var modsDir = Path.Combine(Settings.InstallationPath, "mods");
            _ = Directory.CreateDirectory(modsDir);

            var manifest = await ModSyncService.FetchManifestAsync(Settings.ServerHost, Settings.SyncPort);
            var manifestLookup = manifest.Files.ToDictionary(f => Path.GetFileName(f.Path), StringComparer.OrdinalIgnoreCase);

            var localFiles = Directory.EnumerateFiles(modsDir, "*.jar*")
                .Where(f => f.EndsWith(".jar", StringComparison.OrdinalIgnoreCase) || f.EndsWith(".jar.disabled", StringComparison.OrdinalIgnoreCase))
                .ToList();

            var items = new List<ModItemViewModel>();
            var processedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var filePath in localFiles)
            {
                var isEnabled = filePath.EndsWith(".jar", StringComparison.OrdinalIgnoreCase);
                var fullFileName = Path.GetFileName(filePath);
                var canonicalName = isEnabled ? fullFileName : fullFileName[..^9];
                _ = processedNames.Add(canonicalName);

                var fileInfo = new FileInfo(filePath);
                var sizeMb = fileInfo.Length >= 1048576
                    ? $"{(double)fileInfo.Length / (1024 * 1024):F2} МБ"
                    : $"{(double)fileInfo.Length / 1024:F0} КБ";

                var displayName = canonicalName.EndsWith(".jar", StringComparison.OrdinalIgnoreCase)
                    ? canonicalName[..^4]
                    : canonicalName;

                items.Add(new ModItemViewModel
                {
                    Name = displayName,
                    FileName = canonicalName,
                    FilePath = filePath,
                    SizeFormatted = sizeMb,
                    IsInstalled = true,
                    IsEnabled = isEnabled,
                    Status = isEnabled ? "Включен" : "Отключен"
                });
            }

            foreach (var kvp in manifestLookup)
            {
                if (!processedNames.Contains(kvp.Key))
                {
                    var item = kvp.Value;
                    var sizeMb = item.Size > 0 ? $"{(double)item.Size / (1024 * 1024):F2} МБ" : "—";
                    var canonicalName = Path.GetFileName(item.Path);
                    var displayName = canonicalName.EndsWith(".jar", StringComparison.OrdinalIgnoreCase)
                        ? canonicalName[..^4]
                        : canonicalName;

                    items.Add(new ModItemViewModel
                    {
                        Name = displayName,
                        FileName = canonicalName,
                        FilePath = Path.Combine(modsDir, canonicalName),
                        SizeFormatted = sizeMb,
                        IsInstalled = false,
                        IsEnabled = false,
                        Status = "Будет загружен с сервера"
                    });
                }
            }

            var sorted = items
                .OrderByDescending(i => i.IsEnabled)
                .ThenBy(i => i.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();

            Dispatcher.UIThread.Post(() =>
            {
                Mods.Clear();
                foreach (var mod in sorted)
                {
                    Mods.Add(mod);
                }
            });
        }
        catch
        {
        }
    }

    [RelayCommand]
    public void ToggleMod(ModItemViewModel mod)
    {
        try
        {
            var modsDir = Path.Combine(Settings.InstallationPath, "mods");
            var activePath = Path.Combine(modsDir, mod.FileName);
            var disabledPath = activePath + ".disabled";

            if (mod.IsEnabled && File.Exists(activePath))
            {
                File.Move(activePath, disabledPath);
                mod.IsEnabled = false;
                mod.FilePath = disabledPath;
                mod.Status = "Отключен";
            }
            else if (!mod.IsEnabled && File.Exists(disabledPath))
            {
                File.Move(disabledPath, activePath);
                mod.IsEnabled = true;
                mod.FilePath = activePath;
                mod.Status = "Включен";
            }
        }
        catch
        {
        }
    }

    [RelayCommand]
    public void OpenModsFolder()
    {
        try
        {
            var modsDir = Path.Combine(Settings.InstallationPath, "mods");
            _ = Directory.CreateDirectory(modsDir);
            _ = Process.Start(new ProcessStartInfo
            {
                FileName = modsDir,
                UseShellExecute = true
            });
        }
        catch
        {
        }
    }

    [RelayCommand]
    public void OpenGameFolder()
    {
        try
        {
            _ = Directory.CreateDirectory(Settings.InstallationPath);
            _ = Process.Start(new ProcessStartInfo
            {
                FileName = Settings.InstallationPath,
                UseShellExecute = true
            });
        }
        catch
        {
        }
    }

    [RelayCommand]
    public void ClearLogs() => ConsoleLogs.Clear();

    private CancellationTokenSource? _deviceCodeCts;

    [RelayCommand]
    public async Task CopyDeviceCodeAsync()
    {
        try
        {
            if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop &&
                desktop.MainWindow?.Clipboard is { } clipboard)
            {
                await clipboard.SetTextAsync(DeviceCode);
                StatusMessage = "Код скопирован в буфер обмена!";
                return;
            }
        }
        catch
        {
        }

        StatusMessage = "Код отображен на экране.";
    }

    [RelayCommand]
    public void OpenVerificationUrl()
    {
        try
        {
            _ = Process.Start(new ProcessStartInfo
            {
                FileName = DeviceVerificationUrl,
                UseShellExecute = true
            });
        }
        catch
        {
        }
    }

    [RelayCommand]
    public void CancelDeviceCodeLogin()
    {
        _deviceCodeCts?.Cancel();
        IsDeviceCodeDialogVisible = false;
        IsBusy = false;
        StatusMessage = "Авторизация Microsoft отменена";
    }

    [RelayCommand]
    public async Task LoginMicrosoftAsync()
    {
        try
        {
            if (_deviceCodeCts != null)
            {
                await _deviceCodeCts.CancelAsync();
            }
            _deviceCodeCts = new CancellationTokenSource();
            IsBusy = true;
            StatusMessage = "Ожидание авторизации в Microsoft...";
            AddLog($"[{DateTime.Now:HH:mm:ss}] Запуск авторизации Microsoft...");

            var session = await AuthService.LoginMicrosoftDeviceCodeAsync((code, url) =>
                Dispatcher.UIThread.InvokeAsync(async () =>
                {
                    DeviceCode = code;
                    DeviceVerificationUrl = url;
                    IsDeviceCodeDialogVisible = true;

                    if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop &&
                        desktop.MainWindow?.Clipboard is { } clipboard)
                    {
                        try
                        {
                            await clipboard.SetTextAsync(code);
                        }
                        catch
                        {
                        }
                    }

                    try
                    {
                        _ = Process.Start(new ProcessStartInfo
                        {
                            FileName = url,
                            UseShellExecute = true
                        });
                    }
                    catch
                    {
                    }
                }), _deviceCodeCts.Token);

            if (!string.IsNullOrWhiteSpace(session.Username))
            {
                Dispatcher.UIThread.Post(() =>
                {
                    IsDeviceCodeDialogVisible = false;
                    Settings.Nickname = session.Username;
                    Settings.IsMicrosoftAuth = true;
                    Nickname = session.Username;
                    OnPropertyChanged(nameof(IsMicrosoftAuth));
                    OnPropertyChanged(nameof(AuthModeBadgeText));
                    OnPropertyChanged(nameof(CanPlay));
                    OnPropertyChanged(nameof(IsNicknameValid));
                    OnPropertyChanged(nameof(NicknameToolTip));
                    SaveSettings();
                    ReloadAvatar();
                });
                AddLog($"[{DateTime.Now:HH:mm:ss}] Успешная авторизация Microsoft: {session.Username} (UUID: {session.UUID})");
                StatusMessage = $"Вход выполнен: {session.Username}";
                _ = SyncCloudSkinAsync(session.AccessToken);
            }
        }
        catch (OperationCanceledException)
        {
            Dispatcher.UIThread.Post(() =>
            {
                IsDeviceCodeDialogVisible = false;
                StatusMessage = "Авторизация Microsoft отменена.";
            });
            AddLog($"[{DateTime.Now:HH:mm:ss}] Авторизация Microsoft отменена пользователем.");
        }
        catch (Exception ex)
        {
            Dispatcher.UIThread.Post(() =>
            {
                IsDeviceCodeDialogVisible = false;
                StatusMessage = $"Ошибка авторизации: {ex.Message}";
            });
            AddLog($"[{DateTime.Now:HH:mm:ss}] [ERROR] Ошибка Microsoft: {ex.Message}");
        }
        finally
        {
            Dispatcher.UIThread.Post(() =>
            {
                IsBusy = false;
                IsDeviceCodeDialogVisible = false;
            });
        }
    }

    [RelayCommand]
    public async Task LogoutMicrosoftAsync()
    {
        try
        {
            await AuthService.SignoutMicrosoftAsync();
        }
        catch
        {
        }

        Dispatcher.UIThread.Post(() =>
        {
            Settings.IsMicrosoftAuth = false;
            OnPropertyChanged(nameof(IsMicrosoftAuth));
            OnPropertyChanged(nameof(AuthModeBadgeText));
            SaveSettings();
        });
        AddLog($"[{DateTime.Now:HH:mm:ss}] Выход из аккаунта Microsoft. Режим переключен на офлайн.");
        StatusMessage = "Режим переключен на офлайн";
    }

    [RelayCommand]
    public void OpenLogsFolder()
    {
        try
        {
            var logsDir = Path.Combine(Settings.InstallationPath, "logs");
            _ = Directory.CreateDirectory(logsDir);
            _ = Process.Start(new ProcessStartInfo
            {
                FileName = logsDir,
                UseShellExecute = true
            });
        }
        catch
        {
        }
    }

    [RelayCommand]
    public void OpenClientLogFile()
    {
        try
        {
            var logFile = Path.Combine(Settings.InstallationPath, "logs", "launcher-client.log");
            if (File.Exists(logFile))
            {
                _ = Process.Start(new ProcessStartInfo
                {
                    FileName = logFile,
                    UseShellExecute = true
                });
            }
        }
        catch
        {
        }
    }

    public void AddLog(string line)
    {
        Console.WriteLine(line);
        Dispatcher.UIThread.Post(() =>
        {
            if (ConsoleLogs.Count > 1500)
            {
                ConsoleLogs.RemoveAt(0);
            }
            ConsoleLogs.Add(line);
        });
    }

    [RelayCommand]
    public void StopGame()
    {
        if (_gameProcess is { HasExited: false } proc)
        {
            try
            {
                AddLog($"[{DateTime.Now:HH:mm:ss}] Запрос на остановку Minecraft (PID: {proc.Id})...");
                StatusMessage = "Остановка Minecraft...";
                _ = proc.CloseMainWindow();
                _ = Task.Run(async () =>
                {
                    try
                    {
                        using var timeoutCts = new CancellationTokenSource(3000);
                        await proc.WaitForExitAsync(timeoutCts.Token);
                    }
                    catch (OperationCanceledException)
                    {
                        if (!proc.HasExited)
                        {
                            proc.Kill(entireProcessTree: true);
                        }
                    }
                });
            }
            catch (Exception ex)
            {
                AddLog($"[{DateTime.Now:HH:mm:ss}] [ERROR] Не удалось остановить процесс Minecraft: {ex.Message}");
            }
        }
    }

    [RelayCommand]
    public async Task PlayAsync()
    {
        if (IsBusy || IsGameRunning)
        {
            return;
        }

        if (!IsNicknameValid)
        {
            StatusMessage = "Укажите игровой никнейм перед запуском игры!";
            AddLog($"[{DateTime.Now:HH:mm:ss}] [WARN] Ошибка запуска: никнейм игрока не указан.");
            return;
        }

        IsBusy = true;
        IsProgressVisible = true;
        ProgressPercentage = 0;
        StatusMessage = "Подготовка к запуску игры...";

        var progress = new Progress<(string message, double percentage)>(t =>
        {
            StatusMessage = t.message;
            ProgressPercentage = t.percentage;
            AddLog($"[{DateTime.Now:HH:mm:ss}] {t.message}");
        });

        try
        {
            AddLog($"[{DateTime.Now:HH:mm:ss}] Инициализация запуска DevByte Minecraft Fabric {Settings.MinecraftVersion}...");
            AddLog($"[{DateTime.Now:HH:mm:ss}] Папка установки: {Settings.InstallationPath}, RAM: {AllocatedRamGb} ГБ");
            StatusMessage = "Проверка обновлений сборки с сервера...";
            var manifest = await ModSyncService.FetchManifestAsync(Settings.ServerHost, Settings.SyncPort, _cts.Token);
            if (!string.IsNullOrWhiteSpace(manifest.FabricLoaderVersion))
            {
                Settings.FabricVersion = manifest.FabricLoaderVersion;
            }
            if (!string.IsNullOrWhiteSpace(manifest.Version))
            {
                Settings.MinecraftVersion = manifest.Version;
            }
            SaveSettings();

            if (manifest.Files.Count > 0)
            {
                AddLog($"[{DateTime.Now:HH:mm:ss}] [MODS] Получен манифест сервера: {manifest.Files.Count} модов.");
                var syncOk = await ModSyncService.SyncModsAsync(Settings.InstallationPath, manifest, progress, _cts.Token);
                if (!syncOk)
                {
                    StatusMessage = "Не удалось загрузить все моды сборки. Запуск отменен.";
                    AddLog($"[{DateTime.Now:HH:mm:ss}] [ERROR] Запуск отменен: часть модов не загрузилась из-за нестабильности сети. Повторите попытку.");
                    IsBusy = false;
                    IsProgressVisible = false;
                    return;
                }
            }
            else
            {
                var modsDir = Path.Combine(Settings.InstallationPath, "mods");
                var localJars = Directory.Exists(modsDir) ? Directory.GetFiles(modsDir, "*.jar").Length : 0;
                if (localJars < 10)
                {
                    StatusMessage = "Не удалось подключиться к серверу сборки (mc.obxodka.one). Запуск отменен.";
                    AddLog($"[{DateTime.Now:HH:mm:ss}] [ERROR] Сервер сборки недоступен и локальная папка mods пуста ({localJars} модов). Подключитесь к интернету и повторите попытку.");
                    IsBusy = false;
                    IsProgressVisible = false;
                    return;
                }
                AddLog($"[{DateTime.Now:HH:mm:ss}] [MODS] Синхронизация: задействована локальная сборка клиента ({localJars} модов).");
            }

            IsResourcePacksSyncing = true;
            ResourcePacksStatusText = "Синхронизация ресурспаков...";
            AddLog($"[{DateTime.Now:HH:mm:ss}] [RESOURCES] Синхронизация ресурспаков (Fresh Animations, Stay True)...");
            try
            {
                _ = await ModSyncService.SyncResourcePacksAsync(Settings.InstallationPath, manifest, progress, _cts.Token);
                LoadResourcePacksState();
                AddLog($"[{DateTime.Now:HH:mm:ss}] [RESOURCES] Ресурспаки успешно синхронизированы.");
            }
            catch (Exception ex)
            {
                AddLog($"[{DateTime.Now:HH:mm:ss}] [WARN] Пропуск синхронизации ресурспаков: {ex.Message}");
            }
            finally
            {
                IsResourcePacksSyncing = false;
            }

            IsShadersSyncing = true;
            ShadersStatusText = "Синхронизация шейдеров...";
            AddLog($"[{DateTime.Now:HH:mm:ss}] [SHADERS] Синхронизация шейдерпаков...");
            try
            {
                _ = await ModSyncService.SyncShaderPacksAsync(Settings.InstallationPath, manifest, progress, _cts.Token);
                LoadShaderState();
                AddLog($"[{DateTime.Now:HH:mm:ss}] [SHADERS] Шейдерпаки успешно синхронизированы.");
            }
            catch (Exception ex)
            {
                AddLog($"[{DateTime.Now:HH:mm:ss}] [WARN] Пропуск синхронизации шейдеров: {ex.Message}");
            }
            finally
            {
                IsShadersSyncing = false;
            }

            StatusMessage = $"Подготовка к запуску Fabric {Settings.MinecraftVersion}...";
            var process = await MinecraftLaunchService.LaunchGameAsync(Settings, progress, AddLog, _cts.Token);

            _gameProcess = process;
            IsGameRunning = true;
            IsBusy = false;
            IsProgressVisible = false;
            StatusMessage = "Игра запущена! Приятной игры на DevByte Homelab.";
            AddLog($"[{DateTime.Now:HH:mm:ss}] Клиент Minecraft успешно стартовал (PID: {process.Id}).");
            _ = LoadModsListAsync();

            process.EnableRaisingEvents = true;
            process.Exited += (_, _) => Dispatcher.UIThread.Post(() =>
            {
                _gameProcess = null;
                IsGameRunning = false;
                IsBusy = false;
                IsProgressVisible = false;
                StatusMessage = "Minecraft закрыт. Готов к новому запуску.";
                AddLog($"[{DateTime.Now:HH:mm:ss}] [SYSTEM] Minecraft закрыт. Лаунчер готов к игре.");

                if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop &&
                    desktop.MainWindow is { } window)
                {
                    window.Show();
                    if (window.WindowState == WindowState.Minimized)
                    {
                        window.WindowState = WindowState.Normal;
                    }
                    window.Activate();
                    _ = window.Focus();
                }
            });

            if (Settings.CloseOnLaunch)
            {
                AddLog($"[{DateTime.Now:HH:mm:ss}] Скрытие окна лаунчера на время игры...");
                await Task.Delay(1500, _cts.Token);
                Dispatcher.UIThread.Post(() =>
                {
                    if (IsGameRunning && Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
                    {
                        desktop.MainWindow?.Hide();
                    }
                });
            }
        }
        catch (Exception ex)
        {
            IsBusy = false;
            IsGameRunning = false;
            IsProgressVisible = false;
            StatusMessage = $"Ошибка запуска: {ex.Message}";
            AddLog($"[{DateTime.Now:HH:mm:ss}] [ERROR] Сбой запуска: {ex.Message}");
        }
        finally
        {
            if (!IsGameRunning)
            {
                IsBusy = false;
                IsProgressVisible = false;
            }
        }
    }

    [RelayCommand]
    public void OpenShaderpacksFolder()
    {
        try
        {
            var shaderDir = Path.Combine(Settings.InstallationPath, "shaderpacks");
            _ = Directory.CreateDirectory(shaderDir);
            _ = Process.Start(new ProcessStartInfo
            {
                FileName = shaderDir,
                UseShellExecute = true
            });
        }
        catch
        {
        }
    }

    [RelayCommand]
    public async Task CheckForUpdatesAsync()
    {
        try
        {
            Dispatcher.UIThread.Post(() => UpdateStatusText = "Проверка обновлений...");
            var info = await UpdateService.CheckForUpdatesAsync(_cts.Token);
            Dispatcher.UIThread.Post(() =>
            {
                AvailableUpdate = info;
                IsUpdateAvailable = info.IsAvailable;
                if (info.IsAvailable)
                {
                    UpdateStatusText = $"Доступна новая версия: v{info.LatestVersion}";
                    AddLog($"[{DateTime.Now:HH:mm:ss}] [UPDATER] Найдено обновление: v{info.LatestVersion} ({info.ReleaseName})");
                }
                else
                {
                    UpdateStatusText = "У вас установлена последняя версия";
                }
            });
        }
        catch (Exception ex)
        {
            Dispatcher.UIThread.Post(() => UpdateStatusText = "Ошибка при проверке обновлений");
            AddLog($"[{DateTime.Now:HH:mm:ss}] [ERROR] Ошибка проверки обновлений: {ex.Message}");
        }
    }

    [RelayCommand]
    public void DismissUpdate() => IsUpdateAvailable = false;

    [RelayCommand]
    public async Task ApplyUpdateAsync()
    {
        if (AvailableUpdate == null || IsUpdating)
        {
            return;
        }

        try
        {
            IsUpdating = true;
            UpdateStatusText = "Загрузка обновления...";
            AddLog($"[{DateTime.Now:HH:mm:ss}] [UPDATER] Загрузка обновления v{AvailableUpdate.LatestVersion}...");

            var progress = new Progress<double>(p =>
            {
                UpdateProgress = p;
                UpdateStatusText = $"Загрузка обновления: {p:F0}%";
            });

            await UpdateService.ApplyUpdateAsync(AvailableUpdate, progress, _cts.Token);
        }
        catch (Exception ex)
        {
            IsUpdating = false;
            UpdateStatusText = $"Ошибка обновления: {ex.Message}";
            AddLog($"[{DateTime.Now:HH:mm:ss}] [ERROR] Сбой установки обновления: {ex.Message}");
        }
    }

    public void Dispose()
    {
        _cts.Cancel();
        _cts.Dispose();
        _deviceCodeCts?.Cancel();
        _deviceCodeCts?.Dispose();
        _gameProcess?.Dispose();
        GC.SuppressFinalize(this);
    }
}
