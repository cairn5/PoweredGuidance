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

## Validation status (tf=81 s, N=120, Example-1 params)

P4 (min fuel, unique optimum) agrees between C#/ECOS and Python/CVXPY/
Clarabel to max |dr| 0.5 mm, |dv| 0.7 mm/s, |dm| 0.8 g over the trajectory;
both use 377.34 kg. P3 paths differ mid-trajectory (its objective only
prices the landing point, so the path is non-unique) but both land with
~zero error.

Hard-won numerics lesson: in raw SI units BOTH solvers fail on this problem
— ECOS loudly ("unreliable search direction", ~1.5% suboptimal), Clarabel
silently (returns "Solved" with a false optimality certificate, 383.6 kg).
GfoldPlanner therefore nondimensionalizes internally (length ~ |r0|, time
sqrt(L/g)) and unscales results; the Python replica has --scaled for parity.

Known formulation gap inherited from the reference: no m(tf) >= m_dry
constraint, so the optimizer may burn more fuel than is aboard (the console
flags it). Add the fuel floor before real use. Also note the reference
repo's own static config (N=250, dt=4.5 -> tf 1120 s) exceeds its fuel
limit tf_max = 125 s; use a feasible tf.

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
