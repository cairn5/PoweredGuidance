# Mission Files Summary

After restructuring the target configuration system, we now have the following mission configurations:

## Orbital Missions
- **saturnV.json** - Earth orbital insertion mission with 175km circular orbit at 56.5° inclination
- **LEMascent.json** - Lunar orbital insertion from surface, 100km circular orbit at 50° inclination

## Surface Targeting Mission  
- **LEMdescent.json** - Lunar surface landing mission targeting latitude 0°, longitude 70°

## Test/Example Missions
- **shuttle.json** - Space shuttle mission profile
- **surfaceLanding.json** - Generic surface landing mission (created as example)
- **dynamicGuidance.json** - Dynamic guidance test mission

## Key Features

### Target Types Available:
1. **Orbital** (`"orbital"`) - For orbit insertion missions using perigee, apogee, and inclination
2. **Surface** (`"surface"`) - For landing missions using latitude and longitude coordinates
3. **Gravity Turn** (`"gravityturn"`) - For ascent guidance without specific orbital parameters

### UPFG Modes:
- **Mode 1** - Orbital targeting (used by LEMascent, saturnV)  
- **Mode 2** - Surface targeting (used by LEMdescent)

### Mission Structure:
All missions now use the new `Target` configuration:
```json
{
  "Target": {
    "type": "surface",
    "parameters": {
      "latitude": 0,
      "longitude": 70
    }
  }
}
```

### Backward Compatibility:
The system automatically migrates old `Orbit` configurations to the new `Target` system, so existing mission files continue to work.

### Mission Profiles:
- **LEMdescent**: Single-stage powered descent mission targeting surface coordinates (lat/long)
- **LEMascent**: Multi-stage orbital insertion from lunar surface using ascent stage  
- **saturnV**: Multi-stage Earth orbital mission with gravity turn ascent and powered guidance
