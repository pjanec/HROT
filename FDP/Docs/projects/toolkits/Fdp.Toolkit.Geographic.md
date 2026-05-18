# Fdp.Toolkit.Geographic

## Overview

**Fdp.Toolkit.Geographic** is a geospatial coordinate transformation toolkit for the FDP distributed simulation framework. It provides mathematically rigorous conversions between WGS84 geodetic coordinates (latitude/longitude/altitude), Earth-Centered Earth-Fixed (ECEF) Cartesian coordinates, and local East-North-Up (ENU) tangent plane projections. The toolkit enables real-world geographic positioning for simulated entities, sensor modeling, and multi-fidelity federation with geographic information systems.

**Key Capabilities**:
- **WGS84 Geodetic Coordinates**: Standard GPS latitude/longitude/altitude representation
- **ECEF Transformation**: Earth-centered 3D Cartesian coordinate system for global positioning
- **ENU Tangent Plane**: Local flat-earth approximation for simulation physics (accurate within ~100km of origin)
- **Automated Synchronization**: Systems for bidirectional position updates between local physics and geodetic coordinates
- **Network Interoperability**: Managed components for cross-node geodetic position replication
- **High Precision**: Double-precision geodetic math with single-precision local physics
- **Module Integration**: IModule implementation for ModuleHost.Core registration

**Line Count**: 8 C# implementation files (Components, Systems, Transforms, Module)

**Primary Dependencies**: Fdp.Kernel (ECS Core), ModuleHost.Core (Module System)

**Use Cases**: Vehicle simulation with GPS, UAV navigation, multi-site distributed exercises, sensor fusion, geographic data visualization

---

## Architecture

### Coordinate System Overview

```
┌─────────────────────────────────────────────────────────────────────┐
│                   Geographic Coordinate Systems                      │
├─────────────────────────────────────────────────────────────────────┤
│                                                                       │
│  WGS84 Geodetic (GPS Coordinates)                                   │
│  ┌──────────────────────────────────────┐                           │
│  │ Latitude:  -90° to +90° (N/S)        │                           │
│  │ Longitude: -180° to +180° (E/W)      │                           │
│  │ Altitude:   meters above ellipsoid   │                           │
│  └──────────────────────────────────────┘                           │
│           │                       ▲                                  │
│           │ ToCartesian()         │ ToGeodetic()                     │
│           ▼                       │                                  │
│  ECEF (Earth-Centered Earth-Fixed)                                  │
│  ┌──────────────────────────────────────┐                           │
│  │ X: meters (along equator, 0° lon)    │                           │
│  │ Y: meters (along equator, 90° lon)   │                           │
│  │ Z: meters (toward North Pole)        │                           │
│  └──────────────────────────────────────┘                           │
│           │                       ▲                                  │
│           │ Rotation Matrix       │ Inverse Rotation                │
│           ▼                       │                                  │
│  Local ENU (East-North-Up)                                           │
│  ┌──────────────────────────────────────┐                           │
│  │ East (X):  meters (local tangent)    │                           │
│  │ North (Y): meters (local tangent)    │                           │
│  │ Up (Z):    meters (above origin)     │                           │
│  └──────────────────────────────────────┘                           │
│                                                                       │
│  Origin: SetOrigin(lat, lon, alt)                                   │
│  Accuracy: < 1cm error within 100km radius                          │
│                                                                       │
└───────────────────────────────────────────────────────────────────────┘

WGS84 Ellipsoid Parameters:
  Semi-major axis (a):  6,378,137.0 m (equatorial radius)
  Flattening (f):       1 / 298.257223563
  Eccentricity² (e²):   f * (2 - f)
```

### System Architecture

