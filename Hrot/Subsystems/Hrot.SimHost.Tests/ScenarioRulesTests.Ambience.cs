using System;
using System.Linq;
using System.Numerics;
using Fdp.Core;
using Fdp.Toolkit.Ambient;
using Fdp.Toolkit.NetworkSpawning.Events;
using Fdp.Toolkit.Replication.Components;
using Fdp.Toolkit.Scenario;
using Fdp.Toolkit.ScenarioRules;
using Fdp.Toolkit.ScenarioRules.Actions;
using Hrot.AI.Behaviors.Brains.Objectives.Verbs;
using Xunit;

namespace Hrot.SimHost.Tests
{
    /// <summary>
    /// ⭐⭐ <c>CE-11034</c> (R-11014, AM-3, AM-4; 📄 docs/designs/audio/DESIGN_Ambience.md §1, §4, §5 slice 1) — the <c>SetAmbience</c> shared action, run as a trigger's <c>do</c> list through the real load
    /// path: it asks the ONE creation path for a TRANSIENT layer with its FINAL <see cref="Ambience"/> (and a <c>SimTransform</c> only with a place); the same kind at the same place is ONE layer — a
    /// second call UPDATES it, never duplicates; intensity 0 REMOVES it; an unknown kind is allowed (logged) and an empty one refused at assignment. 🔴 Mutations are on each test.
    /// </summary>
    public sealed partial class ScenarioRulesTests
    {
        private static void WithAmbience(ObjWorld w)
        {
            AllowCreation(w);
            if (!w.Repo.IsComponentTypeRegistered<Ambience>()) w.Repo.RegisterComponent<Ambience>();
            if (!w.Repo.IsComponentTypeRegistered<EntityGuid>()) w.Repo.RegisterComponent<EntityGuid>();
        }

        /// <summary>The creating node, in miniature: the layer entity the request describes — its Ambience, its GUID, and its position when it has one.</summary>
        private static Entity MakeLayer(ObjWorld w, EntityCreationRequest r)
        {
            var e = w.Repo.CreateEntity();
            foreach (var c in r.InitialComponents!)
                switch (c)
                {
                    case Ambience a: w.Repo.AddComponent(e, a); break;
                    case EntityGuid g: w.Repo.AddComponent(e, g); break;
                    case SimTransform t: w.Repo.AddComponent(e, t); break;
                }
            return e;
        }

        private static string Raise(string kind, string extra = "") => Step("SetAmbience", "{\"kind\":\"" + kind + "\"" + extra + "}");

        /// <summary>A POSITIONED layer: one transient request of TKB type 8841 carrying the FINAL component (kind, intensity, radius, the sim time it ends = now + duration) and its place; the step holds (Acting)
        /// until the layer appears here, then the list ends. 🔴 Mutation: Success right after asking ⇒ the trigger is Done while the layer is still being made ⇒ red.</summary>
        [Fact]
        public void SetAmbience_AsksForATransientPositionedLayer_WithItsFinalValue_AndWaitsForIt()
        {
            var w = new ObjWorld(); WithAmbience(w);
            var src = Cond(w);
            var t = Fire(w, src, Do(Raise("alarm.airraid", ",\"intensity\":0.9,\"place\":[100,200,0],\"radius\":800,\"duration\":60")));
            Flip(w, src, true); w.Run(1);

            var r = Assert.Single(w.Created);
            Assert.Equal((long)Hrot.Map.Common.TkbEntityTypes.AmbienceLayer, (long)r.TkbType);
            Assert.True(r.IsTransient);                                                          // SR-171: the trigger is saved, never what it raised
            var a = r.InitialComponents!.OfType<Ambience>().Single();
            Assert.Equal(("alarm.airraid", 0.9f, 800f), (a.Kind.ToString(), a.Intensity, a.Radius));
            Assert.InRange(a.Until.Seconds - 60.0, w.Time - 1.0, w.Time);                        // ends 60 sim seconds after it was asked
            var at = r.InitialComponents!.OfType<SimTransform>().Single();
            Assert.Equal((100f, 200f), (at.Position.X, at.Position.Y));
            Assert.Single(r.InitialComponents!.OfType<EntityGuid>());
            Assert.Equal(TriggerState.Acting, Phase(w, t));

            MakeLayer(w, r);
            w.Run(1);
            Assert.NotEqual(TriggerState.Acting, Phase(w, t));
            Assert.NotEqual(ObjectiveState.Failed, w.S(t).State);
            Assert.Single(w.Created);                                                            // asked ONCE
        }

