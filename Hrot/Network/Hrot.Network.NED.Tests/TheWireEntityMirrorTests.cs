using System;
using System.Linq;
using System.Threading;
using CycloneDDS.Runtime;
using Fdp.Toolkit.DER;
using Hrot.NED.Descriptors;
using Xunit;

namespace Hrot.Network.NED.Tests
{
    /// <summary>
    /// ⭐ AQ109 §5b step 6 (X5) — the ONE generic wire mirror: every entity-keyed type is mirrored; a sample of a type no code names
    /// appears under its entity id; nothing is read once the mirror is gone. 📄 docs/blueprints/Architect_Question_109_Retire_ExCon.md §6.5.
    /// The type set comes from <see cref="WireTypeCatalog"/> — the helper the gate rail and the inspector window share.
    /// </summary>
    public sealed class TheWireEntityMirrorTests
    {
        private const uint PrivateDomain = 124;

        [Fact]
        public void EveryEntityKeyedTypeIsMirrored()
        {
            var keyed = TheEntityKeyedWireTypesAreReadableByReflectionTests.EntityKeyedTypes();
            using var participant = new DdsParticipant(PrivateDomain);
            using var mirror = new WireEntityMirror(participant, keyed);

            Assert.True(keyed.Count >= 50);
            Assert.Equal(keyed.Select(t => t.FullName).OrderBy(n => n, StringComparer.Ordinal),
                         mirror.OpenedTypes.Select(t => t.FullName).OrderBy(n => n, StringComparer.Ordinal));
        }

        [Fact]
        public void ASampleOfATypeWithNoHandWrittenCodeAppearsUnderItsEntityAndPart()
        {
            // EntityWeaponTaskStatus: keyed (parent entity id, part id); nothing in the inspector names it.
            var keyed = TheEntityKeyedWireTypesAreReadableByReflectionTests.EntityKeyedTypes();
            using var participant = new DdsParticipant(PrivateDomain + 1);
            using var mirror = new WireEntityMirror(participant, keyed);
            using var writer = new DdsWriter<EntityWeaponTaskStatus>(participant);

            Thread.Sleep(400);   // DDS discovery
            writer.Write(new EntityWeaponTaskStatus { ParentNetworkId = 4242, PartId = 2001, RoundsFired = 7 });

            IDerEntity? entity = null;
            for (var i = 0; i < 100 && entity?.HasDescriptor<EntityWeaponTaskStatus>(2001) != true; i++)
            {
                Thread.Sleep(50);
                mirror.Poll();
                entity = mirror.Repo.GetEntity(4242);
            }

            Assert.NotNull(entity);
            var got = entity!.GetDescriptor<EntityWeaponTaskStatus>(2001);
            Assert.Equal((ushort)7, got.RoundsFired);
            Assert.Single(mirror.Repo.GetAllEntities());
        }

        [Fact]
        public void TwoEntityIdsThatDifferOnlyAboveBit31AreTwoEntities()
        {
            // Network ids are long and allocated per node (AQ107 E): 5 and 5 + 2^32 must not merge into one inspector row.
            const long low = 5, high = 5 + (1L << 32);
            var keyed = TheEntityKeyedWireTypesAreReadableByReflectionTests.EntityKeyedTypes();
            using var participant = new DdsParticipant(PrivateDomain + 3);
            using var mirror = new WireEntityMirror(participant, keyed);
            using var writer = new DdsWriter<EntityWeaponTaskStatus>(participant);

            Thread.Sleep(400);   // DDS discovery
            writer.Write(new EntityWeaponTaskStatus { ParentNetworkId = low, PartId = 1, RoundsFired = 1 });
            writer.Write(new EntityWeaponTaskStatus { ParentNetworkId = high, PartId = 1, RoundsFired = 2 });

            for (var i = 0; i < 100 && mirror.Repo.GetAllEntities().Count() < 2; i++) { Thread.Sleep(50); mirror.Poll(); }

            Assert.Equal(new[] { low, high }, mirror.Repo.GetAllEntities().Select(e => e.EntityId).OrderBy(x => x).ToArray());
            Assert.Equal((ushort)1, mirror.Repo.GetEntity(low)!.GetDescriptor<EntityWeaponTaskStatus>(1).RoundsFired);
            Assert.Equal((ushort)2, mirror.Repo.GetEntity(high)!.GetDescriptor<EntityWeaponTaskStatus>(1).RoundsFired);
        }

        [Fact]
        public void DisposingTheMirrorDropsEveryEntityAndItsReaders()
        {
            var keyed = TheEntityKeyedWireTypesAreReadableByReflectionTests.EntityKeyedTypes();
            using var participant = new DdsParticipant(PrivateDomain + 2);
            var mirror = new WireEntityMirror(participant, keyed);
            using var writer = new DdsWriter<EntityWeaponTaskStatus>(participant);
            Thread.Sleep(400);   // DDS discovery
            writer.Write(new EntityWeaponTaskStatus { ParentNetworkId = 9, PartId = 1 });
            for (var i = 0; i < 100 && !mirror.Repo.GetAllEntities().Any(); i++) { Thread.Sleep(50); mirror.Poll(); }
            Assert.NotEmpty(mirror.Repo.GetAllEntities());

            mirror.Dispose();

            Assert.Empty(mirror.Repo.GetAllEntities());
            Assert.Empty(mirror.OpenedTypes);
        }
    }
}
