namespace Fdp.Toolkit.Spatial.Eqs
{
    /// <summary>
    /// ⭐ <c>CE-2046</c> — the IDENTITY of EQS §6.6 starter template #6, <c>FindSafeRetreatPoint</c> (positional,
    /// distance-from-threats + reachability). ⛔ <b>The template itself is NOT built</b> (deferred,
    /// <c>.dev/_DONE/eqs-2/TASK-DETAIL.md</c>; <c>CE-2051</c>): this class carries no <see cref="EqsTemplateAttribute"/> and
    /// no <c>Build</c>, so no registry discovers it and no sensor is ever keyed by it.
    /// </summary>
    /// <remarks>
    /// It exists so a consumer can name the template by its stable AssetId today — the starter-pack
    /// <c>CombatPostureDecision</c>'s <c>Flee</c> option does — and so building it later is "add the attribute and
    /// <c>Build</c>", not "pick an id and find every consumer". Until then that consideration reads 0.
    /// 📄 <c>docs/blueprints/DESIGN_Unified_Behaviour_Run.md</c> "S8i".
    /// </remarks>
    public static class FindSafeRetreatPoint
    {
        /// <summary>The template's asset identity.</summary>
        public const string AssetId = "da31e4b9-1404-483d-a256-384780b0c9cb";

        /// <summary>
        /// FNV-1a over <see cref="AssetId"/>'s 16 bytes — <see cref="EqsTemplateRegistry.BlueprintIdOf"/>; a rail pins it.
        /// </summary>
        public const uint BlueprintId = 0x9392175Bu;
    }
}
