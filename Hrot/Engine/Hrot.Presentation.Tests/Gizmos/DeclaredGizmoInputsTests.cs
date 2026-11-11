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
    
        [Fact]
        public void HealthBarGizmo_GatedPictureEqualsTheUngatedOne()
        {
            var settings = new Fdp.Toolkit.Diagnostics.Gizmos.Settings.GizmoSettingsRegistry();
            var rig = new GatedRuleRig(new Hrot.Common.Diagnostics.Gizmos.HealthBarGizmo(settings),
                r => { Common(r); r.RegisterComponent<Fdp.Toolkit.Combat.Components.Health>(); },
                (r, i) => { var e = Basic(r, i); r.AddComponent(e, new Fdp.Toolkit.Combat.Components.Health { Current = 10, Max = 10 }); return e; });
            rig.Standard(
                g => g.Repo.SetComponent(g.Ents[2], new Fdp.Toolkit.Combat.Components.Health { Current = 3, Max = 10 }),   // a value
                g => settings.Write(Fdp.Toolkit.Diagnostics.Gizmos.Settings.GizmoSettingsRegistry.ComputeHash(Hrot.Common.Diagnostics.Gizmos.HealthBarGizmoSettings.BarHeightKey),
                                    Fdp.Toolkit.Diagnostics.Gizmos.Settings.GizmoSettingValue.From(9f)));                      // the setting
        }
    
        [Fact]
        public void CommandPictureAbsentGizmo_GatedPictureEqualsTheUngatedOne()
        {
            var made = new System.Collections.Generic.List<Entity>();
            var rig = new GatedRuleRig(new CommandPictureAbsentGizmo(),
                r => { Common(r); r.RegisterComponent<Fdp.Core.CommandHierarchy.UnitSubordinate>(); },
                (r, i) =>
                {
                    var e = Basic(r, i);
                    r.AddComponent(e, new Fdp.Core.CommandHierarchy.UnitSubordinate { Commander = i % 3 == 0 ? Entity.Null : made[i - 1] });
                    made.Add(e);
                    return e;
                });
            rig.Standard(
                g => g.Repo.SetComponent(g.Ents[7], new Fdp.Core.CommandHierarchy.UnitSubordinate { Commander = g.Ents[8] }),   // another entity becomes a commander
                g =>                                                                                                           // the Squad-command layer is switched off, then on
                {
                    var off = default(Fdp.Toolkit.Diagnostics.Gizmos.LayerMask256); off.SetBit(1);
                    g.GatedBuf.SetActiveLayers(off); g.PlainBuf.SetActiveLayers(off); g.Frames(8);
                    g.Repo.SetComponent(g.Ents[1], new SimTransform { Position = new Vector3(15f, 1f, 0), Rotation = Quaternion.Identity }); g.Frames(8);   // a redraw WHILE hidden: the held picture is the empty one
                    var on = default(Fdp.Toolkit.Diagnostics.Gizmos.LayerMask256); on.SetAll();
                    g.GatedBuf.SetActiveLayers(on); g.PlainBuf.SetActiveLayers(on);
                },
                g => g.Repo.SetComponent(g.Ents[4], new SimTransform { Position = new Vector3(5f, 5f, 0), Rotation = Quaternion.Identity }));   // a position
        }
    }
}
