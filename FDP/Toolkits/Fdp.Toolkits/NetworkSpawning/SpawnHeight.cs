namespace Fdp.Toolkit.NetworkSpawning
{
    /// <summary>
    /// ⭐ <c>CE-1017</c> S2 — how a spawn's birth Z is chosen. 📄 docs/DESIGN_Add_Entity_Picker.md §2b–§2e (D6b rev 4/5/7).
    /// <para>⭐ Explicit mode + level rather than magic level values: a sentinel reads like a real level in logs.</para>
    /// </summary>
    public enum SpawnHeightMode : byte
    {
        /// <summary>The sent Z is used as is (today's behaviour; the motion model clamps per W8). 0 on the wire = absent.</summary>
        Absolute = 0,
        /// <summary>Z = the level's surface; the sent Z is ignored. Add Entity's default.</summary>
        OnLevel = 1,
        /// <summary>Z = the level's surface + the sent Z (a parachutist above the ground, a submarine below it).</summary>
        AboveLevel = 2,
    }

    /// <summary>
    /// ⭐ <c>CE-1017</c> S2 — a birth-height request: <see cref="Level"/> 0 = the ground (<c>TerrainWorld.GroundZ</c>, also
    /// inside a building footprint), +n = the n-th level above, −n = the n-th below; past either end ⇒ the highest / lowest.
    /// Resolved by <c>NetworkSpawningSystem</c> through <c>TerrainWorld.ResolveLevel</c>; with no terrain resident the
    /// sent Z stands.
    /// </summary>
    public readonly record struct SpawnHeight(SpawnHeightMode Mode, short Level)
    {
        /// <summary>The ground, ignoring the sent Z.</summary>
        public static SpawnHeight OnGround => new(SpawnHeightMode.OnLevel, 0);

        /// <summary>The Z to spawn at, given the level's surface Z and the sent Z.</summary>
        public float Apply(float levelZ, float sentZ) => Mode switch
        {
            SpawnHeightMode.OnLevel => levelZ,
            SpawnHeightMode.AboveLevel => levelZ + sentZ,
            _ => sentZ,
        };

        /// <summary>The wire pair → the request; <see cref="SpawnHeightMode.Absolute"/> (or an unknown mode) ⇒ null (absent).</summary>
        public static SpawnHeight? FromWire(byte mode, short level)
            => mode is (byte)SpawnHeightMode.OnLevel or (byte)SpawnHeightMode.AboveLevel
                ? new SpawnHeight((SpawnHeightMode)mode, level)
                : null;
    }
}
