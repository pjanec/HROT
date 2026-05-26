using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;
using Fdp.Core;
using Fdp.Core.Tkb;
using Fdp.Interfaces;
using Hrot.MuscleCharacter.Animation.Baking;
using Hrot.MuscleCharacter.Animation.Components;
using Hrot.MuscleCharacter.Animation.Contracts;
using Hrot.MuscleCharacter.Animation.Descriptors;
using Hrot.MuscleCharacter.Animation.Hashing;
using Hrot.MuscleCharacter.Animation.Translators;
using Hrot.Editor.AiShared.Catalog;

namespace Hrot.MuscleCharacter.Animation.Tests
{
    // â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
    // Fix 1: AnimationTkbTranslator.Inject tests
    // â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

    public class AnimationTranslatorTests
    {
        private static EntityRepository CreateRepo()
        {
            var repo = new EntityRepository();
            repo.RegisterComponent<AnimationChannel>();
            repo.RegisterComponent<LookAtChannel>();
            repo.RegisterComponent<StanceIntent>();
            repo.RegisterComponent<StanceStatus>();
            repo.RegisterComponent<AnimationMontageQueue>();
            repo.RegisterComponent<AnimationMontageQueueState>();
            repo.RegisterComponent<CharacterAnimationDefRuntime>();
            repo.RegisterComponent<AnimationExecutorState>();
            repo.RegisterComponent<LookAtExecutorState>();
            return repo;
        }

        private static CharacterAnimationDefDto CreateMinimalDto(bool withAimConfig)
        {
            return new CharacterAnimationDefDto
            {
                Slots = new List<SlotDefDto>
                {
                    new SlotDefDto
                    {
                        SlotId = 0,
                        Name = "FullBody",
                        BoneMask = new[] { "root" },
                        Mode = SlotCompositingMode.Override,
                        Priority = 0,
                    }
                },
                Montages = new List<MontageDefDto>
                {
                    new MontageDefDto
                    {
                        Name = "TestMontage",
                        AssetRef = "Animations/Test.clip",
                        Slot = 0,
                        DefaultBlendInTime = 0.1f,
                        DefaultBlendOutTime = 0.2f,
                        DurationSeconds = 1.0f,
                        Sections = new[] { "Start" },
                        Notifies = new List<MontageNotifyRefDto>(),
                        IsStanceTransition = false,
                    }
                },
                SupportedStances = new[] { StanceId.Standing, StanceId.Crouched },
                StanceTransitions = new List<StanceTransitionDto>(),
                AimConfig = withAimConfig
                    ? new AimConfigDto { MaxYawDegrees = 90f, MaxPitchDegrees = 70f, AimSourceBone = "head" }
                    : null,
                NotifyMarkers = new List<NotifyMarkerDefDto>(),
            };
        }

        [Fact]
        public void Inject_WithNonAnimatedTemplate_AddsNoComponents()
        {
            using var repo = CreateRepo();
            var entity = repo.CreateEntity();
            // Template WITHOUT animation descriptor
            var template = new TkbTemplate("test", 1L);
            var translator = new AnimationTkbTranslator(hotReloadEvents: null);

            translator.Inject(repo, entity, template);

            Assert.False(repo.HasComponent<AnimationChannel>(entity));
            Assert.False(repo.HasComponent<StanceIntent>(entity));
        }

        [Fact]
        public void Inject_WithAnimatedEntity_AddsRequiredComponents()
        {
            using var repo = CreateRepo();
            var entity = repo.CreateEntity();
            var dto = CreateMinimalDto(withAimConfig: false);
            var template = new TkbTemplate("sniper", 1L);
            template.AddDescriptor(dto);
            var translator = new AnimationTkbTranslator(hotReloadEvents: null);

            translator.Inject(repo, entity, template);

            Assert.True(repo.HasComponent<AnimationChannel>(entity));
            Assert.True(repo.HasComponent<StanceIntent>(entity));
            Assert.True(repo.HasComponent<StanceStatus>(entity));
            Assert.True(repo.HasComponent<AnimationMontageQueue>(entity));
            Assert.True(repo.HasComponent<AnimationMontageQueueState>(entity));
            Assert.True(repo.HasComponent<CharacterAnimationDefRuntime>(entity));
            Assert.True(repo.HasComponent<AnimationExecutorState>(entity));
        }

