using System.Numerics;
using Fdp.Core;
using Fdp.Toolkit.Behavior;
using Fdp.Toolkit.Behavior.Components;
using Fdp.Toolkit.Replication.Components;
using Hrot.ScenarioEditor.Gizmos;
using Xunit;

namespace Hrot.Presentation.Tests.Gizmos
{
    /// <summary>⭐ CE-1480 G5 — one equivalence rail per rule that declares <c>IStatelessGizmo.Inputs</c> (gated picture == ungated picture over still / pan / change / seek).
    /// Red-proof of each: drop one input from the rule's declaration ⇒ its change is not followed ⇒ the rail fails.</summary>
    public sealed class DeclaredGizmoInputsTests
    {
        private static void Common(EntityRepository r)
        {
            r.RegisterComponent<SimTransform>(); r.RegisterComponent<NetworkIdentity>();
        }

        private static Entity Basic(EntityRepository r, int i)
        {
            var e = r.CreateEntity();
            r.AddComponent(e, new SimTransform { Position = new Vector3(i * 10f, 0, 0), Rotation = Quaternion.Identity });
            r.AddComponent(e, new NetworkIdentity(1000 + i));
            return e;
        }

        [Fact]
        public void EntityEditorLabelGizmo_GatedPictureEqualsTheUngatedOne()
        {
            var rig = new GatedRuleRig(new EntityEditorLabelGizmo(new BehaviorRegistry()),
                r => { Common(r); r.RegisterComponent<BehaviorState>(); r.RegisterComponent<IsEmbarkedTag>(); },
                (r, i) =>
                {
                    var e = Basic(r, i);
                    r.AddComponent(e, new BehaviorState { ActiveBehaviorHash = i == 1 ? 777 : 0 });
                    if (i % 2 == 0) r.AddComponent(e, new IsEmbarkedTag { Exposed = 1 });
                    return e;
                });
            rig.Standard(
                g => g.Repo.SetComponent(g.Ents[3], new BehaviorState { ActiveBehaviorHash = 12345 }),   // an active behaviour appears: row 40 "?"
                g => g.Repo.SetComponent(g.Ents[0], new IsEmbarkedTag { Exposed = 0 }));                  // an exposed rider becomes enclosed: no card
        }
    }
}