        /// <summary>A POSITIONLESS layer (no place) is heard everywhere: no <c>SimTransform</c> in the request, no radius, an unlimited life with no duration (Until 0), intensity defaulting to 1. 🔴 Mutation: always adding a SimTransform ⇒ red.</summary>
        [Fact]
        public void SetAmbience_WithNoPlace_IsPositionless_AndWithNoDuration_NeverEnds()
        {
            var w = new ObjWorld(); WithAmbience(w);
            var src = Cond(w);
            Fire(w, src, Do(Raise("battle.distant")));
            Flip(w, src, true); w.Run(1);
            var r = Assert.Single(w.Created);
            Assert.Empty(r.InitialComponents!.OfType<SimTransform>());
            var a = r.InitialComponents!.OfType<Ambience>().Single();
            Assert.Equal(("battle.distant", 1f, 0f, 0.0), (a.Kind.ToString(), a.Intensity, a.Radius, a.Until.Seconds));
        }

        /// <summary>⭐ AM-3 — a second call for the SAME layer (kind + place) UPDATES it: no second request, the live layer's intensity / radius / end change in place; the same kind at ANOTHER place is another layer
        /// (a new request); a positionless layer and a positioned one of one kind are two layers. 🔴 Mutation: <c>Find</c> never matching ⇒ every call asks for a new layer (a duplicate) ⇒ red.</summary>
        [Fact]
        public void SetAmbience_ASecondCallForTheSameLayer_Updates_NeverDuplicates()
        {
            var w = new ObjWorld(); WithAmbience(w);
            var src1 = Cond(w);
            Fire(w, src1, Do(Raise("alarm.airraid", ",\"intensity\":0.5,\"place\":[100,200,0],\"radius\":800,\"duration\":60")));
            Flip(w, src1, true); w.Run(1);
            var layer = MakeLayer(w, Assert.Single(w.Created));
            w.Run(1);

            var src2 = Cond(w);
            Fire(w, src2, Do(Raise("alarm.airraid", ",\"intensity\":1.0,\"place\":[100,200,0],\"radius\":1200,\"duration\":90")));
            Flip(w, src2, true); w.Run(1);
            Assert.Single(w.Created);                                                            // no second request
            var now = w.Repo.GetComponentRO<Ambience>(layer);
            Assert.Equal((1f, 1200f), (now.Intensity, now.Radius));
            Assert.InRange(now.Until.Seconds - 90.0, w.Time - 1.5, w.Time);                      // the new end
            Assert.Equal("alarm.airraid", now.Kind.ToString());

            var elsewhere = Cond(w); var everywhere = Cond(w);   // the same kind elsewhere, and everywhere: two MORE layers
            Fire(w, elsewhere, Do(Raise("alarm.airraid", ",\"place\":[900,900,0]")));
            Fire(w, everywhere, Do(Raise("alarm.airraid")));
            Flip(w, elsewhere, true); Flip(w, everywhere, true); w.Run(1);
            Assert.Equal(3, w.Created.Count);                                                    // the first, and these two
        }

        /// <summary>⭐ AM-3 — intensity 0 REMOVES the layer (the one network destroy, else destroyed here); removing what is not sounding is a no-op Success, never a request. 🔴 Mutation: 0 written as an intensity ⇒ the layer stays ⇒ red.</summary>
        [Fact]
        public void SetAmbience_WithIntensityZero_RemovesTheLayer_AndNothingIsAskedFor()
        {
            var w = new ObjWorld(); WithAmbience(w);
            var src = Cond(w);
            Fire(w, src, Do(Raise("alarm.bell", ",\"place\":[10,10,0]")));
            Flip(w, src, true); w.Run(1);
            var layer = MakeLayer(w, Assert.Single(w.Created));
            w.Run(1);
            Assert.True(w.Repo.IsAlive(layer));

            var src2 = Cond(w);
            var t = Fire(w, src2, Do(Raise("alarm.bell", ",\"intensity\":0,\"place\":[10,10,0]"), Raise("battle.night", ",\"intensity\":0")));   // the second: nothing sounds to remove
            Flip(w, src2, true); w.Run(1);
            Assert.False(w.Repo.IsAlive(layer));
            Assert.Single(w.Created);                                                            // nothing new asked for
            Assert.NotEqual(ObjectiveState.Failed, w.S(t).State);
        }

