using Fdp.Kernel;
using Raylib_cs;

namespace Fdp.Examples.Showcase.Components
{
    /// <summary>
    /// Visual representation properties for rendering entities.
    /// Defines shape, color, and visual attributes.
    /// </summary>
    public struct RenderSymbol
    {
        public EntityShape Shape;
        public byte R;
        public byte G;
        public byte B;
        public float Size;
        
        public Color ToColor() => new Color(R, G, B, (byte)255);
    }
    
    public enum EntityShape
    {
        Triangle,    // Fighter/Aircraft
        Square,      // Tank/Vehicle
        Circle,      // Infantry/Personnel
        Diamond,     // Special units
        Hexagon,     // Structures
        Cross        // Projectiles
    }
}