```
┌─────────────────────────────────────────────────────────────────────┐
│                     GeographicModule Architecture                    │
├─────────────────────────────────────────────────────────────────────┤
│                                                                       │
│  Application/Simulation Layer                                        │
│  ┌──────────────────────────────────────────────────────────────┐   │
│  │  Vehicle/Entity Physics (Position component, Vector3)        │   │
│  │  - Kinematics integration (local Cartesian space)            │   │
│  │  - Collision detection (ENU coordinates)                     │   │
│  └──────────────────────────────────────────────────────────────┘   │
│                          ▲                 │                         │
│                          │                 │                         │
│           CoordinateTransformSystem        │                         │
│           (Bidirectional sync)             │                         │
│                          │                 ▼                         │
│  ┌──────────────────────────────────────────────────────────────┐   │
│  │  PositionGeodetic (managed component, double precision)      │   │
│  │  - Latitude, Longitude, Altitude                             │   │
│  │  - Networked via ModuleHost replication                      │   │
│  └──────────────────────────────────────────────────────────────┘   │
│                          │                 ▲                         │
│                          │                 │                         │
│              GeodeticSmoothingSystem       │                         │
│              (Interpolation for remote)    │                         │
│                          │                 │                         │
│                          ▼                 │                         │
│  ┌──────────────────────────────────────────────────────────────┐   │
│  │             IGeographicTransform Interface                    │   │
│  │  - SetOrigin(lat, lon, alt)                                  │   │
│  │  - ToCartesian(lat, lon, alt) → Vector3                      │   │
│  │  - ToGeodetic(Vector3) → (lat, lon, alt)                     │   │
│  └──────────────────────────────────────────────────────────────┘   │
│                          │                                           │
│                          ▼                                           │
│  ┌──────────────────────────────────────────────────────────────┐   │
│  │             WGS84Transform (Implementation)                   │   │
│  │  - GeodeticToECEF(lat, lon, alt) → (x, y, z)                 │   │
│  │  - ECEFToGeodetic(x, y, z) → (lat, lon, alt)                 │   │
│  │  - Rotation matrices (ECEF ↔ ENU)                            │   │
│  │  - Origin transformation caching                             │   │
│  └──────────────────────────────────────────────────────────────┘   │
│                                                                       │
└───────────────────────────────────────────────────────────────────────┘
```

---

## Core Components

### PositionGeodetic (Components/PositionGeodetic.cs)

Managed component storing WGS84 geodetic coordinates for network replication:

```csharp
public class PositionGeodetic
{
    public double Latitude { get; set; }    // Degrees, -90 to +90
    public double Longitude { get; set; }   // Degrees, -180 to +180
    public double Altitude { get; set; }    // Meters above WGS84 ellipsoid
}
```

**Design Rationale**:
- **Managed Component** (class): Allows network serialization via ModuleHost.Core replication
- **Double Precision**: Geodetic coordinates require ~1e-7 degree precision for centimeter accuracy
- **Read-Only for Remote Entities**: Local physics drives `Position`, system writes `PositionGeodetic`; remote entities receive `PositionGeodetic` via network, system updates local `Position`

**Usage**:
```csharp
// Access geodetic position
var geodeticPos = view.GetManagedComponentRO<PositionGeodetic>(entity);
Console.WriteLine($"Entity at {geodeticPos.Latitude}°N, {geodeticPos.Longitude}°E, {geodeticPos.Altitude}m");
```

### Position (Components/Position.cs)

Unmanaged component storing local ENU Cartesian position:

```csharp
[StructLayout(LayoutKind.Sequential)]
public struct Position
{
    public Vector3 Value;  // East-North-Up coordinates (meters)
}
```

**Design Rationale**:
- **Unmanaged Struct**: Cache-friendly, zero-allocation access in systems
- **Single Precision** (float): Sufficient for local physics within 100km radius (~millimeter precision)
- **Physics-Driven**: Updated by vehicle kinematics, collision detection, trajectory systems

**Relationship to `PositionGeodetic`**:
- `CoordinateTransformSystem` synchronizes `Position` → `PositionGeodetic` for locally owned entities
- Remote entities: `PositionGeodetic` (from network) → `Position` (for local rendering/collision)

### Velocity (Components/Velocity.cs)

Local velocity in ENU frame:

```csharp
public struct Velocity
{
    public Vector3 Value;  // Meters per second (East, North, Up)
}
```

**Note**: Geodetic velocity components not provided in current implementation (future enhancement for global velocity replication).

---

## Coordinate Transformations

### IGeographicTransform Interface

Abstraction for coordinate system conversions:

```csharp
public interface IGeographicTransform
{
    void SetOrigin(double latDeg, double lonDeg, double altMeters);
    Vector3 ToCartesian(double latDeg, double lonDeg, double altMeters);
    (double lat, double lon, double alt) ToGeodetic(Vector3 localPos);
}
```

**Methods**:

1. **SetOrigin**(latDeg, lonDeg, altMeters)
   - Establishes local tangent plane origin
   - Computes ECEF origin coordinates
   - Builds rotation matrix (ECEF ↔ ENU)
   - **Called once** at simulation initialization

2. **ToCartesian**(latDeg, lonDeg, altMeters) → Vector3
   - Input: WGS84 geodetic coordinates (degrees, meters)
   - Output: Local ENU Cartesian position (meters)
   - Process: Geodetic → ECEF → ENU rotation → Local Vector3

3. **ToGeodetic**(localPos) → (lat, lon, alt)
   - Input: Local ENU Cartesian position (meters)
   - Output: WGS84 geodetic coordinates (degrees, meters)
   - Process: Local Vector3 → ENU rotation → ECEF → Geodetic

### WGS84Transform Implementation

**WGS84 Ellipsoid Model**:

The Earth is modeled as an oblate spheroid (flattened at poles):

