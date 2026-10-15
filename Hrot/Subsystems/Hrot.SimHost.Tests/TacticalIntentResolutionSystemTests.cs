using System.Collections.Generic;
using System.Linq;
using Fdp.Core;
using Fdp.Toolkit.Behavior;
using Fdp.Toolkit.Behavior.Components;
using Fdp.Toolkit.Behavior.Events;
using Fdp.Toolkit.Behavior.TacticalOrderMapper;
using Hrot.CGF.Systems;
using Xunit;

namespace Hrot.SimHost.Tests
{
    /// <summary>
    /// Unit tests for <see cref="TacticalIntentResolutionSystem"/> (TASK-TI003).
    ///
    /// Authority model used in tests:
    /// <list type="bullet">
    ///   <item>
    ///     <b>Local authority:</b> entity has <c>BehaviorState</c> component AND
    ///     <c>SetAuthority&lt;BehaviorState&gt;(entity, true)</c> has been called.
    ///   </item>
    ///   <item>
    ///     <b>No authority:</b> entity is dead, OR entity lacks <c>BehaviorState</c>,
    ///     OR <c>BehaviorState</c> authority bit is not set (simulates remote-owned
    ///     cognitive state).
    ///   </item>
    /// </list>
    /// </summary>
    public class TacticalIntentResolutionSystemTests
    {
        // ── Helpers ──────────────────────────────────────────────────────────

        /// <summary>
        /// Creates a minimal world with <c>BehaviorState</c> registered.
        /// </summary>
        private static EntityRepository CreateTestWorld()
        {
            var repo = new EntityRepository();
            repo.RegisterComponent<BehaviorState>();
            return repo;
        }

        /// <summary>
        /// Creates an entity with local authority over <c>BehaviorState</c>.
        /// </summary>
        private static Entity CreateAuthorityEntity(EntityRepository repo)
        {
            var entity = repo.CreateEntity();
            repo.AddComponent(entity, new BehaviorState());
            repo.SetAuthority<BehaviorState>(entity, true);
            return entity;
        }

        // ── Stub mapper ───────────────────────────────────────────────────────

        /// <summary>Stub mapper that always succeeds and returns a fixed behavior name.</summary>
        private sealed class SuccessMapper : ITacticalOrderMapper
        {
            private readonly string _resultBehavior;

            public string TargetIntentId { get; }

            public SuccessMapper(string targetIntentId, string resultBehavior)
            {
                TargetIntentId = targetIntentId;
                _resultBehavior = resultBehavior;
            }

            public bool TryMap(Entity self, EntityRepository repo, string jsonParams,
                               out AssignBehaviorEvent assignment)
            {
                assignment = new AssignBehaviorEvent
                {
                    Entity       = self,
                    BehaviorName = _resultBehavior,
                    JsonParams   = jsonParams,
                };
                return true;
            }
        }

        /// <summary>Stub mapper that always returns <c>false</c> from TryMap.</summary>
        private sealed class FailingMapper : ITacticalOrderMapper
        {
            public string TargetIntentId { get; }

            public FailingMapper(string targetIntentId) => TargetIntentId = targetIntentId;

            public bool TryMap(Entity self, EntityRepository repo, string jsonParams,
                               out AssignBehaviorEvent assignment)
            {
                assignment = null!;
                return false;
            }
        }

        // ── SC-1 ─────────────────────────────────────────────────────────────

        /// <summary>
        /// SC-1: Registry has a mapper for "DefendArea"; entity has <c>BehaviorState</c>
        /// with local authority.  After publishing the intent and executing the system
        /// the mapper-translated <see cref="AssignBehaviorEvent"/> must be published.
        /// </summary>
        [Fact]
        public void Execute_MapperFound_PublishesTranslatedBehaviorEvent()
        {
            using var repo = CreateTestWorld();
            var entity = CreateAuthorityEntity(repo);

            var registry = new TacticalIntentMapperRegistry();
            registry.Register(new SuccessMapper("DefendArea", "InfantryDefend"));

            var system = new TacticalIntentResolutionSystem(registry, new BehaviorRegistry());

            repo.Bus.PublishManaged(new AssignTacticalIntentEvent
            {
                Entity     = entity,
                IntentId   = "DefendArea",
                JsonParams = "{}",
            });
            repo.Bus.SwapBuffers();

            system.Execute(repo, 0.016f);
            repo.Bus.SwapBuffers();

            var behaviorEvents = repo.Bus.ReadManaged<AssignBehaviorEvent>();
            Assert.Single(behaviorEvents);
            Assert.Equal("InfantryDefend", behaviorEvents[0].BehaviorName);
            Assert.Equal(entity, behaviorEvents[0].Entity);
        }

