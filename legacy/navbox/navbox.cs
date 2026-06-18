// See https://aka.ms/new-console-template for more information
using System;
using System.Collections.Generic;
using System.Numerics;
using System.Security;
using System.Threading;
using System.Threading.Tasks;
using System.Diagnostics;
using ScottPlot.LayoutEngines;
using ConsoleTables;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using System.Drawing;
using System.Drawing.Imaging;
using OpenTK.Windowing.Desktop;
using OpenTK.Windowing.Common;
using OpenTK.Graphics.OpenGL;
using lib;
using lib.graphics;

class Handler
{
    // Shared simulation state
    private static Simulator sharedSim;
    private static GuidanceProgram sharedGuidance;
    private static readonly object simLock = new object();
    private static bool simulationRunning = true;
    private static CancellationTokenSource cancellationTokenSource = new CancellationTokenSource();

    static void Main(string[] args)
    {
        // Initialize simulation
        InitializeSimulation(args);
        
        // Start simulation on background thread
        Task simulationTask = StartSimulationAsync(cancellationTokenSource.Token);
        
        try
        {
            // Run OpenGL visualizer on main thread (required for OpenGL context)
            Visualizer.PlotRealtimeSync(sharedSim, sharedGuidance);
        }
        finally
        {
            // Clean shutdown
            cancellationTokenSource.Cancel();
            simulationRunning = false;
            
            try
            {
                simulationTask.Wait(TimeSpan.FromSeconds(2));
            }
            catch (OperationCanceledException)
            {
                Console.WriteLine("Simulation cancelled successfully.");
            }
        }
    }

    private static void InitializeSimulation(string[] args)
    {
        string missionPath = args.Length > 0 ? args[0] : "missions/saturnV.json";
        
        // Load the complete mission configuration
        Mission mission = Mission.Load(missionPath);
        Vehicle veh = Vehicle.FromStages(mission);
        
        // Initialize simulator with mission configuration
        sharedSim = new Simulator();
        sharedSim.LoadSimVarsFromJson(missionPath);
        sharedSim.SetVehicle(veh);
        
        // Create targets dynamically based on mission orbit configuration
        Dictionary<GuidanceMode, IGuidanceTarget> targets = CreateTargetsFromMission(mission, sharedSim);
        
        // Initialize guidance program with mission configuration
        sharedGuidance = new GuidanceProgram(targets, veh, sharedSim, mission);
    }

    private static Dictionary<GuidanceMode, IGuidanceTarget> CreateTargetsFromMission(Mission mission, Simulator sim)
    {
        var targets = new Dictionary<GuidanceMode, IGuidanceTarget>();
        
        // Create primary target from mission target configuration
        IGuidanceTarget primaryTarget = TargetFactory.CreateTarget(mission.Target, sim);
        
        // Create gravity turn target that's compatible with GravityTurnMode (needs to be UPFGTarget)
        UPFGTarget gravityTurnTarget = new UPFGTarget();
        // For gravity turn, we can use a simple circular orbit as target or reuse primary target
        if (primaryTarget is UPFGTarget upfgPrimary)
        {
            gravityTurnTarget = upfgPrimary; // Reuse the primary target for gravity turn
        }
        else
        {
            // Create a basic orbital target as fallback
            var basicOrbitParams = new Dictionary<string, float>
            {
                {"pe", 100}, {"ap", 100}, {"inc", 0} // Basic 100km circular orbit
            };
            gravityTurnTarget.Set(basicOrbitParams, sim);
        }
        
        // Check if mission has specific guidance configuration
        if (mission.Guidance?.programConfig?.modes != null)
        {
            // Dynamically assign targets based on guidance mode types defined in mission
            foreach (var modeKvp in mission.Guidance.programConfig.modes)
            {
                if (Enum.TryParse<GuidanceMode>(modeKvp.Key, true, out var guidanceMode))
                {
                    // Assign appropriate target based on mode type
                    switch (modeKvp.Value.type)
                    {
                        case "GravityTurnMode":
                            targets[guidanceMode] = gravityTurnTarget;
                            break;
                        case "UpfgMode":
                        case "PreLaunchMode":
                        case "FinalMode":
                        case "IdleMode":
                        default:
                            targets[guidanceMode] = primaryTarget;
                            break;
                    }
                }
            }
            
            // Also assign primary target to any mode that exists in the sequence but wasn't configured
            if (mission.Guidance.programConfig.sequence != null)
            {
                foreach (var modeName in mission.Guidance.programConfig.sequence)
                {
                    if (Enum.TryParse<GuidanceMode>(modeName, true, out var sequenceMode) && 
                        !targets.ContainsKey(sequenceMode))
                    {
                        targets[sequenceMode] = primaryTarget;
                    }
                }
            }
        }
        else
        {
            // Fallback to default target assignment if no specific configuration
            targets[GuidanceMode.Prelaunch] = primaryTarget;
            targets[GuidanceMode.Ascent] = primaryTarget;
            targets[GuidanceMode.PoweredGuidance] = primaryTarget;
            targets[GuidanceMode.FinalBurn] = primaryTarget;
            targets[GuidanceMode.Idle] = primaryTarget;
        }
        
        return targets;
    }

