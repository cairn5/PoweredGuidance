using System;
using Brutal.Numerics;
using KSA;

// Deterministic check of the steering-direction -> CustomAttitudeTarget conversion,
// calling KSA's own quaternion functions (no game boot). For several sample geometries
// it reconstructs the orientation exactly as the flight computer will and confirms the
// resulting thrust axis points along the desired steering vector.
class Program
{
    const string KsaDir = @"C:\Program Files\Kitten Space Agency";

    static void Main()
    {
        AppDomain.CurrentDomain.AssemblyResolve += (_, a) =>
        {
            string p = System.IO.Path.Combine(KsaDir, new System.Reflection.AssemblyName(a.Name).Name + ".dll");
            return System.IO.File.Exists(p) ? System.Reflection.Assembly.LoadFrom(p) : null;
        };
        Run();
    }

    static void Run()
    {
        var rng = new Random(1);
        double worstQuat = 1, worstAlign = 1;
        for (int i = 0; i < 8; i++)
        {
            double3 pos = Rand(rng);
            double3 steer = Rand(rng);
            // A representative frame2Cci (any orientation); use ECL builder with a random cce2Cci.
            doubleQuat cce2Cci = doubleQuat.Normalize(new doubleQuat(rng.NextDouble(), rng.NextDouble(), rng.NextDouble(), rng.NextDouble()));
            doubleQuat frame2Cci = VehicleReferenceFrameEx.GetEclBody2Cci(cce2Cci);

            float3 posDir = float3.Pack(double3.Normalize(pos));
            float3 steerDir = float3.Pack(double3.Normalize(steer));
            doubleQuat desired = BurnTarget.ComputeBurnBody2Cci(posDir, steerDir);

            double3 euler = doubleQuat.Concatenate(desired, doubleQuat.Inverse(frame2Cci)).ToRollYawPitchRadians();
            doubleQuat recon = doubleQuat.Concatenate(QuaternionEx.CreateFromRollYawPitchRadians(euler), frame2Cci);

            double qdot = Math.Abs(desired.X*recon.X + desired.Y*recon.Y + desired.Z*recon.Z + desired.W*recon.W);
            double3 thrustAxis = double3.Transform(double3.UnitX, recon);
            double align = double3.Dot(thrustAxis, double3.Normalize(steer));

            Console.WriteLine($"#{i}: quatMatch={qdot:F6}  thrustAxis.steer={align:F6}");
            worstQuat = Math.Min(worstQuat, qdot);
            worstAlign = Math.Min(worstAlign, align);
        }
        Console.WriteLine($"WORST quatMatch={worstQuat:F6} (want 1)  WORST align={worstAlign:F6} (want 1)");
        Console.WriteLine(worstQuat > 0.9999 && worstAlign > 0.9999 ? "PASS" : "FAIL");
    }

    static double3 Rand(Random r) => new double3(r.NextDouble()*2-1, r.NextDouble()*2-1, r.NextDouble()*2-1);
}
