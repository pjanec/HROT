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

            repo.AddComponent(entity, VehiclePresets.GetPreset(VehicleClass.Tank));

            Assert.True(repo.HasComponent<VehicleParams>(entity));
            var read = repo.GetComponent<VehicleParams>(entity);
            Assert.Equal(VehicleClass.Tank, read.Class);
            Assert.Equal(1.8f, read.AccelGain);
        }

        /// <summary>
        /// <see cref="VehiclePresets.GetPreset"/> stamps the class it describes, for every
        /// member of the enum.
        /// </summary>
        /// <remarks>
        /// It did not, until a rail written for this batch tripped over it: the function
        /// filled thirteen kinematic fields and left <see cref="VehicleParams.Class"/> at
        /// its default, so a Tank preset reported itself as a <c>PersonalCar</c> -- and
        /// because <c>PersonalCar</c> is <c>0</c>, nothing surfaced the mismatch.  Three
        /// callers had each independently written the missing assignment; those three lines
        /// are now gone.  Asserted over <c>Enum.GetValues</c> rather than a hand-listed set
        /// so a new vehicle class cannot be added without an arm that stamps it.
        /// </remarks>
        [Theory]
        [MemberData(nameof(AllVehicleClasses))]
        public void GetPreset_stamps_the_class_it_describes(VehicleClass vehicleClass)
        {
            Assert.Equal(vehicleClass, VehiclePresets.GetPreset(vehicleClass).Class);
        }

        public static TheoryData<VehicleClass> AllVehicleClasses()
        {
            var data = new TheoryData<VehicleClass>();
            foreach (VehicleClass c in System.Enum.GetValues<VehicleClass>())
                data.Add(c);
            return data;
        }

        /// <summary>
        /// An undefined class normalises to <c>PersonalCar</c> and reports it, so the
        /// returned <c>Class</c> always describes the data returned rather than the value
        /// that was asked for.
        /// </summary>
        [Fact]
        public void GetPreset_normalises_an_undefined_class_rather_than_echoing_it()
        {
            var bogus = (VehicleClass)99;
            var p = VehiclePresets.GetPreset(bogus);

            Assert.Equal(VehicleClass.PersonalCar, p.Class);
            Assert.Equal(VehiclePresets.GetPreset(VehicleClass.PersonalCar).MaxSteerAngle, p.MaxSteerAngle);
        }
    }
}