        [Fact]
        public void Inject_WithAimCapableEntity_AddsLookAtComponents()
        {
            using var repo = CreateRepo();
            var entity = repo.CreateEntity();
            var dto = CreateMinimalDto(withAimConfig: true);
            var template = new TkbTemplate("sniper", 1L);
            template.AddDescriptor(dto);
            var translator = new AnimationTkbTranslator(hotReloadEvents: null);

            translator.Inject(repo, entity, template);

            Assert.True(repo.HasComponent<LookAtChannel>(entity));
            Assert.True(repo.HasComponent<LookAtExecutorState>(entity));
        }

        [Fact]
        public void Inject_WithoutAimConfig_DoesNotAddLookAtComponents()
        {
            using var repo = CreateRepo();
            var entity = repo.CreateEntity();
            var dto = CreateMinimalDto(withAimConfig: false);
            var template = new TkbTemplate("sniper", 1L);
            template.AddDescriptor(dto);
            var translator = new AnimationTkbTranslator(hotReloadEvents: null);

            translator.Inject(repo, entity, template);

            Assert.False(repo.HasComponent<LookAtChannel>(entity));
            Assert.False(repo.HasComponent<LookAtExecutorState>(entity));
        }
    }

    // â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
    // Fix 2: BakedAnimationCache hot-reload tests
    // â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

    public class BakedAnimationCacheTests
    {
        private static CharacterAnimationDefDto CreateSniperDto()
        {
            return new CharacterAnimationDefDto
            {
                Slots = new List<SlotDefDto>
                {
                    new SlotDefDto { SlotId = 0, Name = "Locomotion", BoneMask = new[] { "root" }, Mode = SlotCompositingMode.Override, Priority = 0 },
                    new SlotDefDto { SlotId = 1, Name = "FullBody", BoneMask = new[] { "root" }, Mode = SlotCompositingMode.Override, Priority = 100 },
                    new SlotDefDto { SlotId = 2, Name = "UpperBody", BoneMask = new[] { "spine" }, Mode = SlotCompositingMode.Override, Priority = 200 },
                },
                Montages = new List<MontageDefDto>
                {
                    new MontageDefDto
                    {
                        Name = "Reload_Rifle",
                        AssetRef = "Animations/Reload.clip",
                        Slot = 2,
                        DefaultBlendInTime = 0.1f,
                        DefaultBlendOutTime = 0.2f,
                        DurationSeconds = 3.4f,
                        Sections = new[] { "Start", "Insert", "Close" },
                        Notifies = new List<MontageNotifyRefDto>
                        {
                            new MontageNotifyRefDto { MarkerName = "MagOut", TimeSeconds = 0.8f },
                            new MontageNotifyRefDto { MarkerName = "MagIn", TimeSeconds = 2.1f },
                        },
                        IsStanceTransition = false,
                    },
                    new MontageDefDto
                    {
                        Name = "Trans_StandToCrouch",
                        AssetRef = "Animations/Trans_StandToCrouch.clip",
                        Slot = 1,
                        DefaultBlendInTime = 0.1f,
                        DefaultBlendOutTime = 0.1f,
                        DurationSeconds = 0.5f,
                        Sections = Array.Empty<string>(),
                        Notifies = new List<MontageNotifyRefDto>(),
                        IsStanceTransition = true,
                    },
                },
                SupportedStances = new[] { StanceId.Standing, StanceId.Crouched },
                StanceTransitions = new List<StanceTransitionDto>(),
                AimConfig = null,
                NotifyMarkers = new List<NotifyMarkerDefDto>
                {
                    new NotifyMarkerDefDto { Name = "MagOut", Hash = 0xA1B2C3D4, Kind = AnimNotifyCategory.Generic },
                    new NotifyMarkerDefDto { Name = "MagIn", Hash = 0xB1C2D3E4, Kind = AnimNotifyCategory.Generic },
                },
            };
        }