        // ── SC-2 ─────────────────────────────────────────────────────────────

        /// <summary>
        /// SC-2: Empty registry; entity has local authority.
        /// <see cref="AssignBehaviorEvent"/> is published with the original
        /// <c>IntentId</c> as the behavior name (pass-through).
        /// </summary>
        [Fact]
        public void Execute_EmptyRegistry_PassesThroughIntentIdAsBehaviorName()
        {
            using var repo = CreateTestWorld();
            var entity = CreateAuthorityEntity(repo);

            var system = new TacticalIntentResolutionSystem(new TacticalIntentMapperRegistry(), new BehaviorRegistry());

            repo.Bus.PublishManaged(new AssignTacticalIntentEvent
            {
                Entity     = entity,
                IntentId   = "ConvoyEscort",
                JsonParams = string.Empty,
            });
            repo.Bus.SwapBuffers();

            system.Execute(repo, 0.016f);
            repo.Bus.SwapBuffers();

            var behaviorEvents = repo.Bus.ReadManaged<AssignBehaviorEvent>();
            Assert.Single(behaviorEvents);
            Assert.Equal("ConvoyEscort", behaviorEvents[0].BehaviorName);
        }

        // ── SC-3 ─────────────────────────────────────────────────────────────

        /// <summary>
        /// SC-3: Event published for an entity that does not exist (destroyed).
        /// System must not throw and must not publish any <see cref="AssignBehaviorEvent"/>.
        /// </summary>
        [Fact]
        public void Execute_DeletedEntity_NoExceptionNoBehaviorEventPublished()
        {
            using var repo = CreateTestWorld();
            var entity = CreateAuthorityEntity(repo);
            repo.DestroyEntity(entity);

            var system = new TacticalIntentResolutionSystem(new TacticalIntentMapperRegistry(), new BehaviorRegistry());

            repo.Bus.PublishManaged(new AssignTacticalIntentEvent
            {
                Entity     = entity,
                IntentId   = "WanderMilitary",
                JsonParams = string.Empty,
            });
            repo.Bus.SwapBuffers();

            var ex = Record.Exception(() => system.Execute(repo, 0.016f));
            Assert.Null(ex);

            repo.Bus.SwapBuffers();
            var behaviorEvents = repo.Bus.ReadManaged<AssignBehaviorEvent>();
            Assert.Empty(behaviorEvents);
        }

        // ── SC-4 ─────────────────────────────────────────────────────────────

        /// <summary>
        /// SC-4: Mapper is registered for the intent ID but <c>TryMap</c> returns
        /// <c>false</c>; entity has local authority.  The fallback path must publish
        /// a new <see cref="AssignBehaviorEvent"/> with <c>BehaviorName == evt.IntentId</c>.
        /// </summary>
        [Fact]
        public void Execute_MapperReturnsFailure_FallbackPublishesIntentIdAsBehaviorName()
        {
            using var repo = CreateTestWorld();
            var entity = CreateAuthorityEntity(repo);

            var registry = new TacticalIntentMapperRegistry();
            registry.Register(new FailingMapper("DefendArea"));

            var system = new TacticalIntentResolutionSystem(registry, new BehaviorRegistry());

            repo.Bus.PublishManaged(new AssignTacticalIntentEvent
            {
                Entity     = entity,
                IntentId   = "DefendArea",
                JsonParams = "{\"radius\":100}",
            });
            repo.Bus.SwapBuffers();

            system.Execute(repo, 0.016f);
            repo.Bus.SwapBuffers();

            var behaviorEvents = repo.Bus.ReadManaged<AssignBehaviorEvent>();
            Assert.Single(behaviorEvents);
            Assert.Equal("DefendArea", behaviorEvents[0].BehaviorName);
            Assert.Equal("{\"radius\":100}", behaviorEvents[0].JsonParams);
        }

