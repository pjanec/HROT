using System;
using System.Collections.Generic;
using CycloneDDS.Schema;
using Hrot.NED.Common;
using Hrot.NED.Messages;

namespace Hrot.NED.Descriptors
{
    // ===================================================================================
    // MAP / SCENARIO DESCRIPTORS
    // ===================================================================================
    // These descriptors are specific to the 2D/3D map functionality. 
    // They allow the ExCon to draw tactical graphics, override visuals, and define routes.
    // ===================================================================================

    // Defines a visual override for a specific entity, targeted at a specific
    // group of displays (MapGroup).
    // Purpose: Allows "False Flag" operations or Instructor-only highlights.
    // Principle:
    // - If MapGroupId == 0, it applies to EVERYONE (Global override).
    // - If MapGroupId > 0, it applies ONLY to IGs configured with that GroupId.
    // - IG Logic: Look for specific override; if none, look for global; if none, use TKB default.
    [DdsTopic]
    [DdsIdlFile("hrot-map-desc")]
    [DdsQos(Reliability = DdsReliability.Reliable, Durability = DdsDurability.TransientLocal, HistoryKind = DdsHistoryKind.KeepLast, HistoryDepth = 1, AutodisposeUnregisteredInstances = false)]
    [DdsManaged]
    public partial struct MapEntitySymbol
    {
        // Primary Key: Which entity is being modified?
        [DdsKey]
        public long EntityId;

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

    [DdsTopic]
    [DdsIdlFile("hrot-map-desc")]
    [DdsQos(Reliability = DdsReliability.Reliable, Durability = DdsDurability.TransientLocal, HistoryKind = DdsHistoryKind.KeepLast, HistoryDepth = 1, AutodisposeUnregisteredInstances = false)]
    [DdsManaged]
    public partial struct MapVisualOverlay
    {
        [DdsKey]
        public long EntityId;

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
        // ⭐ AQ107 B: ABSOLUTE positions in local ENU metres (X east, Y north, Z up), like every position on the wire. ⛔ Not the old
        //   relative lat/lon offsets (three producers had three conventions); a receiver subtracts the entity's own position to get the
        //   entity-relative polyline its ECS holds.
        [DdsManaged]
        public List<Vec3d> Points;

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
    [DdsIdlFile("hrot-map-desc")]
    [DdsManaged]
    public partial struct Waypoint
    {
        // 3D Position: local ENU metres (X east, Y north, Z up) — AQ107 B.
        public Vec3d Position;

        // Optional label (e.g., "Checkpoint Alpha").
        public string Name;

        // Desired speed when traveling to this point (m/s).
        public double SpeedMetersPerSec;

        // JSON payload for mission-specific logic (e.g., "Hold for 5 mins", "Deploy sensors").
        public string ExtensionJson;

        // ⭐ Air Ops B1 (DESIGN_Air_Operations §5; R-169: APPENDED, reported) — a ground unit ignores these, a flyer reads them.
        // Waypoint kind (Fdp.Toolkit.Navigation.RouteWaypointKind: 0 Pass, 1 Ground).
        public byte Kind;
        // > 0 ⇒ a flyer holds this height above the terrain (m); 0 ⇒ Position's altitude.
        public float HeightAgl;
        // Hold on a Ground waypoint (Fdp.Toolkit.Navigation.RouteHold: 0 None, 1 Seconds, 2 Unload, 3 Pickup).
        public byte Hold;
        // The Seconds hold (s).
        public float HoldSeconds;
        // The unit a Pickup hold waits for (network id); 0 = none.
        public long HoldUnit;
    }

    // Defines a navigation route composed of multiple waypoints.
    [DdsTopic]
    [DdsIdlFile("hrot-map-desc")]
    [DdsQos(Reliability = DdsReliability.Reliable, Durability = DdsDurability.TransientLocal, HistoryKind = DdsHistoryKind.KeepLast, HistoryDepth = 1, AutodisposeUnregisteredInstances = false)]
    [DdsManaged]
    public partial struct MapRoute
    {
        // The Entity ID representing this route.
        [DdsKey]
        public long EntityId;

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
    // - ExCon sends "Configuration" to a MapGroup (Role).
    // - IG sends "Status" for its specific MapId (Instance).
    // ===================================================================================


    // Feedback from a concrete IG instance reporting its current state.
    // Used by the ExCon to synchronize its UI (e.g., checkboxes) with the reality.
    // Scope: Instance-based.
    [DdsTopic]
    [DdsIdlFile("hrot-map-desc")]
    [DdsQos(Reliability = DdsReliability.Reliable, Durability = DdsDurability.TransientLocal, HistoryKind = DdsHistoryKind.KeepLast, HistoryDepth = 1, AutodisposeUnregisteredInstances = false)]
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
    // Enables the ExCon to dynamically build its UI based on what the IG supports.
    // IG -> ExCon
    [DdsTopic]
    [DdsIdlFile("hrot-map-desc")]
    [DdsQos(Reliability = DdsReliability.Reliable, Durability = DdsDurability.TransientLocal, HistoryKind = DdsHistoryKind.KeepLast, HistoryDepth = 1, AutodisposeUnregisteredInstances = false)]
    [DdsManaged]
    public partial struct IGCapabilitiesAnnounce
    {
        // The specific IG Instance.
        [DdsKey]
        public int MapId;

        // Defines the layer structure (folders/items) for the ExCon "Layers" panel.
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
