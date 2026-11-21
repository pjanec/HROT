using System.IO;
using Xunit;
using Fdp.Core;
using Fdp.Core.FlightRecorder;

namespace Fdp.Tests
{
    /// <summary>
    /// ⭐ <c>CE-3299</c> B5 (restore design D10) — a frame that carries a component the target world has NOT registered applies anyway: 25
    /// components register on first use, so a recording made by a process that used one is restored (or replayed) into a fresh world that
    /// has not. 📌 Found by R1: <c>ua-tank-ammo</c>'s checkpoint at 3 s carried <c>DesignatedTarget</c> (id 360) and the restoring process
    /// threw "Component type ID 360 not found in repository".
    /// </summary>
    public class PlaybackRegistersRecordedTypesTests
    {
        /// <summary>Only this test names it — so the type registry of the test process has not seen it before the test asks.</summary>
        [ComponentId(511)]
        public struct NeverTouchedBeforeThisTest { public int Value; }

        [Fact]
        public void FindType_NamesARecordedId_ThisProcessHasNotTouched()
        {
            Assert.Null(ComponentTypeRegistry.GetType(511));
            Assert.Equal(typeof(NeverTouchedBeforeThisTest), ComponentTypeRegistry.FindType(511));
        }

        [Fact]
        public void ApplyFrame_RegistersAComponentTheTargetWorldHasNot_AndRestoresItsValues()
        {
            using var ms = new MemoryStream();
            using (var writer = new BinaryWriter(ms, System.Text.Encoding.UTF8, leaveOpen: true))
            using (var source = new EntityRepository())
            {
                source.RegisterComponent<IntComponent>();
                var e = source.CreateEntity();
                source.AddComponent(e, new IntComponent { Value = 42 });
                new RecorderSystem().RecordKeyframe(source, writer, 0L);
            }

            using var fresh = new EntityRepository();   // ⛔ IntComponent not registered here
            Assert.False(fresh.TryGetTable(typeof(IntComponent), out _));
            ms.Position = 0;
            using var reader = new BinaryReader(ms);
            new PlaybackSystem().ApplyFrame(fresh, reader);

            Assert.True(fresh.TryGetTable(typeof(IntComponent), out _));
            int found = 0;
            foreach (var restored in fresh.Query().With<IntComponent>().Build())
            {
                Assert.Equal(42, fresh.GetComponentRO<IntComponent>(restored).Value);
                found++;
            }
            Assert.Equal(1, found);
        }
    }
}
