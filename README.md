# WinForge

**WinForge** is a 100% native **WinUI 3 (Windows App SDK)** PowerTools Suite unifying package management, feature velocity toggles, and system optimization into a single modern desktop experience.

Built with .NET 9, Windows App SDK 2.3.1, and CommunityToolkit.Mvvm with full Windows 11 Fluent Design (Mica backdrop, dark/light theme switching, and custom titlebar).

---

## Capabilities

1. **Winget Package Store (`HomePage`, `InstalledPage`, `UpdatesPage`)**
   - Browse curated and popular Windows software.
   - One-click install, batch updates, and uninstallation via the Windows Package Manager (`winget`).
2. **Feature Velocity Manager (`FeaturesPage`)**
   - Discover and unlock experimental Windows 11 features and velocity flags.
   - Built-in feature definitions and integrated `ViVeTool` execution runner.
3. **System Optimizer (`OptimizerPage`)**
   - Telemetry debloating, diagnostics disabling, and privacy tuning.
   - Disk and system cache cleaner.
   - Standby list and RAM working set memory flusher.
   - System privacy and health audit with full restore/rollback support.

---

## Solution Structure

```text
WinForge/
├── src/
│   └── WinForge/               # 100% Native WinUI 3 Application
│       ├── Controls/           # Custom Fluent controls
│       ├── Models/             # App, Winget, and ViVeTool models
│       ├── Pages/              # Discover, Installed, Updates, Features, Optimizer, Settings
│       ├── Services/           # WingetService, ViVeToolRunner, SettingsService
│       └── WinForge.csproj
├── tools/
│   └── cli/                    # ViVeTool binary and PowerShell optimization scripts
│       ├── ViVeTool.exe
│       ├── ViVeToolEnabler.psm1
│       └── optimizer/          # Core tuning and debloating engine
└── WinForge.sln
```

## Building
```powershell
dotnet build WinForge.sln -p:Platform=x64
```
