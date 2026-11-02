using System.Runtime.InteropServices;
using Fdp.Core;
using Fdp.Toolkit.Replication;

namespace Fdp.Toolkit.ScenarioRules
{
    /// <summary>
    /// ⭐ rules S1-a / S1-c (📄 docs/DESIGN_Scenario_Rules.md §00.8) — an objective's subject (whose men it watches) and parent (the tree,
    /// SR-53). A COPY of the entity's params (the one owner), rewritten by its behaviour each tick; replicated inside
    /// <c>dtObjectiveStatus</c> as net ids. NoScenario.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    [ComponentId(GlobalComponentIds.ObjectiveLinks)]
    [DataPolicy(DataPolicy.NoScenario)]
    public struct ObjectiveLinks
    {
        public EntityRef Subject;
        public EntityRef Parent;
    }
}
