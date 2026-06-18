using System;
using System.Collections.Generic;
using System.Data;
using System.Dynamic;
using System.Numerics;
using System.Reflection;
using System.Reflection.Metadata;
using System.Runtime.InteropServices;
using System.Security;
using OpenTK.Platform.Windows;
using ScottPlot.LayoutEngines;
using SixLabors.ImageSharp.Formats.Tga;

namespace lib;


public class UPFGState
{
    public Dictionary<string, double> Cser { get; private set; } = new Dictionary<string, double>();
    public Vector3 rbias { get; private set; }
    public Vector3 rd { get; private set; }
    public Vector3 rgrav { get; private set; }
    public double tb { get; set; }
    public double time { get; private set; }
    public double tgo { get; private set; }
    public Vector3 v { get; private set; }
    public Vector3 vgo { get; private set; }
    public double K { get; private set; }

    public void SetVals(Dictionary<string, double> cserin,
                        Vector3 rbiasin, Vector3 rdin, Vector3 rgravin,
                        double tbin, double timein, double tgoin, Vector3 vin, Vector3 vgoin, double Kin)
    {
        Cser = cserin;
        rbias = rbiasin;
        rd = rdin;
        rgrav = rgravin;
        tb = tbin;
        time = timein;
        tgo = tgoin;
        v = vin;
        vgo = vgoin;
        K = Kin;

    }

}

public class Upfg
{
    public UPFGState PrevVals { get; private set; } = new UPFGState();
    public UPFGState CurrentVals { get; private set; } = new UPFGState();
    public bool ConvergenceFlag { get; private set; } = false;
    public bool SetupFlag { get; private set; } = false;
    public bool StagingFlag { get; set; } = false;
    public Vector3 Steering { get; private set; } = new Vector3(0, 0, 0);
    public UPFGTarget? Target { get; private set; } = null;
    public int mode { get; private set; } = 1;

    // --- Added state for single convergence -> coast -> precision descent ---
    private bool isCoasting = false;            // True while passively phasing to align predicted landing with desired
    private double coastStartTime = 0;          // Simulation time when coast began
    private double coastDuration = 0;           // Duration of passive coast until ignition for precision phase
    private Vector3 predictedLandingPoint;      // rd captured at end of Mode 2 convergence
    private double alignmentAngle = 0;          // Angular separation between predicted rd and desired rdes
    private const double angleEpsilon = 0.25 * Math.PI / 180.0; // 0.25 deg tolerance

    // public Upfg()
    // {
    //     PrevVals = new UPFGState();
    //     CurrentVals = new UPFGState();
    //     ConvergenceFlag = false;
    //     Steering = new Vector3(0, 0, 0);
    // }
    public void SetTarget(UPFGTarget target)
    {
        Target = target;
    }

    public void StageEvent()
    {
        StagingFlag = true;
    }

    public void step(Simulator sim, Vehicle vehicle, UPFGTarget target, int modeNum)
    {
        if (!SetupFlag)
        {
            mode = modeNum;
            SetTarget(target);
            Setup(sim);
            SetupFlag = true;
        }
        else
        {
            Run(sim, vehicle);
        }
    }

