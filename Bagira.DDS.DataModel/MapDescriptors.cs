using System;
using System.Collections.Generic;
using CycloneDDS.Schema;
using Bagira.DDS.DM;

namespace Bagira.BDC.SSTD
{
    // ===================================================================================
    // MAP / SCENARIO DESCRIPTORS
    // ===================================================================================
    // These descriptors are specific to the 2D/3D map functionality. 
    // They allow the IOS to draw tactical graphics, override visuals, and define routes.
    // ===================================================================================

    // Defines a visual override for a specific entity, targeted at a specific
    // group of displays (MapGroup).
    // Purpose: Allows "False Flag" operations or Instructor-only highlights.
    // Principle:
    // - If MapGroupId == 0, it applies to EVERYONE (Global override).
    // - If MapGroupId > 0, it applies ONLY to IGs configured with that GroupId.
    // - IG Logic: Look for specific override; if none, look for global; if none, use TKB default.
    [DdsTopic("MapEntitySymbol")]
    [DdsIdlFile("bdc-sst-map-desc")]
    [DdsQos(Reliability = DdsReliability.Reliable, Durability = DdsDurability.TransientLocal, HistoryKind = DdsHistoryKind.KeepLast, HistoryDepth = 1)]
    [DdsManaged]
    public partial struct MapEntitySymbol
    {
        // Primary Key: Which entity is being modified?
        [DdsKey]
        public int EntityId;

        // Target Group (Role).
        // 0 = Global Override (Applies to everyone).
        // >0 = Scoped Override (Applies ONLY to IGs with this MapGroupId).
        // Resolution Priority: Scoped > Global > TKB Default.
        [DdsKey]
        public int MapGroupId;

        // Named style set to apply (e.g., "False_Flag_Blue").
        // If empty, uses the entity's standard style.
        public string StyleSetId;

        // Fine-grained visual overrides in JSON format.
        // e.g., { "colorOverride": "#0000FF", "forceLabel": "DECOY", "halo": true }
        public string StyleParamsJson;
    }

    // Visual overlay descriptor (fire lines, tactical graphics, effects)
    // Supports both persistent and volatile (auto-deleting) instances
    public enum PersistenceMode
    {
        MODE_VOLATILE,    // RAM-only, auto-delete timeout supported
        MODE_PERSISTENT   // Saved to database, survives restarts
    }

    [DdsTopic("MapVisualOverlay")]
    [DdsIdlFile("bdc-sst-map-desc")]
    [DdsQos(Reliability = DdsReliability.Reliable, Durability = DdsDurability.TransientLocal, HistoryKind = DdsHistoryKind.KeepLast, HistoryDepth = 1)]
    [DdsManaged]
    public partial struct MapVisualOverlay
    {
        [DdsKey]
        public int EntityId;

        // Persistence and lifecycle
        public PersistenceMode PersistenceMode;

        // Birth Timestamp
        // The absolute time (csharp UTC datetime now ticks) when this graphic was created.
        // Requires for MODE_VOLATILE to calculate remaining life correctly.
        public long BirthTimestamp;

        // Auto-delete timeout (real-time, not sim-time)
        // 0.0 = manual delete only, > 0.0 = auto-delete after N seconds
        // Only valid for MODE_VOLATILE
        public float AutoDeleteTimeoutSeconds;

        // TKB-based styling (3-layer resolution)
        // 1. styleOverrideJson (highest priority - instance-specific)
        // 2. stylePresetName (named variant, e.g., "Hostile_Dashed")
        // 3. TKB default based on tkbTypeId in Master (lowest priority)
        public string StylePresetName;       // Empty = use TKB default

        public string StyleOverrideJson;     // Empty = no overrides

        // Geometry - interpretation depends on TKB type
        // For icon/bitmap: points[0] is anchor position
        // For line/area: points define vertices
        [DdsManaged]
        public List<GeoPosition> Points;

        // Performance optimization for large shapes during editing
        public bool IsPartialUpdate;

        [DdsManaged]
        public List<int> ChangedIndices;  // Which points changed

        // Interaction flags
        public bool IsEditable;   // Can user drag/reshape?

        public bool IsClickable;  // Can user select/click for details?
    }

    // Defines a single point in a navigation path.
    [DdsStruct]
    [DdsIdlFile("bdc-sst-map-desc")]
    [DdsManaged]
    public partial struct Waypoint
    {
        // 3D Position.
        public GeoPosition Position;

        // Optional label (e.g., "Checkpoint Alpha").
        public string Name;

        // Desired speed when traveling to this point (m/s).
        public double SpeedMetersPerSec;