    private static async Task StartSimulationAsync(CancellationToken cancellationToken)
    {
        Vehicle veh;
        lock (simLock)
        {
            veh = sharedSim.SimVehicle; // Get a reference to work with
        }
        
        // Launch guidance task
        Task guidanceTask = Task.Run(async () =>
        {
            int guidanceIter = 0;
            while (simulationRunning && !cancellationToken.IsCancellationRequested)
            {
                lock (simLock)
                {
                    sharedGuidance.UpdateVehicle(veh);
                    sharedGuidance.Step();
                    sharedSim.SetGuidance(sharedGuidance.GetCurrentSteering(), veh.Stages[0]);
                }
                await Task.Delay((int)(sharedSim.dtguidance * 1000f / sharedSim.simspeed), cancellationToken);
                guidanceIter++;
            }
        }, cancellationToken);

        // Physics loop (fast)
        try
        {
            while (simulationRunning && !cancellationToken.IsCancellationRequested)
            {
                lock (simLock)
                {
                    sharedSim.StepForward();
                    if (sharedSim.State.mass < sharedSim.SimVehicle.CurrentStage.MassDry) //staging logic
                    {
                        if (veh.Stages.Count > 1)
                        {
                            veh.AdvanceStage();
                            sharedSim.SetVehicle(veh);
                        }
                        else
                        {
                            Console.WriteLine("SIMULATION STOPPED - FUEL DEPLETED");
                            break;
                        }
                    }
                }
                await Task.Delay((int)(sharedSim.dt * 1000f / sharedSim.simspeed), cancellationToken);
            }
        }
        catch (OperationCanceledException)
        {
            Console.WriteLine("Physics simulation cancelled.");
        }

        try
        {
            await guidanceTask;
        }
        catch (OperationCanceledException)
        {
            Console.WriteLine("Guidance task cancelled.");
        }
        
        simulationRunning = false;
    }

    // Static methods to safely access simulation data from OpenGL thread
    public static (Vector3 position, Vector3 velocity, double time, float mass) GetCurrentState()
    {
        lock (simLock)
        {
            if (sharedSim?.State != null)
            {
                return (
                    new Vector3(sharedSim.State.r.Y, sharedSim.State.r.Z, sharedSim.State.r.X),
                    new Vector3(sharedSim.State.v.Y, sharedSim.State.v.Z, sharedSim.State.v.X),
                    sharedSim.State.t,
                    sharedSim.State.mass
                );
            }
            return (Vector3.Zero, Vector3.Zero, 0, 0);
        }
    }

    public static List<Vector3> GetTrajectoryHistory()
    {
        lock (simLock)
        {
            if (sharedSim?.History != null)
            {
                var trajectory = new List<Vector3>();
                foreach (var state in sharedSim.History)
                {
                    trajectory.Add(new Vector3(state.r.Y, state.r.Z, state.r.X));
                }
                return trajectory;
            }
            return new List<Vector3>();
        }
    }

    public static (Vector3 steering, GuidanceMode mode) GetGuidanceInfo()
    {
        lock (simLock)
        {
            if (sharedGuidance != null)
            {
                var steering = sharedGuidance.GetCurrentSteering();
                return (
                    new Vector3(steering.Y, steering.Z, steering.X),
                    sharedGuidance.ActiveMode
                );
            }
            return (Vector3.Zero, GuidanceMode.Idle);
        }
    }

    public static bool IsSimulationRunning()
    {
        return simulationRunning;
    }
}