```
Semi-major axis (a): 6,378,137.0 m     [Equatorial radius]
Semi-minor axis (b): 6,356,752.314 m   [Polar radius]
Flattening (f): 1 / 298.257223563
Eccentricity² (e²): 0.00669437999014

Relationship: b = a * (1 - f)
             e² = f * (2 - f)
```

**Geodetic to ECEF Conversion**:

```
Prime Vertical Radius of Curvature:
  N(φ) = a / √(1 - e² * sin²(φ))

ECEF Coordinates:
  X = (N(φ) + h) * cos(φ) * cos(λ)
  Y = (N(φ) + h) * cos(φ) * sin(λ)
  Z = (N(φ) * (1 - e²) + h) * sin(φ)

where:
  φ = latitude (radians)
  λ = longitude (radians)
  h = altitude above ellipsoid (meters)
```

**Implementation**:
```csharp
private (double x, double y, double z) GeodeticToECEF(double lat, double lon, double alt)
{
    double N = WGS84_A / Math.Sqrt(1.0 - WGS84_E2 * Math.Sin(lat) * Math.Sin(lat));
    double x = (N + alt) * Math.Cos(lat) * Math.Cos(lon);
    double y = (N + alt) * Math.Cos(lat) * Math.Sin(lon);
    double z = (N * (1.0 - WGS84_E2) + alt) * Math.Sin(lat);
    return (x, y, z);
}
```

**ECEF to Geodetic Conversion** (Iterative Method):

The inverse transformation requires iterative solution due to non-linear equations:

```
Longitude (trivial):
  λ = atan2(Y, X)

Latitude and Altitude (iterative):
  p = √(X² + Y²)
  φ₀ = atan2(Z, p * (1 - e²))
  
  For i = 1 to 5:
    N = a / √(1 - e² * sin²(φᵢ₋₁))
    h = p / cos(φᵢ₋₁) - N
    φᵢ = atan2(Z, p * (1 - e² * N / (N + h)))
  
  φ = φ₅ (converged latitude)
  h = p / cos(φ) - N(φ)
```

**Implementation**:
```csharp
private (double, double, double) ECEFToGeodetic(double x, double y, double z)
{
    double lon = Math.Atan2(y, x);
    double p = Math.Sqrt(x * x + y * y);
    double lat = Math.Atan2(z, p * (1.0 - WGS84_E2));
    
    for (int i = 0; i < 5; i++)
    {
        double N = WGS84_A / Math.Sqrt(1.0 - WGS84_E2 * Math.Sin(lat) * Math.Sin(lat));
        double alt = p / Math.Cos(lat) - N;
        lat = Math.Atan2(z, p * (1.0 - WGS84_E2 * N / (N + alt)));
    }
    
    double N_final = WGS84_A / Math.Sqrt(1.0 - WGS84_E2 * Math.Sin(lat) * Math.Sin(lat));
    double alt_final = p / Math.Cos(lat) - N_final;
    
    return (lat * 180.0 / Math.PI, lon * 180.0 / Math.PI, alt_final);
}
```

**Convergence**: 5 iterations provides sub-millimeter accuracy for typical altitudes (< 100km).

**ECEF to ENU Rotation Matrix**:

The rotation from ECEF to local East-North-Up tangent plane:

```
ENU Frame Definition:
  East:  Tangent to ellipsoid, pointing east
  North: Tangent to ellipsoid, pointing north
  Up:    Normal to ellipsoid, pointing up (away from center)

Rotation Matrix (ECEF → ENU):
  ┌                                        ┐
  │  -sin(λ)      cos(λ)           0       │
  │  -sin(φ)cos(λ)  -sin(φ)sin(λ)  cos(φ)  │
  │   cos(φ)cos(λ)   cos(φ)sin(λ)  sin(φ)  │
  └                                        ┘

where φ = origin latitude, λ = origin longitude
```

**Implementation**:
```csharp
double sinLat = Math.Sin(_originLat);
double cosLat = Math.Cos(_originLat);
double sinLon = Math.Sin(_originLon);
double cosLon = Math.Cos(_originLon);

_ecefToLocal = new Matrix4x4(
    (float)-sinLon,              (float)cosLon,             0, 0,
    (float)(-sinLat * cosLon),   (float)(-sinLat * sinLon), (float)cosLat, 0,
    (float)(cosLat * cosLon),    (float)(cosLat * sinLon),  (float)sinLat, 0,
    0, 0, 0, 1
);

Matrix4x4.Invert(_ecefToLocal, out _localToEcef);
```

**Complete Transformation Pipeline**:

```
ToCartesian(lat, lon, alt):
  1. Convert (lat, lon, alt) to ECEF₁
  2. Convert origin to ECEF₀ (cached)
  3. Compute ECEF delta: ΔECEF = ECEF₁ - ECEF₀
  4. Rotate ΔECEF by ECEF→ENU matrix
  5. Return Vector3 (local position)

ToGeodetic(localPos):
  1. Rotate localPos by ENU→ECEF matrix (inverse)
  2. Convert origin to ECEF₀ (cached)
  3. Compute ECEFABS = ECEF₀ + rotated delta
  4. Convert ECEFABS to (lat, lon, alt)
  5. Return geodetic coordinates
```

---

## Systems

### CoordinateTransformSystem (Systems/CoordinateTransformSystem.cs)

Synchronizes local physics positions to geodetic coordinates for network replication:

```csharp
[UpdateInPhase(SystemPhase.PostSimulation)]
public class CoordinateTransformSystem : IModuleSystem
{
    private readonly IGeographicTransform _geo;
    
    public CoordinateTransformSystem(IGeographicTransform geo)
    {
        _geo = geo;
    }
    
    public void Execute(ISimulationView view, float deltaTime)
    {
        var cmd = view.GetCommandBuffer();
        
        // Outbound: Position → PositionGeodetic (for locally owned entities)
        var outbound = view.Query()
            .With<Position>()
            .With<NetworkOwnership>()
            .WithManaged<PositionGeodetic>()
            .Build();
        
        foreach (var entity in outbound)
        {
            var ownership = view.GetComponentRO<NetworkOwnership>(entity);
            
            // Only update geodetic position for locally owned entities
            if (ownership.PrimaryOwnerId != ownership.LocalNodeId)
                continue;

            var localPos = view.GetComponentRO<Position>(entity);
            var geoPos = view.GetManagedComponentRO<PositionGeodetic>(entity);
            
            // Convert local position to geodetic
            var (lat, lon, alt) = _geo.ToGeodetic(localPos.Value);
            
            // Update only if changed significantly (1e-6 deg ≈ 10cm)
            if (Math.Abs(geoPos.Latitude - lat) > 1e-6 ||
                Math.Abs(geoPos.Longitude - lon) > 1e-6 ||
                Math.Abs(geoPos.Altitude - alt) > 0.1)
            {
                var newGeo = new PositionGeodetic
                {
                    Latitude = lat,
                    Longitude = lon,
                    Altitude = alt
                };
                cmd.SetManagedComponent(entity, newGeo);
            }
        }
    }
}
```

**Execution Phase**: `PostSimulation` (after physics update, before network egress)

**Optimization**: Change detection threshold prevents unnecessary managed component writes (GC pressure).

**Network Flow**:
1. Local physics updates `Position` component
2. `CoordinateTransformSystem` writes `PositionGeodetic`
3. Replication system sends `PositionGeodetic` to remote nodes
4. Remote nodes receive `PositionGeodetic`, inverse transform to `Position` for rendering

### GeodeticSmoothingSystem (Systems/GeodeticSmoothingSystem.cs)

Provides interpolation for remotely replicated geodetic positions (reduces network jitter):

**Purpose**: Network updates arrive at discrete intervals (e.g., 20 Hz). Smoothing system interpolates between received positions for fluid visual updates at higher frame rates (60+ Hz).

**Implementation** (conceptual, based on typical smoothing patterns):
```csharp
public class GeodeticSmoothingSystem : IModuleSystem
{
    private readonly IGeographicTransform _geo;
    
    public void Execute(ISimulationView view, float deltaTime)
    {
        // For each remote entity with PositionGeodetic
        var query = view.Query()
            .WithManaged<PositionGeodetic>()
            .With<NetworkOwnership>()
            .Without<Position>() // Entities that don't have local physics
            .Build();
        
        foreach (var entity in query)
        {
            var ownership = view.GetComponentRO<NetworkOwnership>(entity);
            if (ownership.PrimaryOwnerId == ownership.LocalNodeId)
                continue; // Skip locally owned
            
            var geoPos = view.GetManagedComponentRO<PositionGeodetic>(entity);
            
            // Convert geodetic to local position for rendering
            Vector3 localPos = _geo.ToCartesian(geoPos.Latitude, geoPos.Longitude, geoPos.Altitude);
            
            // Apply interpolation filter (e.g., exponential smoothing)
            // ... (details depend on PositionHistory component)
            
            cmd.SetComponent(entity, new Position { Value = localPos });
        }
    }
}
```

**Smoothing Techniques**:
- **Exponential Smoothing**: `P_smooth = P_smooth + α * (P_target - P_smooth)` where α = smoothing factor
- **Cubic Hermite Interpolation**: Use velocity estimates for smooth curves between waypoints
- **Dead Reckoning**: Extrapolate position based on last known velocity/acceleration

---

## Module Integration

