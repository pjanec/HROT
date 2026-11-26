namespace Fdp.Toolkit.Replication.Edits
{
    /// <summary>
    /// ⭐ CE-11061 (📄 docs/DESIGN_Injury_States.md §9, RV-4) — how a node that does not apply a unit's Health asks its owner to REVIVE it: the JSON patch
    /// <c>{"$revive":true}</c> on the existing attribute-change request, intercepted by the owner's <c>UpdateEntityAttributeRequestSystem</c> beside the F3
    /// <see cref="ComponentEdits.PatchKey"/> intercept. ⛔ No new wire type, and not a value in the attribute vocabulary — a verb rides as its own patch key.
    /// The work is <c>Fdp.Toolkit.Combat.CombatLife.Revive</c>, the ONE body, on either path.
    /// </summary>
    public static class ReviveEdits
    {
        /// <summary>The patch key the owner's request system intercepts.</summary>
        public const string PatchKey = "$revive";

        /// <summary>The patch a non-owner sends.</summary>
        public const string Patch = "{\"" + PatchKey + "\":true}";
    }
}
