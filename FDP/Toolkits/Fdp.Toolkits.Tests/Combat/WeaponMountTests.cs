using System;
using System.Collections.Generic;
using Fdp.Core;
using Fdp.Interfaces;
using Fdp.Toolkit.Combat;
using Fdp.Toolkit.Combat.Components;
using Fdp.Toolkit.Combat.Translators;
using Fdp.Toolkit.Replication.Components;
using Fdp.Toolkit.Tkb.Domain;
using Xunit;

namespace Fdp.Toolkit.Combat.Tests
{
    /// <summary>
    /// Unit tests for multi-mount weapon entity spawning (P0.02).
    /// </summary>
    public class WeaponMountTests : IDisposable
    {
        private readonly EntityRepository _repo;
        private readonly CombatTkbTranslator _translator;

        public WeaponMountTests()
        {
            _repo = new EntityRepository();
            _repo.RegisterComponent<WeaponState>();
            _repo.RegisterComponent<WeaponMountInfo>();
            _repo.RegisterComponent<PartMetadata>();
            _translator = new CombatTkbTranslator();
        }

        public void Dispose() => _repo.Dispose();

        private TkbTemplate BuildTemplate(List<WeaponMountDto> mounts, float? effectiveRange = null)
        {
            var template = new TkbTemplate("TestPlatform", 1);
            template.AddDescriptor(new WeaponSuiteDto { Mounts = mounts });
            if (effectiveRange.HasValue)
                template.AddDescriptor(new WeaponCapabilitiesDto { EffectiveRange = effectiveRange.Value });
            return template;
        }

        // SC-P0-02-1: 3-mount definition → 3 WeaponState components (1 on owner, 2 children)
        [Fact]
        public void ThreeMountDefinition_SpawnsThreeWeaponStateComponents()
        {
            var template = BuildTemplate(new List<WeaponMountDto>
            {
                new WeaponMountDto { InitialAmmunition = 30, MuzzleVelocity = 800f },
                new WeaponMountDto { InitialAmmunition = 20, MuzzleVelocity = 600f, WeaponGuid = 0xABCDEF01 },
                new WeaponMountDto { InitialAmmunition = 4,  MuzzleVelocity = 0f,   WeaponGuid = 0xABCDEF02 },
            });

            var owner = _repo.CreateEntity();
            _translator.Inject(_repo, owner, template);

            // Owner has WeaponState (primary)
            Assert.True(_repo.HasComponent<WeaponState>(owner));

            // Enumerate all entities and count those with WeaponState
            int wsCount = 0;
            var allQuery = _repo.Query().With<WeaponState>().Build();
            foreach (var e in allQuery) wsCount++;
            Assert.Equal(3, wsCount);
        }

        // SC-P0-02-2: EnumerateMounts returns count=3; dest[0]=owner; children in MountIndex order
        [Fact]
        public void EnumerateMounts_ThreeMounts_ReturnsOwnerThenChildrenInOrder()
        {
            var template = BuildTemplate(new List<WeaponMountDto>
            {
                new WeaponMountDto { InitialAmmunition = 30, MuzzleVelocity = 800f },
                new WeaponMountDto { InitialAmmunition = 20, MuzzleVelocity = 600f, WeaponGuid = 0xABCDEF01 },
                new WeaponMountDto { InitialAmmunition = 4,  MuzzleVelocity = 0f,   WeaponGuid = 0xABCDEF02 },
            });

            var owner = _repo.CreateEntity();
            _translator.Inject(_repo, owner, template);

            Span<Entity> mounts = stackalloc Entity[8];
            int count = WeaponMountQuery.EnumerateMounts(_repo, owner, mounts);

            Assert.Equal(3, count);
            Assert.Equal(owner, mounts[0]);

            // Children must be in MountIndex order (1, 2)
            Assert.Equal(1, _repo.GetComponentRO<WeaponMountInfo>(mounts[1]).MountIndex);
            Assert.Equal(2, _repo.GetComponentRO<WeaponMountInfo>(mounts[2]).MountIndex);
        }

        // SC-P0-02-3: 1-mount definition → 1 WeaponState on owner, no children
        [Fact]
        public void SingleMountDefinition_SpawnsOnlyOwnerWeaponState_NoChildren()
        {
            var template = BuildTemplate(new List<WeaponMountDto>
            {
                new WeaponMountDto { InitialAmmunition = 30, MuzzleVelocity = 800f }
            });

            var owner = _repo.CreateEntity();
            _translator.Inject(_repo, owner, template);

            Assert.True(_repo.HasComponent<WeaponState>(owner));

            // No WeaponMountInfo child entities should exist
            int mountInfoCount = 0;
            var q = _repo.Query().With<WeaponMountInfo>().Build();
            foreach (var e in q) mountInfoCount++;
            Assert.Equal(0, mountInfoCount);
        }