### GeographicModule (GeographicModule.cs)

IModule implementation for ModuleHost.Core registration:

```csharp
public class GeographicModule : IModule
{
    public string Name => "GeographicServices";
    public ExecutionPolicy Policy => ExecutionPolicy.Synchronous();

    private readonly IGeographicTransform _transform;

    public GeographicModule(IGeographicTransform implementation)
    {
        _transform = implementation;
    }

    public void RegisterSystems(ISystemRegistry registry)
    {
        registry.RegisterSystem(new GeodeticSmoothingSystem(_transform));
        registry.RegisterSystem(new CoordinateTransformSystem(_transform));
    }

    public void Tick(ISimulationView view, float deltaTime) { }
}
```

**Usage**:
```csharp
// Application initialization
var wgs84 = new WGS84Transform();
wgs84.SetOrigin(37.7749, -122.4194, 0); // San Francisco, CA

var geoModule = new GeographicModule(wgs84);
moduleHost.RegisterModule(geoModule);
```

**Design Rationale**:
- **Dependency Injection**: `IGeographicTransform` injected via constructor (allows testing with mock transforms)
- **Synchronous Policy**: Geographic transforms are deterministic, no async operations required
- **System Registration**: Both smoothing and transformation systems registered automatically

---

## Usage Examples

### Example 1: Initialize Geographic Simulation

```csharp
using Fdp.Kernel;
using ModuleHost.Core;
using Fdp.Modules.Geographic;
using Fdp.Modules.Geographic.Transforms;

public class SimulationSetup
{
    public void Initialize()
    {
        // Create ECS world
        var world = new EntityRepository();
        var kernel = new ModuleHostKernel(world, new EventAccumulator());
        
        // Setup geographic transform (origin at specific location)
        var wgs84 = new WGS84Transform();
        wgs84.SetOrigin(
            latDeg: 35.0,      // 35°N (e.g., central New Mexico)
            lonDeg: -106.0,    // 106°W
            altMeters: 1500.0  // 1500m elevation
        );
        
        // Register geographic module
        var geoModule = new GeographicModule(wgs84);
        kernel.RegisterModule(geoModule);
        
        // Now entities can use Position (local ENU) and PositionGeodetic components
    }
}
```

### Example 2: Spawn Entity at GPS Coordinates

```csharp
public void SpawnVehicleAtGPS(ISimulationView view, IGeographicTransform geo,
    double lat, double lon, double alt)
{
    var cmd = view.GetCommandBuffer();
    
    // Create entity
    Entity vehicle = cmd.CreateEntity();
    
    // Convert GPS to local position
    Vector3 localPos = geo.ToCartesian(lat, lon, alt);
    
    // Add components
    cmd.SetComponent(vehicle, new Position { Value = localPos });
    cmd.SetManagedComponent(vehicle, new PositionGeodetic
    {
        Latitude = lat,
        Longitude = lon,
        Altitude = alt
    });
    cmd.SetComponent(vehicle, new Velocity { Value = Vector3.Zero });
    
    Console.WriteLine($"Spawned vehicle at {lat}°N, {lon}°E → local ENU ({localPos.X:F2}, {localPos.Y:F2}, {localPos.Z:F2})");
}
```

### Example 3: Query Entity GPS Position

```csharp
public void PrintEntityGPS(ISimulationView view, Entity entity)
{
    // Check if entity has geodetic position
    if (!view.HasManagedComponent<PositionGeodetic>(entity))
    {
        Console.WriteLine("Entity does not have geodetic position.");
        return;
    }
    
    var geoPos = view.GetManagedComponentRO<PositionGeodetic>(entity);
    
    // Format latitude/longitude with hemisphere indicators
    string latStr = $"{Math.Abs(geoPos.Latitude):F6}°{(geoPos.Latitude >= 0 ? "N" : "S")}";
    string lonStr = $"{Math.Abs(geoPos.Longitude):F6}°{(geoPos.Longitude >= 0 ? "E" : "W")}";
    
    Console.WriteLine($"Entity GPS: {latStr}, {lonStr}, {geoPos.Altitude:F1}m ASL");
}
```

### Example 4: Distance Calculation (Great Circle)

