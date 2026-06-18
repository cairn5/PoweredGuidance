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

public class Simulator
{
    public Vehicle SimVehicle { get; set; }
    public Dictionary<string, object> Mission { get; private set; }
    public SimState State { get; set; }
    public Dictionary<string, Vector3> Iteration { get; private set; }
    public float dt { get; private set; }
    public float dtguidance { get; set; }
    public double simspeed { get; private set; }
    public double throttle { get; private set; }
    public Vector3 ThrustVector { get; private set; }
    public List<SimState> History { get; set; }
    public double historylen { get; set; }


    public Simulator()
    {
        SimVehicle = new Vehicle();
        Mission = new Dictionary<string, object> { };
        State = new SimState();

        State.CalcMiscParams();
        State.CartToKepler();

        Iteration = new Dictionary<string, Vector3>();

        ThrustVector = new Vector3(0, 0, 0);
        dt = 1;
        historylen = 100; // Set default history length

        History = new List<SimState>();

    }

    public void SetVehicle(Vehicle vehiclein)
    {
        SimVehicle = vehiclein;
        State.mass = (float)SimVehicle.CurrentStage.MassTotal;
    }

    public SimState GetVesselState()
    {
        return State;
    }

    public void SetVesselStateFromLatLongAir(Dictionary<string, double> initial)
    {
        double altitude = initial["altitude"];
        double fpa = initial["fpa"];
        double latitude = initial["latitude"];
        double longitude = initial["longitude"];
        double heading = initial["heading"];
        double speed = initial["speed"];

        fpa = Utils.DegToRad(fpa);
        heading = Utils.DegToRad(heading);

        double r = Constants.Re + altitude * 1000;

        State.r = Utils.SphericalToCartesian(latitude, longitude, r);
        State.v = Utils.ComputeVelocity(State.r, speed, fpa, heading);
        State.t = 0;

        State.CartToKepler();
        State.CalcMiscParams();

    }

    public void SetVesselStateFromLatLongGround(Dictionary<string, double> initial)
    {
        double latitude = initial["latitude"];
        double longitude = initial["longitude"];

        double r = Constants.Re;

        State.r = Utils.SphericalToCartesian(latitude, longitude, r);
        State.v = Vector3.Zero;
        State = Utils.SurfaceRestECIVelocity(State); //transform for earth's

        State.CartToKepler();
        State.CalcMiscParams();

    }

    public void SetTimeStep(float timestep)
    {
        dt = timestep;
    }

    public void SetGuidance(Vector3 thrustvector, Stage stage)
    {
        ThrustVector = thrustvector * (float)stage.Thrust;
    }


    public float CalcMassDecrement()
    {
        return (float)(dt * ThrustVector.Length() / (Constants.g0 * SimVehicle.CurrentStage.Isp));
    }

    private Vector3 CalcAccel(Vector3 position, Vector3 velocity, float time)
    {
        Vector3 gravAccel = Utils.CalcGravVector(Constants.Mu, position);
        Vector3 thrustAccel = ThrustVector / State.mass;
        return gravAccel + thrustAccel;
    }

    private void CalcVelAndPos()
    {
        // Current state
        Vector3 r0 = State.r;
        Vector3 v0 = State.v;
        float t0 = State.t;
        
        // RK4 for velocity (dv/dt = acceleration)
        Vector3 k1_v = CalcAccel(r0, v0, t0);
        Vector3 k1_r = v0;
        
        Vector3 k2_v = CalcAccel(r0 + k1_r * dt/2, v0 + k1_v * dt/2, t0 + dt/2);
        Vector3 k2_r = v0 + k1_v * dt/2;
        
        Vector3 k3_v = CalcAccel(r0 + k2_r * dt/2, v0 + k2_v * dt/2, t0 + dt/2);
        Vector3 k3_r = v0 + k2_v * dt/2;
        
        Vector3 k4_v = CalcAccel(r0 + k3_r * dt, v0 + k3_v * dt, t0 + dt);
        Vector3 k4_r = v0 + k3_v * dt;
        
        // Final RK4 step
        Iteration["v"] = v0 + (k1_v + 2*k2_v + 2*k3_v + k4_v) * dt / 6;
        Iteration["r"] = r0 + (k1_r + 2*k2_r + 2*k3_r + k4_r) * dt / 6;
    }

