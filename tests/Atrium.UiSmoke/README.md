# Native Library smoke test

On Windows, build and run the opt-in test host:

```powershell
dotnet build src/Atrium.App/Atrium.App.csproj -c Release -p:UiSmokeTest=true -p:OutputPath=artifacts/ui-smoke/
Start-Process .\src\Atrium.App\artifacts\ui-smoke\Atrium.App.exe -Wait
Get-Content .\src\Atrium.App\artifacts\ui-smoke\ui-smoke.json
```

The host loads the current local library, adds temporary cards in memory for missing, invalid, and corrupt artwork, navigates between Sources and Library twice, and scrolls through the native game-card containers. It verifies the corrupt-image title fallback and records unhandled XAML exceptions. It writes the JSON report beside the executable and exits with code 0 on success or 1 on failure.

It also checks unmatched-search empty states, clearing filters, Hidden and Installed results, recent-play ordering, switching game-detail bindings, and control/detail bounds in a compact window.

The host verifies dark/light caption colors, reserved caption-button space and native pointer regions for title-bar actions, and Sources progress/cancel/control bindings. `--controls-only` skips the complete game-card scroll when diagnosing those controls; its report identifies that reduced scope.

The test does not sync games, save fixture cards, launch games, or change GPU preferences. Only builds with `UiSmokeTest=true` include the host; its output is separate from normal Release builds. If .NET is installed outside the default location, set `DOTNET_ROOT` to that runtime directory before launching.
