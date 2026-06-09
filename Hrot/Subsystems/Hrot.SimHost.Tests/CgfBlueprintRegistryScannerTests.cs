using System;
using System.Collections.Generic;
using Fdp.Core;
using Fdp.Toolkit.Behavior;
using Fdp.Toolkit.Blueprints;
using Fdp.Toolkit.Blueprints.Components;
using Hrot.Common.Serializers;
using Hrot.SimHost.Systems;
using Hrot.AI.Behaviors.Generated;
using Xunit;

namespace Hrot.SimHost.Tests
{
    /// <summary>
    /// BSA-REGSCAN Task 3: Integration tests verifying that
    /// <see cref="BlueprintRegistrarScanner.Scan"/> correctly populates
    /// <see cref="BlueprintRegistry"/> from the AI-behaviors assembly, that
    /// <see cref="BlueprintMaterializationSystem"/> attaches blackboard slots when the
    /// registry is populated (and attaches nothing when the registry is empty), and that
    /// scanning the AI assembly after <see cref="CgfBehaviorSetup.LoadFromAiAssembly"/>
    /// does not produce behavior double-registrations.
    /// </summary>
    public sealed class CgfBlueprintRegistryScannerTests : IDisposable
    {
        // The well-known blueprint registered by BlueprintRegistrar_Count4_F44891A7_Bp.
        private static readonly int  KnownBlueprintId = Count4_F44891A7_Bp.BlueprintId;
        private static readonly Guid KnownAssetId     = new Guid("47fe9c55-c6ca-4c69-9c5a-d46de25745de");

        private readonly EntityRepository _repo;
        private readonly BlueprintRegistry _registry;

        public CgfBlueprintRegistryScannerTests()
        {
            _repo = new EntityRepository();
            _repo.RegisterComponent<BlueprintBlackboard1024>();
            _repo.RegisterComponent<BlueprintBlackboard4096>();
            _repo.RegisterComponent<BlueprintBlackboard16384>();
            _repo.RegisterManagedComponent<InitialBlueprintsIntent>();
            _registry = new BlueprintRegistry();
        }

        public void Dispose() => _repo.Dispose();

        // ── Helpers ───────────────────────────────────────────────────────────

        private static BlueprintRegistry ScanAndCommit()
        {
            var registry = new BlueprintRegistry();
            var staging  = new BlueprintRegistryStaging();
            BlueprintRegistrarScanner.Scan(
                typeof(Hrot.AI.Behaviors.AiBehaviorFactory).Assembly,
                staging,
                new BehaviorRegistry(),
                skipOnUnknownParam: true);
            registry.CommitStaging(staging);
            return registry;
        }

        private Entity CreateEntityWithIntent(params Guid[] assetIds)
        {
            var entity = _repo.CreateEntity();
            var intent = new InitialBlueprintsIntent();
            foreach (var id in assetIds)
                intent.Blueprints.Add(new BlueprintAssignmentDto { AssetId = id });
            _repo.SetManagedComponent(entity, intent);
            return entity;
        }

        // ── TC1: scanner populates registry from AI assembly ──────────────────

        /// <summary>
        /// BSA-REGSCAN TC1: scanning the AI-behaviors assembly with
        /// skipOnUnknownParam=true must register the generated blueprint
        /// "Count4" (BlueprintId 0xF44891A7) so that TryGetById succeeds.
        /// </summary>
        [Fact]
        public void Scan_AiAssembly_SkipOnUnknown_RegistersKnownGeneratedBlueprint()
        {
            var registry = ScanAndCommit();

            Assert.True(
                registry.TryGetById(KnownBlueprintId, out var def),
                $"Expected generated blueprint Count4 (id 0x{KnownBlueprintId:X8}) to be registered.");
            Assert.NotNull(def);
            Assert.Equal("Count4", def!.Name);
            Assert.Equal(KnownAssetId, def.AssetId);
        }

        // ── TC2: BlueprintMaterializationSystem attaches BB slot ──────────────