        // ── SC-5 ─────────────────────────────────────────────────────────────

        /// <summary>
        /// SC-5: Entity does NOT have authority over <c>BehaviorState</c> (simulating a
        /// remote-owned entity).  No <see cref="AssignBehaviorEvent"/> must be published
        /// and no exception must be thrown.
        /// </summary>
        [Fact]
        public void Execute_NoAuthority_NoBehaviorEventPublished()
        {
            using var repo = CreateTestWorld();

            // Create entity with BehaviorState but WITHOUT setting authority —
            // simulates remote-owned cognitive state.
            var entity = repo.CreateEntity();
            repo.AddComponent(entity, new BehaviorState());
            // Authority bit deliberately NOT set.

            var system = new TacticalIntentResolutionSystem(new TacticalIntentMapperRegistry(), new BehaviorRegistry());

            repo.Bus.PublishManaged(new AssignTacticalIntentEvent
            {
                Entity     = entity,
                IntentId   = "WanderMilitary",
                JsonParams = string.Empty,
            });
            repo.Bus.SwapBuffers();

            var ex = Record.Exception(() => system.Execute(repo, 0.016f));
            Assert.Null(ex);

            repo.Bus.SwapBuffers();
            var behaviorEvents = repo.Bus.ReadManaged<AssignBehaviorEvent>();
            Assert.Empty(behaviorEvents);
        }

        // ── CE-3167: ClearsPriorIntent and Kind ──────────────────────────────

        private sealed class RecordingProvider : IIntentClearProvider
        {
            public readonly List<Entity> Cleared = new();
            public string Name => "Recording";
            public void ClearFor(Entity entity, EntityRepository repo) => Cleared.Add(entity);
        }

        private static (EntityRepository Repo, Entity Unit, RecordingProvider Provider, TacticalIntentResolutionSystem System) ClearRig(bool authority = true, BehaviorRegistry? behaviors = null)
        {
            var repo = CreateTestWorld();
            repo.RegisterEvent<ClearBehaviorEvent>();
            var unit = repo.CreateEntity();
            repo.AddComponent(unit, new BehaviorState());
            repo.SetAuthority<BehaviorState>(unit, authority);
            var provider = new RecordingProvider();
            var clears = new IntentClearRegistry();
            clears.Register(provider);
            return (repo, unit, provider, new TacticalIntentResolutionSystem(new TacticalIntentMapperRegistry(), behaviors ?? new BehaviorRegistry(), clears));
        }

        /// <summary>⭐ CE-3167 (Entity Commanding §2.5b-d) — an order with ClearsPriorIntent runs every provider for the unit in the
        /// same call that publishes the assign; an order without the flag runs none.</summary>
        [Fact]
        public void CE3167_ClearsPriorIntent_RunsTheProviders_AndStillAssigns_WithoutTheFlagNoneRun()
        {
            var (repo, unit, provider, system) = ClearRig();
            using var _ = repo;
            repo.Bus.PublishManaged(new AssignTacticalIntentEvent { Entity = unit, IntentId = "Idle" });
            repo.Bus.SwapBuffers();
            system.Execute(repo, 0.016f);
            Assert.Empty(provider.Cleared);

            repo.Bus.SwapBuffers();
            repo.Bus.PublishManaged(new AssignTacticalIntentEvent { Entity = unit, IntentId = "Idle", ClearsPriorIntent = true, Origin = BehaviorOrigin.Operator });
            repo.Bus.SwapBuffers();
            system.Execute(repo, 0.016f);
            repo.Bus.SwapBuffers();

            Assert.Equal(new[] { unit }, provider.Cleared);
            var assigns = repo.Bus.ReadManaged<AssignBehaviorEvent>();
            Assert.Single(assigns);
            Assert.Equal(BehaviorOrigin.Operator, assigns[0].Origin);
        }

        /// <summary>⭐ CE-3167 — the providers run on the brain OWNER only: a node without authority clears nothing.</summary>
        [Fact]
        public void CE3167_ANonOwner_RunsNoProvider()
        {
            var (repo, unit, provider, system) = ClearRig(authority: false);
            using var _ = repo;
            repo.Bus.PublishManaged(new AssignTacticalIntentEvent { Entity = unit, IntentId = "Idle", ClearsPriorIntent = true });
            repo.Bus.SwapBuffers();
            system.Execute(repo, 0.016f);
            Assert.Empty(provider.Cleared);
        }

