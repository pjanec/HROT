namespace Fdp.Modules.Geographic
{
    /// <summary>
    /// Engine-side clamping mode enum used by ECS components in the geographic toolkit.
    /// Mirrors the DDS wire-format  enum;
    /// kept separate per the Dual-Enum Pattern (MOD1-DESIGN §2.5).
    /// </summary>
    public enum EClampingMode : byte
    {
        /// <summary>Engine decides: grounded vehicle = clamped, airborne = unclamped.</summary>
        Auto  = 0,

        /// <summary>Explicitly clamped — e.g. taxiing aircraft, editor drag-and-drop on terrain.</summary>
        ForceOn  = 1,

        /// <summary>Explicitly unclamped — e.g. in-flight, editor aerial drag.</summary>
        ForceOff = 2,
    }
}
