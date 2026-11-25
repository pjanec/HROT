namespace Hrot.AI.Behaviors.Brains
{
    /// <summary>
    /// ⭐ <c>CE-4686</c> ② (DESIGN_LLM_Commander §5d) — a play that gives its subordinates ROLES says which role each roster slot holds, read-only, from
    /// its own plan (its blackboard). A reader (the LLM commander's snapshot) finds the running play's blackboard through
    /// <see cref="Fdp.Toolkit.Behavior.OccurrenceSlotCatalog"/> (the boxed params slot) and asks it — the play is read, never changed.
    /// 📌 Found by the CE-4685 Haiku run: the model saw a platoon's Defend under <c>CompanyAttack</c>, took it for "an old Defend order I did not
    /// intend", and ended the attack to undo it — the Defend was the play's reserve role.
    /// </summary>
    public interface IPlayRoles
    {
        /// <summary>The role of roster slot <paramref name="slot"/> in words ("assault", "base of fire", "reserve" …); null = none.</summary>
        string? RoleOf(int slot);
    }

    /// <summary>The role words, one place for every play.</summary>
    public static class PlayRoleWords
    {
        public const string Assault = "assault", BaseOfFire = "base of fire", Reserve = "reserve", VehicleSupport = "vehicle support by fire",
                            Drones = "drones", Line = "on the line", Observation = "observation post";
    }
}
