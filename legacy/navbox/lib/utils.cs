namespace lib;

using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Diagnostics;
using System.IO.Compression;
using System.Net.Mail;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Security;
using ScottPlot.LayoutEngines;
using ScottPlot.Rendering.RenderActions;
using System.Security.Cryptography;
using ScottPlot;
using System.Reflection.Metadata;
using ConsoleTables;
using HarfBuzzSharp;
using System.Runtime.InteropServices;


// public class SimHistory
// {
//     public List<SimState> r { get; set; } = new List<SimState>();
// }



public static class Constants
{
    public static float Re { get; private set; } = 6371000;  // Earth radius in meters (default)
    public static double We { get; private set; } = 7.2921150e-5; // Earth rotation rate rad/s (default)
    public static float Mu { get; private set; } = 3.986e14f; // Earth gravitational parameter (default)
    public static float g0 { get; private set; } = 9.80665f;
    
    public static void SetCelestialBody(string bodyName)
    {
        switch (bodyName.ToLower())
        {
            case "earth":
                Re = 6371000f;  // Earth radius in meters
                We = 7.2921150e-5; // Earth rotation rate rad/s
                Mu = 3.986004418e14f;  // Earth gravitational parameter m³/s²
                break;
            case "moon":
                Re = 1737400f;  // Moon radius in meters
                We = 2.6617e-6; // Moon rotation rate rad/s (synchronous rotation)
                Mu = 4.9048695e12f;  // Moon gravitational parameter m³/s²
                break;
            default:
                throw new ArgumentException($"Unknown celestial body: {bodyName}");
        }
    }
}

public static class Utils
{
    public static double GoldenSectionSearch(Func<double, double> func, double a, double b, double tol = 1e-5)
    {
        double phi = (1 + Math.Sqrt(5)) / 2; // Golden ratio
        double resphi = 2 - phi; // 2 - golden ratio (approximately 0.618)

        double c = a + resphi * (b - a);
        double d = b - resphi * (b - a);
        double fc = func(c);
        double fd = func(d);

        while (Math.Abs(b - a) > tol)
        {
            if (fc < fd)
            {
                b = d;
                d = c;
                fd = fc;
                c = a + resphi * (b - a);
                fc = func(c);
            }
            else
            {
                a = c;
                c = d;
                fc = fd;
                d = b - resphi * (b - a);
                fd = func(d);
            }
        }

        return (a + b) / 2;
    }
    public static double CalcLaunchAzimuthNonRotating(double inc, double lat)
    {
        double azimuth = Math.Asin(Math.Cos(Utils.DegToRad(inc) / Math.Cos(Utils.DegToRad(lat))));

        return azimuth;
    }

    public static double CalcLaunchAzimuthRotating(Simulator sim, UPFGTarget tgt)
    {
        // tgt.inc and sim.State.Misc["latitude"] are already in radians
        double azimuth = Math.Asin(Math.Cos(tgt.inc) / Math.Cos(sim.State.Misc["latitude"]));

        double vorbit = tgt.velocity;
        double veqrot = Constants.We * Constants.Re;

        double numerator = vorbit * Math.Sin(azimuth) - veqrot * Math.Cos(sim.State.Misc["latitude"]);
        double denominator = vorbit * Math.Cos(azimuth);
        double correctedAz = Math.Atan2(numerator, denominator);

        return correctedAz;
    }

    // Convert ECI (Earth-Centered Inertial) SimState to ECEF (Earth-Centered Earth-Fixed) SimState
    public static SimState ECItoECEF(SimState statein)
    {
        double omega = Constants.We;
        double t = statein.t;
        double theta = omega * t;
        Vector3 r_eci = statein.r;
        Vector3 v_eci = statein.v;
        float cosTheta = (float)Math.Cos(theta);
        float sinTheta = (float)Math.Sin(theta);
        float x_ecef = cosTheta * r_eci.X + sinTheta * r_eci.Y;
        float y_ecef = -sinTheta * r_eci.X + cosTheta * r_eci.Y;
        float z_ecef = r_eci.Z;
        Vector3 r_ecef = new(x_ecef, y_ecef, z_ecef);
        float vx_ecef = cosTheta * v_eci.X + sinTheta * v_eci.Y;
        float vy_ecef = -sinTheta * v_eci.X + cosTheta * v_eci.Y;
        float vz_ecef = v_eci.Z;
        Vector3 v_ecef = new(vx_ecef, vy_ecef, vz_ecef);
        Vector3 omegaVec = new(0, 0, (float)omega);
        Vector3 v_surface = v_ecef - Vector3.Cross(omegaVec, r_ecef);
        var stateout = new SimState
        {
            r = r_ecef,
            v = v_surface,
            t = statein.t,
            mass = statein.mass
        };
        stateout.CalcMiscParams();
        stateout.CartToKepler();
        return stateout;
    }

