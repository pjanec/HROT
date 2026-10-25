using Fdp.Core;

namespace Fdp.Toolkit.Behavior.Components
{
    /// <summary>
    /// ⭐ <c>CE-2375</c> (AQ104 A-1, R-2081, behaviors) — a lone man is WAITING for the squad his area order asked to be created for him: the
    /// squad's name (how the brain recognises it when it appears — the creation result does not reach the brain node), the play to give it
    /// and its params, and when it was asked. Brain-local; gone once the squad is assigned or the wait gives up.
    /// 📄 docs/blueprints/Architect_Question_104_Area_And_Building_Combat.md §7.3a.
    /// </summary>
    [DataPolicy(DataPolicy.NoScenario)]
    [ComponentId(GlobalComponentIds.PendingUnitCommand)]
    public sealed class PendingUnitCommand
    {
        public string SquadName { get; set; } = "";
        public string Play { get; set; } = "";
        public string ParamsJson { get; set; } = "";
        public double AskedAt { get; set; }
    }
}
