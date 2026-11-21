using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Fdp.Core;
using Fdp.Toolkit.Behavior.Components;
using Fdp.Toolkit.Squad;
using Xunit;

namespace Fdp.Toolkit.Squad.Tests
{
    /// <summary>
    /// P0-02: Verifies that <see cref="SquadCognitiveState"/> is exactly 1024 bytes
    /// and that its sub-region offsets match the compile-time constants in
    /// <see cref="SquadCognitiveStateOffsets"/>.
    /// </summary>
    public unsafe class SquadCognitiveStateLayoutTests
    {
        [Fact]
        public void SquadCognitiveState_TotalSizeIs1024()
        {
            Assert.Equal(1024, Unsafe.SizeOf<SquadCognitiveState>());
        }

        [Fact]
        public void SquadCognitiveState_OffsetsMatchConstants()
        {
            // Use a zero-initialised instance on the stack and take address of each field.
            SquadCognitiveState s = default;
            byte* origin = (byte*)&s;

            // Scalars region starts at offset 0 (ManeuverKind is first field).
            Assert.Equal(SquadCognitiveStateOffsets.Scalars,
                (int)((byte*)&s.ManeuverKind - origin));

            // Elements sub-struct at offset 16.
            Assert.Equal(SquadCognitiveStateOffsets.Elements,
                (int)((byte*)&s.Elements - origin));

            // Slots sub-struct at offset 48.
            Assert.Equal(SquadCognitiveStateOffsets.Slots,
                (int)((byte*)&s.Slots - origin));

            // Roles sub-struct at offset 144.
            Assert.Equal(SquadCognitiveStateOffsets.Roles,
                (int)((byte*)&s.Roles - origin));

            // Assignment sub-struct at offset 176.
            Assert.Equal(SquadCognitiveStateOffsets.Assignment,
                (int)((byte*)&s.Assignment - origin));

            // Contacts sub-struct at offset 432.
            Assert.Equal(SquadCognitiveStateOffsets.Contacts,
                (int)((byte*)&s.Contacts - origin));
        }

        [Fact]
        public void SquadCognitiveState_DefaultIsAllZero()
        {
            SquadCognitiveState s = default;
            byte* p = (byte*)&s;
            for (int i = 0; i < 1024; i++)
                Assert.Equal(0, p[i]);
        }

        [Fact]
        public void SquadCognitiveState_IsItsOwnComponent_NotABlackboardAlias()
        {
            // ⛔⛔ REPLACES SquadCognitiveState_ProjectAliasesBb (O1, 2026-09-20).
            //   That test asserted the OPPOSITE of what is now true: it wrote through
            //   SquadCognitiveState.Project(ref bb) and read the bytes back out of the commander's
            //   shared Blackboard1024. The aliasing was the defect — it made "has a blackboard" an
            //   accidental proxy for "is a commander with squad state".
            // ⭐ The claim that survives is the one that matters: the state has its OWN storage, and
            //   writing it does not touch the blackboard.
            using var repo = new EntityRepository();
            repo.RegisterComponent<SquadCognitiveState>();
            repo.RegisterComponent<SquadCognitiveState>();

            var e = repo.CreateEntity();
            repo.AddComponent(e, default(SquadCognitiveState));
            repo.AddComponent(e, default(SquadCognitiveState));

            repo.GetComponentRW<SquadCognitiveState>(e).ManeuverKind = 0xABCD;

            Assert.Equal((ushort)0xABCD, repo.GetComponentRO<SquadCognitiveState>(e).ManeuverKind);

            // ⛔⛔ P4-① (2026-09-22): the anti-vacuity half of this rail is RETIRED, and by the
            //    strongest possible means. It asserted that writing SquadCognitiveState leaves
            //    `Blackboard1024` untouched — i.e. that `O1` really did stop the two aliasing.
            //    ⭐ That component NO LONGER EXISTS, so the two cannot alias by construction and
            //    there is nothing left to observe. 📄 §30.13 — squad state moving to its own
            //    [ComponentId] component is exactly tenant ② of the retirement argument.
            // ⚠ The LIVE half above — the round-trip through SquadCognitiveState — is unchanged and
            //   still carries the rail's actual claim.
        }
    }
}