    public void Setup(Simulator sim)
    {
        if (Target == null)
            throw new InvalidOperationException("UPFGTarget must be set before calling Setup.");

        Vector3 curR = sim.State.r;
        Vector3 curV = sim.State.v;
        Vector3 desR = Vector3.Zero;
        Vector3 tgoV = Vector3.Zero;

        if (mode == 1)
        {
            Vector3 unitvec = Utils.RodriguesRotation(curR, Target.normal, Utils.DegToRad(15));
            desR = unitvec / unitvec.Length() * Target.radius;
            Vector3 tempvec = Vector3.Cross(Target.normal, desR);
            tgoV = Target.velocity * (tempvec / tempvec.Length()) - curV;
        }
        else if (mode == 2)
        {
            // Predictive (single convergence) phase: just shape rd ahead; cancel current velocity.
            Vector3 unitvec = Utils.RodriguesRotation(curR, Target.normal, Utils.DegToRad(15));
            desR = unitvec / unitvec.Length() * Target.radius;
            Vector3 tempvec = Vector3.Cross(Target.normal, desR);
            tgoV = -curV;
        }
        else if (mode == 3)
        {
            // Precision reference trajectory phase: drive to actual desired landing vector.
            desR = Target.rdes;
            tgoV = -curV; // refine burn solution starting from present state
        }
        else 
        {
            throw new InvalidOperationException("Unknown UPFG mode in Setup.");
        }
        Dictionary<string, double> cser = new Dictionary<string, double>
        {
            {"dtcp", 0 },
            {"xcp", 0 },
            {"A", 0 },
            {"D", 0 },
            {"E", 0 }
        };
        PrevVals.SetVals(cser, new Vector3(0, 0, 0), desR, (float)0.5 * Utils.CalcGravVector(Constants.Mu, curR), sim.State.t, sim.State.t, 100, curV, tgoV, 1);
    }

