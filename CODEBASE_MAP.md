# DevByte Launcher Architecture Map (CODEBASE_MAP.md)

## Обзор архитектуры
DevByte Launcher — клиентский лаунчер для выделенного игрового хоумлаба `DEVBYTE` (Fabric 1.20.1) на базе Avalonia UI / .NET 10 (C#). Обеспечивает синхронизацию сборки модов по HTTP через Beam 2 SSH-туннель (`mc.obxodka.one:25566`), аутентификацию (Offline / Microsoft), мониторинг статуса сервера (`mc.obxodka.one:25565`) и запуск игрового клиента с защитой от переполнения накопителей.

---

## 1. Слой моделей (Models)
- `Models/AppSettings.cs`
  - **Роль**: Настройки приложения и сборки.
  - **Ключевые поля**: `InstallationPath`, `Nickname`, `ServerHost` ("mc.obxodka.one"), `ServerPort` (25565), `SyncPort` (25566), `AllocatedRamMb`, `CustomJavaPath`, `ShadersEnabled`, `SelectedShaderPack`.
  - **SSOT**: Файл конфигурации `%APPDATA%/DevByteLauncher/settings.json` (или `~/.config/DevByteLauncher/settings.json` на Linux).
- `Models/DriveStorageInfo.cs`
  - **Роль**: Информация о смонтированных накопителях и дисках.
  - **Ключевые поля**: `Name`, `VolumeLabel`, `TotalGigabytes`, `FreeGigabytes`, `IsSystemDrive`.
- `Models/ModManifest.cs`
  - **Роль**: Модель манифеста сборки сервера.
  - **Ключевые поля**: `Version`, `FabricLoaderVersion`, `Files`, `ResourcePacks`, `ShaderPacks`, `ModManifestItem` (Name, Path, Url, Size, Sha1).
- `Models/ServerStatusInfo.cs`
  - **Роль**: Состояние Minecraft-сервера для верхнего статус-бара.
  - **Ключевые поля**: `IsOnline`, `PlayerCount`, `MaxPlayers`, `LatencyMs`, `StatusDisplay`.
- `Models/UpdateInfo.cs`
  - **Роль**: Модель проверки обновлений самого лаунчера.

---

## 2. Слой сервисов (Services)
- `Services/SettingsService.cs`
  - **Роль**: Загрузка и сохранение настроек, сканирование накопителей для защиты диска.
  - **Ключевые методы**:
    - `LoadSettings()`: Загружает JSON или генерирует дефолтные настройки.
    - `SaveSettings(AppSettings settings)`: Сериализует настройки в файл.
    - `GetDefaultInstallationPath()`: Определяет безопасный путь (`~/.local/share/DevByteMC` на Linux/macOS, наибольший несистемный диск на Windows).
    - `GetDriveForPath(string path)`: Возвращает `DriveStorageInfo` для заданного пути.
  - **Связи**: Используется в `MainViewModel`.
- `Services/ModSyncService.cs`
  - **Роль**: Высокопроизводительная синхронизация модов, шейдеров и ресурспаков по HTTP.
  - **Ключевые методы**:
    - `FetchManifestAsync(host, port, ct)`: Запрашивает `http://{host}:{port}/manifest.json` (таймаут 15с, 2 попытки).
    - `SyncModsAsync(installationPath, manifest, progress, ct)`: Удаляет запрещенные серверные моды (`c2me`, `chunky`, `easyauth`), чистит лишние jar, скачивает недостающие моды параллельно с проверкой SHA-1 и потоковым отображением МБ.
    - `SyncResourcePacksAsync(...)` & `SyncShaderPacksAsync(...)`: Догружает текстуры и шейдеры.
    - `DownloadWithRetryAsync(...)`: Скачивание с проверкой размера, SHA1, HTTP Range докачкой и потоковым `fileProgress`.
    - `DownloadFileAsync(...)`: Потоковая загрузка чанками по 128 КБ с поддержкой докачки поврежденных/оборванных `.tmp` файлов через заголовок `Range: bytes={len}-`.
  - **Связи**: Вызывается из `MainViewModel.PlayCommand`.
- `Services/MinecraftLaunchService.cs`
  - **Роль**: Сборка аргументов запуска Java/Fabric, проверка библиотек и порождение процесса игры.
- `Services/ServerPingService.cs`
  - **Роль**: Пинг Minecraft-сервера по протоколу Server List Ping (TCP 25565) для отображения онлайна и пинга в UI.
- `Services/JavaService.cs`
  - **Роль**: Обнаружение установленных JRE/JDK 17/21 или автоматическая загрузка Adoptium Temurin.
- `Services/AuthService.cs`
  - **Роль**: Microsoft OAuth авторизация и валидация оффлайн никнеймов.
- `Services/ResourcePackService.cs` & `Services/ShaderService.cs` & `Services/SkinService.cs`
  - **Роль**: Вспомогательные сервисы управления графическими компонентами и скинами игроков.
- `Services/UpdateService.cs`
  - **Роль**: Проверка релизов лаунчера через GitHub API.

---

## 3. Слой представления (ViewModels & Views)
- `ViewModels/MainViewModel.cs`
  - **Роль**: Главный координатор состояния UI, команд и фоновых процессов.
  - **Ключевые свойства**: `StatusMessage`, `ProgressPercentage`, `IsBusy`, `CurrentDriveSummary`, `ServerStatus`, `Logs`.
  - **Ключевые команды**:
    - `PlayCommand`: Запуск синхронизации и игры.
    - `UpdateDriveSummary()`: Форматирует путь к папке установки (`~/.local/share/DevByteMC` на Linux или `C:\Games\DevByteMC` на Windows) и свободное место.
  - **SSOT**: Оркестрирует `AppSettings` и передает путь `Settings.InstallationPath` сервисам синхронизации.
- `Views/MainWindow.axaml` / `MainWindow.axaml.cs`
  - **Роль**: Главное окно приложения в стиле Fluent 2 / Mica Dark.

---

## 4. Векторные потоки данных
- **Запуск игры**: `PlayCommand` -> `ModSyncService.FetchManifestAsync` -> `ModSyncService.SyncModsAsync` (с Range и байтовым прогрессом) -> `Resource/Shader Sync` -> `MinecraftLaunchService.LaunchAsync`.
- **Фоновый опрос**: Таймер 15с -> `ServerPingService.PingServerAsync` -> `ServerStatus`.
- **Серверный HTTP демон**: DEVBYTE `192.168.1.3:25566` (`sync_server.py`, таймаут 300с, HTTP 206 Partial Content / `Range: bytes`, буфер 256 КБ).