        // JSON payload for mission-specific logic (e.g., "Hold for 5 mins", "Deploy sensors").
        public string ExtensionJson;
    }

    // Defines a navigation route composed of multiple waypoints.
    [DdsTopic("MapRoute")]
    [DdsIdlFile("bdc-sst-map-desc")]
    [DdsQos(Reliability = DdsReliability.Reliable, Durability = DdsDurability.TransientLocal, HistoryKind = DdsHistoryKind.KeepLast, HistoryDepth = 1)]
    [DdsManaged]
    public partial struct MapRoute
    {
        // The Entity ID representing this route.
        [DdsKey]
        public int EntityId;

        // Ordered list of waypoints.
        [DdsManaged]
        public List<Waypoint> Points;

        // If true, the route connects the last point back to the first.
        public bool IsLoop;

        // Global mission data for the whole route (JSON).
        public string ExtensionJson;
    }

    // ===================================================================================
    // MAP CONFIGURATION & STATUS
    // ===================================================================================
    // These messages manage the setup of the map display (Layers, Tools) and the
    // reporting of the IG's capabilities.
    // Principle:
    // - IOS sends "Configuration" to a MapGroup (Role).
    // - IG sends "Status" for its specific MapId (Instance).
    // ===================================================================================

    // Configuration command sent from IOS to a group of IGs.
    // Uses JSON Merge Patch to support partial updates (e.g., just toggle one layer).
    // Scope: Group-based (Role). All IGs in "Blue Force" group receive this.
    [DdsTopic("MapInteractionConfig")]
    [DdsIdlFile("bdc-sst-map-desc")]
    [DdsQos(Reliability = DdsReliability.Reliable, Durability = DdsDurability.TransientLocal, HistoryKind = DdsHistoryKind.KeepLast, HistoryDepth = 1)]
    [DdsManaged]
    public partial struct MapInteractionConfig
    {
        // The Target Group ID.
        [DdsKey]
        public int MapGroupId;

        // The Correlation ID of the currently active tool on the IOS side.
        // Example: IOS activates "Place Tank" tool -> Generates GUID "A".
        // IG receives "A" -> Stores it.
        // When user clicks map -> IG sends ClickEvent with ContextId "A".
        // IOS validates "A" matches current tool -> Executes logic.
        public Guid ActiveContextId;

        // Version number for the JSON schema, ensuring compatibility.
        public int JsonSchemaVersion;

        // The configuration payload (JSON Merge Patch - RFC 7396).
        // Keys include: "view" (layers), "tools" (active cursor), "styles".
        // Null values in JSON indicate "Reset to Default".
        public string ConfigurationJson;
    }

    // Feedback from a concrete IG instance reporting its current state.
    // Used by the IOS to synchronize its UI (e.g., checkboxes) with the reality.
    // Scope: Instance-based.
    [DdsTopic("MapConfigStatus")]
    [DdsIdlFile("bdc-sst-map-desc")]
    [DdsQos(Reliability = DdsReliability.Reliable, Durability = DdsDurability.TransientLocal, HistoryKind = DdsHistoryKind.KeepLast, HistoryDepth = 1)]
    [DdsManaged]
    public partial struct MapConfigStatus
    {
        // The specific IG Instance reporting status.
        [DdsKey]
        public int MapId;

        // Name of the preset currently loaded (e.g., "Tactical_Default").
        public string PresetName;

        // The FULL current configuration state (JSON).
        // Unlike 'MapInteractionConfig' which can be partial, this is always the complete Truth.
        public string CurrentSettingsJson;
    }

    // Announcement message sent by an IG instance when it starts up.
    // Enables the IOS to dynamically build its UI based on what the IG supports.
    // IG -> IOS
    [DdsTopic("IGCapabilitiesAnnounce")]
    [DdsIdlFile("bdc-sst-map-desc")]
    [DdsQos(Reliability = DdsReliability.Reliable, Durability = DdsDurability.TransientLocal, HistoryKind = DdsHistoryKind.KeepLast, HistoryDepth = 1)]
    [DdsManaged]
    public partial struct IGCapabilitiesAnnounce
    {
        // The specific IG Instance.
        [DdsKey]
        public int MapId;

        // Defines the layer structure (folders/items) for the IOS "Layers" panel.
        // JSON Tree format.
        public string LayerTreeJson;

        // JSON Schemas defining valid configuration options (e.g., "What tools are available?").
        public string ConfigurationSchemasJson;

        // JSON Schema validating the 'styleOverrideJson' field in overlays.
        public string OverlayStyleSchemaJson;

        // JSON Manifest of TKB types that this IG specifically supports with special visuals.
        // Used to populate "Add Entity" menus with only valid options.
        public string TkbManifestJson;
    }
}
