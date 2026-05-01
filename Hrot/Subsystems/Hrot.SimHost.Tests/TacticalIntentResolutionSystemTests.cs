using System.Collections.Generic;
using Fdp.Core;
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
    ///     <b>Local authority:</b> entity has <c>DoctrineState</c> component AND
    ///     <c>SetAuthority&lt;DoctrineState&gt;(entity, true)</c> has been called.
    ///   </item>
    ///   <item>
    ///     <b>No authority:</b> entity is dead, OR entity lacks <c>DoctrineState</c>,
    ///     OR <c>DoctrineState</c> authority bit is not set (simulates remote-owned
    ///     cognitive state).
    ///   </item>
    /// </list>
    /// </summary>
    public class TacticalIntentResolutionSystemTests
    {
        // ── Helpers ──────────────────────────────────────────────────────────

        /// <summary>
        /// Creates a minimal world with <c>DoctrineState</c> registered.
        /// </summary>
        private static EntityRepository CreateTestWorld()
        {
            var repo = new EntityRepository();
            repo.RegisterComponent<DoctrineState>();
            return repo;
        }

        /// <summary>
        /// Creates an entity with local authority over <c>DoctrineState</c>.
        /// </summary>
        private static Entity CreateAuthorityEntity(EntityRepository repo)
        {
            var entity = repo.CreateEntity();
            repo.AddComponent(entity, new DoctrineState());
            repo.SetAuthority<DoctrineState>(entity, true);
            return entity;
        }

        // ── Stub mapper ───────────────────────────────────────────────────────

        /// <summary>Stub mapper that always succeeds and returns a fixed doctrine name.</summary>
        private sealed class SuccessMapper : ITacticalOrderMapper
        {
            private readonly string _resultDoctrine;

            public string TargetIntentId { get; }

            public SuccessMapper(string targetIntentId, string resultDoctrine)
            {
                TargetIntentId = targetIntentId;
                _resultDoctrine = resultDoctrine;
            }

            public bool TryMap(Entity self, EntityRepository repo, string jsonParams,
                               out AssignDoctrineEvent assignment)
            {
                assignment = new AssignDoctrineEvent
                {
                    Entity       = self,
                    DoctrineName = _resultDoctrine,
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
                               out AssignDoctrineEvent assignment)
            {
                assignment = null!;
                return false;
            }
        }

        // ── SC-1 ─────────────────────────────────────────────────────────────

        /// <summary>
        /// SC-1: Registry has a mapper for "DefendArea"; entity has <c>DoctrineState</c>
        /// with local authority.  After publishing the intent and executing the system
        /// the mapper-translated <see cref="AssignDoctrineEvent"/> must be published.
        /// </summary>
        [Fact]
        public void Execute_MapperFound_PublishesTranslatedDoctrineEvent()
        {
            using var repo = CreateTestWorld();
            var entity = CreateAuthorityEntity(repo);

            var registry = new TacticalIntentMapperRegistry();
            registry.Register(new SuccessMapper("DefendArea", "InfantryDefend"));

            var system = new TacticalIntentResolutionSystem(registry);

            repo.Bus.PublishManaged(new AssignTacticalIntentEvent
            {
                Entity     = entity,
                IntentId   = "DefendArea",
                JsonParams = "{}",
            });
            repo.Bus.SwapBuffers();

            system.Execute(repo, 0.016f);
            repo.Bus.SwapBuffers();

            var doctrineEvents = repo.Bus.ReadManaged<AssignDoctrineEvent>();
            Assert.Single(doctrineEvents);
            Assert.Equal("InfantryDefend", doctrineEvents[0].DoctrineName);
            Assert.Equal(entity, doctrineEvents[0].Entity);
        }

        // ── SC-2 ─────────────────────────────────────────────────────────────

        /// <summary>
        /// SC-2: Empty registry; entity has local authority.
        /// <see cref="AssignDoctrineEvent"/> is published with the original
        /// <c>IntentId</c> as the doctrine name (pass-through).
        /// </summary>
        [Fact]
        public void Execute_EmptyRegistry_PassesThroughIntentIdAsDoctrineName()
        {
            using var repo = CreateTestWorld();
            var entity = CreateAuthorityEntity(repo);

            var system = new TacticalIntentResolutionSystem(new TacticalIntentMapperRegistry());

            repo.Bus.PublishManaged(new AssignTacticalIntentEvent
            {
                Entity     = entity,
                IntentId   = "ConvoyEscort",
                JsonParams = string.Empty,
            });
            repo.Bus.SwapBuffers();

            system.Execute(repo, 0.016f);
            repo.Bus.SwapBuffers();

            var doctrineEvents = repo.Bus.ReadManaged<AssignDoctrineEvent>();
            Assert.Single(doctrineEvents);
            Assert.Equal("ConvoyEscort", doctrineEvents[0].DoctrineName);
        }

        // ── SC-3 ─────────────────────────────────────────────────────────────

        /// <summary>
        /// SC-3: Event published for an entity that does not exist (destroyed).
        /// System must not throw and must not publish any <see cref="AssignDoctrineEvent"/>.
        /// </summary>
        [Fact]
        public void Execute_DeletedEntity_NoExceptionNoDoctrineEventPublished()
        {
            using var repo = CreateTestWorld();
            var entity = CreateAuthorityEntity(repo);
            repo.DestroyEntity(entity);

            var system = new TacticalIntentResolutionSystem(new TacticalIntentMapperRegistry());

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
            var doctrineEvents = repo.Bus.ReadManaged<AssignDoctrineEvent>();
            Assert.Empty(doctrineEvents);
        }

        // ── SC-4 ─────────────────────────────────────────────────────────────

        /// <summary>
        /// SC-4: Mapper is registered for the intent ID but <c>TryMap</c> returns
        /// <c>false</c>; entity has local authority.  The fallback path must publish
        /// a new <see cref="AssignDoctrineEvent"/> with <c>DoctrineName == evt.IntentId</c>.
        /// </summary>
        [Fact]
        public void Execute_MapperReturnsFailure_FallbackPublishesIntentIdAsDoctrineName()
        {
            using var repo = CreateTestWorld();
            var entity = CreateAuthorityEntity(repo);

            var registry = new TacticalIntentMapperRegistry();
            registry.Register(new FailingMapper("DefendArea"));

            var system = new TacticalIntentResolutionSystem(registry);

            repo.Bus.PublishManaged(new AssignTacticalIntentEvent
            {
                Entity     = entity,
                IntentId   = "DefendArea",
                JsonParams = "{\"radius\":100}",
            });
            repo.Bus.SwapBuffers();

            system.Execute(repo, 0.016f);
            repo.Bus.SwapBuffers();

            var doctrineEvents = repo.Bus.ReadManaged<AssignDoctrineEvent>();
            Assert.Single(doctrineEvents);
            Assert.Equal("DefendArea", doctrineEvents[0].DoctrineName);
            Assert.Equal("{\"radius\":100}", doctrineEvents[0].JsonParams);
        }

        // ── SC-5 ─────────────────────────────────────────────────────────────

        /// <summary>
        /// SC-5: Entity does NOT have authority over <c>DoctrineState</c> (simulating a
        /// remote-owned entity).  No <see cref="AssignDoctrineEvent"/> must be published
        /// and no exception must be thrown.
        /// </summary>
        [Fact]
        public void Execute_NoAuthority_NoDoctrineEventPublished()
        {
            using var repo = CreateTestWorld();

            // Create entity with DoctrineState but WITHOUT setting authority —
            // simulates remote-owned cognitive state.
            var entity = repo.CreateEntity();
            repo.AddComponent(entity, new DoctrineState());
            // Authority bit deliberately NOT set.

            var system = new TacticalIntentResolutionSystem(new TacticalIntentMapperRegistry());

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
            var doctrineEvents = repo.Bus.ReadManaged<AssignDoctrineEvent>();
            Assert.Empty(doctrineEvents);
        }
    }
}