        /// <summary>⭐ CE-3167 — a Clear intent ends the running behaviour at the intent's origin (a ClearBehaviorEvent), and
        /// assigns nothing.</summary>
        [Fact]
        public void CE3167_AClearIntent_PublishesAClearAtItsOrigin_AndNoAssign()
        {
            var (repo, unit, _, system) = ClearRig();
            using var __ = repo;
            repo.Bus.PublishManaged(new AssignTacticalIntentEvent { Entity = unit, Kind = TacticalIntentKind.Clear, Origin = BehaviorOrigin.Operator });
            repo.Bus.SwapBuffers();
            system.Execute(repo, 0.016f);
            repo.Bus.SwapBuffers();

            Assert.Empty(repo.Bus.ReadManaged<AssignBehaviorEvent>());
            var clears = repo.Bus.Read<ClearBehaviorEvent>().ToArray();
            Assert.Single(clears);
            Assert.Equal(unit, clears[0].Entity);
            Assert.Equal(BehaviorOrigin.Operator, clears[0].Origin);
            Assert.Equal(0, clears[0].OnlyBehaviorHash);
        }

        // ── ⭐ CE-2141 (D2, docs/DESIGN_Orders_Params_Entity_Classes.md §7) — a release names what it ends; the last word wins ──

        private static ClearBehaviorEvent[] Resolve(EntityRepository repo, TacticalIntentResolutionSystem system, params AssignTacticalIntentEvent[] intents)
        {
            foreach (var i in intents) repo.Bus.PublishManaged(i);
            repo.Bus.SwapBuffers();
            system.Execute(repo, 0.016f);
            repo.Bus.SwapBuffers();
            return repo.Bus.Read<ClearBehaviorEvent>().ToArray();
        }

        /// <summary>⭐ D2-C — a Clear intent naming a behaviour (a commander releasing its ended drill's order) becomes a clear of THAT
        /// behaviour only; a name this node does not know cannot be running here, so nothing is published.</summary>
        [Fact]
        public void CE2141_AClearNamingABehaviour_ClearsOnlyThatBehaviour_AnUnknownNameNothing()
        {
            var behaviors = new BehaviorRegistry();
            behaviors.Register(0x2141, "AdvanceTo", new BehaviorDefinition { Name = "AdvanceTo" });
            var (repo, unit, _, system) = ClearRig(behaviors: behaviors);
            using var __ = repo;

            var named = Resolve(repo, system, new AssignTacticalIntentEvent { Entity = unit, Kind = TacticalIntentKind.Clear, IntentId = "AdvanceTo", Origin = BehaviorOrigin.Superior });
            Assert.Single(named);
            Assert.Equal(0x2141, named[0].OnlyBehaviorHash);
            Assert.Equal(BehaviorOrigin.Superior, named[0].Origin);

            Assert.Empty(Resolve(repo, system, new AssignTacticalIntentEvent { Entity = unit, Kind = TacticalIntentKind.Clear, IntentId = "NoSuchBehaviour" }));
        }

        /// <summary>⭐⭐ D2-D — the ingress applies a frame's assigns BEFORE its clears, so a release and the next drill's order to one
        /// member in the same frame would end the new order. Here a clear followed by an assign to the same entity is dropped (the
        /// last word wins); an assign followed by a clear keeps both, in order; another entity's clear is untouched.</summary>
        [Fact]
        public void CE2141_AClearFollowedByAnAssign_ToTheSameEntity_InOneFrame_IsDropped()
        {
            var (repo, unit, _, system) = ClearRig();
            using var __ = repo;
            var other = CreateAuthorityEntity(repo);

            var clears = Resolve(repo, system,
                new AssignTacticalIntentEvent { Entity = unit, Kind = TacticalIntentKind.Clear, Origin = BehaviorOrigin.Superior },
                new AssignTacticalIntentEvent { Entity = other, Kind = TacticalIntentKind.Clear, Origin = BehaviorOrigin.Superior },
                new AssignTacticalIntentEvent { Entity = unit, IntentId = "Idle", Origin = BehaviorOrigin.Superior });
            Assert.Equal(new[] { other }, clears.Select(c => c.Entity).ToArray());
            Assert.Single(repo.Bus.ReadManaged<AssignBehaviorEvent>());

            repo.Bus.SwapBuffers();
            var kept = Resolve(repo, system,
                new AssignTacticalIntentEvent { Entity = unit, IntentId = "Idle", Origin = BehaviorOrigin.Superior },
                new AssignTacticalIntentEvent { Entity = unit, Kind = TacticalIntentKind.Clear, Origin = BehaviorOrigin.Superior });
            Assert.Single(kept);
        }

