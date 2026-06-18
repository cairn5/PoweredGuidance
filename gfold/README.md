# gfold

G-FOLD (convex powered-descent guidance, Açıkmeşe/Blackmore) in C#, intended as
the terminal-guidance trajectory generator for the KSA mod (`../ksamod`). The
mod consumes this as compiled artifacts only — `Gfold.Core.dll` + `ecos.dll`
dropped next to the mod DLL — so none of the optimization machinery leaks into
the mod project.

## Layout

- `ecos/` — vendored ECOS conic solver sources (embotech/ecos, develop branch;
  see `ecos/ECOS-VERSION.txt` for the pinned commit). GPLv3.
- `build-ecos.ps1` — compiles `native/ecos.dll` with Zig as a drop-in C
  compiler (`zig cc`, no MSVC needed). Expects `zig` on PATH; pass
  `-ZigExe <path>` to point at a portable copy.
- `native/` — build output (gitignored). Rebuild with the script.
- `shim/ecos_shim.c` — accessors for `pwork` internals compiled into
  ecos.dll, so managed code never depends on the struct layout.
- `Gfold.Core/` — the managed library: `EcosSolver.Solve(EcosProblem)`
  (standard conic form: min c'x s.t. Ax=b, Gx+s=h, s in R+^l x SOC(q...)),
  `SparseCcs` triplet->CCS builder, P/Invoke bindings with pinned-array
  lifetime management (ECOS retains caller pointers from setup to cleanup).
- `Gfold.Console/` — runs the P3 -> P4 flow on the reference "Numerical
  Example 1" case, verifies the result physically (dynamics replay, bounds),
  writes CSVs. `--check <csv>` audits any trajectory against the constraint
  set; `--verbose` shows ECOS iterations.
- `python_ref/` — CVXPY/Clarabel replica of the original Python for
  cross-validation (`gfold_ref.py [tf] [N] [--scaled] [--feascheck csv]`).


## Build notes

ECOS is built without `DLONG`, so the C `idxint` is a 32-bit `int` — the C#
interop maps `idxint -> int`, `pfloat -> double`. `CTRLC=0` keeps console
signal handlers out of the game process. Verbosity is a runtime setting
(`settings.verbose`), not a compile-time one.

Smoke test (PowerShell):

```powershell
Add-Type -TypeDefinition 'using System; using System.Runtime.InteropServices;
public static class E { [DllImport("native\\ecos.dll")] public static extern IntPtr ECOS_ver(); }'
[Runtime.InteropServices.Marshal]::PtrToStringAnsi([E]::ECOS_ver())  # -> 2.0.10
```
