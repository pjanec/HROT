using System;
using System.Collections.Generic;
using Fdp.Core;
using Fdp.Toolkit.Behavior.Components;
using Fdp.Toolkit.Perception.Components;
using Xunit;

namespace Fdp.Toolkit.Scenario.Tests
{
    /// <summary>
    /// Verifies that runtime execution components tagged with [DataPolicy(DataPolicy.NoScenario)]
    /// are excluded from GetSaveableTypeIds() but remain in GetRecordableTypeIds().
    ///
    /// Success conditions for TASK-S102, TASK-S103, TASK-S104.
    /// </summary>
    public sealed class DataPolicyNoSaveTests : IDisposable
    {
        private readonly EntityRepository _repo;

        public DataPolicyNoSaveTests()
        {
            // ComponentTypeRegistry is a shared static — clear before each test class.
            ComponentTypeRegistry.Clear();
            _repo = new EntityRepository();
        }

        public void Dispose() => _repo.Dispose();

        // ── TASK-S102: Execution Channel Components ───────────────────────────────

        [Fact]
        public void ChannelComponents_AbsentFromSaveableTypeIds()
        {
            _repo.RegisterComponent<LocomotionChannel>();
            _repo.RegisterComponent<WeaponChannel>();
            _repo.RegisterComponent<InteractionChannel>();

            var saveableIds = new HashSet<int>(ComponentTypeRegistry.GetSaveableTypeIds());

            int locomotionId    = ComponentTypeRegistry.GetId(typeof(LocomotionChannel));
            int weaponId        = ComponentTypeRegistry.GetId(typeof(WeaponChannel));
            int interactionId   = ComponentTypeRegistry.GetId(typeof(InteractionChannel));

            Assert.DoesNotContain(locomotionId,  saveableIds);
            Assert.DoesNotContain(weaponId,      saveableIds);
            Assert.DoesNotContain(interactionId, saveableIds);
        }

        [Fact]
        public void ChannelComponents_PresentInRecordableTypeIds()
        {
            _repo.RegisterComponent<LocomotionChannel>();
            _repo.RegisterComponent<WeaponChannel>();
            _repo.RegisterComponent<InteractionChannel>();

            var recordableIds = new HashSet<int>(ComponentTypeRegistry.GetRecordableTypeIds());

            int locomotionId    = ComponentTypeRegistry.GetId(typeof(LocomotionChannel));
            int weaponId        = ComponentTypeRegistry.GetId(typeof(WeaponChannel));
            int interactionId   = ComponentTypeRegistry.GetId(typeof(InteractionChannel));

            Assert.Contains(locomotionId,  recordableIds);
            Assert.Contains(weaponId,      recordableIds);
            Assert.Contains(interactionId, recordableIds);
        }

        // ── TASK-S103: Brain Execution Components ─────────────────────────────────

        // ⚠⚠ RE-HOMED by O7c-④d (2026-09-23). The probe used to be BrainHsm128, which is deleted:
        //   brain EXECUTION STATE now lives in the entity's occurrence store, so the component that
        //   must carry DataPolicy.NoScenario is the store's tier component. ⭐ The CLAIM is
        //   unchanged and it is the one that matters — brain execution state is RECORDABLE (a replay
        //   must reproduce it exactly) and NOT SAVEABLE (a scenario file must never pin a machine
        //   mid-transition). ⛔ It would have been easy to drop these two tests with the component;
        //   the policy they protect outlived it. 📄 DESIGN_Occurrence_Scoped_Storage.md §31.19.

        [Fact]
        public void BrainExecutionState_AbsentFromSaveableTypeIds_O7c4d()
        {
            _repo.RegisterComponent<global::Fdp.Toolkit.Blueprints.Components.BlueprintBlackboard1024>();

            var saveableIds = new HashSet<int>(ComponentTypeRegistry.GetSaveableTypeIds());

            int storeId = ComponentTypeRegistry.GetId(
                typeof(global::Fdp.Toolkit.Blueprints.Components.BlueprintBlackboard1024));

            Assert.DoesNotContain(storeId, saveableIds);
        }

        [Fact]
        public void BrainExecutionState_PresentInRecordableTypeIds_O7c4d()
        {
            _repo.RegisterComponent<global::Fdp.Toolkit.Blueprints.Components.BlueprintBlackboard1024>();

            var recordableIds = new HashSet<int>(ComponentTypeRegistry.GetRecordableTypeIds());

            int storeId = ComponentTypeRegistry.GetId(
                typeof(global::Fdp.Toolkit.Blueprints.Components.BlueprintBlackboard1024));

            Assert.Contains(storeId, recordableIds);
        }

        // ── TASK-S104: Transient Perception Components ────────────────────────────

        [Fact]
        public void PerceptionComponents_AbsentFromSaveableTypeIds()
        {
            _repo.RegisterComponent<SensorContactList>();
            _repo.RegisterComponent<ActiveSensorTracks>();

            var saveableIds = new HashSet<int>(ComponentTypeRegistry.GetSaveableTypeIds());

            int contactListId     = ComponentTypeRegistry.GetId(typeof(SensorContactList));
            int activeSensorTracksId = ComponentTypeRegistry.GetId(typeof(ActiveSensorTracks));

            Assert.DoesNotContain(contactListId,        saveableIds);
            Assert.DoesNotContain(activeSensorTracksId, saveableIds);
        }

        [Fact]
        public void PerceptionComponents_PresentInRecordableTypeIds()
        {
            _repo.RegisterComponent<SensorContactList>();
            _repo.RegisterComponent<ActiveSensorTracks>();

            var recordableIds = new HashSet<int>(ComponentTypeRegistry.GetRecordableTypeIds());

            int contactListId        = ComponentTypeRegistry.GetId(typeof(SensorContactList));
            int activeSensorTracksId = ComponentTypeRegistry.GetId(typeof(ActiveSensorTracks));

            Assert.Contains(contactListId,        recordableIds);
            Assert.Contains(activeSensorTracksId, recordableIds);
        }
    }
}