    private void UpdateStateHistory()
    {

        History.Add((SimState)State.Clone());

        while( History.Count > historylen / dt)
        {
            History.RemoveAt(0);
        }

        State.r = Iteration["r"];
        State.v = Iteration["v"];
        State.t = State.t + dt;
        State.mass = State.mass - (float)(dt * ThrustVector.Length() / (Constants.g0 * SimVehicle.CurrentStage.Isp));

        State.CartToKepler();
        State.CalcMiscParams();

    }

    public void StepForward()
    {
        CalcVelAndPos();  // Combined RK4 step
        UpdateStateHistory();
    }

    public void LoadSimVarsFromJson(string path)
    {
        string json = File.ReadAllText(path);
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement.GetProperty("Simulator");

        double startLat = root.GetProperty("startLat").GetDouble();
        double startLong = root.GetProperty("startLong").GetDouble();
        double startGround = root.GetProperty("startGround").GetDouble();
        double airVel = root.GetProperty("airVel").GetDouble();
        double airFpa = root.GetProperty("airFpa").GetDouble();
        float dtlocal = root.GetProperty("dtsim").GetSingle();
        double simspeedlocal = root.GetProperty("speed").GetDouble();
        double altitude = root.GetProperty("altitude").GetSingle();
        double headinglocal = root.GetProperty("heading").GetSingle();
        double historylenLocal = root.GetProperty("historylength").GetDouble();
        
        // Set celestial body constants
        if (root.TryGetProperty("Body", out var bodyProperty))
        {
            string bodyName = bodyProperty.GetString() ?? "Earth";
            Constants.SetCelestialBody(bodyName);
        }
        else
        {
            // Default to Earth if not specified
            Constants.SetCelestialBody("Earth");
        }


        // Set initial state using these values
        // Convert degrees to radians where needed
        double latitude = Utils.DegToRad(startLat);
        double longitude = Utils.DegToRad(startLong);

        if (startGround == 0)
        {
            double speed = airVel;
            double fpa = airFpa; // Flight path angle in degrees
            double heading = headinglocal; // Default east, can be parameterized

            var initial = new Dictionary<string, double>
            {
                {"altitude", altitude},
                {"fpa", fpa},
                {"latitude", latitude},
                {"longitude", longitude},
                {"heading", heading},
                {"speed", speed}
            };
            SetVesselStateFromLatLongAir(initial);
        }
        if (startGround == 1)
        {

            var initial = new Dictionary<string, double>
            {
                {"latitude", latitude},
                {"longitude", longitude}
            };
            SetVesselStateFromLatLongGround(initial);
        }
        SetTimeStep(dtlocal);
        this.simspeed = simspeedlocal;
        historylen = historylenLocal;
    }


}

public class KeplerianElements
{
    public double SemiMajorAxis { get; set; }       // a - semi-major axis (m)
    public double Eccentricity { get; set; }       // e - eccentricity 
    public double Inclination { get; set; }        // i - inclination (degrees)
    public double ArgumentOfPeriapsis { get; set; } // ω - argument of periapsis (degrees)
    public double LongitudeOfAscendingNode { get; set; } // Ω - longitude of ascending node (degrees)
    public double MeanAnomaly { get; set; }         // M - mean anomaly (degrees)
    public double Apoapsis { get; set; }           // ap - apoapsis altitude (m)
    public double Periapsis { get; set; }          // pe - periapsis altitude (m)

    public KeplerianElements() { }

