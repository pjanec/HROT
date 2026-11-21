using Fdp.Core;
using Fdp.ModuleHost.Abstractions;

namespace Fdp.Toolkit.Scenario
{
    /// <summary>
    /// ⭐⭐ CE-3389 (📄 docs/UX/DESIGN_Scenario_Presets.md §2 "saveable ⇔ variant 0", §4, D10) — THE one answer to "may this world be saved?", so the Save button,
    /// its tooltip and the mode glyph (the yellow pen) cannot disagree. Null = saveable; else the reason, in the user's words.
    /// <para>⭐ Reads <see cref="VariantForms.Variant"/> on any entity (CE-3374): a world built with a variant ≠ 0 lost the entities whose Presence roll failed,
    /// so saving it would drop them from the file. ⛔ Parameter values never block a save (each bound field keeps its binding, SR-168).</para>
    /// </summary>
    public static class ScenarioSaveGate
    {
        /// <summary>Null when <paramref name="world"/> may be saved; else why not.</summary>
        public static string? CanSave(EntityRepository world)
        {
            if (world == null) return "no world is loaded";
            if (!world.GetRegisteredComponentTypes().ContainsKey(typeof(VariantForms))) return null;   // nothing was ever built from variant forms
            foreach (var e in world.Query().WithManaged<VariantForms>().Build())
            {
                int v = ((ISimulationView)world).GetManagedComponentRO<VariantForms>(e).Variant;
                if (v != 0) return "this world was built with variant " + v + " — a variant is preview only; load the scenario as authored to save";
            }
            return null;
        }
    }
}
