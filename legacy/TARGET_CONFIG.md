# Target Configuration System

The navbox application now uses a flexible target configuration system that allows you to define different types of targets for your missions.

## Target Types

### 1. Orbital Targets (`"orbital"`)
For missions targeting specific orbits around a celestial body.

**Parameters:**
- `pe`: Perigee altitude in kilometers
- `ap`: Apogee altitude in kilometers  
- `inc`: Inclination in degrees
- `LAN`: (optional) Longitude of Ascending Node in degrees
- `alt`: (optional) Specific altitude for circular orbits

**Example:**
```json
{
  "Target": {
    "type": "orbital",
    "parameters": {
      "pe": 175,
      "ap": 175,
      "inc": 56.5
    }
  }
}
```

### 2. Surface Targets (`"surface"`)
For missions targeting specific locations on the surface of a celestial body.

**Parameters:**
- `latitude`: Target latitude in degrees
- `longitude`: Target longitude in degrees

**Example:**
```json
{
  "Target": {
    "type": "surface", 
    "parameters": {
      "latitude": 28.5,
      "longitude": -70.0
    }
  }
}
```

### 3. Gravity Turn Targets (`"gravityturn"`)
For missions using gravity turn guidance without specific orbital parameters.

**Example:**
```json
{
  "Target": {
    "type": "gravityturn",
    "parameters": {}
  }
}
```

## Backward Compatibility

The system maintains backward compatibility with the old `Orbit` configuration. If a mission file contains an `Orbit` section but no `Target` section, the system will automatically migrate the orbit parameters to a new orbital target configuration.

**Old format (still supported):**
```json
{
  "Orbit": {
    "mode": 1,
    "pe": 175,
    "ap": 175, 
    "inc": 56.5
  }
}
```

**New format (recommended):**
```json
{
  "Target": {
    "type": "orbital",
    "parameters": {
      "pe": 175,
      "ap": 175,
      "inc": 56.5
    }
  }
}
```

## Usage with Guidance Modes

The target configuration works seamlessly with the existing guidance mode system. Different guidance modes will use the appropriate target type:

- **UPFG modes** (PoweredGuidance) use orbital or surface targets
- **Gravity turn modes** can use gravity turn targets or fall back to the primary target
- **Other modes** use the primary target defined in the Target configuration

## Implementation Details

The `TargetFactory` class handles the creation of appropriate target objects based on the configuration. The factory pattern allows for easy extension with new target types in the future.

## Migration Guide

To migrate existing mission files to use the new target system:

1. **Simple orbital missions:** Change `Orbit.mode` to `Target.type: "orbital"` and move orbital parameters to `Target.parameters`
2. **Surface missions:** Create a new `Target` section with `type: "surface"` and appropriate latitude/longitude parameters
3. **Complex missions:** Consider using different target types for different guidance phases
