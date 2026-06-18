using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Gfold;

// Raw P/Invoke surface for ecos.dll (built by gfold/build-ecos.ps1: idxint is a
// 32-bit int, pfloat is double). All pointers are IntPtr because ECOS_setup
// RETAINS the caller's arrays (preproc.c wraps them without copying, and
// equilibration scales them in place) — the caller must keep them pinned from
// setup to cleanup, which EcosSolver manages with GCHandles.
//
// The ecsh_* functions come from gfold/shim/ecos_shim.c and exist so managed
// code never depends on pwork's struct layout.
internal static partial class EcosNative
{
    private const string Lib = "ecos";

    // CA2255 discourages ModuleInitializer in libraries, but registering the
    // DllImport resolver before the first P/Invoke is exactly the advanced
    // scenario it exists for — there is no other hook that runs early enough
    // in every host (console, tests, the game with the mod loaded).
#pragma warning disable CA2255
    [ModuleInitializer]
#pragma warning restore CA2255
    internal static void Init()
    {
        // Resolve ecos.dll from next to Gfold.Core.dll regardless of host
        // process working directory (console runner, test host, or the game
        // with Gfold.Core.dll + ecos.dll in the mod folder).
        NativeLibrary.SetDllImportResolver(typeof(EcosNative).Assembly, (name, asm, _) =>
        {
            if (name != Lib)
                return IntPtr.Zero;
            string dir = Path.GetDirectoryName(asm.Location) ?? ".";
            string candidate = Path.Combine(dir, "ecos.dll");
            return NativeLibrary.TryLoad(candidate, out IntPtr handle) ? handle : IntPtr.Zero;
        });
    }

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    internal static extern IntPtr ECOS_setup(
        int n, int m, int p, int l, int ncones, IntPtr q, int nex,
        IntPtr gpr, IntPtr gjc, IntPtr gir,
        IntPtr apr, IntPtr ajc, IntPtr air,
        IntPtr c, IntPtr h, IntPtr b);

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    internal static extern int ECOS_solve(IntPtr work);

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    internal static extern void ECOS_cleanup(IntPtr work, int keepvars);

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    internal static extern IntPtr ECOS_ver();

    // --- shim accessors ---

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    internal static extern IntPtr ecsh_x(IntPtr work);

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    internal static extern double ecsh_pcost(IntPtr work);

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    internal static extern double ecsh_dcost(IntPtr work);

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    internal static extern int ecsh_iter(IntPtr work);

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    internal static extern void ecsh_set_verbose(IntPtr work, int verbose);

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    internal static extern void ecsh_set_maxit(IntPtr work, int maxit);

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    internal static extern void ecsh_set_nitref(IntPtr work, int nitref);

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    internal static extern void ecsh_set_tols(IntPtr work, double feastol, double abstol, double reltol);

    internal static string Version() => Marshal.PtrToStringAnsi(ECOS_ver()) ?? "?";
}