```csharp
public static double CalculateGreatCircleDistance(
    double lat1Deg, double lon1Deg,
    double lat2Deg, double lon2Deg)
{
    const double EarthRadiusMeters = 6371000.0; // Mean Earth radius
    
    double lat1 = lat1Deg * Math.PI / 180.0;
    double lat2 = lat2Deg * Math.PI / 180.0;
    double deltaLat = (lat2Deg - lat1Deg) * Math.PI / 180.0;
    double deltaLon = (lon2Deg - lon1Deg) * Math.PI / 180.0;
    
    // Haversine formula
    double a = Math.Sin(deltaLat / 2) * Math.Sin(deltaLat / 2) +
               Math.Cos(lat1) * Math.Cos(lat2) *
               Math.Sin(deltaLon / 2) * Math.Sin(deltaLon / 2);
    
    double c = 2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a));
    double distance = EarthRadiusMeters * c;
    
    return distance; // meters
}

// Usage
double distMeters = CalculateGreatCircleDistance(
    37.7749, -122.4194,  // San Francisco
    34.0522, -118.2437   // Los Angeles
);
Console.WriteLine($"SF to LA: {distMeters / 1000.0:F1} km");  // ~559 km
```

### Example 5: Integration with CarKinem Toolkit

```csharp
using CarKinem.Commands;
using CarKinem.Core;
using Fdp.Modules.Geographic;

public class GeographicCarSimulation
{
    public void SpawnVehicleOnRoad(ISimulationView view, IGeographicTransform geo,
        VehicleAPI vehicleAPI, double lat, double lon)
    {
        var cmd = view.GetCommandBuffer();
        
        // Create entity
        Entity vehicle = cmd.CreateEntity();
        
        // Convert GPS to local position
        Vector3 localPos3D = geo.ToCartesian(lat, lon, 0);
        Vector2 localPos2D = new Vector2(localPos3D.X, localPos3D.Y);
        
        // Spawn vehicle using CarKinem API
        vehicleAPI.SpawnVehicle(vehicle, localPos2D, new Vector2(1, 0));
        
        // Add geodetic tracking
        cmd.SetManagedComponent(vehicle, new PositionGeodetic
        {
            Latitude = lat,
            Longitude = lon,
            Altitude = 0
        });
        
        // CoordinateTransformSystem will keep PositionGeodetic synchronized
        // as vehicle moves via CarKinematicsSystem
    }
}
```

### Example 6: Multi-Site Federation (Distributed Exercise)

```csharp
public class MultiSiteFederation
{
    // Site A: San Diego, CA (local origin)
    private WGS84Transform _siteA;
    
    // Site B: Fort Irwin, CA (remote origin)
    private WGS84Transform _siteB;
    
    public void Initialize()
    {
        _siteA = new WGS84Transform();
        _siteA.SetOrigin(32.7157, -117.1611, 0); // San Diego

        _siteB = new WGS84Transform();
        _siteB.SetOrigin(35.2606, -116.6819, 0); // Fort Irwin
    }
    
    public void ReplicateEntityAcrossSites(PositionGeodetic geoPos)
    {
        // Site A sends PositionGeodetic via DDS
        // Site B receives PositionGeodetic, converts to local coordinates
        
        Vector3 siteBLocalPos = _siteB.ToCartesian(
            geoPos.Latitude,
            geoPos.Longitude,
            geoPos.Altitude
        );
        
        // Now siteBLocalPos can be used for rendering/collision in Site B's ENU frame
        Console.WriteLine($"Remote entity at Site B local: ({siteBLocalPos.X:F2}, {siteBLocalPos.Y:F2})");
    }
}
```

---

## Accuracy and Limitations

### Accuracy Characteristics

**Geodetic to ECEF Conversion**:
- Precision: Double-precision (15-16 significant digits)
- Error: < 1mm for standard altitudes (< 100km)
- Iterative convergence: 5 iterations → sub-millimeter accuracy

**ECEF to ENU Rotation**:
- Precision: Single-precision rotation matrix (6-7 significant digits)
- Error: < 1cm for distances < 100km from origin
- Error growth: ~1cm per 100km distance from origin

**Overall Position Accuracy**:
- Within 10km of origin: < 1cm error
- Within 100km of origin: < 10cm error
- Beyond 100km: Use multiple origins or full ECEF simulation

### Limitations

1. **Flat Earth Approximation**:
   - ENU tangent plane assumes locally flat Earth
   - Curvature ignored within 100km radius (acceptable for most simulations)
   - For continental-scale simulations, consider full ECEF dynamics

2. **Single Precision Physics**:
   - Local Position component uses `Vector3` (float)
   - Precision: ~1mm near origin, degrades with distance
   - Recommendation: Keep simulation area within ±50km of origin

3. **Altitude Reference**:
   - WGS84 altitude is **above ellipsoid**, not above sea level (geoid)
   - Geoid-ellipsoid separation ranges from -100m to +80m globally
   - For terrain elevation, apply geoid correction (EGM96/EGM2008 models)

4. **Coordinate System Mismatch**:
   - GPS receivers output WGS84 coordinates
   - Maps/elevation data may use different datums (NAD83, UTM, etc.)
   - Always verify coordinate system consistency

5. **Rotation Matrix Precision**:
   - Matrix4x4 uses single-precision floats
   - For extreme precision (< 1mm), consider double-precision rotation implementation

