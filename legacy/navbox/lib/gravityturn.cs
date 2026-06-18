using System;
using System.Collections.Generic;
using System.Data;
using System.Dynamic;
using System.Numerics;
using System.Reflection;
using System.Security;
using ScottPlot.LayoutEngines;

namespace lib;

public class GravityTurn
{
    public double pitchAngle { get; set; } = Utils.DegToRad(5f);
    public double pitchTime { get; set; } = 100;
    public double heading { get; set; } = -1;
    public int guidanceMode { get; set; } = 0; // 0 = straight up, 1 = initial pitchover, 2 = following ECEF prograde
    public Vector3 guidance { get; set; } = Vector3.Zero;
    public bool SetupFlag { get; set; } = false;

    public void calcPitchAngle(Simulator mainSim, Vehicle veh, double heading)
    {
        Simulator sim = new();
        sim.SetVehicle(veh);
        sim.State = (SimState)mainSim.State.Clone();

    }

    public string phase { get; set; } = "vertical";
    public double kickAngle { get; set; } = Utils.DegToRad(5.0);
    public double kickVelocity { get; set; } = 100.0; // m/s
    public double currentPitch { get; set; } = 0.0; // Track current pitch angle
    public double maxPitchRate { get; set; } = Utils.DegToRad(2.0); // Max pitch rate in rad/s (default 2 deg/s)
    public double dt { get; set; } = 0.1; // Time step in seconds

    public void PitchProgram(Simulator sim)
    {
        double currentVelocity = sim.State.v.Length();
        
        // Calculate velocity angle (flight path angle)
        Vector3 position = sim.State.r;
        Vector3 velocity = sim.State.v;
        Vector3 radialUnit = Vector3.Normalize(position);
        double velocityAngle = Math.Asin(Vector3.Dot(velocity, radialUnit) / velocity.Length());

        double desiredPitch;

        if (phase == "vertical")
        {
            // Phase 1: Vertical flight until kick velocity
            if (currentVelocity > kickVelocity)  // Start kick when velocity > 100 m/s
            {
                desiredPitch = kickAngle;
                phase = "hold";
            }
            else
            {
                desiredPitch = 0;
            }
        }
        else if (phase == "hold")
        {
            // Phase 2: Hold kick angle until velocity vector coincides with kick angle
            double angleDifference = velocityAngle - kickAngle;
            if (angleDifference > 0.0)
            {
                phase = "follow";
                desiredPitch = velocityAngle;
            }
            else
            {
                desiredPitch = kickAngle;  // Continue holding kick angle
            }
        }
        else if (phase == "follow")
        {
            // Phase 3: Follow velocity vector
            desiredPitch = velocityAngle;
        }
        else
        {
            desiredPitch = 0; // Default fallback
        }

        // Update the pitch angle
        pitchAngle = desiredPitch;

        // Apply rotational dynamics constraint
        double maxPitchChange = maxPitchRate * dt;
        double pitchError = desiredPitch - currentPitch;

        double calculatedPitch;

        // Limit the pitch change rate
        if (Math.Abs(pitchError) > maxPitchChange)
        {
            if (pitchError > 0)
            {
                calculatedPitch = currentPitch + maxPitchChange;
            }
            else
            {
                calculatedPitch = currentPitch - maxPitchChange;
            }
        }
        else
        {
            calculatedPitch = desiredPitch;
        }

        // Update current pitch and pitch angle
        currentPitch = calculatedPitch;
        pitchAngle = calculatedPitch;
    }
    public void step(Simulator sim, UPFGTarget target, Vehicle veh, GravityTurnModeConfig config)
    {

        if (!SetupFlag)
        {
            pitchAngle = Utils.DegToRad(config.PitchAngle);
            pitchTime = config.PitchTime;
            heading = Utils.CalcLaunchAzimuthRotating(sim, target);

            calcPitchAngle(sim, veh, heading);

            SetupFlag = true;

            Console.WriteLine("Gravity Turn :: Pitch Angle: " + pitchAngle + " Pitch Time: " + pitchTime + " Heading: " + heading);
        }

        if (guidanceMode == 0)
        {
            guidance = Vector3.Cross(Utils.GetEastUnit(sim.State.r), Utils.GetNorthUnit(sim.State.r));

            if (sim.State.t > pitchTime)
            {
                guidanceMode = 1;
            }

        }

        if (guidanceMode == 1)
        {
            // Rotate up vector by pitch angle, then rotate to desired heading
            Vector3 north = Utils.GetNorthUnit(sim.State.r);
            Vector3 east = Utils.GetEastUnit(sim.State.r);
            Vector3 up = -Vector3.Cross(north, east);
            Vector3 transformedVector = Utils.RodriguesRotation(up, east, -(float)pitchAngle);

            guidance = Utils.RodriguesRotation(transformedVector, up, -(float)(heading));

            // If dot product betwen thrust vector and velocity vector is less than value, hold prograde
            Vector3 localVelNorm = Vector3.Normalize(Utils.ECItoECEF(sim.State).v);

            float dotProduct = Vector3.Dot(guidance, localVelNorm);
            if (dotProduct < 0.99f)
            {
                guidanceMode = 2;
            }

        }

        if (guidanceMode == 2)
        {
            guidance = Vector3.Normalize(Utils.ECItoECEF(sim.State).v);
        }

    }

}