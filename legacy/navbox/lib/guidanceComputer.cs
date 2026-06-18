namespace lib;
using System;
using System.Collections.Generic;
using System.Numerics;
using System.Text.Json;
using System.Linq;
using ConsoleTables;

public enum GuidanceMode
{
    Idle,
    Prelaunch,
    Ascent,
    PoweredGuidance,
    FinalBurn,
    Abort,
}

public interface IGuidanceTarget { }
// Use the existing Target (UPFGTarget) class for UPFG
// Example for another target type
public class GravityTurnTarget : IGuidanceTarget
{
    // Add properties specific to gravity turn targeting if needed
}

public interface IGuidanceMode
{
    bool Converged { get; }
    bool StagingFlag { get; set; }
    void Initialize(JsonElement config);
    GuidanceMode? Step(Simulator sim, IGuidanceTarget? tgt, Vehicle veh, GuidanceProgram program);
    Vector3? GetSteering();
}

public class UpfgMode : IGuidanceMode
{
    private readonly Upfg _upfg = new Upfg();
    private int _modeKey = 1;
    private float _cutoffTime = 5.0f;
    
    public bool Converged => _upfg.ConvergenceFlag;
    public bool StagingFlag { get; set; } = false;
    public Vector3? PrevSteering { get; private set; } = new();

    public void Initialize(JsonElement config)
    {
        if (config.TryGetProperty("ModeKey", out var mode)) _modeKey = mode.GetInt32();
        if (config.TryGetProperty("CutoffTime", out var cutoff)) _cutoffTime = cutoff.GetSingle();
    }

    public GuidanceMode? Step(Simulator sim, IGuidanceTarget? tgt, Vehicle veh, GuidanceProgram program)
    {
        if (tgt is UPFGTarget upfgTarget)
        {
            PrevSteering = sim.ThrustVector;
            if (StagingFlag)
            {
                _upfg.StageEvent();
                StagingFlag = false;
            }
            _upfg.step(sim, veh, upfgTarget, _modeKey);

            if (_upfg.PrevVals.tgo < _cutoffTime)
                    return program.GetNextModeInSequence(GuidanceMode.PoweredGuidance);
            return null;
        }
        return null;
    }

    public Vector3? GetSteering() => Converged ? _upfg.Steering : PrevSteering;

    public string userOutput(Simulator sim)
    {
        // Transposed table: each parameter is a row
        var upfgTable = new ConsoleTable("PARAM", "VALUE");
        upfgTable.AddRow("TB", _upfg.PrevVals.tb.ToString("F1").PadLeft(6));
        upfgTable.AddRow("TGO", _upfg.PrevVals.tgo.ToString("F1").PadLeft(6));
        upfgTable.AddRow("VGO", _upfg.PrevVals.vgo.Length().ToString("F1").PadLeft(6));
        upfgTable.AddRow("RGO", (_upfg.PrevVals.rd - sim.State.r).Length().ToString("F1").PadLeft(6));
        upfgTable.AddRow("RGRAV", _upfg.PrevVals.rgrav.Length().ToString("F1").PadLeft(6));
        upfgTable.AddRow("RBIAS", _upfg.PrevVals.rbias.Length().ToString("F1").PadLeft(6));
        return upfgTable.ToString();
    }
}

public class FinalMode : IGuidanceMode
{
    private float _burnTime = 5.0f;
    private float _initialTime = -1f;
    private Vector3 _prevGuidance = Vector3.Zero;
    
    public bool Converged => true;
    public bool StagingFlag { get; set; } = false;

    public void Initialize(JsonElement config)
    {
        if (config.TryGetProperty("BurnTime", out var burn)) _burnTime = burn.GetSingle();
    }

    public GuidanceMode? Step(Simulator sim, IGuidanceTarget? tgt, Vehicle veh, GuidanceProgram program)
    {
        _prevGuidance = sim.ThrustVector;

        if (_initialTime == -1)
        {
            _initialTime = sim.State.t;
        }

        if (sim.State.t > _initialTime + _burnTime)
        {
            return program.GetNextModeInSequence(GuidanceMode.FinalBurn);
        }
        return null;
    }
    
    public Vector3? GetSteering() => null;
}

public class IdleMode : IGuidanceMode
{
    public bool Converged => true;
    public bool StagingFlag { get; set; } = false;
    
    public void Initialize(JsonElement config) { }
    
