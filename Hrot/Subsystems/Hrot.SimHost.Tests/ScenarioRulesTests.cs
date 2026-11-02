using System.Collections.Generic;
using System.Text.Json.Nodes;
using Fdp.Core;
using Fdp.Modules.Geographic;
using Fdp.Modules.Geographic.Transforms;
using Fdp.Toolkit.Behavior;
using Fdp.Toolkit.Behavior.Components;
using Fdp.Toolkit.Behavior.Systems;
using Fdp.Toolkit.Behavior.Translators;
using Hrot.MuscleCharacter.Animation.Components;
using Fdp.Toolkit.Blueprints.Partitioning;
using Fdp.Toolkit.Replication.Components;
using Fdp.Toolkit.ScenarioRules;
using Fdp.Toolkit.Tkb;
using Fdp.Toolkit.Tkb.Domain;
using Hrot.CGF.Configuration;
using Hrot.Map.Common;
using Hrot.SimHost.Serializers;
using Hrot.SimHost.Systems;
using Xunit;

namespace Hrot.SimHost.Tests
{
    /// <summary>
    /// ⭐ rules S1 ① (📄 docs/DESIGN_Scenario_Rules.md §00.8) — the foundation: an objective is an ENTITY (TKB type 8860) that runs a
    /// brain with no body. The rail follows the load path as far as a unit test can: the production catalogue's template, its
    /// <see cref="BehaviorTkbTranslator"/> (the creation step), the scenario record's <c>"Behavior"</c> read by the production
    /// <see cref="BrainSnapshotTranslator"/>, the materialization and ingress, and the real <see cref="BrainTickSystem"/>.
    /// </summary>
    public sealed class ScenarioRulesTests
    {
        /// <summary>The Objective type is in the catalogue as a brain with no body (cannot move, shoot or interact), and a scenario entity of
        /// that type whose record names <c>HoldStance</c> gets a ticking brain. ✅ Red-proof: the Objective template's
        /// <c>BehaviorProfileDto</c> removed from <c>NedTkbCatalog</c> ⇒ no <c>BehaviorState</c>, the behaviour never starts ⇒ red.</summary>
        [Fact]
        public void ObjectiveEntity_FromAScenarioRecord_GetsABrainThatTicks()
        {
            var db = new TkbDatabase();
            Hrot.Map.Definitions.Tkb.NedTkbCatalog.RegisterAll(db);
            var template = db.GetByType(TkbEntityTypes.Objective);
            var profile = template.GetDescriptor<BehaviorProfileDto>();
            Assert.NotNull(profile);
            Assert.True(profile!.BrainTier != 0);
            Assert.False(profile.CanMove || profile.CanShoot || profile.CanInteract);

            var repo = new EntityRepository();
            SimHostComponentRegistry.RegisterAll(repo);
            CognitiveComponentRegistry.RegisterAll(repo);
            BlueprintTierTable.RegisterAll(repo);
            var geo = new WGS84Transform();
            geo.SetOrigin(0.0, 0.0, 0.0);
            repo.SetSingletonManaged<IGeographicTransform>(geo);
            if (!repo.IsComponentTypeRegistered<StanceIntent>()) repo.RegisterComponent<StanceIntent>();   // the probe: HoldStance writes it when present

            var registry = new BehaviorRegistry();
            CgfBehaviorSetup.LoadFromAiAssembly(registry);
            var ingress = new BehaviorIngressSystem(registry);
            var brain = new BrainTickSystem(registry);
            var materialize = new InitialBrainMaterializationSystem();

            // the creation step: the entity of the type, its template's translators, then the record's "Behavior"
            var objective = repo.CreateEntity();
            repo.AddComponent(objective, new TkbIdentity { TkbType = (TkbTypeId)TkbEntityTypes.Objective });
            new BehaviorTkbTranslator().Inject(repo, objective, template);
            repo.AddComponent(objective, new StanceIntent());
            new BrainSnapshotTranslator(registry).Inject(repo, objective,
                new Dictionary<string, object> { [BrainSnapshotTranslator.BehaviorKey] = JsonNode.Parse("{\"Name\":\"HoldStance\",\"Params\":\"{\\\"stance\\\":\\\"Prone\\\"}\"}")! },
                guidResolver: null!);
            Assert.True(repo.HasManagedComponent<InitialBrainIntent>(objective));

            for (int i = 0; i < 5; i++)
            {
                repo.Bus.SwapBuffers();
                materialize.Execute(repo, 0.016f);
                ingress.Execute(repo, 0.016f);
                brain.Execute(repo, 0.016f);
                repo.FlushCommandBuffers();
            }

            Assert.True(registry.TryGetName(repo.GetComponent<BehaviorState>(objective).ActiveBehaviorHash, out var name));
            Assert.Equal("HoldStance", name);
            Assert.Equal(StanceId.Prone, repo.GetComponentRO<StanceIntent>(objective).TargetStance);   // the tree TICKED: it wrote its stance
        }

        /// <summary>The two components are what the design says: ids 425 / 426, runtime-only (NoScenario), unmanaged (recordable), and a
        /// state is final exactly when it is Achieved, Failed or Skipped (SR-117). ✅ Red-proof: <c>IsFinal</c> counting Inactive ⇒ red.</summary>
        [Fact]
        public void TheComponents_AreRuntimeOnly_Unmanaged_AndFinalMeansAchievedFailedOrSkipped()
        {
            Assert.Equal(425, GlobalComponentIds.ObjectiveStatus);
            Assert.Equal(426, GlobalComponentIds.ObjectiveLinks);
            foreach (var t in new[] { typeof(ObjectiveStatus), typeof(ObjectiveLinks) })
            {
                Assert.True(System.Reflection.CustomAttributeExtensions.GetCustomAttribute<DataPolicyAttribute>(t)!.Policy.HasFlag(DataPolicy.NoScenario), t.Name);
                Assert.False(System.Runtime.CompilerServices.RuntimeHelpers.IsReferenceOrContainsReferences<ObjectiveStatus>() && t == typeof(ObjectiveStatus));
            }
            var expected = new Dictionary<ObjectiveState, bool>
            {
                [ObjectiveState.Pending] = false, [ObjectiveState.Active] = false, [ObjectiveState.Achieved] = true,
                [ObjectiveState.Failed] = true, [ObjectiveState.Inactive] = false, [ObjectiveState.Skipped] = true,
            };
            foreach (var (state, final) in expected) Assert.Equal(final, new ObjectiveStatus { State = state }.IsFinal);
        }
    }
}
