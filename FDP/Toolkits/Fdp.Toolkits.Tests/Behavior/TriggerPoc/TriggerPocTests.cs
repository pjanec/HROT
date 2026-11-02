using Fdp.Core;
using Fdp.Toolkit.ScenarioRules.Tests.Actions;
using Xunit;

namespace Fdp.Toolkit.Behavior.Tests.TriggerPoc
{
    /// <summary>
    /// ⭐ TRIGGER PoC, what is LEFT (SR-93..98, docs/DESIGN_Scenario_Rules.md): the action machinery was lifted into production
    /// (<c>Fdp.Toolkit.ScenarioRules.Actions</c>, rails in <c>ScenarioRules/Actions/</c>, task CE-4160…4165). Only P5 — the <c>when</c> + the trigger driver
    /// <c>PocTrigger</c> — remains here, until the production <c>Trigger</c> behaviour (S2-c) re-homes its rails and this folder is deleted.
    /// </summary>
    public sealed unsafe partial class TriggerPocTests
    {
        private static EntityRepository NewWorld()
        {
            var w = ActionKit.NewWorld();
            w.RegisterComponent<PocSignal>();
            w.RegisterComponent<PocSignalB>();
            w.RegisterComponent<PocTriggerState>();
            return w;
        }

        private static Entity Unit(EntityRepository w, params string[] lists) => ActionKit.Unit(w, lists);
        private static string Steps(params string[] steps) => ActionKit.Steps(steps);
        private static string Step(string act, string args = "{}") => ActionKit.Step(act, args);
    }
}
