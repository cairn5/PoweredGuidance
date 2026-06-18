# Powered Guidance

Guidance for [Kitten Space Agency](https://ahwoo.com/app/100000/kitten-space-agency)
(KSA): a mod that flies powered ascent and powered descent, with the numerical
guidance developed and validated out-of-game alongside it.

- **Ascent** — a double-precision port of UPFG (Unified Powered Flight Guidance).
- **Descent** — convex G-FOLD powered-descent guidance (lossless convexification,
  solved with ECOS), with a world-space debug overlay and click-to-retarget.

## Repository layout

| Path | What it is |
| --- | --- |
| `ksamod/` | The KSA mod itself — UI, landing state machine, overlay, UPFG bridge, G-FOLD bridge. Loaded by the [StarMap](https://github.com/StarMapLoader/StarMap) mod loader. |
| `gfold/Gfold.Core/` | The convex G-FOLD solver (managed; P/Invokes ECOS). Shipped with the mod. |
| `gfold/ecos/` | Vendored [ECOS](https://github.com/embotech/ecos) conic solver sources (GPLv3); built to `ecos.dll` by `gfold/build-ecos.ps1`. |
| `gfold/Gfold.Console/` | Out-of-game solver validation harness (dynamics replay, constraint checks, CSV). |
| `gfold/python_ref/` | CVXPY/Clarabel reference for cross-validating the solver. |
| `tools/convtest/` | Verifies the steering→Euler attitude conversion against KSA's own quaternion math. |
| `tools/ksaprobe/` | KSA.dll metadata probe for API discovery. |
| `docs/reference/` | Reference papers (UPFG, state-vector/orbit conversions). |
| `legacy/` | The original standalone `navbox` prototype — kept for reference, not built or shipped. |

The mod is the only thing released; the validators and tools never ship.

## Build & install

The mod needs the [StarMap](https://github.com/StarMapLoader/StarMap) loader installed
in the game. Then build (.NET 10; game assemblies referenced from
`C:\Program Files\Kitten Space Agency`, override with `-p:KsaDir=...`):

```
dotnet build ksamod/PoweredGuidance.csproj -c Release
```

The build installs the mod into `Documents\My Games\Kitten Space Agency\mods\PoweredGuidance\`,
and a Release build also produces a distributable `PoweredGuidance.zip`. See
[`ksamod/README.md`](ksamod/README.md) for details, and
[`gfold/README.md`](gfold/README.md) for the solver and `ecos.dll` build.

## License

GPLv3 — see [`LICENSE`](LICENSE). This is required because the mod links the vendored
ECOS solver, which is GPLv3; the whole work is therefore distributed under GPLv3.

## Credits

- [ECOS](https://github.com/embotech/ecos) (embotech) — the conic solver, GPLv3.
- [PEGAS](https://github.com/Noiredd/PEGAS) by Noiredd — reference and foundation for
  the UPFG implementation.
- G-FOLD — Açıkmeşe & Blackmore, lossless convexification of powered-descent guidance.
