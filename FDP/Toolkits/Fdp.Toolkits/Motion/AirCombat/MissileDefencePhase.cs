namespace Fdp.Toolkit.Motion
{
    // ⭐ CE-10000 — its own file: the DDS code generator writes one IDL per file and IDL enumerators share a scope (two `None`s collide).
    /// <summary>⭐ AC-9 — where the one missile defence is.</summary>
    public enum MissileDefencePhase : byte
    {
        None = 0,
        /// <summary>IR far (&gt; 4 s to go): turn away and bleed the missile's energy.</summary>
        Drag = 1,
        /// <summary>IR near: beam + flares (§7e E4); radar: beam / notch + chaff in the end-game.</summary>
        Beam = 2,
        /// <summary>Inside 2.5 s: the motion node's reflex break (<see cref="FighterPilotState.LastDitchAt"/>).</summary>
        LastDitch = 3,
    }
}
