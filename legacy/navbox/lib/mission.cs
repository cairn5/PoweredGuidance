using System.Text.Json;

namespace lib;

public class Stage : ICloneable
{
    public int Id { get; set; }
    public int Mode { get; set; }
    public double GLim { get; set; }
    public double MassTotal { get; set; }
    public double MassDry { get; set; }
    public double Thrust { get; set; }
    public double Isp { get; set; }

    public object Clone()
    {
        return this.MemberwiseClone();
    }

}

public class GuidanceConfig
{
    public string program { get; set; } = "";
    public float dt { get; set; }
    public GuidanceProgramConfig? programConfig { get; set; }
}

public class Mission
{
    public Dictionary<string, float> Orbit { get; set; } = new Dictionary<string, float> { }; // Deprecated - kept for backwards compatibility
    public TargetConfig Target { get; set; } = new TargetConfig(); // New target configuration system
    public GuidanceConfig Guidance { get; set; } = new ();
    public Dictionary<string, object> Simulator { get; set; } = new Dictionary<string, object> { };
    public List<Stage> StageList { get; set; } = new List<Stage>();

    public static Mission Load(string filepath)
    {
        string json = File.ReadAllText(filepath);
        Mission? mission = JsonSerializer.Deserialize<Mission>(json);
        if (mission == null)
            throw new Exception($"Failed to deserialize mission file: {filepath}");
            
        // Backward compatibility: if Target is not configured but Orbit is, migrate Orbit to Target
        if (mission.Target.parameters.Count == 0 && mission.Orbit.Count > 0)
        {
            mission.Target.type = "orbital";
            mission.Target.parameters = new Dictionary<string, float>(mission.Orbit);
            
            // Remove the deprecated "mode" key if present
            if (mission.Target.parameters.ContainsKey("mode"))
            {
                mission.Target.parameters.Remove("mode");
            }
        }
        
        return mission;
    }
}
