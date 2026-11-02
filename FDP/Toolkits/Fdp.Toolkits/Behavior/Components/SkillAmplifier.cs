using System.Runtime.InteropServices;
using Fdp.Core;

namespace Fdp.Toolkit.Behavior.Components
{
    /// <summary>
    /// ⭐⭐ Macros S3b B3 (📄 docs/DESIGN_Scenario_Macros.md §17, SR-100 / R-4106 / P-5) — the AI skill AMPLIFIER per SIDE, in tiers (−2 … +2;
    /// one tier = one preset step: Militia → Regular). On the World Environment: authored in the scenario, changed live by the
    /// <c>SetSkillAmplifier</c> verb (a trigger, a director, the game master). ⭐ NEVER a leveller: <c>ProficiencyFillSystem</c> copies the side's
    /// tiers onto each man (<see cref="CombatProficiency.SideTiers"/>) and <see cref="ProficiencyOf.Amplify"/> applies them where the level is READ;
    /// nobody's own level is written, so the slider back to 0 restores every man exactly. Unmanaged, recorded, saved.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    [ComponentId(GlobalComponentIds.SkillAmplifier)]
    [DataPolicy(DataPolicy.Default)]
    public struct SkillAmplifier
    {
        public sbyte Friend;
        public sbyte Hostile;
        public sbyte Neutral;

        /// <summary>The largest amplifier either way (P-5: −2 … +2).</summary>
        public const int MaxTiers = 2;

        /// <summary>The tiers of <paramref name="side"/> (0 for a side with none).</summary>
        public readonly int TiersOf(ForceId side) => side switch
        {
            ForceId.Friend => Friend,
            ForceId.Hostile => Hostile,
            ForceId.Neutral => Neutral,
            _ => 0,
        };

        /// <summary>Sets <paramref name="side"/>'s tiers (clamped to ±<see cref="MaxTiers"/>); <c>false</c> for a side that holds none.</summary>
        public bool Set(ForceId side, int tiers)
        {
            sbyte t = (sbyte)System.Math.Clamp(tiers, -MaxTiers, MaxTiers);
            switch (side)
            {
                case ForceId.Friend: Friend = t; return true;
                case ForceId.Hostile: Hostile = t; return true;
                case ForceId.Neutral: Neutral = t; return true;
                default: return false;
            }
        }
    }
}