    // Convert ECEF (Earth-Centered Earth-Fixed) SimState to ECI (Earth-Centered Inertial) SimState
    public static SimState ECEFtoECI(SimState statein)
    {
        double omega = Constants.We;
        double t = statein.t;
        double theta = omega * t;
        Vector3 r_ecef = statein.r;
        Vector3 v_ecef = statein.v;
        Vector3 omegaVec = new(0, 0, (float)omega);
        Vector3 v_ecef_rot = v_ecef + Vector3.Cross(omegaVec, r_ecef);
        float cosTheta = (float)Math.Cos(-theta);
        float sinTheta = (float)Math.Sin(-theta);
        float x_eci = cosTheta * r_ecef.X + sinTheta * r_ecef.Y;
        float y_eci = -sinTheta * r_ecef.X + cosTheta * r_ecef.Y;
        float z_eci = r_ecef.Z;
        Vector3 r_eci = new(x_eci, y_eci, z_eci);
        float vx_eci = cosTheta * v_ecef_rot.X + sinTheta * v_ecef_rot.Y;
        float vy_eci = -sinTheta * v_ecef_rot.X + cosTheta * v_ecef_rot.Y;
        float vz_eci = v_ecef_rot.Z;
        Vector3 v_eci = new(vx_eci, vy_eci, vz_eci);
        var stateout = new SimState
        {
            r = r_eci,
            v = v_eci,
            t = statein.t,
            mass = statein.mass
        };
        stateout.CalcMiscParams();
        stateout.CartToKepler();
        return stateout;
    }

    // Calculate the ECI velocity of a point at rest on the Earth's surface (in ECEF)
    // Returns a SimState with ECI velocity and ECI position
    public static SimState SurfaceRestECIVelocity(SimState statein)
    {
        // At rest in ECEF, so v = 0
        var ecefState = new SimState
        {
            r = statein.r,
            v = Vector3.Zero,
            t = statein.t,
            mass = statein.mass
        };
        var eciState = ECEFtoECI(ecefState);
        return eciState;
    }
    

    public static Vector3 SphericalToCartesian(double latitude, double longitude, double r)
    {
        double rX = r * Math.Cos(latitude) * Math.Cos(longitude);
        double rY = r * Math.Cos(latitude) * Math.Sin(longitude);
        double rZ = r * Math.Sin(latitude);

        return new Vector3((float)rX, (float)rY, (float)rZ);

    }

    // Get the East unit vector from position in ECI
    public static Vector3 GetEastUnit(Vector3 position)
    {
        Vector3 east = new Vector3(-position.Y, position.X, 0);
        return Vector3.Normalize(east);
    }

    // Get the North unit vector from position in ECI
    public static Vector3 GetNorthUnit(Vector3 position)
    {
        Vector3 rHat = Vector3.Normalize(position);
        Vector3 eastHat = GetEastUnit(position);
        Vector3 north = Vector3.Normalize(Vector3.Cross(rHat, eastHat)); // Up direction

        return north;
    }

    // Compute velocity vector from position, speed, flight path angle, and heading
    public static Vector3 ComputeVelocity(Vector3 position, double speed, double gamma, double heading)
    {
        Vector3 rHat = Vector3.Normalize(position);
        Vector3 eastHat = GetEastUnit(position);
        Vector3 northHat = GetNorthUnit(position);

        // Local horizontal direction
        Vector3 horizontalDirection = (float)(Math.Cos(heading)) * northHat + (float)(Math.Sin(heading)) * eastHat;

        // Final velocity vector
        Vector3 velocityDirection = (float)(Math.Cos(gamma)) * horizontalDirection + (float)(Math.Sin(gamma)) * rHat;

        return (float)speed * velocityDirection;
    }

    public static (float b, float c) CalculateSphericalAAS(float a, float beta, float alpha)
    {
        // alpha = 90 - alpha
        double b = Math.Asin((Math.Sin(a) * Math.Sin(beta)) / Math.Sin(alpha));

        double tanaMb = Math.Tan(0.5f * (a - b));
        double sinalphaPbeta = Math.Tan(0.5f * (alpha + beta));
        double sinalphaMbeta = Math.Tan(0.5f * (alpha - beta));

        double c = 2 * Math.Atan(tanaMb * sinalphaPbeta / sinalphaMbeta);

        //alternative approach
        b = Math.Asin(Math.Tan(a) / Math.Tan(alpha));

        return ((float)b, (float)c);
    }

