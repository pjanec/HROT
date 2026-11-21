using FluentAssertions;
using StructEdit.Core.Attributes;

namespace StructEdit.Tests.Foundation;

/// <summary>
/// ⭐ U-6 (docs/UX/DESIGN_Condition_Editor.md §8.4, R-4149) — ONE display-name resolver: the attribute wins, the humanizer otherwise, cached.
/// 🔴 Red-proof (compiles): <c>Resolve</c> ignoring <see cref="EditDisplayNameAttribute"/> ⇒ the attribute rows go red.
/// </summary>
public class DisplayNamesTests
{
    private sealed class Probe
    {
        public double DeadlineSeconds;
        public int EntryOrExit;
        [EditDisplayName("Hold for")] public float HoldSeconds;
        [EditUnit("km")] public float RangeMetres;
        [EditIcon("objective")] public int Target;
        public int IFVCount;
    }

    [EditDisplayName("All of these")] private sealed class CompoundPredicateDto { }
    private sealed class TimeElapsedPredicateDto { }
    private sealed class SpawnParamsDto { }

    private enum Edge { Rising, [EditDisplayName("Falls")] Falling, EntryOrExit }

    private static System.Reflection.FieldInfo F(string name) => typeof(Probe).GetField(name)!;

    [Fact]
    public void U6_TheAttributeWins_TheHumanizerOtherwise()
    {
        DisplayNames.Of(F(nameof(Probe.DeadlineSeconds))).Should().Be("Deadline");
        DisplayNames.UnitOf(F(nameof(Probe.DeadlineSeconds))).Should().Be("s");
        DisplayNames.Of(F(nameof(Probe.EntryOrExit))).Should().Be("Entry or exit");
        DisplayNames.Of(F(nameof(Probe.HoldSeconds))).Should().Be("Hold for");                 // the attribute wins
        DisplayNames.Of(F(nameof(Probe.RangeMetres))).Should().Be("Range");
        DisplayNames.UnitOf(F(nameof(Probe.RangeMetres))).Should().Be("km");                    // [EditUnit] wins over the name's unit word
        DisplayNames.IconOf(F(nameof(Probe.Target))).Should().Be("objective");
        DisplayNames.Of(F(nameof(Probe.IFVCount))).Should().Be("IFV count");                    // an acronym keeps its case
    }

    [Fact]
    public void U6_Types_DropTheirSuffix_OrTakeTheirAttribute()
    {
        DisplayNames.Of(typeof(CompoundPredicateDto)).Should().Be("All of these");
        DisplayNames.Of(typeof(TimeElapsedPredicateDto)).Should().Be("Time elapsed");
        DisplayNames.Of(typeof(SpawnParamsDto)).Should().Be("Spawn");
    }

    [Fact]
    public void U6_EnumMembers()
    {
        DisplayNames.Of(Edge.Rising).Should().Be("Rising");
        DisplayNames.Of(Edge.Falling).Should().Be("Falls");
        DisplayNames.Of(Edge.EntryOrExit).Should().Be("Entry or exit");
    }

    [Fact]
    public void U6_TheHumanizer_FoldsTheOldLocalCopy()
    {
        DisplayNames.Humanize("TankGun").Should().Be("Tank gun");      // what EntityRoleLine.Words returned
        DisplayNames.Humanize("SmallArms").Should().Be("Small arms");
        DisplayNames.Humanize("Seconds").Should().Be("Seconds");       // only a unit word: kept
        DisplayNames.Humanize("max_speed").Should().Be("Max speed");
    }

    [Fact]
    public void U6_CachedPerMember_NoAllocationAfterTheFirstAsk()
    {
        var f = F(nameof(Probe.EntryOrExit));
        DisplayNames.Of(f);
        DisplayNames.Of(Edge.Falling);
        long before = System.GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 100; i++) { DisplayNames.Of(f); DisplayNames.UnitOf(f); }
        System.GC.GetAllocatedBytesForCurrentThread().Should().Be(before);
    }
}