        // SC-P0-02-4: Mutating one mount's Ammo doesn't affect others
        [Fact]
        public void MutatingOneMount_DoesNotAffectOthers()
        {
            var template = BuildTemplate(new List<WeaponMountDto>
            {
                new WeaponMountDto { InitialAmmunition = 30, MuzzleVelocity = 800f },
                new WeaponMountDto { InitialAmmunition = 20, MuzzleVelocity = 600f, WeaponGuid = 0xABCDEF01 },
            });

            var owner = _repo.CreateEntity();
            _translator.Inject(_repo, owner, template);

            // Decrement owner's ammo to 0
            ref var ownerWs = ref _repo.GetComponentRW<WeaponState>(owner);
            ownerWs.Ammo = 0;

            // Child mount ammo should be unchanged (20)
            Span<Entity> mounts = stackalloc Entity[4];
            int count = WeaponMountQuery.EnumerateMounts(_repo, owner, mounts);
            Assert.Equal(2, count);

            Assert.Equal(0,  _repo.GetComponentRO<WeaponState>(mounts[0]).Ammo);  // owner — zeroed
            Assert.Equal(20, _repo.GetComponentRO<WeaponState>(mounts[1]).Ammo);  // child — unchanged
        }

        // SC-P0-02-5a: WeaponMountInfo.EffectiveRange matches WeaponCapabilitiesDto when present
        [Fact]
        public void WeaponMountInfo_EffectiveRange_MatchesCapabilitiesDto_WhenPresent()
        {
            const float expectedRange = 500f;
            var template = BuildTemplate(new List<WeaponMountDto>
            {
                new WeaponMountDto { InitialAmmunition = 30, MuzzleVelocity = 800f },
                new WeaponMountDto { InitialAmmunition = 20, MuzzleVelocity = 600f, WeaponGuid = 0xABC },
            }, effectiveRange: expectedRange);

            var owner = _repo.CreateEntity();
            _translator.Inject(_repo, owner, template);

            Span<Entity> mounts = stackalloc Entity[4];
            int count = WeaponMountQuery.EnumerateMounts(_repo, owner, mounts);
            Assert.Equal(2, count);
            Assert.Equal(expectedRange, _repo.GetComponentRO<WeaponMountInfo>(mounts[1]).EffectiveRange);
        }

        // SC-P0-02-5b: WeaponMountInfo.EffectiveRange is 0 when no WeaponCapabilitiesDto
        [Fact]
        public void WeaponMountInfo_EffectiveRange_IsZero_WhenCapabilitiesAbsent()
        {
            var template = BuildTemplate(new List<WeaponMountDto>
            {
                new WeaponMountDto { InitialAmmunition = 30, MuzzleVelocity = 800f },
                new WeaponMountDto { InitialAmmunition = 20, MuzzleVelocity = 600f, WeaponGuid = 0xABC },
            }); // no effectiveRange → no WeaponCapabilitiesDto

            var owner = _repo.CreateEntity();
            _translator.Inject(_repo, owner, template);

            Span<Entity> mounts = stackalloc Entity[4];
            int count = WeaponMountQuery.EnumerateMounts(_repo, owner, mounts);
            Assert.Equal(2, count);
            Assert.Equal(0f, _repo.GetComponentRO<WeaponMountInfo>(mounts[1]).EffectiveRange);
        }

        // PartMetadata back-link to owner
        [Fact]
        public void ChildMountEntities_HavePartMetadata_BackLinkingToOwner()
        {
            var template = BuildTemplate(new List<WeaponMountDto>
            {
                new WeaponMountDto { InitialAmmunition = 30, MuzzleVelocity = 800f },
                new WeaponMountDto { InitialAmmunition = 20, MuzzleVelocity = 600f, WeaponGuid = 0xABC },
                new WeaponMountDto { InitialAmmunition = 4,  MuzzleVelocity = 0f,   WeaponGuid = 0xDEF },
            });

            var owner = _repo.CreateEntity();
            _translator.Inject(_repo, owner, template);

            Span<Entity> mounts = stackalloc Entity[4];
            int count = WeaponMountQuery.EnumerateMounts(_repo, owner, mounts);
            Assert.Equal(3, count);

            Assert.Equal(owner, _repo.GetComponentRO<PartMetadata>(mounts[1]).ParentEntity);
            Assert.Equal(owner, _repo.GetComponentRO<PartMetadata>(mounts[2]).ParentEntity);
        }

        // SC-P0-01-2: Translator path sets WeaponState.MaxAmmo = InitialAmmunition
        // This test would FAIL if `MaxAmmo = primary.InitialAmmunition` were removed from
        // CombatTkbTranslator.Inject (the corrective fix from BATCH-01 review).
        [Fact]
        public void ThreeMountDefinition_WeaponStateMaxAmmo_SetByTranslator()
        {
            var template = BuildTemplate(new List<WeaponMountDto>
            {
                new WeaponMountDto { InitialAmmunition = 30, MuzzleVelocity = 800f },
                new WeaponMountDto { InitialAmmunition = 20, MuzzleVelocity = 600f, WeaponGuid = 0xABCDEF01 },
            });

            var owner = _repo.CreateEntity();
            _translator.Inject(_repo, owner, template);

            // Owner's primary weapon MaxAmmo must match InitialAmmunition (SC-P0-01-2)
            Assert.Equal(30, _repo.GetComponentRO<WeaponState>(owner).MaxAmmo);

            Span<Entity> mounts = stackalloc Entity[4];
            int count = WeaponMountQuery.EnumerateMounts(_repo, owner, mounts);
            Assert.Equal(2, count);

            // Child mount MaxAmmo must also match its own InitialAmmunition
            Assert.Equal(20, _repo.GetComponentRO<WeaponState>(mounts[1]).MaxAmmo);
        }
    }
}