    public static Vector3 CalcOrbitNormal(float inc, float LAN)
    {
        //inc = (float)DegToRad(inc); LAN = (float)DegToRad(LAN);

        Vector3 vout = new Vector3(0, 0, 1);
        vout.X = (float)(Math.Sin(inc) * Math.Sin(LAN));
        vout.Y = (float)(-Math.Sin(inc) * Math.Cos(LAN));
        vout.Z = (float)Math.Cos(inc);

        return vout;

    }

    
    public static Vector3 CalcGravVector(float mu, Vector3 r21)
    {
        Vector3 a = Constants.Mu * -r21 / (float)Math.Pow(r21.Length(), 3);

        return a;
    }
    public static Vector3 RodriguesRotation(Vector3 vin, Vector3 axis, double angle)
    {
        Vector3 vout;

        double angleRad = angle;
        double cosAngle = Math.Cos(angleRad);
        double sinAngle = Math.Sin(angleRad);

        vout = vin * (float)cosAngle
            + Vector3.Cross(axis, vin) * (float)sinAngle
            + axis * Vector3.Dot(axis, vin) * (float)(1 - cosAngle);

        return vout;
    }



    public static double DegToRad(double degrees)
    {
        double rad = degrees * Math.PI / 180.0;

        return rad;
    }

    public static double RadToDeg(double rad)
    {
        double degrees = rad / (Math.PI / 180.0);

        return degrees;
    }

    public static float[] GetKeplerPoints(KeplerianElements elements, int numPoints = 500)
    {
        return elements.GetOrbitPoints(numPoints);
    }

    // Backward compatibility overload
    public static float[] GetKeplerPoints(Dictionary<string, double> elements)
    {
        var keplerElements = new KeplerianElements
        {
            SemiMajorAxis = elements["a"],
            Eccentricity = elements["e"],
            Apoapsis = elements["ap"],
            Periapsis = elements["pe"],
            ArgumentOfPeriapsis = elements["omega"],
            LongitudeOfAscendingNode = elements["LAN"],
            Inclination = elements["i"],
            MeanAnomaly = elements["M"]
        };
        return keplerElements.GetOrbitPoints();
    }
    public static void PrintDebugParamHeader()
    {
        Console.WriteLine(
        "{0,8} {1,8} {2,8} {3,8}  {4,8} {5,8} {6,8}  {7,8} {8,8} {9,8}  {10,8} {11,8} {12,8}",
        "Tgo",
        "VgoX", "VgoY", "VgoZ",
        "RdX", "RdY", "RdZ",
        "GravX", "GravY", "GravZ",
        "BiasX", "BiasY", "BiasZ"
    );
    }

    public static void PrintDebugParams(Upfg guidance)
    {

        Console.WriteLine(
        "{0,8:F3} {1,8:F3} {2,8:F3} {3,8:F3}  {4,8:F3} {5,8:F3} {6,8:F3}  {7,8:F3} {8,8:F3} {9,8:F3}  {10,8:F3} {11,8:F3} {12,8:F3}",
        guidance.PrevVals.tgo,
        guidance.PrevVals.vgo.X, guidance.PrevVals.vgo.Y, guidance.PrevVals.vgo.Z,
        guidance.PrevVals.rd.X, guidance.PrevVals.rd.Y, guidance.PrevVals.rd.Z,
        guidance.PrevVals.rgrav.X, guidance.PrevVals.rgrav.Y, guidance.PrevVals.rgrav.Z,
        guidance.PrevVals.rbias.X, guidance.PrevVals.rbias.Y, guidance.PrevVals.rbias.Z);
    }

    public static string PrintUPFG(Upfg guidance, Simulator sim)
    {

        // Transposed table: each parameter is a row
        var upfgTable = new ConsoleTable("PARAM", "VALUE");
        upfgTable.AddRow("TB", guidance.PrevVals.tb.ToString("F1").PadLeft(6));
        upfgTable.AddRow("TGO", guidance.PrevVals.tgo.ToString("F1").PadLeft(6));
        upfgTable.AddRow("VGO", guidance.PrevVals.vgo.Length().ToString("F1").PadLeft(6));
        upfgTable.AddRow("RGO", (guidance.PrevVals.rd - sim.State.r).Length().ToString("F1").PadLeft(6));
        upfgTable.AddRow("RGRAV", guidance.PrevVals.rgrav.Length().ToString("F1").PadLeft(6));
        upfgTable.AddRow("RBIAS", guidance.PrevVals.rbias.Length().ToString("F1").PadLeft(6));
        return upfgTable.ToString();
    }

