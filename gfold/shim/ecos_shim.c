/*
 * C accessors for ECOS pwork internals, compiled into ecos.dll alongside the
 * vendored sources (not part of upstream ECOS).
 *
 * The C# interop calls these instead of reading struct fields at offsets:
 * pwork's layout shifts with compile flags (EXPCONE, PROFILING, EQUILIBRATE),
 * so hardcoded offsets in managed code would break silently on a rebuild.
 */
#include "ecos.h"

double* ecsh_x(pwork* w)      { return w->x; }
double* ecsh_y(pwork* w)      { return w->y; }
double* ecsh_z(pwork* w)      { return w->z; }
double* ecsh_s(pwork* w)      { return w->s; }

double  ecsh_pcost(pwork* w)  { return w->info->pcost; }
double  ecsh_dcost(pwork* w)  { return w->info->dcost; }
int     ecsh_iter(pwork* w)   { return (int)w->info->iter; }

void ecsh_set_verbose(pwork* w, int v)   { w->stgs->verbose = (idxint)v; }
void ecsh_set_maxit(pwork* w, int it)    { w->stgs->maxit = (idxint)it; }
void ecsh_set_nitref(pwork* w, int n)    { w->stgs->nitref = (idxint)n; }
void ecsh_set_tols(pwork* w, double feastol, double abstol, double reltol)
{
    w->stgs->feastol = feastol;
    w->stgs->abstol = abstol;
    w->stgs->reltol = reltol;
}
