# WingetStore 🛍️

[![License](https://img.shields.io/badge/license-MIT-blue.svg)](LICENSE)
[![Platform](https://img.shields.io/badge/platform-Windows%2010%20%7C%20Windows%2011-blue)](https://microsoft.com/windows)
[![Framework](https://img.shields.io/badge/framework-WinUI%203%20%7C%20.NET%2010-512bd4)](https://learn.microsoft.com/windows/apps/winui/winui3/)
[![Build Status](https://img.shields.io/badge/tests-600%2B%20passing-brightgreen)](WingetStore.Tests)

**WingetStore** is a modern, fast, and intuitive Windows 11 desktop GUI for the **Windows Package Manager (`winget`)**. Built from the ground up using **WinUI 3**, **C#**, and **.NET 10**, WingetStore provides a seamless Fluent Design interface to discover, install, update, and manage applications on Windows.

---

## 📸 Screenshots & Visual Interface

<p align="center">
  <img src="Assets/SplashScreen.scale-200.png" alt="WingetStore Splash Banner" width="700" />
</p>

> *Designed with Fluent Design system principles, featuring acrylic materials, smooth animations, native dark/light mode switching, and accessible keyboard navigation.*

---

## ✨ Features

- 🔍 **App Discovery & Search**
  - Instant searching across the full Windows Package Manager catalog.
  - Curated categories (*Developer Tools*, *Productivity*, *Utilities*, *Media & Entertainment*, *Security*).
  - Highlighted recommendations and popular packages.

- 📦 **Installed Applications Management**
  - View all installed packages on your system with rich metadata.
  - Filter and search through installed software instantly.
  - One-click uninstallation with real-time feedback.

- 🔄 **Updates & Batch Upgrades**
  - Automatic detection of outdated applications.
  - Individual package upgrades or one-click **Upgrade All** batch operations.
  - Real-time progress indicators and status logging.

- ℹ️ **Comprehensive Package Details**
  - Detailed package page exposing version history, publisher information, license type, installer details, and release notes.
  - Direct links to project homepages and source manifests.
  - Copyable `winget` command snippets for terminal users.

- ⚙️ **Customization & Configuration**
  - Theme customization (System default, Light Mode, Dark Mode).
  - Configurable `winget` CLI path and caching mechanisms.
  - Custom notification preferences and automatic update check toggles.

- 🛡️ **Graceful Fallback & Diagnostics**
  - Built-in detection for system `winget.exe` availability.
  - Helpful setup guidance screen if Windows Package Manager is missing or disabled.

---

## 🛠️ Architecture & Tech Stack

WingetStore follows the **Model-View-ViewModel (MVVM)** architectural pattern for clean separation of concerns:

- **UI Layer (Views & Controls)**: Built with **WinUI 3 (Windows App SDK)** and XAML.
- **ViewModel Layer**: Exposes observable properties, relay commands, and asynchronous task management (`CommunityToolkit.Mvvm`).
- **Services Layer**: Thread-safe services handling process execution (`CliProcessRunner`), output parsing (`WingetParser`), caching (`CachingWingetService`), icons (`IconService`), and app settings (`SettingsService`).
- **Target Framework**: `.NET 10.0` (`net10.0-windows10.0.26100.0`).

```
WingetStore/
├── Assets/             # App icons, splash screens, and category JSON manifests
├── Controls/           # Reusable UI controls (ResponsivePageContainer, PackageProgressControl)
├── Models/             # Data models (Package, Category, UpdateInfo, AppSettings)
├── Pages/              # WinUI 3 XAML views (HomePage, InstalledPage, UpdatesPage, DetailsPage, SettingsPage, AboutPage, NoWingetPage)
├── ViewModels/         # MVVM view models (HomeViewModel, InstalledViewModel, UpdatesViewModel, etc.)
├── Services/           # Services (WingetService, IconService, SettingsService, LogService)
├── Testing/            # WinUI integration test runner harness
└── WingetStore.Tests/  # Comprehensive xUnit unit testing suite
```

---

## 🚀 Getting Started

### System Requirements

- **OS**: Windows 10 (version 1809 / build 17763 or higher) or Windows 11
- **Runtime**: [.NET 10.0 SDK](https://dotnet.microsoft.com/download/dotnet/10.0) or later
- **CLI Dependency**: [Windows Package Manager (`winget`)](https://learn.microsoft.com/windows/package-manager/winget/) installed on system PATH

### Building from Source

1. **Clone the repository:**
   ```bash
   git clone https://github.com/your-org/WingetStore.git
   cd WingetStore
   ```

2. **Restore dependencies & build:**
   ```bash
   dotnet restore WingetStore.sln
   dotnet build WingetStore.csproj --configuration Release
   ```

3. **Run the application:**
   ```bash
   dotnet run --project WingetStore.csproj
   ```
   Or launch the generated executable in `bin/Release/net10.0-windows10.0.26100.0/win-x64/WingetStore.exe`.

---

## 🧪 Testing Strategy

WingetStore maintains rigorous quality standards verified by a dual test suite:

### 1. Unit Tests (xUnit)
Over 600 unit tests cover business logic, service contracts, data parsing, caching, and state management.

To execute the unit test suite:
```bash
dotnet test WingetStore.Tests/WingetStore.Tests.csproj --filter "FullyQualifiedName!~WinUIPageCreationTests"
```

### 2. UI Integration Tests
WinUI-bound controls and page creation tests require the WinUI runtime message pump. Run these via the embedded integration test harness:

```bash
bin/Debug/net10.0-windows10.0.26100.0/win-x64/WingetStore.exe --run-ui-tests
```

---

## 🤝 Contributing Guidelines

We welcome contributions from the community! To contribute:

1. **Fork & Branch**: Create a feature branch off `main` (`git checkout -b feature/amazing-feature`).
2. **Follow Coding Standards**:
   - Use standard C# coding conventions and Async/Await best practices (avoid `async void` delegates).
   - Enforce thread-safety in services.
   - Maintain 16 DIP container margins and Fluent Design UI guidelines.
3. **Add Tests**: Include xUnit unit tests for any new service logic or ViewModels in `WingetStore.Tests`.
4. **Verify**: Ensure all unit and integration tests pass before submitting your PR.
5. **Open a Pull Request**: Provide a detailed summary of changes and reference relevant issue numbers.

---

## 📄 License

This project is licensed under the [MIT License](LICENSE).

---

## 💖 Acknowledgments

- [Windows Package Manager (`winget-cli`)](https://github.com/microsoft/winget-cli) by Microsoft.
- [WinUI 3 / Windows App SDK](https://github.com/microsoft/microsoft-ui-xaml) for modern Windows UI controls.
- Community contributors and package maintainers.
