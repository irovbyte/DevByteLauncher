# DevByteLauncher

<div align="center">
  <img src="Assets/app-icon.png" alt="DevByte Launcher Logo" width="128" height="128" />
  <h1>DevByte Launcher</h1>
  <p><strong>Современный высокопроизводительный клиент и лаунчер Minecraft Fabric 1.21.1 для DevByte Homelab</strong></p>

  <p>
    <a href="https://github.com/irovbyte/DevByteLauncher/releases/latest"><img src="https://img.shields.io/github/v/release/irovbyte/DevByteLauncher?color=60cdff&label=Версия&style=flat-square" alt="GitHub Release" /></a>
    <a href="https://dotnet.microsoft.com/"><img src="https://img.shields.io/badge/.NET-10.0-512bd4?style=flat-square" alt=".NET 10" /></a>
    <a href="https://avaloniaui.net/"><img src="https://img.shields.io/badge/Avalonia%20UI-12.1.4-cc292b?style=flat-square" alt="Avalonia UI" /></a>
    <a href="https://fluent2.microsoft.design/"><img src="https://img.shields.io/badge/Design-Microsoft%20Fluent%202-0078d4?style=flat-square" alt="Microsoft Fluent 2" /></a>
    <a href="LICENSE"><img src="https://img.shields.io/badge/License-MIT-green?style=flat-square" alt="MIT License" /></a>
  </p>
</div>

---

## О проекте

**DevByteLauncher** — специализированный игровой клиент и лаунчер для выделенного игрового сервера **DevByte Homelab** (`mc.obxodka.one`). Построен на базе передового стека технологий **.NET 10** и **Avalonia UI 12.1.4** с полным соблюдением стандартов дизайна **Microsoft Fluent 2 (Windows 11 Dark)**.

Лаунчер обеспечивает бесшовный запуск **Minecraft Fabric 1.21.1**, автоматическую фоновую синхронизацию серверных модификаций, встроенную графическую систему с поддержкой шейдеров (Iris + Sodium), мониторинг статуса хомелаба и автоматическое обновление приложения через GitHub Releases.

---

## Ключевые возможности

### 🎨 Интерфейс Microsoft Fluent 2
- Эстетика Windows 11 Dark с полупрозрачными акриловыми слоями, закруглениями 8px/12px и акцентной подсветкой.
- Плавные кинематические переходы между вкладками (`PageSlide` + `CrossFade`).
- Интерактивные статусные бейджи сервера и пинга в заголовке окна с защитой от наложения на системные кнопки окна.
- Адаптивный скроллинг без визуального шума полос прокрутки.

### ⚡ Производительность и движок запуска
- Сборка на **.NET 10** с компиляцией в единый исполняемый файл (`single-file win-x64`).
- Оптимизированные флаги виртуальной машины Java Aikar G1GC (`-XX:+AlwaysPreTouch`, `-XX:+UnlockExperimentalVMOptions`, `-XX:MaxGCPauseMillis=200`).
- Опция автоматического закрытия лаунчера при успешном старте игры для полного освобождения оперативной памяти и ресурсов процессора.
- Возможность выбора диска и директории установки для защиты системного диска `C:`.

### 🌟 Шейдеры и графика (Iris + Sodium)
- Предустановленные шейдерпаки: **Complementary Reimagined** и **BSL Shaders**.
- Быстрый переключатель шейдеров в лаунчере с двусторонней синхронизацией конфигурации `config/iris.properties` (в игре клавиша `K`).
- Автоматическая валидация и загрузка библиотек графического рендеринга `Iris` и `Sodium` для Fabric 1.21.1.

### 🔄 Автообновление через GitHub Releases
- Фоновая проверка свежих релизов через GitHub REST API.
- Атомарная замена исполняемого файла на лету с автоматической перезагрузкой.
- Автоматическая очистка временных резервных копий предыдущих сборок.

### 🌐 Интеграция с DevByte Homelab
- Прямой пинг сервера `mc.obxodka.one:25565` с отображением онлайна, пинга и списка игроков.
- Фоновый сервис синхронизации модов (`:25566`) с локальным кешированием и сверкой контрольных сумм.
- Поддержка офлайн-аккаунтов и лицензионной авторизации через Microsoft Store.
- Генерация и рендеринг скинов и аватарок персонажей с использованием SkiaSharp.

---

## Системные требования

| Компонент | Минимальные требования | Рекомендуемые |
|---|---|---|
| **Операционная система** | Windows 10 x64 (версия 1809+) | Windows 11 x64 (версия 23H2+) |
| **Среда выполнения** | Встроена в Single-File Exe | Встроена в Single-File Exe |
| **Java Runtime** | Java 21 LTS x64 (Temurin / Oracle) | Eclipse Temurin JDK 21+ x64 |
| **Оперативная память** | 6 ГБ ОЗУ (выделение лаунчеру от 4 ГБ) | 16 ГБ ОЗУ (выделение лаунчеру 8-10 ГБ) |
| **Видеокарта** | Поддержка OpenGL 4.5 | Дискретный GPU с поддержкой шейдеров Iris |

---

## Быстрый старт

### Скачивание готового релиза
1. Перейдите в раздел [Releases](https://github.com/irovbyte/DevByteLauncher/releases/latest).
2. Скачайте файл `DevByteLauncher.exe`.
3. Поместите его в удобную папку (например, на рабочий стол) и запустите.
4. В разделе **Параметры** укажите желаемый никнейм и диск установки.
5. Нажмите **Играть**.

---

## Сборка из исходного кода

### Необходимые инструменты
- [.NET 10.0 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)
- Git CLI

### Клонирование и сборка
```powershell
git clone https://github.com/irovbyte/DevByteLauncher.git
cd DevByteLauncher
dotnet build -c Release
```

### Публикация единого исполняемого файла
```powershell
dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o ./publish
```

### Режим самодиагностики (Smoke Test)
```powershell
./publish/DevByteLauncher.exe --smoke-test
```

---

## Структура репозитория

```
DevByteLauncher/
├── Assets/                # Иконки приложения, векторные SVG и маскот
├── Models/                # Модели данных настроек, модов, накопителей и обновлений
├── Services/              # Сервисы пинга, шейдеров, синхронизации, скинов и запуска
├── ViewModels/            # Реактивные ViewModel (CommunityToolkit.Mvvm)
├── Views/                 # Окна и XAML разметка (Fluent 2 Dark)
├── .github/workflows/     # CI/CD пайплайны GitHub Actions
├── App.axaml              # Стили и темы Avalonia
├── LICENSE                # Лицензия MIT
└── README.md              # Документация проекта
```

---

## Лицензия

Проект распространяется под открытой лицензией [MIT](LICENSE).

Автор: **Роман Зопиров ([irovbyte](https://github.com/irovbyte))**
