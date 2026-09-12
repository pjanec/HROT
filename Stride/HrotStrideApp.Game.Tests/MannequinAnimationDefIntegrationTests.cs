using System.Linq;
using Fdp.Toolkit.Tkb;
using Fdp.Examples.Scenarios.Integrated;
using Hrot.MuscleCharacter.Animation.Baking;
using Hrot.MuscleCharacter.Animation.Components;
using Hrot.MuscleCharacter.Animation.Contracts;
using Fdp.Toolkit.Tkb.Domain;
using Hrot.MuscleCharacter.Animation.Hashing;
using Xunit;

namespace HrotStrideApp.Tests;

/// <summary>
/// Integration tests for STR-P4-T2: the mannequin <see cref="CharacterAnimationDefDto"/>
/// authored on the UrbanCombat humanoid templates (InfantrySoldier 2002, Insurgent 2003)
/// and its bake into runtime form via the canonical baking algorithm
/// (<see cref="BakingUtils.BakeDef"/>, equivalent to <c>CharacterAnimationDefRuntime.BakeForTest</c>).
///
/// <para>Mirrors how <c>StrideVisualBindingIntegrationTests</c> asserts the presence of
/// <c>StrideRenderModelDefDto</c> on the templates, then additionally verifies the baked
/// montage dictionary resolves the locomotion + jump clip AssetRefs to the <c>Animations/*</c>
/// Stride asset URLs.</para>
/// </summary>
public sealed class MannequinAnimationDefIntegrationTests
{
    private const int TkbInfantrySoldier = 2002;
    private const int TkbInsurgent       = 2003;

    private static TkbDatabase BuildTkb()
    {
        var tkb = new TkbDatabase();
        UrbanCombatNewScenario.RegisterUrbanCombatTkbTemplates(tkb);
        return tkb;
    }

    // ── Template carries the descriptor (parity with StrideRenderModelDefDto test) ──

    [Theory]
    [InlineData(TkbInfantrySoldier)]
    [InlineData(TkbInsurgent)]
    public void HumanoidTemplate_CarriesCharacterAnimationDefDto(int tkbType)
    {
        var tkb = BuildTkb();

        Assert.True(tkb.TryGetByType(tkbType, out var template));
        var def = template.GetDescriptor<CharacterAnimationDefDto>();
        Assert.NotNull(def);
    }

    [Fact]
    public void NonHumanoidTemplate_HasNoCharacterAnimationDefDto()
    {
        var tkb = BuildTkb();

        // MilitaryAPC (2001) is a vehicle — it must NOT carry humanoid animation data.
        Assert.True(tkb.TryGetByType(2001, out var apc));
        Assert.Null(apc.GetDescriptor<CharacterAnimationDefDto>());
    }

    // ── Descriptor content ──────────────────────────────────────────────────

    [Fact]
    public void Descriptor_DeclaresLocomotionAndFullBodySlots()
    {
        var def = UrbanCombatNewScenario.BuildMannequinAnimationDef();

        Assert.Contains(def.Slots, s => s.SlotId == 0   && s.Name == "Locomotion");
        Assert.Contains(def.Slots, s => s.SlotId == 100 && s.Name == "FullBody");
    }

    [Fact]
    public void Descriptor_CarriesLocomotionAndJumpAssetRefs()
    {
        var def = UrbanCombatNewScenario.BuildMannequinAnimationDef();

        var byName = def.Montages.ToDictionary(m => m.Name);
        Assert.Equal("Animations/Idle",       byName["Idle"].AssetRef);
        Assert.Equal("Animations/Walk",       byName["Walk"].AssetRef);
        Assert.Equal("Animations/Run",        byName["Run"].AssetRef);
        Assert.Equal("Animations/Jump_Start", byName["Jump_Start"].AssetRef);
        Assert.Equal("Animations/Jump_Loop",  byName["Jump_Loop"].AssetRef);
        Assert.Equal("Animations/Jump_End",   byName["Jump_End"].AssetRef);

        // Locomotion clips on slot 0; jump montages on slot 100.
        Assert.Equal(0,   byName["Idle"].Slot);
        Assert.Equal(0,   byName["Walk"].Slot);
        Assert.Equal(0,   byName["Run"].Slot);
        Assert.Equal(100, byName["Jump_Start"].Slot);
        Assert.Equal(100, byName["Jump_Loop"].Slot);
        Assert.Equal(100, byName["Jump_End"].Slot);
    }

