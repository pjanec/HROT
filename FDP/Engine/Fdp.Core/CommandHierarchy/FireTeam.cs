namespace Fdp.Core.CommandHierarchy
{
    /// <summary>
    /// ⭐ <c>CE-2281</c> / R-3019 (docs/designs/group-maneuvers/DESIGN_Defend_And_Attack_Position.md §12c, §13c-1 ④) — the fire team a
    /// subordinate belongs to within his squad (the doctrinal 9-man squad: a leader and two teams of four). It travels with his
    /// assignment like his <see cref="TacticalDesignation"/>: on <see cref="UnitSubordinate"/>, on <see cref="CmdAssignSubordinate"/>, in
    /// the scenario and on the wire (<c>EntityInfo</c>), so the plays that split a squad by team see it on the brain node.
    /// <c>None</c> (0) is the default: no team.
    /// </summary>
    public enum FireTeam : byte
    {
        None = 0,
        A    = 1,
        B    = 2,
        C    = 3,
        D    = 4,
    }
}
