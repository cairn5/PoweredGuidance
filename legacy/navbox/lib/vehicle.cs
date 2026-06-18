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



// public class Stage
// {
//     public int Id { get; set; }
//     public int Mode { get; set; }
//     public int GLim { get; set; }
//     public double MassTotal { get; set; }
//     public double MassDry { get; set; }
//     public double Thrust { get; set; }
//     public double Isp { get; set; }

// }

public class Vehicle : ICloneable
{
    // Static configuration
    public List<Stage> Stages { get; set; } = new List<Stage>();

    // Dynamic state
    public int CurrentStageIndex { get; private set; } = 0;
    public Stage CurrentStage => Stages.Count > 0 ? Stages[0] : null;

    // Factory method for loading from config file
    public static Vehicle FromJson(string filePath)
    {
        if (!File.Exists(filePath))
            throw new FileNotFoundException($"VehicleInfo configuration file not found: {filePath}");

        string json = File.ReadAllText(filePath);
        var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };

        var vehicle = JsonSerializer.Deserialize<Vehicle>(json, options);
        if (vehicle == null)
            throw new InvalidOperationException("Failed to deserialize the vehicle configuration.");

        return vehicle;
    }

    public static Vehicle FromStages(Mission mission)
    {
        return new Vehicle
        {
            Stages = mission.StageList
        };
    }


    public static Vehicle FromStagesDash(List<Stage> stageList)
    {
        return new Vehicle
        {
            Stages = stageList
        };
    }

    public void AdvanceStage()
    {
        if (Stages.Count > 0)
        {
            Stages.RemoveAt(0);
            CurrentStageIndex = 0;
        }
    }
    public object Clone()
    {
        // Deep copy of stages
        var clonedStages = new List<Stage>();
        foreach (var stage in Stages)
            clonedStages.Add((Stage)stage.Clone());
        return new Vehicle
        {
            Stages = clonedStages,
            CurrentStageIndex = this.CurrentStageIndex
            // Copy other properties as needed
        };
    }


    // public void UpdateMass()
    // {
    //     CurrentMass = CurrentMass - (float)(SimVehicle.Stages[0].Thrust / (Constants.g0 * SimVehicle.Stages[0].Isp));
    // }
}