    // ── Bake into runtime form ───────────────────────────────────────────────

    [Fact]
    public void Descriptor_BakesAllSixClipsIntoMontageDict_WithResolvableAssetRefs()
    {
        var def = UrbanCombatNewScenario.BuildMannequinAnimationDef();

        CharacterAnimationBakedData baked = BakingUtils.BakeDef(def);

        // All six clips bake into the montage dict keyed by stable montage asset id.
        string[] expectedNames = { "Idle", "Walk", "Run", "Jump_Start", "Jump_Loop", "Jump_End" };
        foreach (var name in expectedNames)
        {
            int id = StableIdHasher.ComputeMontageAssetId(name);
            Assert.True(baked.MontageDict.ContainsKey(id), $"Baked montage dict must contain '{name}'.");
            Assert.Equal($"Animations/{name}", baked.MontageDict[id].AssetRef);
        }

        Assert.Equal(6, baked.MontageDict.Count);
    }

    [Fact]
    public void Bake_PreservesPerMontageSlotAndBlendTimes()
    {
        var def = UrbanCombatNewScenario.BuildMannequinAnimationDef();
        var baked = BakingUtils.BakeDef(def);

        int jumpEndId = StableIdHasher.ComputeMontageAssetId("Jump_End");
        var jumpEnd = baked.MontageDict[jumpEndId];
        Assert.Equal(100, jumpEnd.Slot);
        Assert.Equal(0.5f, jumpEnd.Duration, 3);
        Assert.Equal(0.1f, jumpEnd.DefaultBlendInTime, 3);
        Assert.Equal(0.12f, jumpEnd.DefaultBlendOutTime, 3);
    }

    [Fact]
    public void Bake_ResolvesFootstepNotifyMarkersWithCorrectKind()
    {
        var def = UrbanCombatNewScenario.BuildMannequinAnimationDef();
        var baked = BakingUtils.BakeDef(def);

        int walkId = StableIdHasher.ComputeMontageAssetId("Walk");
        var walk = baked.MontageDict[walkId];

        // Walk carries two footstep notifies; the baker fills Kind from NotifyMarkers registry.
        Assert.Equal(2, walk.Notifies.Count);
        Assert.All(walk.Notifies, n => Assert.Equal(AnimNotifyCategory.Footstep, n.Kind));

        // The runtime marker hash is computed from the name (authoritative, DEBT D-13).
        var left = walk.Notifies.Single(n => n.Name == "Footstep_Left");
        Assert.Equal(StableIdHasher.ComputeMarkerHash("Footstep_Left"), left.MarkerHash);
        Assert.Equal(0.25f, left.TimeSeconds, 3);
    }

    [Fact]
    public void Bake_DeclaresStandingAndCrouchedStances()
    {
        var def = UrbanCombatNewScenario.BuildMannequinAnimationDef();
        var baked = BakingUtils.BakeDef(def);

        Assert.Contains((byte)StanceId.Standing, baked.SupportedStances);
        Assert.Contains((byte)StanceId.Crouched, baked.SupportedStances);
    }

    [Fact]
    public void Bake_SortsSlotsByPriorityAscending()
    {
        var def = UrbanCombatNewScenario.BuildMannequinAnimationDef();
        var baked = BakingUtils.BakeDef(def);

        Assert.Equal(2, baked.Slots.Count);
        Assert.Equal(0,   baked.Slots[0].SlotId); // Locomotion, priority 0
        Assert.Equal(100, baked.Slots[1].SlotId); // FullBody, priority 100
    }
}