        // ── ⭐ CE-2142 (D3, rule O2, docs/DESIGN_Orders_Params_Entity_Classes.md §8) — the receiver resolves an order by its class ──

        private static BehaviorRegistry Behaviours(params string[] names)
        {
            var r = new BehaviorRegistry();
            foreach (var n in names) r.Register(n, new BehaviorDefinition { Name = n });
            return r;
        }

        private static AssignBehaviorEvent[] Order(EntityRepository repo, TacticalIntentResolutionSystem system, Entity e, string order, string json = "{\"Speed\":2}")
        {
            repo.Bus.PublishManaged(new AssignTacticalIntentEvent { Entity = e, IntentId = "Order." + order, JsonParams = json, Origin = BehaviorOrigin.Operator });
            repo.Bus.SwapBuffers();
            system.Execute(repo, 0.016f);
            repo.Bus.SwapBuffers();
            return repo.Bus.ReadManaged<AssignBehaviorEvent>().ToArray();
        }

        private static Entity Typed(EntityRepository repo, ulong dis)
        {
            var e = CreateAuthorityEntity(repo);
            repo.SetDisType(e, new DISEntityType { Value = dis });
            return e;
        }

        private static ulong Dis(byte kind, byte domain, ushort country, byte category, byte sub = 0, byte specific = 0)
            => ((ulong)kind << 56) | ((ulong)domain << 48) | ((ulong)country << 32) | ((ulong)category << 24) | ((ulong)sub << 16) | ((ulong)specific << 8);

        /// <summary>⭐ The acceptance: an M1 (US) and a T-72 (Russia) are both class Tank, so the same order starts the same behaviour,
        /// with the order's JSON and origin untouched.</summary>
        [Fact]
        public void CE2142_AnM1AndAT72_ResolveTheSameOrder_ToTheirClassesBehaviour()
        {
            using var repo = CreateTestWorld();
            var system = new TacticalIntentResolutionSystem(new TacticalIntentMapperRegistry(), Behaviours("TankMove", "MoveToLocation"));
            var m1  = Typed(repo, Dis(1, 1, 225, 1, 1, 3));
            var t72 = Typed(repo, Dis(1, 1, 222, 1, 2, 1));
            foreach (var tank in new[] { m1, t72 })
            {
                var a = Assert.Single(Order(repo, system, tank, "Move"));
                Assert.Equal("TankMove", a.BehaviorName);
                Assert.Equal("{\"Speed\":2}", a.JsonParams);
                Assert.Equal(BehaviorOrigin.Operator, a.Origin);
            }
        }

        /// <summary>⭐ The acceptance: a squad resolves its OWN behaviour — the most specific class first (InfantrySquad), then broader
        /// (a tank platoon with no TankPlatoonMove falls back to UnitMove).</summary>
        [Fact]
        public void CE2142_ASquadResolvesItsOwn_MostSpecificClassFirst_ThenBroader()
        {
            using var repo = CreateTestWorld();
            var system = new TacticalIntentResolutionSystem(new TacticalIntentMapperRegistry(), Behaviours("InfantrySquadMove", "UnitMove"));
            var squad   = Typed(repo, Hrot.Core.Tkb.UnitDis.Land(Hrot.Core.Tkb.UnitArm.Infantry, Hrot.Core.Tkb.UnitEchelon.Squad, 225).Value);
            var platoon = Typed(repo, Hrot.Core.Tkb.UnitDis.Land(Hrot.Core.Tkb.UnitArm.Armor, Hrot.Core.Tkb.UnitEchelon.Platoon, 225).Value);
            Assert.Equal("InfantrySquadMove", Assert.Single(Order(repo, system, squad, "Move")).BehaviorName);
            Assert.Equal("UnitMove", Assert.Single(Order(repo, system, platoon, "Move")).BehaviorName);
        }

