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
    }
}
