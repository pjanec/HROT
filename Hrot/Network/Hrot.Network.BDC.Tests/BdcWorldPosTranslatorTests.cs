using System.Linq;
using System.Numerics;
using CycloneDDS.Runtime;
using Fdp.Core;
using Fdp.ModuleHost.Abstractions;
using Fdp.Modules.Geographic;
using Fdp.Toolkit.Replication.Components;
using Fdp.Toolkit.Replication.Services;
using Hrot.BDC.Common;
using Hrot.BDC.Messages;
using Hrot.BDC.Replication;
using NSubstitute;
using Xunit;

namespace Hrot.Network.BDC.Tests
{
    /// <summary>
    /// ⭐ AQ107 F — the BDC stack's <c>BDC_WorldPos</c> follows the NED wire's rules: local ENU metres, a quaternion vector part with w
    /// implied (the SAME <see cref="QuaternionWireCodec"/>), ENU velocities, <c>double SimTime</c>. 📌 No suite existed for
    /// <c>BdcWorldPosTranslator</c> (searched <c>BdcWorldPos</c> in every test project: only a descriptor-map wiring rail), so this is its suite.
    /// </summary>
    [Collection("BdcDds")]
    public class BdcWorldPosTranslatorTests
    {
        public static IEnumerable<object[]> Poses()
        {
            yield return new object[] { "heading 30 / pitch 10 / roll -20", Quaternion.Normalize(Quaternion.CreateFromYawPitchRoll(0.4f, 0.17f, -0.35f)) };
            yield return new object[] { "nose straight UP (CE-4068)",       Quaternion.CreateFromAxisAngle(Vector3.UnitY, -MathF.PI / 2f) };
            yield return new object[] { "nose straight DOWN (CE-4068)",     Quaternion.CreateFromAxisAngle(Vector3.UnitY,  MathF.PI / 2f) };
            var q = Quaternion.Normalize(Quaternion.CreateFromAxisAngle(Vector3.Normalize(new Vector3(1f, 2f, 3f)), 2.4f));
            yield return new object[] { "w < 0 before canonicalisation",    new Quaternion(-q.X, -q.Y, -q.Z, -q.W) };
        }

        /// <summary>
        /// ⭐⭐ <b>AQ107 F acceptance (c)</b> — an owned entity's pose published by <c>BdcWorldPosTranslator</c> over DDS and read back by a second
        /// translator on a replica arrives: position within 1 mm, rotation dot ≥ 1 − 1e-6 (sign-insensitive), velocity and angular velocity
        /// exact; nose straight up/down and a w &lt; 0 quaternion included. ✅ Red-proof (run): <c>QuaternionWireCodec.Encode</c> without the
        /// <c>w ≥ 0</c> canonicalisation ⇒ the "w &lt; 0" case returns the inverse rotation ⇒ red.
        /// </summary>
        [Theory]
        [MemberData(nameof(Poses))]
        public void AQ107F_APoseOnTheBdcWire_RoundTripsEcsToBdcWorldPosToEcs(string _, Quaternion rotation)
        {
            using var participant = new DdsParticipant(189u);
            var geo   = Substitute.For<IGeographicTransform>();
            var ownerMap = new NetworkEntityMap();
            var egress   = new BdcWorldPosTranslator(participant, ownerMap, localNodeId: 1);

            var position = new Vector3(1234.5f, -8765.25f, 42.125f);
            var linear   = new Vector3(1.5f, -2f, 0.25f);
            var angular  = new Vector3(0.1f, -0.2f, 0.3f);

            var owner = new EntityRepository();
            owner.RegisterComponent<NetworkIdentity>();
            owner.RegisterComponent<NetworkAuthority>();
            owner.RegisterComponent<SimTransform>();
            owner.RegisterComponent<SimVelocity>();
            var e = owner.CreateEntity();
            owner.AddComponent(e, new NetworkIdentity(42));
            owner.AddComponent(e, new NetworkAuthority(primaryOwnerId: 1, localNodeId: 1));
            owner.AddComponent(e, new SimTransform { Position = position, Rotation = rotation });
            owner.AddComponent(e, new SimVelocity { Linear = linear, Angular = angular });

            // the replica side: its own translator (its own reader), a ghost not owned here
            var replica = new EntityRepository();
            replica.RegisterComponent<NetworkIdentity>();
            replica.RegisterComponent<NetworkAuthority>();
            replica.RegisterComponent<SimTransform>();
            replica.RegisterComponent<NetworkTransform>();
            replica.RegisterComponent<NetworkVelocity>();
            var replicaMap = new NetworkEntityMap();
            var ghost = replica.CreateEntity();
            replica.AddComponent(ghost, new NetworkIdentity(42));
            replica.AddComponent(ghost, new SimTransform());
            replica.AddComponent(ghost, new NetworkTransform());
            replica.AddComponent(ghost, new NetworkVelocity());
            replicaMap.Register(42, ghost);
            var ingress = new BdcWorldPosTranslator(participant, replicaMap, localNodeId: 2);

            egress.ScanAndPublish(owner);
            Assert.Equal(1, egress.SentSampleCount);

            for (int i = 0; i < 60 && ingress.ReceivedSampleCount == 0; i++)
            {
                ISimulationView view = replica;
                var cmd = view.GetCommandBuffer();
                ingress.PollIngress(cmd, view);
                ((EntityCommandBuffer)cmd).Playback(replica);
                if (ingress.ReceivedSampleCount == 0) Thread.Sleep(50);
            }
            Assert.True(ingress.ReceivedSampleCount > 0, "no BDC_WorldPos sample arrived over DDS");

            var st = replica.GetComponent<SimTransform>(ghost);
            Assert.True(Vector3.Distance(position, st.Position) < 0.001f, $"position {st.Position}");
            float dot = MathF.Abs(Quaternion.Dot(Quaternion.Normalize(rotation), st.Rotation));
            Assert.True(dot >= 1f - 1e-6f, $"rotation dot {dot:R}");
            Assert.Equal(linear, replica.GetComponent<NetworkVelocity>(ghost).Value);
            Assert.True(MathF.Abs(Quaternion.Dot(replica.GetComponent<NetworkTransform>(ghost).LastRotation, st.Rotation)) >= 1f - 1e-6f);
        }