        [Fact]
        public void BakedAnimationCache_GetOrBake_ReturnsConsistentResult()
        {
            var cache = new BakedAnimationCache(null);
            var dto = CreateSniperDto();

            var result1 = cache.GetOrBake(1L, dto);
            var result2 = cache.GetOrBake(1L, dto);

            Assert.NotNull(result1);
            Assert.Equal(result1.MontageDict.Count, result2.MontageDict.Count);
        }

        [Fact]
        public void BakedAnimationCache_HotReload_InvalidatesEntry()
        {
            var fakeEvents = new FakeHotReloadEvents();
            var cache = new BakedAnimationCache(fakeEvents);
            var dto = CreateSniperDto();

            var result1 = cache.GetOrBake(1L, dto);
            fakeEvents.FireReload(1L);
            var result2 = cache.GetOrBake(1L, dto);

            Assert.NotNull(result2);
            Assert.Equal(result1.MontageDict.Count, result2.MontageDict.Count);
        }

        private sealed class FakeHotReloadEvents : ITkbHotReloadEvents
        {
            private Action<TkbDescriptorChangedEvent>? _handler;

            public IDisposable Subscribe(Action<TkbDescriptorChangedEvent> handler)
            {
                _handler = handler;
                return new ActionDisposable(() => _handler = null);
            }

            public void FireReload(long classId) =>
                _handler?.Invoke(new TkbDescriptorChangedEvent
                {
                    DescriptorName = "Anim.CharacterDef",
                    ClassId = classId,
                });

            private sealed class ActionDisposable : IDisposable
            {
                private readonly Action _dispose;
                public ActionDisposable(Action dispose) { _dispose = dispose; }
                public void Dispose() => _dispose();
            }
        }
    }

    // â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
    // Fix 3: AnimationTkbQueries query-method tests
    // â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

    public class AnimationTkbQueriesTests
    {
        private const string EntityClass = "sniper";

        private static ITkbDatabase CreateDb(CharacterAnimationDefDto dto)
        {
            var template = new TkbTemplate(EntityClass, 1L);
            template.AddDescriptor(dto);
            return new FakeTkbDatabase(EntityClass, template);
        }

        private static CharacterAnimationDefDto CreateSniperDto()
        {
            return new CharacterAnimationDefDto
            {
                Slots = new List<SlotDefDto>
                {
                    new SlotDefDto { SlotId = 0, Name = "Locomotion", BoneMask = new[] { "root" }, Mode = SlotCompositingMode.Override, Priority = 0 },
                    new SlotDefDto { SlotId = 1, Name = "FullBody", BoneMask = new[] { "root" }, Mode = SlotCompositingMode.Override, Priority = 100 },
                    new SlotDefDto { SlotId = 2, Name = "UpperBody", BoneMask = new[] { "spine" }, Mode = SlotCompositingMode.Override, Priority = 200 },
                },
                Montages = new List<MontageDefDto>
                {
                    new MontageDefDto
                    {
                        Name = "Reload_Rifle",
                        AssetRef = "Animations/Reload.clip",
                        Slot = 2,
                        DefaultBlendInTime = 0.1f,
                        DefaultBlendOutTime = 0.2f,
                        DurationSeconds = 3.4f,
                        Sections = new[] { "Start", "Insert", "Close" },
                        Notifies = new List<MontageNotifyRefDto>
                        {
                            new MontageNotifyRefDto { MarkerName = "MagOut", TimeSeconds = 0.8f },
                            new MontageNotifyRefDto { MarkerName = "MagIn", TimeSeconds = 2.1f },
                        },
                        IsStanceTransition = false,
                    },
                    new MontageDefDto
                    {
                        Name = "Vault_Low",
                        AssetRef = "Animations/Vault.clip",
                        Slot = 1,
                        DefaultBlendInTime = 0.1f,
                        DefaultBlendOutTime = 0.15f,
                        DurationSeconds = 1.2f,
                        Sections = new[] { "Approach", "Vault", "Land" },
                        Notifies = new List<MontageNotifyRefDto>(),
                        IsStanceTransition = false,
                    },
                    new MontageDefDto
                    {
                        Name = "Trans_StandToCrouch",
                        AssetRef = "Animations/Trans_StandToCrouch.clip",
                        Slot = 1,
                        DefaultBlendInTime = 0.1f,
                        DefaultBlendOutTime = 0.1f,
                        DurationSeconds = 0.5f,
                        Sections = Array.Empty<string>(),
                        Notifies = new List<MontageNotifyRefDto>(),
                        IsStanceTransition = true,
                    },
                },
                SupportedStances = new[] { StanceId.Standing, StanceId.Crouched },
                StanceTransitions = new List<StanceTransitionDto>(),
                AimConfig = new AimConfigDto
                {
                    MaxYawDegrees = 90f,
                    MaxPitchDegrees = 70f,
                    AimSourceBone = "head",
                },
                NotifyMarkers = new List<NotifyMarkerDefDto>
                {
                    new NotifyMarkerDefDto { Name = "MagOut", Hash = 0xA1B2C3D4, Kind = AnimNotifyCategory.Generic },
                    new NotifyMarkerDefDto { Name = "MagIn", Hash = 0xB1C2D3E4, Kind = AnimNotifyCategory.Generic },
                },
            };
        }

