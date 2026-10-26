namespace DevByteLauncher.ViewModels;

public partial class MainViewModel : ViewModelBase, IDisposable
{
    private readonly CancellationTokenSource _cts = new();

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
    public partial bool IsBusy { get; set; }

    public bool IsNicknameValid => !string.IsNullOrWhiteSpace(Nickname);

    public bool CanPlay => !IsBusy && IsNicknameValid;

    public string DisplayNameText => string.IsNullOrWhiteSpace(Nickname) ? "Игрок не указан" : Nickname;

    public string NicknameBorderBrush => IsNicknameValid ? "#3a3a3a" : "#f87171";

    public string PlayButtonToolTip => !IsNicknameValid
        ? "Введите никнейм перед запуском игры"
        : (IsBusy ? "Выполняется запуск..." : "Запустить Minecraft Fabric 1.21.1");

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
    public partial string SelectedShaderPack { get; set; } = "ComplementaryReimagined_r5.9.3.zip";

    [ObservableProperty]
    public partial bool CloseOnLaunch { get; set; } = true;

    public static string CurrentVersionText => $"v{UpdateService.CurrentVersion}";

    public ObservableCollection<DriveStorageInfo> Drives { get; } = [];
    public ObservableCollection<ModItemViewModel> Mods { get; } = [];
    public ObservableCollection<string> ConsoleLogs { get; } = [];
    public ObservableCollection<string> AvailableShaderPacks { get; } = [];

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
            Settings.Nickname = value ?? string.Empty;
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsNicknameValid));
            OnPropertyChanged(nameof(CanPlay));
            OnPropertyChanged(nameof(DisplayNameText));
            OnPropertyChanged(nameof(NicknameBorderBrush));
            OnPropertyChanged(nameof(PlayButtonToolTip));
            SaveSettings();
        }
    }

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
        AddLog($"[{DateTime.Now:HH:mm:ss}] [SYSTEM] DevByte Launcher v{UpdateService.CurrentVersion} инициализирован.");
        AddLog($"[{DateTime.Now:HH:mm:ss}] [SYSTEM] Директория установки: {Settings.InstallationPath}");
        AddLog($"[{DateTime.Now:HH:mm:ss}] [SYSTEM] Сервер: {Settings.ServerHost}:{Settings.ServerPort} (Порт синхронизации: {Settings.SyncPort})");
        AddLog($"[{DateTime.Now:HH:mm:ss}] [SYSTEM] Выделено памяти: {AllocatedRamGb} ГБ, Режим: {AuthModeBadgeText}");
        AddLog($"[{DateTime.Now:HH:mm:ss}] [GRAPHICS] Шейдеры: {(ShadersEnabled ? $"Включены ({SelectedShaderPack})" : "Выключены (Iris + Sodium готовы)")}");
        _ = RefreshServerStatusAsync();
        _ = LoadModsListAsync();
        _ = StartPeriodicPingLoopAsync();
        _ = CheckForUpdatesAsync();
    }

    public void LoadShaderState()
    {
        var (isEnabled, selectedPack) = ShaderService.LoadShaderState(Settings.InstallationPath);
        ShadersEnabled = isEnabled;
        SelectedShaderPack = selectedPack;
        AvailableShaderPacks.Clear();
        foreach (var pack in ShaderService.GetAvailableShaderPacks(Settings.InstallationPath))
        {
            AvailableShaderPacks.Add(pack);
        }
    }

    partial void OnShadersEnabledChanged(bool value)
    {
        Settings.ShadersEnabled = value;
        ShaderService.SaveShaderState(Settings.InstallationPath, value, SelectedShaderPack);
        SaveSettings();
    }

    partial void OnSelectedShaderPackChanged(string value)
    {
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
        CurrentDriveSummary = info != null ? $"{info.Name} ({info.FreeGigabytes:F1} ГБ свободно из {info.TotalGigabytes:F1} ГБ)" : "Путь не указан";
    }

    public void SaveSettings() => SettingsService.SaveSettings(Settings);

    [RelayCommand]
    public async Task LoadModsListAsync()
    {
        try
        {
            var manifest = await ModSyncService.FetchManifestAsync(Settings.ServerHost, Settings.SyncPort);
            var modsDir = Path.Combine(Settings.InstallationPath, "mods");
            _ = Directory.CreateDirectory(modsDir);

            var items = new List<ModItemViewModel>();
            foreach (var item in manifest.Files)
            {
                var filePath = Path.Combine(modsDir, Path.GetFileName(item.Path));
                var disabledPath = filePath + ".disabled";
                var isInstalled = File.Exists(filePath) || File.Exists(disabledPath);
                var isEnabled = File.Exists(filePath);
                var sizeMb = item.Size > 0 ? $"{(double)item.Size / (1024 * 1024):F2} МБ" : "—";

                items.Add(new ModItemViewModel
                {
                    Name = item.Name,
                    FileName = Path.GetFileName(item.Path),
                    FilePath = isEnabled ? filePath : disabledPath,
                    SizeFormatted = sizeMb,
                    IsInstalled = isInstalled,
                    IsEnabled = isEnabled,
                    Status = isInstalled ? (isEnabled ? "Включен" : "Отключен") : "Будет загружен"
                });
            }

            Dispatcher.UIThread.Post(() =>
            {
                Mods.Clear();
                foreach (var mod in items)
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

    [RelayCommand]
    public async Task LoginMicrosoftAsync()
    {
        try
        {
            IsBusy = true;
            StatusMessage = "Ожидание авторизации в Microsoft...";
            AddLog($"[{DateTime.Now:HH:mm:ss}] Запуск интерактивной авторизации Microsoft...");

            var session = await AuthService.LoginMicrosoftInteractiveAsync();
            if (!string.IsNullOrWhiteSpace(session.Username))
            {
                Dispatcher.UIThread.Post(() =>
                {
                    Settings.Nickname = session.Username;
                    Settings.IsMicrosoftAuth = true;
                    Nickname = session.Username;
                    OnPropertyChanged(nameof(IsMicrosoftAuth));
                    OnPropertyChanged(nameof(AuthModeBadgeText));
                    OnPropertyChanged(nameof(CanPlay));
                    OnPropertyChanged(nameof(IsNicknameValid));
                    SaveSettings();
                });
                AddLog($"[{DateTime.Now:HH:mm:ss}] Успешная авторизация Microsoft: {session.Username} (UUID: {session.UUID})");
                StatusMessage = $"Вход выполнен: {session.Username}";
            }
        }
        catch (OperationCanceledException)
        {
            StatusMessage = "Авторизация Microsoft отменена.";
            AddLog($"[{DateTime.Now:HH:mm:ss}] Авторизация Microsoft отменена пользователем.");
        }
        catch (Exception ex)
        {
            StatusMessage = $"Ошибка авторизации: {ex.Message}";
            AddLog($"[{DateTime.Now:HH:mm:ss}] [ERROR] Ошибка Microsoft: {ex.Message}");
        }
        finally
        {
            Dispatcher.UIThread.Post(() => IsBusy = false);
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
    public async Task PlayAsync()
    {
        if (IsBusy)
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
            AddLog($"[{DateTime.Now:HH:mm:ss}] Инициализация запуска DevByte Minecraft Fabric 1.21.1...");
            AddLog($"[{DateTime.Now:HH:mm:ss}] Папка установки: {Settings.InstallationPath}, RAM: {AllocatedRamGb} ГБ");
            StatusMessage = "Проверка обновлений модов с сервера...";
            var manifest = await ModSyncService.FetchManifestAsync(Settings.ServerHost, Settings.SyncPort);
            AddLog($"[{DateTime.Now:HH:mm:ss}] Получен манифест сервера: {manifest.Files.Count} модов.");
            _ = await ModSyncService.SyncModsAsync(Settings.InstallationPath, manifest, progress);

            StatusMessage = "Подготовка к запуску Fabric 1.21.1...";
            _ = await MinecraftLaunchService.LaunchGameAsync(Settings, progress, AddLog, _cts.Token);

            StatusMessage = "Игра запущена! Приятной игры на DevByte Homelab.";
            AddLog($"[{DateTime.Now:HH:mm:ss}] Клиент Minecraft успешно стартовал.");
            await LoadModsListAsync();

            if (Settings.CloseOnLaunch)
            {
                AddLog($"[{DateTime.Now:HH:mm:ss}] Авто-закрытие лаунчера при запуске игры...");
                await Task.Delay(1500, _cts.Token);
                Dispatcher.UIThread.Post(() =>
                {
                    if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
                    {
                        desktop.Shutdown();
                    }
                    else
                    {
                        Environment.Exit(0);
                    }
                });
            }
        }
        catch (Exception ex)
        {
            StatusMessage = $"Ошибка запуска: {ex.Message}";
            AddLog($"[{DateTime.Now:HH:mm:ss}] [ERROR] Сбой запуска: {ex.Message}");
        }
        finally
        {
            IsBusy = false;
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
        GC.SuppressFinalize(this);
    }
}