    public KeplerianElements(Vector3 r, Vector3 v)
    {
        CalculateFromCartesian(r, v);
    }

    public void CalculateFromCartesian(Vector3 r, Vector3 v)
    {
        Vector3 rdot = v;
        double mu = Constants.Mu;

        Vector3 h = Vector3.Cross(r, rdot);
        double hMag = h.Length();

        Vector3 eVec = Vector3.Cross(rdot, h) / Constants.Mu - r / r.Length();
        double eMag = eVec.Length();

        Vector3 n = Vector3.Cross(new Vector3(0, 0, 1), h);
        double nMag = n.Length();

        double i = Math.Acos(Math.Clamp(h.Z / hMag, -1.0, 1.0));

        // True anomaly
        double cosV = Vector3.Dot(eVec, r) / (eMag * r.Length());
        double v_ = Math.Acos(Math.Clamp(cosV, -1.0, 1.0));
        if (Vector3.Dot(r, rdot) < 0)
        {
            v_ = 2 * Math.PI - v_;
        }

        // Eccentric anomaly
        double tan_v2 = Math.Tan(v_ / 2);
        double E = 2 * Math.Atan2(tan_v2 * Math.Sqrt(1 - eMag), Math.Sqrt(1 + eMag));

        // RAAN (Longitude of Ascending Node)
        double LAN = 0;
        if (nMag > 1e-10)
        {
            double cosLAN = Math.Clamp(n.X / nMag, -1.0, 1.0);
            LAN = Math.Acos(cosLAN);
            if (n.Y < 0)
            {
                LAN = 2 * Math.PI - LAN;
            }
        }

        // Argument of Periapsis
        double omega = 0;
        if (nMag > 1e-10 && eMag > 1e-10)
        {
            double cosOmega = Math.Clamp(Vector3.Dot(n, eVec) / (nMag * eMag), -1.0, 1.0);
            omega = Math.Acos(cosOmega);
            if (eVec.Z < 0)
            {
                omega = 2 * Math.PI - omega;
            }
        }

        // Mean anomaly
        double M = E - eMag * Math.Sin(E);

        // Semi-major axis
        double a = 1 / ((2 / r.Length()) - (Vector3.Dot(rdot, rdot) / mu));

        //ap and pe
        double rp = a * (1 - eMag); // periapsis
        double ra = a * (1 + eMag); // apoapsis

        // Set properties
        SemiMajorAxis = a;
        Eccentricity = eMag;
        Apoapsis = ra;
        Periapsis = rp;
        ArgumentOfPeriapsis = Utils.RadToDeg(omega);
        LongitudeOfAscendingNode = Utils.RadToDeg(LAN);
        Inclination = Utils.RadToDeg(i);
        MeanAnomaly = Utils.RadToDeg(M);
    }