        [Fact]
        public void GetPlayableMontages_ExcludesStanceTransitionMontages()
        {
            var dto = CreateSniperDto();
            var queries = new AnimationTkbQueries(CreateDb(dto));

            var result = queries.GetPlayableMontages(EntityClass);

            Assert.Equal(2, result.Count);
            Assert.All(result, m => Assert.False(m.IsStanceTransition));
        }

        [Fact]
        public void GetSupportedStances_ReturnsAll()
        {
            var dto = CreateSniperDto();
            var queries = new AnimationTkbQueries(CreateDb(dto));

            var stances = queries.GetSupportedStances(EntityClass);

            Assert.Equal(2, stances.Count);
        }

        [Fact]
        public void SupportsAim_TrueWhenAimConfigPresent()
        {
            var dto = CreateSniperDto();
            var queries = new AnimationTkbQueries(CreateDb(dto));

            Assert.True(queries.SupportsAim(EntityClass));
        }

        [Fact]
        public void SupportsAim_FalseWhenAimConfigNull()
        {
            var dto = CreateSniperDto() with { AimConfig = null };
            var queries = new AnimationTkbQueries(CreateDb(dto));

            Assert.False(queries.SupportsAim(EntityClass));
        }

        [Fact]
        public void GetAvailableMarkers_ReturnsAllMarkers()
        {
            var dto = CreateSniperDto();
            var queries = new AnimationTkbQueries(CreateDb(dto));

            var markers = queries.GetAvailableMarkers(EntityClass);

            Assert.Equal(2, markers.Count);
        }

        [Fact]
        public void GetMarkerName_ReverseLookup()
        {
            var dto = CreateSniperDto();
            var queries = new AnimationTkbQueries(CreateDb(dto));

            var name = queries.GetMarkerName(EntityClass, 0xA1B2C3D4);

            Assert.Equal("MagOut", name);
        }

        [Fact]
        public void ResolveMontageId_MatchesStableIdHasher()
        {
            var dto = CreateSniperDto();
            var queries = new AnimationTkbQueries(CreateDb(dto));

            var id = queries.ResolveMontageId(EntityClass, "Reload_Rifle");

            Assert.Equal(StableIdHasher.ComputeMontageAssetId("Reload_Rifle"), id);
        }

        private sealed class FakeTkbDatabase : ITkbDatabase
        {
            private readonly string _name;
            private readonly TkbTemplate _template;

            public FakeTkbDatabase(string name, TkbTemplate template)
            {
                _name = name;
                _template = template;
            }

            public bool TryGetByName(string name, out TkbTemplate template)
            {
                if (name == _name)
                {
                    template = _template;
                    return true;
                }
                template = null!;
                return false;
            }

            public TkbTemplate GetByName(string name) => _template;
            public TkbTemplate GetByType(long tkbType) => _template;
            public bool TryGetByType(long tkbType, out TkbTemplate template) { template = _template; return true; }
            public void Register(TkbTemplate template) { }
            public IEnumerable<TkbTemplate> GetAll() => new[] { _template };
            public void Clear() { }
            public IEnumerable<TkbTemplate> GetEntitiesByCategory(string categoryPath) => Array.Empty<TkbTemplate>();
            public string? ActiveTkbName { get; set; }
        }
    }
}

