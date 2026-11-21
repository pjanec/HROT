using Fdp.Core;
using Fdp.Interfaces;
using Fdp.ModuleHost.Abstractions;

namespace Fdp.Toolkit.Scenario
{
    /// <summary>
    /// ⭐ CE-3045 — a DERIVED part (a child entity rebuilt from the TKB or by its behaviour — a weapon mount, a sensor) is
    /// never saved into a scenario. 🔴 The serializer saves every live entity without <see cref="ScenarioIgnoreTag"/>, so a
    /// derived child was saved AND rebuilt on load: a duplicate, the saved one an orphan.
    /// </summary>
    public static class DerivedParts
    {
        /// <summary>Tags <paramref name="child"/> not-saved (no-op on a world that does not register the tag).</summary>
        public static void MarkNotSaved(EntityRepository repo, Entity child)
        {
            if (repo.IsComponentTypeRegistered<ScenarioIgnoreTag>() && !repo.HasComponent<ScenarioIgnoreTag>(child))
                repo.AddComponent(child, new ScenarioIgnoreTag());
        }

        /// <summary>The command-buffer form, for a child created through a buffer.</summary>
        public static void MarkNotSaved(IEntityCommandBuffer cmd, Entity pendingChild, ISimulationView view)
        {
            if (view is EntityRepository repo && !repo.IsComponentTypeRegistered<ScenarioIgnoreTag>()) return;
            cmd.AddComponent(pendingChild, new ScenarioIgnoreTag());
        }
    }
}
