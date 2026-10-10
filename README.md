# Atrium

**Atrium** is an open-source, native Windows gaming hub and graphics optimizer built with C#, .NET 10, and WinUI 3. It unifies games across **Steam, Epic Games, GOG, Xbox / Windows Store, and custom local directories** into a fast, beautiful library with intelligent GPU power management.

---

## Features

- **All Your Games in One Place:** Seamlessly aggregates libraries across Steam, Epic Games Launcher, GOG Galaxy, Xbox app / Microsoft Store, and standalone executables.
- **Smart Single-Tile Presentation:** Avoids clutter by displaying a single cover tile for games owned across multiple storefronts, with storefront badges and customizable preferred launcher order (Steam by default).
- **Zero API Keys Required:** Native file and manifest inspection discovers installed games. GOG Galaxy's local cache can also import owned games from connected stores.
- **Privacy First:** All data is processed and stored locally in an SQLite database on your machine (`atrium.db`). No telemetry, no third-party cloud servers.
- **DirectX GPU Performance Enforcement:** Automatically detects multi-GPU setups (e.g. NVIDIA GeForce RTX + AMD Radeon iGPU) and allows users to set high-performance GPU execution per game directly via Windows DirectX graphics preferences with one-click revert.
- **Native Fluent Design:** Built for Windows 11 with Mica material, smooth grid virtualization, keyboard navigation, and automatic Light/Dark system theme synchronization.
- **Library Controls:** Search, favorites, installed/store/hidden filters, and recent-play sorting. Game details let you save a launcher preference for each game and hide or restore entries. Failed launches and saves show actionable feedback.
- **Sync Progress and Cancellation:** See which launcher is being scanned, cancel an in-progress sync, and inspect individual provider errors. Cancellation before the save commits rolls back the batch and keeps the previous library.
- **Theme-aware Title Bar:** The custom header and Windows caption buttons follow the app's dark/light theme, with draggable space and working Sync/Cancel controls.

---

## Building from Source

### Prerequisites
- Windows 10 (version 2004+) or Windows 11
- [.NET 10 SDK](https://dotnet.microsoft.com/)
- Visual Studio 2022 / 2026 with the *.NET Desktop Development* workload or Windows App SDK

### Build & Test
```powershell
# Clone repository
git clone https://github.com/oalqx/Atrium.git
cd Atrium

# Build solution
dotnet build Atrium.slnx -c Release

# Run automated tests
dotnet test Atrium.slnx -c Release
```

The [native Library smoke test](tests/Atrium.UiSmoke/README.md) also checks WinUI image bindings and navigation against the current local library.

In Library, click a game to open its details. Choose a launch preference and select **Save**; **Follow library preference** restores the global setting. Hidden games remain saved and can be restored from the **Hidden** filter. **Ctrl+F** focuses search, **Esc** closes details, and **Clear filters** restores the full visible library.

In Launchers & Sources, **Sync Entire Library** reports each scan and save stage. **Cancel** requests cancellation and waits for the current operation to finish. Folder paths are validated and normalized before being saved; overlapping folders do not import the same executable twice. Removing a configured folder keeps games already saved in the library.

---

## Project Architecture

Atrium is engineered with a clean modular separation of concerns:

- **`Atrium.Core`**: High-performance core library containing launcher detection, manifest parsers (Steam VDF/ACF, Epic JSON, GOG Galaxy SQLite, Xbox manifests), Disjoint-Set Union-Find library merger, DXGI/WMI hardware introspection, DirectX graphics preference manager, and SQLite persistence.
- **`Atrium.App`**: Native WinUI 3 desktop application leveraging Windows App SDK 1.6, CommunityToolkit MVVM, dependency injection, Mica Alt backdrop, and Velopack auto-updates.
- **`Atrium.Core.Tests`**: Comprehensive unit and end-to-end integration test suite running in xUnit and FluentAssertions.

---

## Intelligent Graphics Optimizer

Atrium identifies discrete gaming GPUs (such as NVIDIA GeForce RTX) alongside integrated processors (such as AMD Radeon graphics). Using the Windows DirectX Graphics Preferences registry (`HKCU\Software\Microsoft\DirectX\UserGpuPreferences`), Atrium can enforce discrete GPU execution per game or across all installed games in one click.

Atrium backs up the previous registry value into `atrium.db` before applying a preference. Rollback restores only settings backed up by Atrium, including games that have since been uninstalled. Backups are removed after a successful restore and retained if restoration fails.

---

## License

This project is licensed under the [MIT License](LICENSE).