        /// <summary>⭐ No class of the entity fulfils the order ⇒ nothing starts. ⛔ No fallback to a soldier behaviour or to the
        /// intent id as a behaviour name (a body-less unit given a soldier's move stands still for ever).</summary>
        [Fact]
        public void CE2142_AnOrderNoClassFulfils_StartsNothing_NoFallback()
        {
            using var repo = CreateTestWorld();
            var system = new TacticalIntentResolutionSystem(new TacticalIntentMapperRegistry(), Behaviours("MoveToLocation", "Order.Move", "InfantrySquadMove"));
            var squad = Typed(repo, Hrot.Core.Tkb.UnitDis.Land(Hrot.Core.Tkb.UnitArm.Infantry, Hrot.Core.Tkb.UnitEchelon.Squad, 225).Value);
            Assert.Empty(Order(repo, system, squad, "Hold"));
            var untyped = CreateAuthorityEntity(repo);                                   // DIS 0: no class at all
            Assert.Empty(Order(repo, system, untyped, "Move"));
        }

        /// <summary>⭐ Rule O2 — two classes of the same specificity that both fulfil the order are a TIE: nothing is guessed.</summary>
        [Fact]
        public void CE2142_TwoClassesOfEqualSpecificity_BothFulfilling_AreATie_NotAGuess()
        {
            var table = new Hrot.Core.Tkb.EntityClassTable(new[]
            {
                new Hrot.Core.Tkb.EntityClass("Tracked", "Tracked", DisTypeMask.Parse("1.1.*.1,2")),
                new Hrot.Core.Tkb.EntityClass("Armoured", "Armoured", DisTypeMask.Parse("1.1.*.1,3")),
            });
            var dis = new DISEntityType { Value = Dis(1, 1, 225, 1) };
            var tie = Hrot.Core.Tkb.OrderResolver.Resolve(table, dis, "Move", n => n is "TrackedMove" or "ArmouredMove");
            Assert.Equal(Hrot.Core.Tkb.OrderResolution.Tie, tie.Outcome);
            var one = Hrot.Core.Tkb.OrderResolver.Resolve(table, dis, "Move", n => n is "TrackedMove");
            Assert.Equal("TrackedMove", one.Behaviour);

            using var repo = CreateTestWorld();
            var system = new TacticalIntentResolutionSystem(new TacticalIntentMapperRegistry(), Behaviours("TrackedMove", "ArmouredMove"), classes: table);
            Assert.Empty(Order(repo, system, Typed(repo, dis.Value), "Move"));
        }

        /// <summary>⭐ The shipped squad (production registry, its TKB DIS type) resolves Move / Hold / Attack to its own CE-2141
        /// behaviours.</summary>
        [Fact]
        public void CE2142_TheShippedSquad_ResolvesMoveHoldAttack_ToItsOwnBehaviours()
        {
            var registry = new BehaviorRegistry();
            Hrot.CGF.Configuration.CgfBehaviorSetup.LoadFromAiAssembly(registry);
            using var repo = CreateTestWorld();
            var system = new TacticalIntentResolutionSystem(new TacticalIntentMapperRegistry(), registry);
            var tkb = new Fdp.Toolkit.Tkb.TkbDatabase();
            Hrot.Map.Definitions.Tkb.NedTkbCatalog.RegisterAll(tkb);
            Assert.True(((Fdp.Interfaces.ITkbDatabase)tkb).TryGetByType(Hrot.Map.Common.TkbEntityTypes.Unit_InfantrySquad, out var template));
            var squad = Typed(repo, template!.DisType.Value);
            Assert.Equal("InfantrySquadMove",   Assert.Single(Order(repo, system, squad, "Move", "{}")).BehaviorName);
            Assert.Equal("InfantrySquadHold",   Assert.Single(Order(repo, system, squad, "Hold", "{}")).BehaviorName);
            Assert.Equal("InfantrySquadAttack", Assert.Single(Order(repo, system, squad, "Attack", "{}")).BehaviorName);
        }

