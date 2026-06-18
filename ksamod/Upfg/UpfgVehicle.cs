namespace PoweredGuidance.Upfg;

// A propulsion stage as UPFG models it. Mode 1 = constant thrust, Mode 2 = constant
// acceleration (g-limited). For the first KSA integration we build a single constant-
// thrust stage from the vehicle's live engine configuration, but the guidance loop is
// written to accept a list so multi-stage can be added later.
public sealed class UpfgStage
{
    public int Mode = 1;
    public double Thrust;      // N (vacuum)
    public double Isp;         // s
    public double MassTotal;   // kg, wet (current)
    public double MassDry;     // kg, at burnout
    public double GLim;        // acceleration limit in g's (Mode 2); large = unlimited
}

public sealed class UpfgVehicle
{
    public System.Collections.Generic.List<UpfgStage> Stages { get; } = new();
}