        /// <summary>
        /// ⭐ The angular velocity travels too (the wire field, not a translator side channel): encode → decode through
        /// <c>BdcWorldPosCodec</c> keeps it exact. And ⭐ <b>AQ107 F (b)</b> an all-zero <c>BdcWorldPos</c> decodes to the IDENTITY rotation, no NaN.
        /// </summary>
        [Fact]
        public void AQ107F_TheCodecKeepsTheAngularVelocity_AndAnAllZeroSampleIsTheIdentity()
        {
            var wire = BdcWorldPosCodec.Encode(7, 3.5, new Vector3(1, 2, 3), Quaternion.Identity, new Vector3(4, 5, 6), new Vector3(0.7f, 0.8f, 0.9f));
            BdcWorldPosCodec.Decode(in wire, out var p, out var r, out var v, out var w);
            Assert.Equal(3.5, wire.SimTime);
            Assert.Equal(new Vector3(1, 2, 3), p);
            Assert.Equal(new Vector3(4, 5, 6), v);
            Assert.Equal(new Vector3(0.7f, 0.8f, 0.9f), w);

            BdcWorldPosCodec.Decode(new BdcWorldPos(), out var p0, out var r0, out var v0, out var w0);
            Assert.Equal(Quaternion.Identity, r0);
            Assert.Equal(Vector3.Zero, p0);
            Assert.False(float.IsNaN(r0.X + r0.Y + r0.Z + r0.W));
        }

        /// <summary>
        /// ⭐ <b>AQ107 D addendum (2026-10-28)</b> — <c>BdcWorldPos.OrientationXyz</c> is <c>BdcOrientationQuat</c>: it displays as heading/pitch/roll + quaternion
        /// (grid <c>ToString</c> and the reflection-found <c>GetFormatTokens</c>), with the SAME bytes as <c>BdcVec3f</c>, and ⭐ for the same x, y, z it shows
        /// EXACTLY what NED's <c>OrientationQuat</c> shows (one <c>OrientationDisplay</c> behind both). ✅ Red-proofs (run): <c>BdcOrientationQuat.ToString</c> →
        /// <c>base.ToString()</c> ⇒ red; a 4th <c>float W</c> on <c>BdcOrientationQuat</c> ⇒ the descriptor test red; <c>GetFormatTokens</c> passing <c>-Y</c> on the BDC struct only ⇒ the parity test red.
        /// </summary>
        [Theory]
        [InlineData(0f, 0f, 0f)]
        [InlineData(0.003f, -0.029f, 0.382f)]
        [InlineData(0f, -0.7071068f, 0f)]
        [InlineData(-0.168f, 0.045f, 0.951f)]
        public void AQ107D_BdcOrientationQuat_DisplaysLikeTheNedType_ThroughToStringAndTheDuckTypedTokens(float x, float y, float z)
        {
            var bdc = new BdcOrientationQuat { X = x, Y = y, Z = z };
            var ned = new Hrot.NED.Messages.OrientationQuat { X = x, Y = y, Z = z };
            Assert.Equal(ned.ToString(), bdc.ToString());

            var method = typeof(BdcOrientationQuat).GetMethod("GetFormatTokens", Type.EmptyTypes);
            Assert.NotNull(method);
            var tokens = (IEnumerable<CycloneDDS.Schema.Formatting.FormattedToken>)method!.Invoke(bdc, null)!;
            Assert.Equal(bdc.ToString(), string.Concat(tokens.Select(t => t.Text)));
            Assert.Equal(string.Concat(ned.GetFormatTokens().Select(t => t.Text)), string.Concat(tokens.Select(t => t.Text)));
        }

        [Fact]
        public void AQ107D_BdcOrientationQuat_ShowsTheKnownPoseText_AndHasTheSameBytesAsABdcVec3f()
        {
            QuaternionWireCodec.Encode(Quaternion.CreateFromAxisAngle(Vector3.UnitZ, MathF.PI / 4f), out float x, out float y, out float z);
            Assert.Equal("HPR [045.0, 0.0, 0.0] deg, Quat [0.000, 0.000, 0.383, 0.924]", new BdcOrientationQuat { X = x, Y = y, Z = z }.ToString());

            Assert.Equal(BdcVec3f.GetDescriptorOps(), BdcOrientationQuat.GetDescriptorOps());
            Assert.Equal(BdcVec3f.GetDescriptorSize(), BdcOrientationQuat.GetDescriptorSize());
            Assert.Equal(BdcVec3f.GetDescriptorAlign(), BdcOrientationQuat.GetDescriptorAlign());
            Assert.Equal(12u, BdcOrientationQuat.GetDescriptorSize());
        }
    }

    [CollectionDefinition("BdcDds", DisableParallelization = true)]
    public class BdcDdsCollection { }
}