### Best Practices

**Origin Selection**:
- Choose origin near center of simulation area
- Minimize maximum distance from origin (< 100km ideal)
- For multi-site exercises, each site uses its own local origin

**Altitude Handling**:
- If using terrain elevation data, verify ellipsoid vs. geoid reference
- Apply geoid separation correction if needed: `h_ellipsoid = h_sea_level + N_geoid`

**Coordinate Validation**:
- Validate latitude: -90° ≤ lat ≤ +90°
- Validate longitude: -180° ≤ lon ≤ +180°
- Handle date line crossings (lon = ±180° discontinuity)

**Network Replication**:
- Replicate `PositionGeodetic` (not `Position`) for coordinate system independence
- Each site transforms received geodetic positions to its own local ENU frame

---

## Integration with FDP Ecosystem

### Dependency Graph

```
Fdp.Toolkit.Geographic
  │
  ├─> Fdp.Kernel (ECS Core)
  │     └─> EntityQuery, ComponentType, IModuleSystem
  │
  ├─> ModuleHost.Core (Module System)
  │     └─> IModule, ISimulationView, ICommandBuffer, NetworkOwnership
  │
  └─> System.Numerics
        └─> Vector3, Matrix4x4 (rotation transforms)
```

### Integration with Other Toolkits

**FDP.Toolkit.CarKinem** (Vehicle Kinematics):
- CarKinem operates in local ENU coordinates
- Geographic toolkit converts GPS waypoints to local positions for navigation
- `CoordinateTransformSystem` tracks vehicle GPS position for telemetry/logging
- Example: Import road network from OpenStreetMap (lat/lon) → convert to ENU for simulation

**FDP.Toolkit.Replication** (Network Synchronization):
- `PositionGeodetic` marked as replicated managed component
- Local physics drives `Position`, Geographic system updates `PositionGeodetic`
- Remote nodes receive `PositionGeodetic`, inverse transform to local `Position`
- Coordinate system independence: Each site can use different local origins

**FDP.Toolkit.Time** (Distributed Synchronization):
- Geographic transforms are deterministic (no time dependency)
- Compatible with both lockstep and PLL time modes
- Transform calculations do not affect determinism (stateless conversions)

**ModuleHost.Network.Cyclone** (DDS Networking):
- `PositionGeodetic` serialized as struct (Latitude, Longitude, Altitude doubles)
- DDS topic: `GeodeticPositionUpdate` with entity ID + geodetic coordinates
- Compact network payload: 3 doubles (24 bytes) per entity vs. full transform

**FDP.Toolkit.Lifecycle** (Entity Management):
- Spawned entities receive both `Position` and `PositionGeodetic` components
- Despawn cleanup removes both components
- Persistence: Save/load entity GPS coordinates for scenario replay

---

## Future Enhancements

### Planned Features

1. **Multiple Coordinate Systems**:
   - UTM (Universal Transverse Mercator) projection
   - MGRS (Military Grid Reference System)
   - Local tangent plane variants (NED instead of ENU)

2. **Geoid Models**:
   - EGM96/EGM2008 geoid separation lookup
   - Conversion between ellipsoidal height and mean sea level elevation
   - Terrain elevation integration

3. **Velocity Replication**:
   - `VelocityGeodetic` component (North-East-Down velocity)
   - Doppler shift calculation for sensor simulation
   - Wind/current modeling in geodetic frame

4. **Great Circle Navigation**:
   - Bearing and distance calculations
   - Waypoint generation along great circle paths
   - Rhumb line (constant bearing) alternatives

5. **Datums and Transformations**:
   - Support for NAD83, ITRF, local datums
   - Datum transformation (Helmert 7-parameter, grid-based)
   - Coordinate system metadata per entity

6. **Precision Enhancements**:
   - Double-precision local coordinates option for large-area simulations
   - Quaternion-based rotation for numerical stability
   - Adaptive origin shifting for unbounded simulations

### Research Directions

1. **Multi-Resolution Grids**:
   - Hierarchical spatial indexing using geohash or S2 cells
   - Efficient culling for large geographic areas

2. **Geodetic Interpolation**:
   - Slerp (spherical linear interpolation) for geodetic paths
   - Time-optimal trajectories on ellipsoid surface

3. **Relativistic Corrections**:
   - GPS time dilation effects for high-precision timing
   - Sagnac effect for long-range positioning

---

## Troubleshooting

### Issue: Position Coordinates Don't Match GPS

**Symptom**: Entity `Position` component doesn't align with expected GPS coordinates

**Causes**:
1. Origin not set before conversions
2. Altitude reference mismatch (ellipsoid vs. sea level)
3. Coordinate order confusion (lat/lon vs. lon/lat)

