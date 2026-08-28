using CarKinem.Core;
using Fdp.Core;
using Xunit;

namespace CarKinem.Tkb.Tests
{
    /// <summary>
    /// CE-113 / B3 -- <c>VehicleParams</c> must not be persisted into a scenario.
    /// </summary>
    /// <remarks>
    /// The TKB is the single source of vehicle parameters and is present on every node
    /// offline, so a saved copy adds no information: it is a second source that silently
    /// goes stale when the TKB is edited, and nothing on the load path treats it as
    /// authoritative anyway.  Measured before the fix: <c>scenarios/hill-attack</c> stored
    /// a full fifteen-field <c>VehicleParams</c> on six of its eight entities.
    /// <para>
    /// The scenario serializer selects components by mask rather than by an explicit list
    /// (<c>ScenarioSerializer.SerializeEntity</c> walks a <c>BitMask512</c>, and
    /// <c>FdpAutoSerializer</c> handles every remaining bit generically), so a component
    /// opts out by <b>declaration</b>.  That is what makes this a one-attribute fix that
    /// touches none of the hand-tested save path -- and it is also why the guarantee needs
    /// a rail: nothing in the save path names this component, so nothing there can be read
    /// to confirm it is excluded.
    /// </para>
    /// </remarks>
    public class VehicleParamsIsNotScenarioStateTests
    {
        [Fact]
        public void VehicleParams_is_excluded_from_the_saveable_mask()
        {
            var repo = new EntityRepository();
            repo.RegisterComponent<VehicleParams>();

            int id = repo.GetComponentTypeId(typeof(VehicleParams));
            Assert.False(repo.GetSaveableMask().IsSet(id),
                "VehicleParams is derived from the TKB and must not be written into a scenario");
        }

        /// <summary>
        /// The other half of the claim: NoSave must not have cost us the component's
        /// runtime behaviour.  It still has to be a real, registered, readable component --
        /// only its persistence changes.
        /// </summary>
        [Fact]
        public void VehicleParams_is_still_a_live_runtime_component()
        {
            var repo   = new EntityRepository();
            repo.RegisterComponent<VehicleParams>();
            var entity = repo.CreateEntity();

            var tank = VehiclePresets.GetPreset(VehicleClass.Tank);
            tank.Class = VehicleClass.Tank;   // see the rail below -- GetPreset does not
            repo.AddComponent(entity, tank);

            Assert.True(repo.HasComponent<VehicleParams>(entity));
            var read = repo.GetComponent<VehicleParams>(entity);
            Assert.Equal(VehicleClass.Tank, read.Class);
            Assert.Equal(1.8f, read.AccelGain);
        }

        /// <summary>
        /// A trap found while writing the rail above, pinned so the next reader does not
        /// pay for it: <see cref="VehiclePresets.GetPreset"/> fills every kinematic field
        /// but leaves <see cref="VehicleParams.Class"/> at its default, so the struct it
        /// returns is self-inconsistent -- a Tank preset that reports PersonalCar.
        /// </summary>
        /// <remarks>
        /// Every caller therefore has to assign <c>Class</c> itself, which is exactly the
        /// silent-default shape: the value looks authoritative and is wrong, and nothing
        /// makes the omission visible.  <c>VehicleKinematicsTkbTranslator</c> does assign
        /// it.  This rail is descriptive, not aspirational -- if <c>GetPreset</c> is ever
        /// fixed to stamp its own class, this test should fail and be deleted.
        /// </remarks>
        [Fact]
        public void GetPreset_does_not_stamp_its_own_Class_so_callers_must()
        {
            Assert.Equal(VehicleClass.PersonalCar, VehiclePresets.GetPreset(VehicleClass.Tank).Class);
            Assert.Equal(VehicleClass.PersonalCar, VehiclePresets.GetPreset(VehicleClass.Truck).Class);
        }
    }
}
