using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Fdp.Core;

namespace Fdp.Toolkit.ScenarioRules
{
    /// <summary>⭐ rules V-3 — one leaf of a trigger's <c>when</c>: its label, whether it holds now (0/1) and the number behind it (a property's value · the sim time · a count · else 0/1).</summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct RuleLeaf
    {
        public FixedString32 Label;
        public byte Value;
        public float Measured;
    }

    /// <summary>⭐ rules V-3 — up to eight <see cref="RuleLeaf"/>s.</summary>
    [InlineArray(8)] public struct Leaf8 { private RuleLeaf _e; }

    /// <summary>
    /// ⭐ rules V-3 (📄 docs/DESIGN_Scenario_Rules.md §00.10) — a trigger's <c>when</c> leaves with their LIVE values, for the rules view. Written by <c>RuleLeafSampler</c> (the owner, ≤ 2 Hz of sim
    /// time, ONLY while the node feature <c>ui.rules-live-values</c> is on); replicated as <c>dtRuleLeaves</c> (Brain group). Unmanaged (the sampler rewrites it twice a second: a managed
    /// copy would allocate each time), <c>NoScenario</c>.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    [ComponentId(GlobalComponentIds.RuleLeaves)]
    [DataPolicy(DataPolicy.NoScenario)]
    public struct RuleLeaves
    {
        /// <summary>How many of <see cref="Leaves"/> are meaningful.</summary>
        public byte Count;
        public Leaf8 Leaves;
    }
}
