using System;
using System.Linq;
using Fdp.Core;
using Fdp.Toolkit.NetworkSpawning.Events;
using Fdp.Toolkit.Scenario;
using Fdp.Toolkit.ScenarioRules;
using Hrot.AI.Behaviors.Brains.Objectives;
using Xunit;

namespace Hrot.SimHost.Tests
{
    public sealed partial class ScenarioRulesTests
    {
        private static void AllowCreation(ObjWorld w)
        {
            if (!w.Repo.Bus.IsRegisteredManaged<EntityCreationRequest>()) w.Repo.Bus.RegisterManaged<EntityCreationRequest>();
        }

        /// <summary>The creating node, in miniature: the instance entity the request names, with the status the expander would write.</summary>
        private static Entity Instance(ObjWorld w, EntityCreationRequest r, MacroExpansionState state, string reason = "")
        {
            var e = w.Repo.CreateEntity();
            var guid = r.InitialComponents!.OfType<EntityGuid>().Single();
            w.Repo.AddComponent(e, guid);
            w.Repo.AddComponent(e, new MacroExpansionStatus { State = state, Reason = new FixedString128(reason) });
            return e;
        }

        /// <summary>⭐ Macros S3b B1 (SR-93 / SR-96 / SR-157 / SR-171) — a trigger's <c>InsertMacro</c> asks the ONE creation path for a TRANSIENT macro instance
        /// at its <c>at</c> + <c>heading</c>, the step's nested <c>"params"</c> as the instance's args (the ArgsRef route <c>Order</c> uses), and then only READS
        /// the instance's status: Expanding ⇒ the trigger is still Acting, Done ⇒ the list ends. 🔴 Red-proof: the verb returns Success right after asking ⇒
        /// the trigger is Done while the instance still expands.</summary>
        [Fact]
        public void InsertMacro_AsksForATransientInstance_WithItsArgs_AndWaitsForTheExpansion()
        {
            var w = new ObjWorld();
            AllowCreation(w);
            var src = Cond(w);
            var t = Fire(w, src, Do(Step("InsertMacro", "{\"macro\":\"test/fire-team\",\"at\":[10,20,0],\"heading\":90,\"params\":{\"side\":\"Friend\"}}")));

            Flip(w, src, true); w.Run(1);   // the request is read when the NEXT tick swaps the bus
            var r = Assert.Single(w.Created);
            Assert.Equal((long)Hrot.Map.Common.TkbEntityTypes.ScenarioMacro, (long)r.TkbType);
            Assert.True(r.IsTransient);                                                         // SR-171: the trigger is saved, never what it inserted
            var mi = r.InitialComponents!.OfType<MacroInstance>().Single();
            Assert.Equal("test/fire-team", mi.Macro);
            Assert.Contains("Friend", mi.ArgsJson);                                            // the step's nested "params"
            var at = r.InitialComponents!.OfType<SimTransform>().Single();
            Assert.Equal((10f, 20f), (at.Position.X, at.Position.Y));
            Assert.Equal(TriggerState.Acting, Phase(w, t));

            var inst = Instance(w, r, MacroExpansionState.Expanding);
            w.Run(1);
            Assert.Equal(TriggerState.Acting, Phase(w, t));                                      // still expanding ⇒ still running
            Assert.Single(w.Created);                                                           // asked ONCE

            w.Repo.GetComponentRW<MacroExpansionStatus>(inst).State = MacroExpansionState.Done;
            w.Run(1);
            Assert.NotEqual(TriggerState.Acting, Phase(w, t));                                   // the list ended
            Assert.NotEqual(ObjectiveState.Failed, w.S(t).State);
        }

        /// <summary>⭐ B1, found LIVE — <c>at: "@Key"</c> (SR-157) puts the instance at the keyed entity's position. 📌 Live it landed at (0,0,0): the step runner
        /// resolved links only for params holding a network id (<c>HoldsRefs</c>), never a <c>PointLink</c>. 🔴 Red-proof: <c>HoldsRefs</c> back ⇒ X = 0.</summary>
        [Fact]
        public void InsertMacro_AtAKey_StandsAtThatEntity()
        {
            var w = new ObjWorld();
            AllowCreation(w);
            if (!w.Repo.IsComponentTypeRegistered<RefKey>()) w.Repo.RegisterComponent<RefKey>();
            var drop = w.Man(new System.Numerics.Vector3(150, 100, 0), ForceId.Neutral);
            w.Repo.AddComponent(drop, new RefKey("Drop"));
            var src = Cond(w);
            Fire(w, src, Do(Step("InsertMacro", "{\"macro\":\"test/fire-team\",\"at\":\"@Drop\",\"heading\":90}")));
            Flip(w, src, true); w.Run(1);
            var at = Assert.Single(w.Created).InitialComponents!.OfType<SimTransform>().Single();
            Assert.Equal((150f, 100f), (at.Position.X, at.Position.Y));
        }

        /// <summary>⭐ B1 / SR-96 — a FAILED expansion fails the step with the expander's own reason.</summary>
        [Fact]
        public void InsertMacro_AFailedExpansion_FailsTheStep_WithItsReason()
        {
            var w = new ObjWorld();
            AllowCreation(w);
            var src = Cond(w);
            var t = Fire(w, src, Do(Step("InsertMacro", "{\"macro\":\"test/nowhere\",\"at\":[0,0,0]}")));
            Flip(w, src, true); w.Run(1);   // the request is read when the NEXT tick swaps the bus
            var r = Assert.Single(w.Created);
            Assert.Equal("{}", r.InitialComponents!.OfType<MacroInstance>().Single().ArgsJson);   // no "params" ⇒ the macro's declared defaults
            Instance(w, r, MacroExpansionState.Failed, "macro 'test/nowhere' not found");
            w.Run(1);
            Assert.Contains("not found", w.S(t).Reason.ToString());
        }
    }
}