    public GuidanceMode? Step(Simulator sim, IGuidanceTarget? tgt, Vehicle veh, GuidanceProgram program)
    {
        return null;
    }
    public Vector3? GetSteering() => Vector3.Zero;
}

public class PreLaunchMode : IGuidanceMode
{
    private float _duration = 0.1f;
    private float? _startTime;
    
    public bool Converged => true;
    public bool StagingFlag { get; set; } = false;

    public void Initialize(JsonElement config)
    {
        if (config.TryGetProperty("Duration", out var dur)) _duration = dur.GetSingle();
    }
    
    public GuidanceMode? Step(Simulator sim, IGuidanceTarget? tgt, Vehicle veh, GuidanceProgram program)
    {
        _startTime ??= sim.State.t;
        
        if (sim.State.t - _startTime >= _duration)
        {
            return program.GetNextModeInSequence(GuidanceMode.Prelaunch);
        }
        return null;
    }
    
    public Vector3? GetSteering() => Vector3.Zero;
}

public class GravityTurnMode : IGuidanceMode
{
    private readonly GravityTurn _gravityTurn = new GravityTurn();
    private float _altitudeThreshold = 30000f;
    private float _pitchAngle = 2.5f;
    private float _pitchTime = 13f;
    
    public bool Converged => false;
    public bool StagingFlag { get; set; } = false;

    public void Initialize(JsonElement config)
    {
        if (config.TryGetProperty("AltitudeThreshold", out var alt)) _altitudeThreshold = alt.GetSingle();
        if (config.TryGetProperty("PitchAngle", out var angle)) _pitchAngle = angle.GetSingle();
        if (config.TryGetProperty("PitchTime", out var time)) _pitchTime = time.GetSingle();
    }
    
    public GuidanceMode? Step(Simulator sim, IGuidanceTarget? tgt, Vehicle veh, GuidanceProgram program)
    {
        if (tgt is not UPFGTarget upfgTarget)
            throw new ArgumentException("GravityTurnMode requires a UPFGTarget as its target.");
            
        // Create config object for gravity turn
        var config = new GravityTurnModeConfig 
        { 
            AltitudeThreshold = _altitudeThreshold,
            PitchAngle = _pitchAngle,
            PitchTime = _pitchTime
        };
        _gravityTurn.step(sim, upfgTarget, veh, config);
            
        // Check if altitude threshold is reached
        if (sim.State.Misc.TryGetValue("altitude", out var altitude) && altitude > _altitudeThreshold)
        {
            return program.GetNextModeInSequence(GuidanceMode.Ascent);
        }
        return null;
    }
    
    public Vector3? GetSteering() => _gravityTurn.guidance;
}



public class GuidanceProgram
{
    public Dictionary<GuidanceMode, IGuidanceMode> Modes { get; set; } = new();
    public Dictionary<GuidanceMode, IGuidanceTarget> Targets { get; set; } = new();
    public GuidanceMode ActiveMode { get; set; }
    public Vehicle Vehicle { get; set; }
    public Simulator Simulator { get; set; }
    public Vector3? Steering { get; set; }
    public bool StagingFlag { get; set; }
    protected int _lastStageCount;
    public float Dt { get; set; } = 1.0f;
    private List<GuidanceMode>? _modeSequence;

    public GuidanceProgram(Dictionary<GuidanceMode, IGuidanceTarget> targets, Vehicle veh, Simulator sim, Mission mission)
    {
        Vehicle = veh;
        _lastStageCount = veh.Stages.Count();
        Simulator = sim;
        Targets = targets;
        Dt = mission.Guidance.dt;
        
        // Try to build from dynamic configuration, fallback to defaults
        BuildModesFromMission(mission);
        
        // Set initial mode - this will be overridden by BuildModesFromMission if a sequence exists
        if (ActiveMode == default(GuidanceMode)) // Only set if not already set
        {
            ActiveMode = GuidanceMode.Prelaunch;
        }
    }