    public void Run(Simulator sim, Vehicle vehicle)
    {
        if (Target == null)
            throw new InvalidOperationException("UPFGTarget must be set before calling Run.");
        double t = sim.State.t;

        // Passive coast phase: skip guidance computation, maintain time continuity.
        if (isCoasting)
        {
            double elapsed = t - coastStartTime;
            double dtc = t - PrevVals.time;
            CurrentVals.SetVals(PrevVals.Cser, PrevVals.rbias, PrevVals.rd, PrevVals.rgrav, PrevVals.tb + dtc, t, PrevVals.tgo, sim.State.v, PrevVals.vgo, PrevVals.K);
            PrevVals = CurrentVals;
            Steering = Vector3.Zero; // zero thrust during coast

            if (elapsed >= coastDuration)
            {
                // Begin precision phase
                isCoasting = false;
                mode = 3;
                SetupFlag = false;       // force new Setup for mode 3 next step
                ConvergenceFlag = false; // allow reconvergence
                Console.WriteLine($"Coast complete (elapsed {elapsed:F1}s). Entering precision Mode 3.");
            }
            return;
        }

        // Preserve previous tgo for convergence test BEFORE we overwrite PrevVals later
        double prevTgoForConvergence = PrevVals.tgo;

        double gamma = Target.fpa;
        Vector3 iy = -Target.normal;
        double rdval = Target.radius;
        double vdval = Target.velocity;
        Vector3 r_ = sim.State.r;
        Vector3 v_ = sim.State.v;
        double m = sim.State.mass;

        Dictionary<string, double> cser = PrevVals.Cser;
        Vector3 rbias = PrevVals.rbias;
        Vector3 rd = PrevVals.rd;
        Vector3 rgrav = PrevVals.rgrav;
        double tp = PrevVals.time;
        Vector3 vprev = PrevVals.v;
        Vector3 vgo = PrevVals.vgo;
        double K = PrevVals.K;

        if (StagingFlag)
        {
            PrevVals.tb = 0;
            ConvergenceFlag = false;  // Reset convergence after staging
            StagingFlag = false;
        }

        // 1 - Initialization
        int n = vehicle.Stages.Count;
        List<int> stageModes;
        List<double> accelLimits, massFlows, exhaustVelocities, thrusts, thrustAccelerations, characteristicTimes, burnTimes;
        bool splitOccurred = InitializeStageParameters(vehicle, K, out stageModes, out accelLimits, out massFlows, out exhaustVelocities, out thrusts, out thrustAccelerations, out characteristicTimes, out burnTimes);
        if (splitOccurred)
        {
            Run(sim, vehicle);
            return;
        }

        // 2 - Accelerations
        UpdateAccelerations(t, tp, v_, vprev, ref vgo, ref burnTimes);

        // Update first stage parameters with current mass
        if (stageModes[0] == 1)
        {
            double minMass = vehicle.Stages[0].MassDry + 1e-6;
            if (m > minMass && thrusts[0] > 0)
            {
                thrustAccelerations[0] = thrusts[0] / m;
                if (thrustAccelerations[0] > 0)
                {
                    characteristicTimes[0] = exhaustVelocities[0] / thrustAccelerations[0];
                    if (characteristicTimes[0] <= burnTimes[0])
                        characteristicTimes[0] = burnTimes[0] + 1e-3;
                }
            }
        }

        // 3 - Burn time calculation
        List<double> Li;
        List<double> tgoi;
        double L;
        double tgo;
        ComputeBurnTimes(stageModes, thrusts, accelLimits, exhaustVelocities, thrustAccelerations, characteristicTimes, burnTimes, vgo, out Li, out L, out tgoi, out tgo);

        if (L > vgo.Length())
        {
            var clonedVeh = (Vehicle)vehicle.Clone();
            clonedVeh.Stages.RemoveAt(clonedVeh.Stages.Count - 1);
            Run(sim, clonedVeh);
            return;
        }

        // 4 - Thrust integrals
        List<double> Ji, Si, Qi, Pi;
        double J, S, Q_, H, P;
        ComputeThrustIntegrals(stageModes, Li, tgoi, characteristicTimes, exhaustVelocities, burnTimes, accelLimits, out Ji, out Si, out Qi, out Pi, out L, out J, out S, out Q_, out H, out P);

        // 5 - Guidance vectors
        Vector3 lambda_vec, rgo, iz, rgoxy, iF_, vthrust, rthrust, vbias;
        double lambdade, phi, phidot, rgoz;
        Vector3 lambdadot;
        ComputeGuidanceVectors(vgo, rd, r_, v_, tgo, rgrav, iy, S, L, J, Q_, H, P, rbias, out lambda_vec, out rgo, out iz, out rgoxy, out rgoz, out lambdade, out lambdadot, out iF_, out phi, out phidot, out vthrust, out rthrust, out vbias);
        rbias = rgo - rthrust;

        // 6 - Up / East
        // (retained if needed later)

        // 7 - External estimation routine
        Vector3 rc1 = r_ - 0.1f * rthrust - (float)(tgo / 30.0) * vthrust;
        Vector3 vc1 = v_ + 1.2f * rthrust / (float)tgo - 0.1f * vthrust;
        Vector3 rend, vend;
        EstimateExternalCorrections(rc1, vc1, tgo, cser, out rend, out vend, out cser);
        rgrav = rend - rc1 - vc1 * (float)tgo;
        Vector3 vgrav = vend - vc1;

        // 8 - Update target vectors
        UpdateTargetVectors(burnTimes, r_, v_, tgo, t, rgrav, rthrust, iy, rdval, gamma, vdval, vgrav, vbias, ref rd, ref vgo, ref K, ref cser);

        // Finalize state (but first evaluate convergence using old vs new tgo)
        EvaluateConvergence(prevTgoForConvergence, tgo);

        double dt = t - tp;
        CurrentVals.SetVals(cser, rbias, rd, rgrav, PrevVals.tb + dt, t, tgo, v_, vgo, K);
        PrevVals = CurrentVals;

        // 9 - Throttle
        if (stageModes[0] == 2 && (thrusts[0] / m) > accelLimits[0])
        {
            K = accelLimits[0] / (thrusts[0] / m);
        }
        iF_ = (float)K * iF_;
        Steering = iF_;

        // Transition logic after single convergence in Mode 2
        if (mode == 2 && ConvergenceFlag)
        {
            // Capture predicted landing point once
            predictedLandingPoint = rd;
            Vector3 rd_unit = Vector3.Normalize(predictedLandingPoint);
            Vector3 rdes_unit = Vector3.Normalize(Target.rdes);
            double dot = Math.Clamp(Vector3.Dot(rd_unit, rdes_unit), -1.0f, 1.0f);
            alignmentAngle = Math.Acos(dot); // radians

            if (alignmentAngle < angleEpsilon)
            {
                // Already aligned; skip coast
                Console.WriteLine("Mode 2 convergence: alignment within tolerance. Entering precision Mode 3 immediately.");
                mode = 3;
                SetupFlag = false;
                ConvergenceFlag = false;
                return;
            }

            double orbitalPeriod = sim.State.Kepler.GetOrbitalPeriod();
            coastDuration = alignmentAngle / (2.0 * Math.PI) * orbitalPeriod;

            Console.WriteLine($"Mode 2 converged. Predicted landing rd=<{predictedLandingPoint.X:F1},{predictedLandingPoint.Y:F1},{predictedLandingPoint.Z:F1}>. Desired rdes=<{Target.rdes.X:F1},{Target.rdes.Y:F1},{Target.rdes.Z:F1}>");
            Console.WriteLine($"Angular separation {alignmentAngle * 180.0 / Math.PI:F2} deg. Initiating coast for {coastDuration:F1} s.");

            isCoasting = true;
            coastStartTime = t;
            Steering = Vector3.Zero; // ensure thrust cut
            return;
        }
    }