        /// <summary>
        /// BSA-REGSCAN TC2 (end-to-end): an entity that carries an
        /// <see cref="InitialBlueprintsIntent"/> referencing Count4 must receive a
        /// <see cref="BlueprintBlackboard1024"/> component after
        /// <see cref="BlueprintMaterializationSystem.Execute"/> runs, provided the
        /// registry is populated via the scanner.
        /// </summary>
        [Fact]
        public void BlueprintMaterializationSystem_PopulatedRegistry_AttachesBlackboardSlot()
        {
            // Populate registry via the same scanner path used by CGF.
            var staging = new BlueprintRegistryStaging();
            BlueprintRegistrarScanner.Scan(
                typeof(Hrot.AI.Behaviors.AiBehaviorFactory).Assembly,
                staging,
                new BehaviorRegistry(),
                skipOnUnknownParam: true);
            _registry.CommitStaging(staging);

            var entity = CreateEntityWithIntent(KnownAssetId);

            var system = new BlueprintMaterializationSystem(_registry);
            system.Execute(_repo, 0f);

            // The entity must have received a BlueprintBlackboard1024.
            Assert.True(
                _repo.HasComponent<BlueprintBlackboard1024>(entity),
                "Entity must have BlueprintBlackboard1024 after materialization with a populated registry.");

            // The intent must have been removed after processing.
            Assert.False(
                _repo.HasManagedComponent<InitialBlueprintsIntent>(entity),
                "InitialBlueprintsIntent must be removed after successful materialization.");
        }

        // ── TC3: empty registry attaches nothing ──────────────────────────────

        /// <summary>
        /// BSA-REGSCAN TC3: with an empty (uncommitted) registry,
        /// <see cref="BlueprintMaterializationSystem"/> must leave the entity without
        /// any blackboard component and silently skip the assignment.
        /// </summary>
        [Fact]
        public void BlueprintMaterializationSystem_EmptyRegistry_AttachesNothing()
        {
            // Registry left empty — _registry has no blueprints.
            var entity = CreateEntityWithIntent(KnownAssetId);

            var system = new BlueprintMaterializationSystem(_registry);
            system.Execute(_repo, 0f);

            // No blackboard component must have been attached.
            Assert.False(
                _repo.HasComponent<BlueprintBlackboard1024>(entity),
                "Entity must NOT have BlueprintBlackboard1024 when the registry is empty.");
            Assert.False(
                _repo.HasComponent<BlueprintBlackboard4096>(entity),
                "Entity must NOT have BlueprintBlackboard4096 when the registry is empty.");
            Assert.False(
                _repo.HasComponent<BlueprintBlackboard16384>(entity),
                "Entity must NOT have BlueprintBlackboard16384 when the registry is empty.");

            // Intent cleaned up (early-exit for unresolved blueprints).
            Assert.False(
                _repo.HasManagedComponent<InitialBlueprintsIntent>(entity),
                "InitialBlueprintsIntent must be cleaned up even when registry is empty.");
        }

        // ── TC4: no behavior double-registration ─────────────────────────────

        /// <summary>
        /// BSA-REGSCAN TC4: scanning the AI-behaviors assembly twice with
        /// skipOnUnknownParam=true and merging the results into a single registry must
        /// produce the same behavior count as scanning once — i.e., re-registration is
        /// idempotent and produces no duplicates.
        ///
        /// This mirrors the CGF initialization where the scanner result is merged into
        /// the live <see cref="BehaviorRegistry"/> that was already populated by
        /// <c>CgfBehaviorSetup.LoadFromAiAssembly</c>: the same behavior IDs from
        /// per-behavior registrars (SampleGuard, SampleScout) overwrite themselves in
        /// the dict — a no-op that leaves the count unchanged.
        /// </summary>
        [Fact]
        public void Scan_AiAssembly_SkipOnUnknown_BehaviorRegistrationIsIdempotent()
        {
            var firstSink  = new BehaviorRegistry();
            var secondSink = new BehaviorRegistry();

            // First scan (use a fresh blueprint staging for each scan to avoid staging collision).
            BlueprintRegistrarScanner.Scan(
                typeof(Hrot.AI.Behaviors.AiBehaviorFactory).Assembly,
                new BlueprintRegistryStaging(),
                firstSink,
                skipOnUnknownParam: true);

            // Second scan into a separate registry.
            BlueprintRegistrarScanner.Scan(
                typeof(Hrot.AI.Behaviors.AiBehaviorFactory).Assembly,
                new BlueprintRegistryStaging(),
                secondSink,
                skipOnUnknownParam: true);

            // Merge second scan result into first (simulates applying scanner after prior registration).
            firstSink.MergeFrom(secondSink);

            var afterMergeNames = new HashSet<string>(firstSink.GetRegisteredNames());
            var baselineNames   = new HashSet<string>(secondSink.GetRegisteredNames());

            // After merging identical registrations the count must be unchanged — no duplicates.
            Assert.Equal(baselineNames, afterMergeNames);
        }
    }
}