    public static string PrintVars(Simulator sim, UPFGTarget tgt, Vehicle veh)
    {


        Console.CursorVisible = false;

        // Console.WriteLine("-------- ORBITAL ELEMENTS --------");
        var transposedTable = new ConsoleTable(" ", "ACTUAL", "TARGET");

        // Add each orbital element as a row, with corresponding values from sim.State.Kepler and tgt
        transposedTable.AddRow("AP",
            sim.State.Kepler.Apoapsis.ToString("F1").PadLeft(6),
            tgt.ap.ToString("F1").PadLeft(6))
        .AddRow("PE",
            sim.State.Kepler.Periapsis.ToString("F1").PadLeft(6),
            tgt.pe.ToString("F1").PadLeft(6))
        .AddRow("INC",
            sim.State.Kepler.Inclination.ToString("F2").PadLeft(6),
            Utils.RadToDeg(tgt.inc).ToString("F2").PadLeft(6))
        .AddRow("LAN",
            sim.State.Kepler.LongitudeOfAscendingNode.ToString("F2").PadLeft(6),
            Utils.RadToDeg(tgt.LAN).ToString("F2").PadLeft(6))
        .AddRow("ECC",
            sim.State.Kepler.Eccentricity.ToString("F4").PadLeft(6),
            tgt.ecc.ToString("F4").PadLeft(6));

        // transposedTable.Write(Format.Alternative);

        // Console.SetCursorPosition(40, 5);
        // Console.WriteLine("-----------");
        // Console.SetCursorPosition(40, 6);
        // Console.WriteLine("UPFG STATUS:");

        // Console.SetCursorPosition(40, 8);
        // if (false)
        // {
        //     Console.WriteLine("CONVERGED");
        // }
        // else
        // {
        //     Console.WriteLine("UPFG CONVERGING");
        // }
        // Console.SetCursorPosition(40, 9);
        // Console.WriteLine("-----------");

        // Console.SetCursorPosition(40, 11);
        // Console.WriteLine("VEHICLE STATUS:");

        // Console.SetCursorPosition(40, 13);
        // Console.WriteLine("VEL:    " + sim.State.v.Length().ToString("F1"));
        // Console.SetCursorPosition(40, 14);
        // Console.WriteLine("MASS:   " + sim.State.mass.ToString("F1"));

        // Console.SetCursorPosition(40, 15);
        // Console.WriteLine("STAGES: " + veh.Stages.Count().ToString());

        return transposedTable.ToString();

        // Console.WriteLine("----- ORBITAL ELEMENTS -----");
        // var orbitTable = new ConsoleTable("ap","pe", "INC", "LAN", "ECC");
        // orbitTable.AddRow(
        //     sim.State.Kepler["ap"].ToString("F1").PadLeft(6),
        //     sim.State.Kepler["pe"].ToString("F1").PadLeft(6),
        //     sim.State.Kepler["i"].ToString("F2").PadLeft(6),
        //     sim.State.Kepler["LAN"].ToString("F2").PadLeft(6),
        //     sim.State.Kepler["e"].ToString("F4").PadLeft(6)
        // )
        // .AddRow(tgt.ap, tgt.pe, Utils.RadToDeg(tgt.inc), Utils.RadToDeg(tgt.LAN), tgt.ecc);
        // orbitTable.Write(Format.Alternative);

        // Console.WriteLine();


    }

    public static float CalcDownRange(SimState end, SimState start)
    {
        Vector3 r1 = Vector3.Normalize(end.r);
        Vector3 r2 = Vector3.Normalize(start.r);

        double angle = Math.Acos(Vector3.Dot(r1, r2));

        double downrange = angle * Constants.Re;

        return (float)downrange;

    }

    public static void PlotTrajectory(Simulator sim)
    {

        List<float> xp = new();
        List<float> yp = new();
        List<float> zp = new();
        List<float> tp = new();
        List<float> drp = new();
        List<float> altp = new();


        for (int i = 0; i < sim.History.Count(); i++)
        {
            xp.Add(sim.History[i].r.X);
            yp.Add(sim.History[i].r.Y);
            zp.Add(sim.History[i].r.Z);
            tp.Add(sim.History[i].t);
            drp.Add(CalcDownRange(sim.History[i], sim.History[0]));
            altp.Add((float)sim.History[i].Misc["altitude"]);
        }

        ScottPlot.Plot xy = new();
        xy.Add.Scatter(xp, yp);
        xy.SavePng("xy.png", 400, 300);

        ScottPlot.Plot xz = new();
        xz.Add.Scatter(xp, zp);
        xz.SavePng("xz.png", 400, 300);

        ScottPlot.Plot yz = new();
        yz.Add.Scatter(yp, zp);
        yz.SavePng("yz.png", 400, 300);

        ScottPlot.Plot downrange = new();
        downrange.Add.Scatter(drp, altp);
        downrange.SavePng("downrange.png", 400, 300);



    }

}





public class Environment
{
    // Environment properties here
}