    private bool InitializeStageParameters(Vehicle vehicle, double K, out List<int> stageModes, out List<double> accelLimits, out List<double> massFlows, out List<double> exhaustVelocities, out List<double> thrusts, out List<double> thrustAccelerations, out List<double> characteristicTimes, out List<double> burnTimes)
    {
        int n = vehicle.Stages.Count;
        stageModes = new List<int>();
        accelLimits = new List<double>();
        massFlows = new List<double>();
        exhaustVelocities = new List<double>();
        thrusts = new List<double>();
        thrustAccelerations = new List<double>();
        characteristicTimes = new List<double>();
        burnTimes = new List<double>();
        for (int i = 0; i < n; i++)
        {
            var stage = vehicle.Stages[i];

            if (stage.Mode == 2 && stage.Thrust / stage.MassTotal < stage.GLim * Constants.g0)
            {
                //add another stage when accel = glim
                double accel = stage.GLim * Constants.g0;
                double massRequired = stage.Thrust / accel;
                Stage newStage = new Stage
                {
                    Id = stage.Id + 1,
                    Mode = 2, // Constant acceleration mode
                    GLim = stage.GLim,
                    MassTotal = massRequired,
                    MassDry = stage.MassDry,
                    Thrust = stage.Thrust,
                    Isp = stage.Isp
                };
                Stage oldStage = new Stage
                {
                    Id = stage.Id,
                    Mode = 1, // Constant thrust mode
                    GLim = stage.GLim,
                    MassTotal = stage.MassTotal,
                    MassDry = massRequired,
                    Thrust = stage.Thrust,
                    Isp = stage.Isp
                };

                vehicle.Stages[i] = oldStage;
                vehicle.Stages.Insert(i + 1, newStage);
                Console.WriteLine($"Stage {stage.Id} split into two stages: {oldStage.Id} (constant thrust) and {newStage.Id} (constant acceleration).");
                // Console.WriteLine($"  OldStage: MassTotal={oldStage.MassTotal}, MassDry={oldStage.MassDry}, Thrust={oldStage.Thrust}, Isp={oldStage.Isp}, GLim={oldStage.GLim}");
                // Console.WriteLine($"  NewStage: MassTotal={newStage.MassTotal}, MassDry={newStage.MassDry}, Thrust={newStage.Thrust}, Isp={newStage.Isp}, GLim={newStage.GLim}");
                // Return true to indicate a split occurred and prevent further processing
                return true;
            }

            double massflow = stage.Thrust / (stage.Isp * Constants.g0);
            stageModes.Add(stage.Mode);
            accelLimits.Add(stage.GLim * Constants.g0);
            thrusts.Add(stage.Thrust* K) ;
            massFlows.Add(massflow);
            exhaustVelocities.Add(stage.Isp * Constants.g0);
            thrustAccelerations.Add(stage.Thrust / stage.MassTotal);
            characteristicTimes.Add(exhaustVelocities[i] / thrustAccelerations[i]);
            burnTimes.Add((stage.MassTotal - stage.MassDry) / massflow);
            // Console.WriteLine($"Stage {stage.Id}: Mode={stage.Mode}, MassTotal={stage.MassTotal}, MassDry={stage.MassDry}, Thrust={stage.Thrust}, Isp={stage.Isp}, GLim={stage.GLim}, massflow={massflow}, accel={stage.Thrust / stage.MassTotal}, charTime={exhaustVelocities[i] / thrustAccelerations[i]}, burnTime={(stage.MassTotal - stage.MassDry) / massflow}");
        }
        return false;
    }