    public float[] GetOrbitPoints(int numPoints = 500)
    {
        // Convert degrees to radians
        double omega = Utils.DegToRad(ArgumentOfPeriapsis);
        double LAN = Utils.DegToRad(LongitudeOfAscendingNode);
        double i = Utils.DegToRad(Inclination);
        double a = SemiMajorAxis;
        double e = Eccentricity;

        // Solve Kepler's equation
        double SolveKepler(double M, double ecc, double tol = 1e-8)
        {
            double E = ecc < 0.8 ? M : Math.PI;
            for (int iter = 0; iter < 100; iter++)
            {
                double delta = (E - ecc * Math.Sin(E) - M) / (1 - ecc * Math.Cos(E));
                E -= delta;
                if (Math.Abs(delta) < tol) break;
            }
            return E;
        }

        // Generate orbit points
        double[] Mvals = Linspace(0, 2 * Math.PI, numPoints);
        double[] Evals = Mvals.Select(M => SolveKepler(M, e)).ToArray();

        double[] nuVals = Evals.Select(E =>
            2 * Math.Atan2(Math.Sqrt(1 + e) * Math.Sin(E / 2), Math.Sqrt(1 - e) * Math.Cos(E / 2))
        ).ToArray();

        double[] radii = Evals.Select(E => a * (1 - e * Math.Cos(E))).ToArray();

        // Position in perifocal frame
        double[] x_p = radii.Zip(nuVals, (r, nu) => r * Math.Cos(nu)).ToArray();
        double[] y_p = radii.Zip(nuVals, (r, nu) => r * Math.Sin(nu)).ToArray();
        double[] z_p = new double[numPoints]; // all zeros

        // Rotation matrix
        double cosO = Math.Cos(LAN);
        double sinO = Math.Sin(LAN);
        double cosw = Math.Cos(omega);
        double sinw = Math.Sin(omega);
        double cosi = Math.Cos(i);
        double sini = Math.Sin(i);

        double[,] R = new double[3, 3]
        {
            { cosO * cosw - sinO * sinw * cosi, -cosO * sinw - sinO * cosw * cosi, sinO * sini },
            { sinO * cosw + cosO * sinw * cosi, -sinO * sinw + cosO * cosw * cosi, -cosO * sini },
            { sinw * sini, cosw * sini, cosi }
        };

        // Rotate to inertial frame
        float[] trajData = new float[numPoints * 3];

        for (int idx = 0; idx < numPoints; idx++)
        {
            double x = x_p[idx];
            double y = y_p[idx];
            double z = z_p[idx];

            double xp = R[0, 0] * x + R[0, 1] * y + R[0, 2] * z;
            double yp = R[1, 0] * x + R[1, 1] * y + R[1, 2] * z;
            double zp = R[2, 0] * x + R[2, 1] * y + R[2, 2] * z;

            trajData[idx * 3] = (float)yp;
            trajData[idx * 3 + 1] = (float)zp;
            trajData[idx * 3 + 2] = (float)xp;
        }

        return trajData;
    }

    private static double[] Linspace(double start, double end, int num)
    {
        double[] result = new double[num];
        double step = (end - start) / (num - 1);
        for (int i = 0; i < num; i++)
        {
            result[i] = start + step * i;
        }
        return result;
    }

    // Backward compatibility - create dictionary representation
    public Dictionary<string, double> ToDictionary()
    {
        return new Dictionary<string, double>
        {
            { "a", SemiMajorAxis },
            { "e", Eccentricity },
            { "ap", Apoapsis },
            { "pe", Periapsis },
            { "omega", ArgumentOfPeriapsis },
            { "LAN", LongitudeOfAscendingNode },
            { "i", Inclination },
            { "M", MeanAnomaly }
        };
    }

    // Calculate orbital period in seconds using Kepler's Third Law: T = 2π√(a³/μ)
    public double GetOrbitalPeriod()
    {
        return 2 * Math.PI * Math.Sqrt(Math.Pow(SemiMajorAxis, 3) / Constants.Mu);
    }
}

public class SimState
{
    public Vector3 r { get; set; } = new Vector3(0, 0, 0);
    public Vector3 v { get; set; } = new Vector3(0, 0, 0);
    public float t { get; set; } = 0;
    public float mass { get; set; } = 1e5f;
    public KeplerianElements Kepler { get; set; } = new KeplerianElements();
    public Dictionary<string, double> Misc { get; set; } = new Dictionary<string, double> { };

    public void CalcMiscParams()
    {
        Misc = new Dictionary<string, double>
        {
            {"longitude", Math.Atan2(r.Y, r.X)},
            {"latitude", Math.Atan2(r.Z, Math.Pow(r.X * r.X + r.Y * r.Y, 0.5))},
            {"altitude", Math.Pow(r.X * r.X + r.Y * r.Y + r.Z * r.Z, 0.5) - Constants.Re}
        };
    }

    public object Clone()
    {
        return this.MemberwiseClone();
    }

    public void CartToKepler()
    {
        Kepler.CalculateFromCartesian(r, v);
    }

}
