# DevByte Launcher Architecture Map (CODEBASE_MAP.md)

## Обзор архитектуры
DevByte Launcher — клиентский лаунчер для выделенного игрового хоумлаба `DEVBYTE` (Fabric 1.20.1) на базе Avalonia UI / .NET 10 (C#). Обеспечивает синхронизацию сборки модов, шейдеров и серверных конфигураций/раскладки клавиш по HTTP через Beam 2 SSH-туннель (`mc.obxodka.one:25566`), аутентификацию (Offline / Microsoft), мониторинг статуса сервера (`mc.obxodka.one:25565`) и запуск игрового клиента с защитой от переполнения накопителей.

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
  - **Ключевые поля**: `Version`, `FabricLoaderVersion`, `Files`, `ResourcePacks`, `ShaderPacks`, `Configs` (централизованные конфигурации с сервера), `ModManifestItem` (Name, Path, Url, Size, Sha1).
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
  - **Роль**: Высокопроизводительная синхронизация модов, шейдеров, ресурспаков и конфигураций по HTTP.
  - **Ключевые методы**:
    - `FetchManifestAsync(host, port, ct)`: Запрашивает `http://{host}:{port}/manifest.json` (таймаут 15с, 2 попытки).
    - `SyncModsAsync(installationPath, manifest, progress, ct)`: Удаляет запрещенные серверные моды (`c2me`, `chunky`, `easyauth`), чистит лишние jar, скачивает недостающие моды параллельно с проверкой SHA-1 и потоковым отображением МБ.
    - `SyncResourcePacksAsync(...)` & `SyncShaderPacksAsync(...)`: Догружает текстуры и шейдеры.
    - `SyncConfigsAsync(installationPath, manifest, progress, ct)`: Синхронизирует серверные конфигурации и раскладку клавиш (`config/defaultoptions/keybindings.txt`).
    - `ApplyKeybindingsToOptions(installationPath, keybindingsFilePath)`: Сливает эталонную раскладку сервера в клиентский `options.txt` (зум на C, атака на ЛКМ).
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
- `Services/ResourcePackService.cs` & `Services/ShaderService.cs`
  - **Роль**: Вспомогательные сервисы управления ресурспаками и шейдерами сборки.
- `Services/SkinService.cs`
  - **Роль**: Декодирование скинов игроков через SkiaSharp для UI-аватара, загрузка скинов на сервер DevByte и авто-генерация конфигурации CustomSkinLoader.
  - **Ключевые методы**:
    - `LoadAvatarHead(skinPath, targetSize)`: Извлекает лицо и шлем из PNG-текстуры для отображения в Fluent 2 интерфейсе.
    - `UploadSkinToServerAsync(host, port, nickname, skinFilePath, ct)`: Отправляет PNG-текстуру по HTTP `POST /api/skin/upload?nickname={nickname}` на сервер сборки.
    - `EnsureCustomSkinLoaderConfig(installationPath, host, port)`: Автоматически формирует `CustomSkinLoader/CustomSkinLoader.json` и `ExtraList/devbyte.json` с цепочкой провайдеров DevByte CustomSkinAPI -> MojangAPI -> ElyByAPI.
- `Services/UpdateService.cs`
  - **Роль**: Проверка релизов лаунчера через GitHub API.

---

## 3. Слой представления (ViewModels & Views)
- `ViewModels/MainViewModel.cs`
  - **Роль**: Главный координатор состояния UI, команд и фоновых процессов.
  - **Ключевые свойства**: `StatusMessage`, `ProgressPercentage`, `IsBusy`, `CurrentDriveSummary`, `ServerStatus`, `Logs`.
  - **Ключевые команды и методы**:
    - `PlayCommand`: Запуск синхронизации модов/шейдеров/конфигов, обеспечение CSL-конфигурации, фоновая выгрузка скина оффлайн-игрока и запуск игры.
    - `ChangeSkinAsync(newSkinFilePath)`: Сохраняет скин локально, обновляет аватар, публикует в облако Microsoft (для лицензий) и выгружает на DevByte Server (для оффлайн-игроков через CSL).
    - `UpdateDriveSummary()`: Форматирует путь к папке установки (`~/.local/share/DevByteMC` на Linux или `C:\Games\DevByteMC` на Windows) и свободное место.
  - **SSOT**: Оркестрирует `AppSettings` и передает путь `Settings.InstallationPath` сервисам синхронизации.
- `Views/MainWindow.axaml` / `MainWindow.axaml.cs`
  - **Роль**: Главное окно приложения в стиле Fluent 2 / Mica Dark.

---

## 4. Векторные потоки данных
- **Запуск игры**: `PlayCommand` -> `ModSyncService.FetchManifestAsync` -> `ModSyncService.SyncModsAsync` -> `Resource/Shader Sync` -> `Config/Keybind Sync` -> `SkinService.EnsureCustomSkinLoaderConfig` -> `MinecraftLaunchService.LaunchAsync`.
- **Синхронизация скинов (Skin Pipeline)**:
  - Лицензия (Microsoft): `ChangeSkinAsync` -> `AuthService.UploadSkinToCloudAsync` -> Mojang Sessions.
  - Оффлайн (Milano и др.): `ChangeSkinAsync` / `PlayAsync` -> `SkinService.UploadSkinToServerAsync` -> HTTP POST `/api/skin/upload` (`sync_server.py`) -> Сохранение в `/home/irovbyte/minecraft/skins/{nickname}.png`.
  - В игре: Клиентский мод `CustomSkinLoader` (синхронизируется лаунчером) запрашивает `http://mc.obxodka.one:25566/csl/{player}.json` -> мгновенно отображает скин оффлайн-игрока для всех участников; для лицензий фолбэчится на `MojangAPI`.
- **Фоновый опрос**: Таймер 15с -> `ServerPingService.PingServerAsync` -> `ServerStatus`.
- **Серверный HTTP демон**: DEVBYTE `192.168.1.3:25566` (`sync_server.py`, таймаут 300с, HTTP 206 Partial Content / `Range: bytes`, раздача модов, ресурспаков, шейдеров, `configs/defaultoptions/keybindings.txt`, эндпоинты `/csl/{player}.json`, `/skins/{file}`, `POST /api/skin/upload`).