    private void UpdateAccelerations(double t, double tp, Vector3 v_, Vector3 vprev, ref Vector3 vgo, ref List<double> burnTimes)
    {
        double dt = t - tp;
        Vector3 dvsensed = v_ - vprev;
        vgo -= dvsensed;
        burnTimes[0] -= PrevVals.tb;
    }

    private void ComputeBurnTimes(List<int> stageModes, List<double> thrusts, List<double> accelLimits, List<double> exhaustVelocities, List<double> thrustAccelerations, List<double> characteristicTimes, List<double> burnTimes, Vector3 vgo, out List<double> Li, out double L, out List<double> tgoi, out double tgo)
    {
        int n = stageModes.Count;
        Li = new List<double>();
        L = 0;
        for (int i = 0; i < n - 1; i++)
        {
            if (stageModes[i] == 1)
            {
                // Clamp denominator to avoid log of zero or negative
                double denom = Math.Max(characteristicTimes[i] - burnTimes[i], 1e-6);
                double ratio = characteristicTimes[i] / denom;
                if (ratio <= 0) ratio = 1e-6;
                Li.Add(exhaustVelocities[i] * Math.Log(ratio));
            }
            else if (stageModes[i] == 2)
                Li.Add(accelLimits[i] * burnTimes[i]);
            L += Li[i];
        }

        Li.Add((vgo).Length() - L);
        tgoi = new List<double>();
        for (int i = 0; i < n; i++)
        {
            if (stageModes[i] == 1)
            {
                // Clamp denominator to avoid division by zero
                double exvel = Math.Max(exhaustVelocities[i], 1e-6);
                burnTimes[i] = characteristicTimes[i] * (1 - Math.Exp(-Li[i] / exvel));
            }
            else if (stageModes[i] == 2)
                burnTimes[i] = Li[i] / accelLimits[i];
            if (i == 0)
                tgoi.Add(burnTimes[i]);
            else
                tgoi.Add(tgoi[i - 1] + burnTimes[i]);
        }
        tgo = tgoi[n - 1];
    }

