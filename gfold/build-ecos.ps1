# Builds ecos.dll from the vendored ECOS sources (gfold/ecos, embotech/ecos
# develop branch) using a portable Zig as the C compiler — no MSVC required.
#
# Output: gfold/native/ecos.dll  (x86_64, MinGW-style: all public symbols exported)
#
# Build choices:
#  - No DLONG: ECOS idxint is a 32-bit int, which keeps the C# interop layer
#    unambiguous on Windows (DLONG would mean SuiteSparse_long).
#  - CTRLC=0: no console signal handler — this DLL ends up inside the game
#    process, which must own its own signal handling.
#  - NDEBUG: disables AMD's internal debug dumps.

param(
    # Path to the Zig compiler (used as a drop-in C compiler via `zig cc`).
    # Defaults to `zig` on PATH; override with -ZigExe to point at a portable copy.
    [string]$ZigExe = "zig",
    [string]$Configuration = "Release"
)

$ErrorActionPreference = "Stop"
$root = $PSScriptRoot
$ecos = Join-Path $root "ecos"
$out = Join-Path $root "native"

# Accept either a full path or a command name resolved from PATH.
if (-not (Get-Command $ZigExe -ErrorAction SilentlyContinue)) {
    throw "zig not found ('$ZigExe'). Install Zig and put it on PATH, or pass -ZigExe <path>."
}
if (-not (Test-Path $ecos)) { throw "ECOS sources not found at $ecos" }
New-Item -ItemType Directory -Force $out | Out-Null

$sources = @(
    "src/ecos.c", "src/kkt.c", "src/cone.c", "src/spla.c", "src/ctrlc.c",
    "src/timer.c", "src/preproc.c", "src/splamm.c", "src/equil.c",
    "src/expcone.c", "src/wright_omega.c",
    "external/ldl/src/ldl.c"
) + (Get-ChildItem (Join-Path $ecos "external/amd/src") -Filter *.c |
        ForEach-Object { "external/amd/src/" + $_.Name }) +
    @((Join-Path $root "shim/ecos_shim.c"))

$includes = @(
    "-Iinclude", "-Iexternal/ldl/include", "-Iexternal/amd/include",
    "-Iexternal/SuiteSparse_config"
)

$opt = if ($Configuration -eq "Release") { "-O2" } else { "-O0 -g" }

Push-Location $ecos
try {
    $zigArgs = @("cc", "-target", "x86_64-windows-gnu", "-shared") +
        $opt.Split(" ") +
        @("-DCTRLC=0", "-DNDEBUG") +
        $includes + $sources +
        @("-o", (Join-Path $out "ecos.dll"))
    & $ZigExe @zigArgs
    if ($LASTEXITCODE -ne 0) { throw "zig cc failed with exit code $LASTEXITCODE" }
}
finally {
    Pop-Location
}

Write-Host "Built: $(Join-Path $out 'ecos.dll')" -ForegroundColor Green