    private void BuildModesFromMission(Mission mission)
    {
        if (mission.Guidance.programConfig?.modes != null)
        {
            // Store the sequence for navigation
            if (mission.Guidance.programConfig.sequence != null)
            {
                _modeSequence = mission.Guidance.programConfig.sequence
                    .Where(s => Enum.TryParse<GuidanceMode>(s, out _))
                    .Select(s => Enum.Parse<GuidanceMode>(s))
                    .ToList();
            }

            // Build ONLY from dynamic configuration - no defaults
            foreach (var modeConfig in mission.Guidance.programConfig.modes)
            {
                if (Enum.TryParse<GuidanceMode>(modeConfig.Key, out var guidanceMode))
                {
                    var mode = CreateMode(modeConfig.Value.type);
                    mode.Initialize(modeConfig.Value.config);
                    Modes[guidanceMode] = mode;
                }
            }

            // Set initial mode from sequence if available
            var firstMode = mission.Guidance.programConfig.sequence?.FirstOrDefault();
            if (firstMode != null && Enum.TryParse<GuidanceMode>(firstMode, out var initialMode))
            {
                ActiveMode = initialMode;
            }
        }
        else
        {
            // Only fallback to defaults if NO programConfig exists
            BuildDefaultModes();
        }
    }

    private static IGuidanceMode CreateMode(string type) => type switch
    {
        "PreLaunchMode" => new PreLaunchMode(),
        "GravityTurnMode" => new GravityTurnMode(), 
        "UpfgMode" => new UpfgMode(),
        "FinalMode" => new FinalMode(),
        "IdleMode" => new IdleMode(),
        _ => throw new ArgumentException($"Unknown mode type: {type}")
    };

    private void BuildDefaultModes()
    {
        Modes[GuidanceMode.Prelaunch] = new PreLaunchMode();
        Modes[GuidanceMode.Ascent] = new GravityTurnMode();
        Modes[GuidanceMode.PoweredGuidance] = new UpfgMode();
        Modes[GuidanceMode.FinalBurn] = new FinalMode();
        Modes[GuidanceMode.Idle] = new IdleMode();
        
        // Initialize with default configs using JsonElement
        Modes[GuidanceMode.Prelaunch].Initialize(JsonSerializer.SerializeToElement(new { Duration = 0.1f }));
        Modes[GuidanceMode.Ascent].Initialize(JsonSerializer.SerializeToElement(new { AltitudeThreshold = 30000f, PitchAngle = 2.5f, PitchTime = 13f }));
        Modes[GuidanceMode.PoweredGuidance].Initialize(JsonSerializer.SerializeToElement(new { ModeKey = 1, CutoffTime = 5.0f }));
        Modes[GuidanceMode.FinalBurn].Initialize(JsonSerializer.SerializeToElement(new { BurnTime = 5.0f }));
        Modes[GuidanceMode.Idle].Initialize(JsonSerializer.SerializeToElement(new { }));
    }

    public void Step()
    {
        var mode = Modes[ActiveMode];
        var tgt = Targets.ContainsKey(ActiveMode) ? Targets[ActiveMode] : null;
        
        // Propagate staging flag to the mode
        mode.StagingFlag = this.StagingFlag;
        var nextMode = mode.Step(Simulator, tgt, Vehicle, this);
        // Reset after use
        this.StagingFlag = false;

        Vector3? newsteering = mode.GetSteering();
        if (newsteering != null)
        {
            Steering = newsteering;
        }

        if (nextMode.HasValue && Modes.ContainsKey(nextMode.Value))
            {
                ActiveMode = nextMode.Value;
            }
    }

    public void UpdateVehicle(Vehicle veh)
    {
        if (_lastStageCount != veh.Stages.Count)
        {
            StagingFlag = true;
        }
        _lastStageCount = veh.Stages.Count;
        Vehicle = veh;
    }

    public Vector3 GetCurrentSteering()
    {
        return Steering ?? Vector3.Zero;
    }

    public GuidanceMode? GetNextModeInSequence(GuidanceMode currentMode)
    {
        if (_modeSequence == null) return null;
        
        var currentIndex = _modeSequence.IndexOf(currentMode);
        if (currentIndex >= 0 && currentIndex < _modeSequence.Count - 1)
        {
            return _modeSequence[currentIndex + 1];
        }
        return null;
    }
}

// Mission configuration classes
public class GuidanceProgramConfig
{
    public List<string>? sequence { get; set; }
    public Dictionary<string, ModeDefinition>? modes { get; set; }
}

public class ModeDefinition
{
    public string type { get; set; } = "";
    public JsonElement config { get; set; }
}

// Keep this config class for gravity turn compatibility
public class GravityTurnModeConfig
{
    public float AltitudeThreshold { get; set; } = 30000f;
    public float PitchAngle { get; set; } = 2.5f;
    public float PitchTime { get; set; } = 13f;
}

