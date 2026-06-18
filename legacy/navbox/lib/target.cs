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
using System.Diagnostics.Tracing;
using ConsoleTables;

public class UPFGTarget : IGuidanceTarget
{
    public Vector3 rdes { get; private set; } = new Vector3(0, 0, 0);
    public float radius { get; private set; } = 0;
    public float velocity { get; private set; } = 0;
    public float fpa { get; private set; } = 0;
    public Vector3 normal { get; private set; } = new Vector3(0, 0, 0);
    public float inc { get; private set; } = 0;
    public float LAN { get; private set; } = 0;
    public float ap { get; private set; } = 0;
    public float pe { get; private set; } = 0;
    public float ecc { get; private set; } = 0;

    public void Set(Dictionary<string, float> targetParams, Simulator sim)
    {
        SimState state = sim.State;

        pe = targetParams["pe"] * 1000 + Constants.Re;
        ap = targetParams["ap"] * 1000 + Constants.Re;

        ecc = (ap - pe) / (ap + pe);

        if (targetParams.ContainsKey("alt"))
        {
            radius = targetParams["alt"] * 1000 + Constants.Re;
        }
        else
        {
            radius = pe;
        }
            
        float sma = (pe + ap) / 2;
        float vpe = (float)Math.Pow(Constants.Mu * (2 / pe - 1 / sma), 0.5);
        velocity = (float)Math.Pow(Constants.Mu * (2/radius - 1/sma), 0.5);
        float srm = pe * vpe;
        fpa = (float)Math.Acos(srm / (velocity * radius));

        inc = targetParams["inc"];
        inc = (float)Utils.DegToRad(inc);
        // float longitude = (float)Utils.DegToRad(state.Misc["longitude"]);
        // float latitude = (float)Utils.DegToRad(state.Misc["latitude"]);

        if (targetParams.ContainsKey("LAN"))
        {
            LAN = targetParams["LAN"];
        }
        else
        {

            (float b, float c) = Utils.CalculateSphericalAAS((float)state.Misc["latitude"], 1.57f, inc);
            LAN = (float)state.Misc["longitude"] - b;
            if (float.IsNaN(LAN))
            {
                LAN = 0;
            }
        }

        normal = Utils.CalcOrbitNormal(inc, LAN);


    }

    public void setFromLatLong(Dictionary<string, double> initial, Simulator sim)
    {
        SimState state = sim.State;

        double targetLatitude = initial["latitude"];
        double targetLongitude = initial["longitude"];
        double targetAltitude = 0; // Surface intersection (apoapsis at surface)
        double targetSpeed = 5; // 5 m/s eastward velocity at apoapsis

        // Create highly elliptical orbit intersecting surface
        double apRadius = Constants.Re + targetAltitude;  // Apoapsis at surface

        // Calculate periapsis from target velocity at apoapsis
        // Using vis-viva equation: v² = μ(2/r - 1/a)
        // At apoapsis: v_ap² = μ(2/r_ap - 1/a)
        // Solve for semi-major axis: a = 1 / (2/r_ap - v_ap²/μ)
        double sma = 1.0 / (2.0 / apRadius - (targetSpeed * targetSpeed) / Constants.Mu);
        double peRadius = 2 * sma - apRadius; // Periapsis from a = (pe + ap) / 2

        // Set orbital parameters directly
        ap = (float)apRadius; // Apoapsis at surface
        pe = (float)peRadius; // Calculated periapsis

        ecc = (ap - pe) / (ap + pe);
        radius = ap;

        float vpe = (float)Math.Pow(Constants.Mu * (2 / pe - 1 / sma), 0.5);
        velocity = (float)Math.Pow(Constants.Mu * (2 / radius - 1 / sma), 0.5);
        float srm = pe * vpe;
        fpa = (float)Math.Acos(srm / (velocity * radius));

        inc = (float)Utils.DegToRad(targetLatitude); // Inclination = latitude
        LAN = (float)Utils.DegToRad(targetLongitude + 90); // LAN 90 degrees from target longitude

        normal = Utils.CalcOrbitNormal(inc, LAN);

        rdes = Utils.SphericalToCartesian(Utils.DegToRad(initial["latitude"]), Utils.DegToRad(initial["longitude"]), radius);
    }

    public string userOutput(Simulator sim)
    {
        Console.CursorVisible = false;

        // Console.WriteLine("-------- ORBITAL ELEMENTS --------");
        var transposedTable = new ConsoleTable(" ", "ACTUAL", "TARGET");

        // Add each orbital element as a row, with corresponding values from sim.State.Kepler and tgt
        transposedTable.AddRow("AP",
            sim.State.Kepler.Apoapsis.ToString("F1").PadLeft(6),
            ap.ToString("F1").PadLeft(6))
        .AddRow("PE",
            sim.State.Kepler.Periapsis.ToString("F1").PadLeft(6),
            pe.ToString("F1").PadLeft(6))
        .AddRow("INC",
            sim.State.Kepler.Inclination.ToString("F2").PadLeft(6),
            Utils.RadToDeg(inc).ToString("F2").PadLeft(6))
        .AddRow("LAN",
            sim.State.Kepler.LongitudeOfAscendingNode.ToString("F2").PadLeft(6),
            Utils.RadToDeg(LAN).ToString("F2").PadLeft(6))
        .AddRow("ECC",
            sim.State.Kepler.Eccentricity.ToString("F4").PadLeft(6),
            ecc.ToString("F4").PadLeft(6));

        return transposedTable.ToString();

    }

}
