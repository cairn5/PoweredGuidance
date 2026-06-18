using System.Text.Json;

namespace lib;

public class TargetConfig
{
    public string type { get; set; } = "orbital"; // "orbital", "surface", "rendezvous"
    public Dictionary<string, float> parameters { get; set; } = new Dictionary<string, float>();
}

public static class TargetFactory
{
    public static IGuidanceTarget CreateTarget(TargetConfig targetConfig, Simulator sim)
    {
        switch (targetConfig.type.ToLower())
        {
            case "orbital":
                var upfgTarget = new UPFGTarget();
                // Validate required parameters
                if (!targetConfig.parameters.ContainsKey("pe") || 
                    !targetConfig.parameters.ContainsKey("ap") || 
                    !targetConfig.parameters.ContainsKey("inc"))
                {
                    throw new ArgumentException("Orbital target requires 'pe', 'ap', and 'inc' parameters");
                }
                upfgTarget.Set(targetConfig.parameters, sim);
                return upfgTarget;
                
            case "surface":
                var upfgSurfaceTarget = new UPFGTarget();
                var latLongParams = new Dictionary<string, double>();
                if (targetConfig.parameters.ContainsKey("latitude"))
                    latLongParams["latitude"] = targetConfig.parameters["latitude"];
                if (targetConfig.parameters.ContainsKey("longitude"))
                    latLongParams["longitude"] = targetConfig.parameters["longitude"];
                    
                if (latLongParams.Count < 2)
                {
                    throw new ArgumentException("Surface target requires 'latitude' and 'longitude' parameters");
                }
                    
                upfgSurfaceTarget.setFromLatLong(latLongParams, sim);
                return upfgSurfaceTarget;
                
            case "gravityturn":
                return new GravityTurnTarget();
                
            default:
                throw new ArgumentException($"Unknown target type: {targetConfig.type}");
        }
    }
}
