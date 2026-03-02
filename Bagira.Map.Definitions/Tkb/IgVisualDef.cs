namespace Bagira.Map.Definitions.Tkb
{
    /// <summary>
    /// IG visual properties (color, symbol, 3D model).
    /// </summary>
    public class IgVisualDef
    {
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
    }
}
