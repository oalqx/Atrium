# Atrium

**Atrium** is an open-source, native Windows gaming hub and graphics optimizer built with C#, .NET 10, and WinUI 3. It unifies games across **Steam, Epic Games, GOG, Xbox / Windows Store, and custom local directories** into a fast, beautiful library with intelligent GPU power management.

---

## Features

- **All Your Games in One Place:** Seamlessly aggregates libraries across Steam, Epic Games Launcher, GOG Galaxy, Xbox app / Microsoft Store, and standalone executables.
- **Smart Single-Tile Presentation:** Avoids clutter by displaying a single cover tile for games owned across multiple storefronts, with storefront badges and customizable preferred launcher order (Steam by default).
- **Zero API Keys Required:** Native file and manifest inspection discovers installed games immediately. Optional in-app browser sign-in connects full digital libraries without exposing API keys or account credentials.
- **Privacy First:** All data is processed and stored locally in an SQLite database on your machine (`atrium.db`). No telemetry, no third-party cloud servers.
- **DirectX GPU Performance Enforcement:** Automatically detects multi-GPU setups (e.g. NVIDIA GeForce RTX + AMD Radeon iGPU) and allows users to set high-performance GPU execution per game directly via Windows DirectX graphics preferences with one-click revert.
- **Native Fluent Design:** Built for Windows 11 with Mica material, smooth grid virtualization, keyboard navigation, and automatic Light/Dark system theme synchronization.

---

## Building from Source

### Prerequisites
- Windows 10 (version 2004+) or Windows 11
- [.NET 10 SDK](https://dotnet.microsoft.com/)
- Visual Studio 2022 / 2026 with the *.NET Desktop Development* workload or Windows App SDK

### Build & Test
```powershell
# Clone repository
git clone https://github.com/AlexGuadagno/Atrium.git
cd Atrium

# Build solution
dotnet build Atrium.slnx -c Release

# Run automated tests
dotnet test Atrium.slnx -c Release
```

---

## License

This project is licensed under the [MIT License](LICENSE).
