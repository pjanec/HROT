namespace Fdp.Toolkit.Motion
{
    // ⭐ CE-10000 — its own file: the DDS code generator writes one IDL per file and IDL enumerators share a scope (two `None`s collide).
    /// <summary>⭐ AC-12 — a human-like lapse, drawn past the arena's stalemate (per-entity <c>SimRng</c>). ⛔ The gun jam is CE-4644's malfunction.</summary>
    public enum PilotLapse : byte
    {
        None = 0,
        /// <summary>Holds a gun track until GCAS at a third of the floor.</summary>
        Fixation = 1,
        /// <summary>+2 s reaction to a launch.</summary>
        LateWarning = 2,
        /// <summary>A slow turn under corner speed for 10 s.</summary>
        EnergyBleed = 3,
        /// <summary>After &gt; 8 g for 5 s: no control for 3 s, wings level.</summary>
        GLoc = 4,
    }
}
