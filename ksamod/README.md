# PoweredGuidance KSA mod

A Kitten Space Agency (KSA) mod that draws an ImGui window in-game and drives the
flight computer. Includes a standalone port of UPFG (Unified Powered Flight Guidance)
for ascent and a convex G-FOLD powered-descent solver for landing.

Requires the [StarMap](https://github.com/StarMapLoader/StarMap) mod loader.

## How it works

1. **Entry point**: `Mod` is a `[StarMapMod]` class. StarMap discovers this assembly
   (declared as `EntryAssembly` in `mod.toml`), constructs `Mod`, and calls its
   lifecycle methods.
   - `[StarMapAllModsLoaded]` applies the Harmony prefix on `Vehicle.PrepareWorker`
     (autopilot writes that must land just before the sim snapshots the FC).
   - `[StarMapAfterGui]` draws the window every frame inside the active ImGui frame
     (`PoweredGuidanceWindow.Draw(Program.MainViewport)`).
   - `[StarMapUnload]` removes the patches.
2. **Guidance**:
   - `Upfg/` — the double-precision UPFG port (`UpfgGuidance`, `CseRoutine`,
     `UpfgTarget`, `UpfgVehicle`).
   - `Gfold/` — the bridge (`KsaGfold`) to the standalone G-FOLD solver in `../gfold`.
   - `PoweredGuidanceWindow` / `PoweredGuidanceOverlay` — UI, landing state machine, and the world-space
     G-FOLD debug overlay.

## Dependencies

- **Runtime**: the [StarMap](https://github.com/StarMapLoader/StarMap) loader must be
  installed in the game. StarMap provides Harmony at runtime, so the mod ships only
  itself plus its private deps (`Gfold.Core.dll` + native `ecos.dll`).
- **Build**: .NET 10, plus the `StarMap.API` and `Lib.Harmony` NuGet packages
  (restored automatically). Game assemblies are referenced from
  `C:\Program Files\Kitten Space Agency` (override with `-p:KsaDir=...`).

## Build & install

```
dotnet build PoweredGuidance.csproj -c Release
```

The `CopyToMods` build target installs the mod into
`Documents\My Games\Kitten Space Agency\mods\PoweredGuidance\` (the mod DLL, `mod.toml`,
`Gfold.Core.dll`, and `ecos.dll`). KSA auto-discovers the folder via `mod.toml` and
prompts to enable it (or add it to `manifest.toml`).

The solution also contains `tools/convtest` — a console app that verifies the
steering→Euler attitude conversion round-trips through KSA's own quaternion functions.