        /// <summary>
        /// ⭐⭐ <c>CE-2165</c> V4 — with the PRODUCTION registry, a tank's and a technical's ATTACK resolve to <c>LandPlatformAttack</c> (the
        /// exposure cycle, DESIGN_Vehicle_Exposure_Cycle §4) — not the infantry posture behind <c>PlatformAttack</c>; their MOVE still falls
        /// back to <c>PlatformMove</c>, and a soldier's attack is still <c>PersonAttack</c>. 🔴 Red-proof: drop the <c>[Behavior]</c> on
        /// <c>BuildLandPlatformAttack</c> and the tank resolves to <c>PlatformAttack</c>.
        /// </summary>
        [Fact]
        public void V4_ATanksAttackOrder_ResolvesTo_LandPlatformAttack()
        {
            var registry = new BehaviorRegistry();
            Hrot.CGF.Configuration.CgfBehaviorSetup.LoadFromAiAssembly(registry);
            using var repo = CreateTestWorld();
            var system = new TacticalIntentResolutionSystem(new TacticalIntentMapperRegistry(), registry);
            var t72       = Typed(repo, Dis(1, 1, 222, 1, 2, 1));
            var technical = Typed(repo, Dis(1, 1, 222, 6));
            var soldier   = Typed(repo, Dis(3, 1, 222, 0));
            Assert.Equal("LandPlatformAttack", Assert.Single(Order(repo, system, t72, "Attack", "{}")).BehaviorName);
            Assert.Equal("LandPlatformAttack", Assert.Single(Order(repo, system, technical, "Attack", "{}")).BehaviorName);
            Assert.Equal("PlatformMove", Assert.Single(Order(repo, system, t72, "Move", "{}")).BehaviorName);
            Assert.Equal("PersonAttack", Assert.Single(Order(repo, system, soldier, "Attack", "{}")).BehaviorName);
        }
    

        /// <summary>
        /// ⭐ <c>CE-2173</c> (B4, R-2015) — every aircraft resolves its orders through <c>AirPlatform</c> (DIS Kind 1, Domain 2): a helicopter's
        /// and an attack plane's Move is <c>AirPlatformMove</c> (through the air), never the surface <c>PlatformMove</c>; a land vehicle's Move
        /// stays <c>PlatformMove</c>. 🔴 Red before: the helicopter resolved to <c>PlatformMove</c> and flew down to the ground point.
        /// </summary>
        [Fact]
        public void B4_EveryAircraftsMove_ResolvesTo_AirPlatformMove_ALandVehiclesStaysOnTheSurface()
        {
            var registry = new BehaviorRegistry();
            Hrot.CGF.Configuration.CgfBehaviorSetup.LoadFromAiAssembly(registry);
            using var repo = CreateTestWorld();
            var system = new TacticalIntentResolutionSystem(new TacticalIntentMapperRegistry(), registry);
            var blackHawk = Typed(repo, Dis(1, 2, 225, 21));
            var attackJet = Typed(repo, Dis(1, 2, 222, 2));
            var t72       = Typed(repo, Dis(1, 1, 222, 1, 2, 1));
            Assert.Equal("AirPlatformMove", Assert.Single(Order(repo, system, blackHawk, "Move", "{}")).BehaviorName);
            Assert.Equal("AirPlatformMove", Assert.Single(Order(repo, system, attackJet, "Move", "{}")).BehaviorName);
            Assert.Equal("PlatformMove", Assert.Single(Order(repo, system, t72, "Move", "{}")).BehaviorName);
        }

