# WinForge

[![CI](https://github.com/dismount8645/WinForge/actions/workflows/ci.yml/badge.svg)](https://github.com/dismount8645/WinForge/actions/workflows/ci.yml)
[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](LICENSE)
[![WinUI 3](https://img.shields.io/badge/WinUI%203-Windows%20App%20SDK%202.3-blue.svg)](https://learn.microsoft.com/en-us/windows/apps/winui/winui3/)
[![.NET 9](https://img.shields.io/badge/.NET-9.0-purple.svg)](https://dotnet.microsoft.com/)

**WinForge** — 100% native **WinUI 3 (Windows App SDK)** PowerTools Suite unifying package management, feature velocity, and system optimization.

## Features

- **Winget Package Store:** Browse curated software, one-click install, batch updates, uninstall via \winget\
- **Feature Velocity Manager:** Discover, enable/disable Windows 11 velocity flags (integrated ViVeTool runner + offline catalog)
- **System Optimizer:** Telemetry debloat, cache/disk cleaner, RAM working-set flusher, privacy audit with restore

## Tech Stack

- **Language:** C# 12, .NET 9
- **UI:** WinUI 3, Windows App SDK 2.3.1, CommunityToolkit.Mvvm, Mica backdrop, Fluent Design
- **Tools:** ViVeTool, winget, PowerShell optimizer scripts in \	ools/cli\

## Project Structure

``text
WinForge/
├── README.md
├── LICENSE
├── .github/
├── WinForge.sln
├── src/
│   └── WinForge/             # WinUI 3 app (Pages: Home, Installed, Updates, Features, Optimizer, Settings)
└── tools/
    └── cli/                  # ViVeTool + optimizer scripts (FeatureCatalog.json, ViVeTool.exe, etc.)
``

## Quick Start

``bash
git clone https://github.com/dismount8645/WinForge.git
cd WinForge
dotnet build WinForge.sln -p:Platform=x64 -c Release
# Run from Visual Studio or:
dotnet run --project src/WinForge --framework net9.0-windows10.0.26100.0
``

## Usage

Launch the app — use sidebar navigation: **Discover** (store), **Installed**, **Updates**, **Features** (velocity), **Optimizer**, **Settings**.

## Development

- Requires Visual Studio 2022 17.8+ with Windows App SDK
- Build: \dotnet build\ — 0 warnings with \TreatWarningsAsErrors\

## Contributing

See [CONTRIBUTING.md](CONTRIBUTING.md).

## License

[MIT](LICENSE) © 2026 Jacob Krarup Madsen (dismount8645)
