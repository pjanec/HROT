using System;
using System.Collections.Generic;
using System.Reflection;
using Fdp.Core;
using Fdp.Toolkit.Blueprints.Components;
using Fdp.Toolkit.Scenario;
using Xunit;

namespace Fdp.Toolkit.Scenario.Tests
{
    /// <summary>
    /// BSA-101: Verifies that BlueprintBlackboard{1024,4096,16384} carry
    /// [DataPolicy(DataPolicy.NoSave)] so volatile runtime bytes don't leak into scenario JSON.
    /// </summary>
    public sealed class BlueprintBlackboardNoSaveTests : IDisposable
    {
        private readonly EntityRepository _repo;

        public BlueprintBlackboardNoSaveTests()
        {
            ComponentTypeRegistry.Clear();
            _repo = new EntityRepository();
        }

        public void Dispose() => _repo.Dispose();

        // ── Test 1: Reflection ─────────────────────────────────────────────────

        [Fact]
        public void BlueprintBlackboard1024_HasDataPolicyNoSave()
        {
            var attr = typeof(BlueprintBlackboard1024).GetCustomAttribute<DataPolicyAttribute>();
            Assert.NotNull(attr);
            Assert.Equal(DataPolicy.NoSave, attr!.Policy);
        }

        [Fact]
        public void BlueprintBlackboard4096_HasDataPolicyNoSave()
        {
            var attr = typeof(BlueprintBlackboard4096).GetCustomAttribute<DataPolicyAttribute>();
            Assert.NotNull(attr);
            Assert.Equal(DataPolicy.NoSave, attr!.Policy);
        }

        [Fact]
        public void BlueprintBlackboard16384_HasDataPolicyNoSave()
        {
            var attr = typeof(BlueprintBlackboard16384).GetCustomAttribute<DataPolicyAttribute>();
            Assert.NotNull(attr);
            Assert.Equal(DataPolicy.NoSave, attr!.Policy);
        }

        // ── Test 2: Serialization exclusion ────────────────────────────────────

        /// <summary>
        /// An entity carrying a BlueprintBlackboard1024 that is serialized must not
        /// produce a "BlueprintBlackboard1024" key in the JSON.
        ///
        /// NOTE: This test may fail until Task 4's BlueprintStateTranslator is in place,
        /// because the serializer may throw if the NoSave component isn't claimed by any
        /// translator and FdpAutoSerializer tries to process it.
        /// This is expected — it verifies the coupling between BSA-101 and BSA-202.
        /// </summary>
        [Fact]
        public void Serialization_ExcludesBlueprintBlackboard1024()
        {
            _repo.RegisterComponent<BlueprintBlackboard1024>();
            var entity = _repo.CreateEntity();
            _repo.AddComponent(entity, default(BlueprintBlackboard1024));

            var serializer = new ScenarioSerializerBuilder("TestSubsystem").Build();
            var dom = serializer.Serialize(_repo, new ScenarioHeader("TestSubsystem"));

            var json = dom.ToJsonString();
            Assert.DoesNotContain("BlueprintBlackboard1024", json);
        }
    }
}
