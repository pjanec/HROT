namespace Fdp.Toolkit.Behavior.Shared
{
    /// <summary>
    /// ⭐⭐ <b><c>CE-2035</c> — the ONE string hash behind every utility-AI id</b>: a decision's id (from its <c>AssetId</c>),
    /// an input reader's 16-bit id (from its name), and <c>In.Fnv1a32</c> for everything a decision builder names.
    /// 📄 <c>DESIGN_Unified_Behaviour_Run.md</c> "S8g" · formula: <c>Utility_AI_SourceGenerator_Design_v1_1.md</c> §3.3
    /// (<c>hash ^= c</c>, the whole char).
    ///
    /// <para>
    /// ⛔ It was written five times in two forms: the decision and input GENERATORS XOR'd the whole char; the runtime
    /// <c>In.Fnv1a32</c> (so <c>UtilityDecisionCatalog</c>/<c>UtilityDecisionDefBuilder.ComputeId</c> and the editor's preview
    /// input ids) and the blueprint compiler's <c>Stage5_Schedule.ComputeDecisionId</c> XOR'd only the low byte. Identical for
    /// ASCII, so every shipped id agreed — and for a non-ASCII <c>AssetId</c> one generated catalog registered a decision under
    /// one id while its definition carried the other.
    /// </para>
    ///
    /// <para>⛔ A linked <c>internal</c> file for the reason <see cref="HsmActionKey"/> gives (the netstandard2.0 wall).</para>
    /// </summary>
    internal static class UtilityIdHash
    {
        /// <summary>FNV-1a-32 over <paramref name="s"/>'s chars, each XOR'd whole (basis 2166136261, prime 16777619).</summary>
        public static uint Fnv1a32(string s)
        {
            unchecked
            {
                uint hash = 2166136261u;
                foreach (char c in s)
                {
                    hash ^= c;
                    hash *= 16777619u;
                }
                return hash;
            }
        }

        /// <summary>A decision's runtime id: <see cref="Fnv1a32"/> of its <c>AssetId</c>, bit-cast to <c>int</c>.</summary>
        public static int DecisionId(string assetId) => unchecked((int)Fnv1a32(assetId));

        /// <summary>An input reader's id: the low 16 bits of <see cref="Fnv1a32"/> (NOT a native FNV-1a16).</summary>
        public static ushort InputId(string name) => (ushort)(Fnv1a32(name) & 0xFFFF);
    }
}