        /// <summary>⭐ <c>CE-2173</c> slice 5 (B4) — a Strike to ANY aircraft resolves to the one <c>AirPlatformStrike</c>; a land vehicle has no
        /// Strike (refused). ✅ Red-proof: without the behaviour every aircraft's Strike is refused ⇒ red.</summary>
        [Fact]
        public void B4_EveryAircraftsStrike_ResolvesTo_AirPlatformStrike_ALandVehicleHasNone()
        {
            var registry = new BehaviorRegistry();
            Hrot.CGF.Configuration.CgfBehaviorSetup.LoadFromAiAssembly(registry);
            using var repo = CreateTestWorld();
            var system = new TacticalIntentResolutionSystem(new TacticalIntentMapperRegistry(), registry);
            const string strike = "{\"Location\":[0,0]}";
            var blackHawk = Typed(repo, Dis(1, 2, 225, 21));
            var attackJet = Typed(repo, Dis(1, 2, 222, 2));
            var t72       = Typed(repo, Dis(1, 1, 222, 1, 2, 1));
            Assert.Equal("AirPlatformStrike", Assert.Single(Order(repo, system, blackHawk, "Strike", strike)).BehaviorName);
            Assert.Equal("AirPlatformStrike", Assert.Single(Order(repo, system, attackJet, "Strike", strike)).BehaviorName);
            Assert.DoesNotContain(Order(repo, system, t72, "Strike", strike), e => e.BehaviorName == "AirPlatformStrike");
        }

        /// <summary>⭐ <c>CE-2173</c> slice 6c (B12–B14, Air Operations §9e) — an Attack to ANY aircraft resolves to the one
        /// <c>AirPlatformAttack</c> (no longer the ground <c>PlatformAttack</c>, whose helicopter hovered where it was and fired); a tank
        /// keeps <c>LandPlatformAttack</c>. ✅ Red-proof: without the behaviour an aircraft's Attack falls back to <c>PlatformAttack</c> ⇒ red.</summary>
        [Fact]
        public void S6c_EveryAircraftsAttack_ResolvesTo_AirPlatformAttack()
        {
            var registry = new BehaviorRegistry();
            Hrot.CGF.Configuration.CgfBehaviorSetup.LoadFromAiAssembly(registry);
            using var repo = CreateTestWorld();
            var system = new TacticalIntentResolutionSystem(new TacticalIntentMapperRegistry(), registry);
            var blackHawk = Typed(repo, Dis(1, 2, 225, 21));
            var apache    = Typed(repo, Dis(1, 2, 225, 20));
            var attackJet = Typed(repo, Dis(1, 2, 222, 2));
            var t72       = Typed(repo, Dis(1, 1, 222, 1, 2, 1));
            Assert.Equal("AirPlatformAttack", Assert.Single(Order(repo, system, blackHawk, "Attack", "{}")).BehaviorName);
            Assert.Equal("AirPlatformAttack", Assert.Single(Order(repo, system, apache, "Attack", "{}")).BehaviorName);
            Assert.Equal("AirPlatformAttack", Assert.Single(Order(repo, system, attackJet, "Attack", "{}")).BehaviorName);
            Assert.Equal("LandPlatformAttack", Assert.Single(Order(repo, system, t72, "Attack", "{}")).BehaviorName);
        }

        /// <summary>
        /// ⭐ <c>CE-2176</c> IF-2 (FM-1, R-278) — Strike on an artillery battery (a unit, 11.1.*.4) resolves to the battery commander
        /// <c>ArtilleryUnitStrike</c>; on a self-propelled gun (1.1.*.4) to <c>SelfPropelledArtilleryStrike</c>; a rifleman has none.
        /// </summary>
        [Fact]
        public void CE2176_Strike_OnABattery_AndOnASelfPropelledGun_ResolvesToTheirFireMission_ARiflemanHasNone()
        {
            var registry = new BehaviorRegistry();
            Hrot.CGF.Configuration.CgfBehaviorSetup.LoadFromAiAssembly(registry);
            using var repo = CreateTestWorld();
            var system = new TacticalIntentResolutionSystem(new TacticalIntentMapperRegistry(), registry);
            const string strike = "{\"Location\":[0,0]}";
            var battery  = Typed(repo, Dis(11, 1, 225, 4, 5, 1));
            var m109     = Typed(repo, Dis(1, 1, 225, 4));
            var rifleman = Typed(repo, Dis(3, 1, 225, 1));
            Assert.Equal("ArtilleryUnitStrike", Assert.Single(Order(repo, system, battery, "Strike", strike)).BehaviorName);
            Assert.Equal("SelfPropelledArtilleryStrike", Assert.Single(Order(repo, system, m109, "Strike", strike)).BehaviorName);
            Assert.DoesNotContain(Order(repo, system, rifleman, "Strike", strike), e => e.BehaviorName is "ArtilleryUnitStrike" or "SelfPropelledArtilleryStrike");
        }
    }
}