    private void ComputeThrustIntegrals(List<int> stageModes, List<double> Li, List<double> tgoi, List<double> characteristicTimes, List<double> exhaustVelocities, List<double> burnTimes, List<double> accelLimits, out List<double> Ji, out List<double> Si, out List<double> Qi, out List<double> Pi, out double L, out double J, out double S, out double Q_, out double H, out double P)
    {
        int n = stageModes.Count;
        Ji = new List<double>(new double[n]);
        Si = new List<double>(new double[n]);
        Qi = new List<double>(new double[n]);
        Pi = new List<double>(new double[n]);
        L = 0; J = 0; S = 0; Q_ = 0; H = 0; P = 0;
        for (int i = 0; i < n; i++)
        {
            double tgoi1 = (i == 0) ? 0 : tgoi[i - 1];
            if (stageModes[i] == 1)
            {
                Ji[i] = characteristicTimes[i] * Li[i] - exhaustVelocities[i] * burnTimes[i];
                Si[i] = -Ji[i] + Li[i] * burnTimes[i];
                Qi[i] = Si[i] * (characteristicTimes[i] + tgoi1) - 0.5 * exhaustVelocities[i] * burnTimes[i] * burnTimes[i];
                Pi[i] = Qi[i] * (characteristicTimes[i] + tgoi1) - 0.5 * exhaustVelocities[i] * burnTimes[i] * burnTimes[i] * (burnTimes[i] / 3 + tgoi1);
            }
            else if (stageModes[i] == 2)
            {
                Ji[i] = 0.5 * Li[i] * burnTimes[i];
                Si[i] = Ji[i];
                Qi[i] = Si[i] * (burnTimes[i] / 3 + tgoi1);
                Pi[i] = (1.0 / 6.0) * Si[i] * (tgoi[i] * tgoi[i] + 2 * tgoi[i] * tgoi1 + 3 * tgoi1 * tgoi1);
            }
            Ji[i] += Li[i] * tgoi1;
            Si[i] += L * burnTimes[i];
            Qi[i] += J * burnTimes[i];
            Pi[i] += H * burnTimes[i];
            L += Li[i];
            J += Ji[i];
            S += Si[i];
            Q_ += Qi[i];
            P += Pi[i];
            H = J * tgoi[i] - Q_;
        }
    }

    private void ComputeGuidanceVectors(Vector3 vgo, Vector3 rd, Vector3 r_, Vector3 v_, double tgo, Vector3 rgrav, Vector3 iy, double S, double L, double J, double Q_, double H, double P, Vector3 rbias, out Vector3 lambda_vec, out Vector3 rgo, out Vector3 iz, out Vector3 rgoxy, out double rgoz, out double lambdade, out Vector3 lambdadot, out Vector3 iF_, out double phi, out double phidot, out Vector3 vthrust, out Vector3 rthrust, out Vector3 vbias)
    {
        lambda_vec = Vector3.Normalize(vgo);
        if (PrevVals.tgo > 0)
        {
            rgrav = (float)Math.Pow(tgo / PrevVals.tgo, 2) * rgrav;
        }
        rgo = rd - (r_ + v_ * (float)tgo + rgrav);
        iz = Vector3.Normalize(Vector3.Cross(rd, iy));
        rgoxy = rgo - Vector3.Dot(iz, rgo) * iz;
        rgoz = (S - Vector3.Dot(lambda_vec, rgoxy)) / Vector3.Dot(lambda_vec, iz);
        rgo = rgoxy + (float)rgoz * iz + rbias;
        lambdade = Q_ - S * J / L;
        lambdadot = (rgo - (float)S * lambda_vec) / (float)lambdade;
        iF_ = lambda_vec - lambdadot * (float)(J / L);
        iF_ = Vector3.Normalize(iF_);
        phi = Math.Acos(Math.Clamp(Vector3.Dot(iF_, lambda_vec) / (iF_.Length() * lambda_vec.Length()), -1.0, 1.0));
        phidot = -phi * L / J;
        vthrust = (float)(L - 0.5 * L * phi * phi - J * phi * phidot - 0.5 * H * phidot * phidot) * lambda_vec;
        double rthrustMag = S - 0.5 * S * phi * phi - Q_ * phi * phidot - 0.5 * P * phidot * phidot;
        rthrust = (float)rthrustMag * lambda_vec - ((float)(S * phi + Q_ * phidot) * (lambdadot / lambdadot.Length()));
        vbias = vgo - vthrust;
    }

    private void EstimateExternalCorrections(Vector3 rc1, Vector3 vc1, double tgo, Dictionary<string, double> cser, out Vector3 rend, out Vector3 vend, out Dictionary<string, double> cserOut)
    {
        (rend, vend, cserOut) = OrbitalMechanics.CSEroutine(rc1, vc1, tgo, cser);
    }

