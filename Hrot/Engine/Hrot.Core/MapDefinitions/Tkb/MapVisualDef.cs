namespace Hrot.Map.Definitions.Tkb
{
    /// <summary>
    /// IG visual properties (color, symbol, 3D model).
    /// </summary>
    public class MapVisualDef
    {
        /// <summary>
        /// ⭐ CE-8609 (DESIGN_Stochastic_Unit_UI §6 S-1) — what this type IS for a unit's NATO symbol (infantry, armour, mortar …): the override when
        /// <c>UnitSymbolDeriver</c>'s reading of the type's own data (its DIS type, weapons, seats) is wrong. Unset (the norm) ⇒ derived.
        /// </summary>
        public GizmoMap.Contracts.Symbology.SymbolRole? SymbolRole { get; set; }

        /// <summary>
        /// MIL-STD-2525 symbol code (e.g., "SFGPUCIZ-------" for friendly ground tank).
        /// </summary>
        public string SymbolCode { get; set; } = "SFGPUCIZ-------";
        
        /// <summary>
        /// Path to 3D model file (relative to models directory).
        /// </summary>
        public string ModelPath { get; set; } = "models/default.obj";
        
        /// <summary>
        /// Base color in hex format (#RRGGBB).
        /// </summary>
        public string ColorHex { get; set; } = "#FFFFFF";
        
        /// <summary>
        /// Model scale factor (1.0 = original size).
        /// </summary>
        public float Scale { get; set; } = 1.0f;
        
        /// <summary>
        /// Whether to show text label above entity.
        /// </summary>
        public bool ShowLabel { get; set; } = true;
        
        /// <summary>
        /// Layer name for rendering ("units_ground", "units_air", etc.).
        /// </summary>
        public string LayerName { get; set; } = "units_ground";

        /// <summary>
        /// Optional explicit name of the 2-D map shape in the entity shape library.
        /// Leave null to let the renderer auto-select based on DIS Entity Type.
        /// </summary>
        public string? MapShapeName { get; set; }

        /// <summary>⭐ <c>CE-1017</c> S0 — the entity icon's name (<c>assets/icons/&lt;IconName&gt;.png</c>); null ⇒ a generic glyph.</summary>
        public string? IconName { get; set; }

        /// <summary>⭐ <c>CE-4142</c> (G4) — the type's explicit gallery thumbnail (<c>assets/thumbnails/&lt;ThumbnailName&gt;.png</c>); null ⇒ the <c>map.json</c> mask match, else the glyph.</summary>
        public string? ThumbnailName { get; set; }
    }
}