        /// <summary>A layer this node does not OWN (a replica) is not written here — its owner alone writes it: the step ends it through the network and asks for the new one. 🔴 Mutation: writing the replica in place ⇒ the replica's value changes and no new request ⇒ red.</summary>
        [Fact]
        public void SetAmbience_OnAReplica_ReplacesItInsteadOfWritingIt()
        {
            var w = new ObjWorld(); WithAmbience(w);
            if (!w.Repo.IsComponentTypeRegistered<NetworkAuthority>()) w.Repo.RegisterComponent<NetworkAuthority>();
            var replica = w.Repo.CreateEntity();
            w.Repo.AddComponent(replica, new Ambience { Kind = new FixedString32("alarm.airraid"), Intensity = 0.2f, Radius = 100f });
            w.Repo.AddComponent(replica, new NetworkAuthority(primaryOwnerId: 2, localNodeId: 1));
            var src = Cond(w);
            Fire(w, src, Do(Raise("alarm.airraid", ",\"intensity\":0.9")));
            Flip(w, src, true); w.Run(1);
            Assert.False(w.Repo.IsAlive(replica));                                               // ended here (no network id in this miniature: destroyed)
            var r = Assert.Single(w.Created);
            Assert.Equal(0.9f, r.InitialComponents!.OfType<Ambience>().Single().Intensity);
        }

        /// <summary>⭐ AM-4 — an UNKNOWN kind runs (a newer scenario on an older HROT must not fail; a renderer plays the parent or nothing); an EMPTY kind is refused at assignment, and one too long for a layer. 🔴 Mutation: <c>Check</c> refusing unknown kinds ⇒ the first assert throws ⇒ red.</summary>
        [Fact]
        public void SetAmbience_AnUnknownKindRuns_AnEmptyOrTooLongOneIsRefusedAtAssignment()
        {
            ActionList.Validate(Do(Raise("alarm.foghorn")));                                      // a new kind of a known family
            ActionList.Validate(Do(Raise("weather.rain")));                                       // no known ancestor: still allowed at run time
            var empty = Assert.Throws<ActionListException>(() => ActionList.Validate(Do(Raise(""))));
            Assert.Contains("kind", empty.Message);
            var tooLong = Assert.Throws<ActionListException>(() => ActionList.Validate(Do(Raise("alarm." + new string('x', 30)))));
            Assert.Contains("at most", tooLong.Message);

            var w = new ObjWorld(); WithAmbience(w);
            var src = Cond(w);
            Fire(w, src, Do(Raise("weather.rain", ",\"duration\":10")));
            Flip(w, src, true); w.Run(1);
            Assert.Equal("weather.rain", Assert.Single(w.Created).InitialComponents!.OfType<Ambience>().Single().Kind.ToString());
        }

        /// <summary>A layer that never appears fails the step after <see cref="AmbienceVerbs.AppearTimeoutSeconds"/> with its reason (the node serving creation never made it). 🔴 Mutation: no timeout ⇒ Acting forever ⇒ red.</summary>
        [Fact]
        public void SetAmbience_ALayerThatNeverAppears_FailsTheStep_WithItsReason()
        {
            var w = new ObjWorld(); WithAmbience(w);
            var src = Cond(w);
            var t = Fire(w, src, Do(Raise("alarm.airraid", ",\"place\":[1,2,0]")));
            Flip(w, src, true); w.Run(1);
            Assert.Equal(TriggerState.Acting, Phase(w, t));
            w.Run(AmbienceVerbs.AppearTimeoutSeconds + 1);
            Assert.Contains("never appeared", w.S(t).Reason.ToString());
        }
    }
}