    private void UpdateTargetVectors(List<double> burnTimes, Vector3 r_, Vector3 v_, double tgo, double t, Vector3 rgrav, Vector3 rthrust, Vector3 iy, double rdval, double gamma, double vdval, Vector3 vgrav, Vector3 vbias, ref Vector3 rd, ref Vector3 vgo, ref double Kk, ref Dictionary<string, double>cser)
    {
        Vector3 rp = r_ + v_ * (float)tgo + rgrav + rthrust;
        Vector3 vd = Vector3.Zero;

        if (mode == 1) //Standard Ascent
        {
            rp -= Vector3.Dot(rp, iy) * iy;
            rd = (float)rdval * rp / rp.Length();
            Vector3 ix = Vector3.Normalize(rd);
            Vector3 iz = Vector3.Cross(ix, iy);
            Vector3 vv1 = new Vector3(ix.X, iy.X, iz.X);
            Vector3 vv2 = new Vector3(ix.Y, iy.Y, iz.Y);
            Vector3 vv3 = new Vector3(ix.Z, iy.Z, iz.Z);
            Vector3 vop = new Vector3((float)Math.Sin(gamma), 0, (float)Math.Cos(gamma));
            vd = new Vector3(
                Vector3.Dot(vv1, vop),
                Vector3.Dot(vv2, vop),
                Vector3.Dot(vv3, vop)
            ) * (float)vdval;
            Kk = 1;
        }
        else if (mode == 2) // Predictive single-convergence phase (do NOT force rd to desired rdes)
        {
            rp -= Vector3.Dot(rp, iy) * iy; // flatten to orbital plane normal
            rd = (float)rdval * rp / rp.Length(); // predicted cutoff position on sphere
            // Keep current velocity direction (soft guidance) so vgo focuses on positional error
            vd = v_;
            Kk = 1;
        }
        else if (mode == 3) // Precision reference trajectory phase driving to desired landing point
        {
            Vector3 r_ref = Target!.rdes; // Target validated earlier in Run/Setup
            Vector3 ix = Vector3.Normalize(r_ref);
            Vector3 iz = Vector3.Cross(ix, iy);
            Vector3 v_ref = (float)vdval * iz; // tangential reference velocity basis
            float t_ref = 450; // TODO expose
            (rd, vd, cser) = OrbitalMechanics.CSEroutine(r_ref, v_ref, t + tgo - t_ref, cser);
            float drz = Vector3.Dot(iz, rd - rp);
            float vgoz = Vector3.Dot(iz, vgo);
            float dtgo = (Math.Abs(vgoz) < 1e-6f) ? 0 : -2 * drz / vgoz;
            if (burnTimes.Count > 0)
            {
                Kk = (Kk * burnTimes[0]) / (burnTimes[0] + dtgo);
            }
            // Drive rd toward target reference
            rd = r_ref;
            if (Kk > 1) Kk = 1;
            if (Kk < 0.01) Kk = 0.01;
        }
        else
        {
            throw new InvalidOperationException("Unknown UPFG mode in UpdateTargetVectors.");
        }

        vgo = vd - v_ - vgrav + vbias;
    }

    // Added back previously existing convergence check
    public void CheckConvergence() { /* deprecated in new logic; kept for backward compatibility */ }

    private void EvaluateConvergence(double prevTgo, double curTgo)
    {
        if (prevTgo <= 0) return;
        double tgodiff = (curTgo - prevTgo) / prevTgo;
        if (Math.Abs(tgodiff) < 0.01)
        {
            ConvergenceFlag = true;
        }
    }

}

public class CustomUpfg : Upfg
{
    // Add custom properties or methods here as needed
    public CustomUpfg() : base()
    {
        // Custom initialization if required
    }

}