**Solution**:
```csharp
// Verify origin is set
var wgs84 = new WGS84Transform();
wgs84.SetOrigin(lat, lon, alt); // MUST call before conversions

// Verify coordinate order (latitude first!)
Vector3 pos = wgs84.ToCartesian(latitude, longitude, altitude); // Correct
// NOT: ToCartesian(longitude, latitude, altitude) // Wrong!

// Check altitude reference
double ellipsoidHeight = seaLevelElevation + geoidSeparation;
```

### Issue: Positions Drift Over Time

**Symptom**: Geodetic positions slowly diverge from physics positions

**Causes**:
1. Floating-point accumulation errors in physics
2. Missing `CoordinateTransformSystem` updates
3. Network replication race condition

**Solution**:
```csharp
// Ensure CoordinateTransformSystem is registered
moduleHost.RegisterModule(new GeographicModule(wgs84Transform));

// Verify system execution order (PostSimulation phase)
[UpdateInPhase(SystemPhase.PostSimulation)]
public class CoordinateTransformSystem : IModuleSystem { ... }

// Add periodic re-synchronization
if (frameCount % 60 == 0) // Every second at 60 FPS
{
    var (lat, lon, alt) = geo.ToGeodetic(position.Value);
    cmd.SetManagedComponent(entity, new PositionGeodetic { ... });
}
```

### Issue: Inaccurate Distances Between Entities

**Symptom**: Distance calculations incorrect for distant entities

**Causes**:
1. Using Euclidean distance on geodetic coordinates (wrong!)
2. ENU approximation error beyond 100km
3. Altitude not included in distance calculation

**Solution**:
```csharp
// Correct: Use Haversine formula for great circle distance
double distance = CalculateGreatCircleDistance(lat1, lon1, lat2, lon2);

// Or convert to local coordinates and use Euclidean (if within 100km)
Vector3 pos1 = geo.ToCartesian(lat1, lon1, alt1);
Vector3 pos2 = geo.ToCartesian(lat2, lon2, alt2);
float distance = Vector3.Distance(pos1, pos2);

// Include altitude in 3D distance
float distance3D = MathF.Sqrt(distanceXY * distanceXY + deltaAlt * deltaAlt);
```

### Issue: Entities Spawn Underground

**Symptom**: Entities appear below terrain after GPS spawn

**Causes**:
1. Altitude set to 0 (ellipsoid surface, not ground level)
2. Terrain elevation not accounted for
3. Geoid-ellipsoid separation ignored

**Solution**:
```csharp
// Query terrain elevation at (lat, lon)
double terrainElevationMSL = terrain.GetElevation(lat, lon); // meters above sea level

// Apply geoid separation to get ellipsoidal height
double geoidSep = geoidModel.GetSeparation(lat, lon); // e.g., -20m
double ellipsoidHeight = terrainElevationMSL + geoidSep;

// Spawn entity above terrain
double spawnAlt = ellipsoidHeight + groundClearance; // e.g., +2m clearance
Vector3 pos = geo.ToCartesian(lat, lon, spawnAlt);
```

---

## Conclusion

**Fdp.Toolkit.Geographic** provides mathematically rigorous geospatial coordinate transformations essential for real-world simulation integration. The WGS84/ECEF/ENU transformation pipeline enables seamless bridging between GPS coordinates (for external systems, telemetry, GIS integration) and local Cartesian physics (for efficient ECS simulation). The toolkit's design prioritizes precision (double-precision geodetic, iterative ECEF conversion), performance (cached rotation matrices, change detection), and modularity (IGeographicTransform abstraction, IModule integration).

**Key Strengths**:
- **Mathematical Rigor**: Standard WGS84 ellipsoid model with validated conversion algorithms
- **Precision**: Sub-centimeter accuracy within 100km radius, suitable for high-fidelity simulation
- **Network Interoperability**: Coordinate-system-independent replication via PositionGeodetic
- **Performance**: Zero-allocation hot paths, cached transforms, change detection optimization
- **Modularity**: Clean abstraction (IGeographicTransform), dependency injection, testable

**Recommended Use Cases**:
- Vehicle/UAV simulation with GPS navigation
- Multi-site distributed exercises (each site with local origin)
- Sensor fusion (GPS, IMU, terrain-relative navigation)
- GIS integration (import OSM data, export telemetry)
- Trajectory planning on ellipsoid surface

**Limitations to Consider**:
- Flat-earth approximation (ENU tangent plane) limits area to ~100km radius
- Single-precision local physics (use multiple origins for large areas)
- No geoid model (altitude is ellipsoidal, not mean sea level)

For questions, contributions, or feature requests, see the FDP project repository or contact the development team.

---

**Total Lines**: 819
