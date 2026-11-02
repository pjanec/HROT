namespace Hrot.Map.Definitions.Behavior
{
    [Flags]
    public enum BehaviorCategory
    {
        None        = 0,
        Civilian    = 1 << 0,
        MilitaryApc = 1 << 1,
        Infantry    = 1 << 2,
        Insurgent   = 1 << 3,
        AllMilitary = MilitaryApc | Infantry | Insurgent,
        Commander   = 1 << 4,
        /// <summary>⭐ rules S1 — an objective / aggregator: the brain of an Objective entity (TKB 8860), no body.</summary>
        Objective   = 1 << 5,
    }
}
