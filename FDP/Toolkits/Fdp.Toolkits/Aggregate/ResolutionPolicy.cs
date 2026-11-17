using System.Runtime.InteropServices;
using Fdp.Core;
using Fdp.Toolkit.Replication;

namespace Fdp.Toolkit.Aggregate
{
    /// <summary>
    /// ⭐⭐ <c>CE-8300</c> (ST-5, §16b R-1…R-7, SV-9, SV-15, R-8326) — THE RESOLUTION POLICY: the numbers the <see cref="ResolutionManagerSystem"/>
    /// decides by, as an unmanaged component on the World Environment (the entity <c>SetSkillAmplifier</c> writes). Set by the
    /// <c>SetResolutionPolicy</c> verb (a trigger, the game master) or authored in the scenario; saved (default data policy) and recorded.
    /// ⭐ Every number is the same name and unit as <see cref="ResolutionBands"/>; <b>a field ≤ 0 means "the SV-9 default"</b> (an authored file that
    /// holds only some of them is complete; to ask for "none" write a tiny positive value). Read through <see cref="ResolutionPolicies.BandsOf"/>.
    /// ⭐ The detail areas (SV-15: a village the exercise is about — always physical) are up to <see cref="MaxAreas"/> AUTHORED entity references —
    /// <see cref="EntityLink"/>s (CE-8304, R-8380: a person names them, so the file keeps HOW — a GUID / <c>@Key</c> — and the scenario loader resolves
    /// them, <c>DESIGN_Scenario_Rules.md</c> SR-140 as amended by S9′, the <c>RoutePlan.Waypoints[].HoldUnit</c> precedent; never a network id in the file);
    /// ⚠ the manager does not read them yet (no field in <see cref="ResolutionBands"/>; a note to agg-backend).
    /// ⭐ <see cref="QuietSeconds"/> / <see cref="DeadlineSeconds"/> are <see cref="SimDuration"/>s (S0, the <see cref="ResolutionBands"/> precedent);
    /// their memory and JSON are a float's, so a saved file and the verb's JSON read the same. ⭐ <see cref="FrameBudgetMs"/> is the
    /// performance governor's cap (SV-8) — DOCUMENTED and DORMANT: nothing reads it.
    /// </summary>
    [ComponentId(GlobalComponentIds.ResolutionPolicy)]
    [StructLayout(LayoutKind.Sequential)]
    public struct ResolutionPolicy
    {
        /// <summary>The most detail areas one policy names (SV-15).</summary>
        public const int MaxAreas = 8;

        public float PhysicalBand;
        public float LeadMetres;
        public float MergeMargin;
        public SimDuration QuietSeconds;
        public float ForcedRange;
        public SimDuration DeadlineSeconds;
        /// <summary>SV-8, dormant: the frame budget (ms) a future governor would merge the quietest units to keep. Nothing reads it.</summary>
        public float FrameBudgetMs;
        /// <summary>How many of <see cref="Area0"/>… are set.</summary>
        public int AreaCount;
        // explicit fields, not an InlineArray: the scenario loader's link resolution (and the EntityRef remap) walks fields
        public EntityLink Area0, Area1, Area2, Area3, Area4, Area5, Area6, Area7;

        /// <summary>The detail area in slot <paramref name="i"/> as authored (none when out of range).</summary>
        public readonly EntityLink LinkAt(int i) => i switch
        {
            0 => Area0, 1 => Area1, 2 => Area2, 3 => Area3, 4 => Area4, 5 => Area5, 6 => Area6, 7 => Area7, _ => EntityLink.None,
        };

        /// <summary>The detail area in slot <paramref name="i"/> — its live answer (none when out of range).</summary>
        public readonly EntityRef AreaAt(int i) => LinkAt(i).Ref;

        /// <summary>Appends <paramref name="area"/> (once — the same live entity, or the same authored form); <c>false</c> when full or none.</summary>
        public bool AddArea(EntityLink area)
        {
            if (area.IsNone) return false;
            for (int i = 0; i < AreaCount; i++)
            {
                var have = LinkAt(i);
                if (area.Ref.IsNone ? have.Spec == area.Spec : have.Ref.Equals(area.Ref)) return true;
            }
            if (AreaCount >= MaxAreas) return false;
            switch (AreaCount)
            {
                case 0: Area0 = area; break; case 1: Area1 = area; break; case 2: Area2 = area; break; case 3: Area3 = area; break;
                case 4: Area4 = area; break; case 5: Area5 = area; break; case 6: Area6 = area; break; default: Area7 = area; break;
            }
            AreaCount++;
            return true;
        }

        /// <summary>Forgets every detail area.</summary>
        public void ClearAreas()
        {
            AreaCount = 0;
            Area0 = Area1 = Area2 = Area3 = Area4 = Area5 = Area6 = Area7 = EntityLink.None;
        }

        /// <summary>The bands this policy says: each field > 0, else SV-9's default.</summary>
        public readonly ResolutionBands Bands()
        {
            var d = ResolutionBands.Default;
            return new ResolutionBands(
                PhysicalBand > 0f ? PhysicalBand : d.PhysicalBand,
                LeadMetres > 0f ? LeadMetres : d.LeadMetres,
                MergeMargin > 0f ? MergeMargin : d.MergeMargin,
                QuietSeconds > 0f ? QuietSeconds : d.QuietSeconds,
                ForcedRange > 0f ? ForcedRange : d.ForcedRange,
                DeadlineSeconds > 0f ? DeadlineSeconds : d.DeadlineSeconds);
        }
    }

    /// <summary>⭐ CE-8300 — the manager's <see cref="ResolutionManagerSystem.Policy"/> function.</summary>
    public static class ResolutionPolicies
    {
        /// <summary>The bands in force: the World Environment's <see cref="ResolutionPolicy"/>, else <see cref="ResolutionBands.Default"/>.
        /// ⭐ Allocation-free (a component query's first match); called once per manager step.</summary>
        public static ResolutionBands BandsOf(EntityRepository repo)
        {
            if (!repo.IsComponentTypeRegistered<ResolutionPolicy>()) return ResolutionBands.Default;
            foreach (var e in repo.Query().With<ResolutionPolicy>().Build())
                return repo.GetComponentRO<ResolutionPolicy>(e).Bands();
            return ResolutionBands.Default;
        }
    }
}
