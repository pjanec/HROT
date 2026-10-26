using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Fdp.TestShared;
using Xunit;
using Xunit.Abstractions;

namespace Hrot.ClusterRunner.Tests;

/// <summary>
/// ⭐⭐ <c>CE-4046</c> (docs/DESIGN_Replay_Census_H0.md §2) — every RECORDED managed component is an immutable record, so it cannot be
/// changed in place behind the recorder's back (a managed chunk is written only when its version moved; an in-place change through
/// <c>GetManagedComponentRO</c> never reaches a delta frame). 🔒 User, 2026-10-25: <i>"a managed component (besides singleton) should never
/// be changed via read only accessor … The managed components was meant to be csharp records"</i> · <i>"lets build that rail"</i>.
/// <para>⭐ The types that predate the rule are in <see cref="Existing"/>, which may only SHRINK: a type converted to a record must leave
/// it (the rail fails on a stale entry), and a new mutable managed component fails at once. ⚠ No recorded managed SINGLETON exists today;
/// one that appears goes in <see cref="Singletons"/> with its reason — singletons are written whole on every keyframe and are exempt.
/// ⚠ The Stride assemblies are not walked (CE-3307; the H0 census found they define no component types).</para>
/// </summary>
public sealed class RecordedManagedComponentsAreImmutableTests
{
    /// <summary>The recorded managed singletons exempt from the rule, with why. Empty today.</summary>
    private static readonly Dictionary<string, string> Singletons = new();

    /// <summary>⛔ SHRINK ONLY — the 23 recorded mutable managed components found when the rail was written (2026-10-25), each to convert
    /// (an immutable record, or its per-frame state moved to an unmanaged struct) under CE-4046.</summary>
    private static readonly HashSet<string> Existing = new()
    {
        "Fdp.Toolkit.Behavior.Components.ActiveMissionPlan",
        "Fdp.Toolkit.Behavior.Components.PausedTask",
        "Fdp.Toolkit.Behavior.Components.PendingUnitCommand",
        "Fdp.Toolkit.Behavior.Components.SopStartRecord",
        "Fdp.Toolkit.Perception.Components.SensorCapability",
        "Fdp.Toolkit.Perception.Survey.WorldSurveyRequest",
        "Fdp.Toolkit.Replication.Components.BinaryGhostStore",
        "Fdp.Toolkit.Replication.Components.ChildMap",
        "Fdp.Toolkit.Replication.Components.DescriptorOwnership",
        "Fdp.Toolkit.Replication.Components.NetworkAckPeerSet",
        "Fdp.Toolkit.Replication.Components.PendingAuthorityGrants",
        "Hrot.AI.Behaviors.BpManagedCollectionDemo",
        "Hrot.IG.Components.EditablePolyline",
        "Hrot.Map.Common.Components.RoutePlan",
        "Hrot.Map.Definitions.Tkb.SimCombatDef",
        "Hrot.Map.Definitions.Tkb.TkbCompositionDef",
    };

    private readonly ITestOutputHelper _out;
    public RecordedManagedComponentsAreImmutableTests(ITestOutputHelper output) => _out = output;

    [Fact]
    public void EveryRecordedManagedComponent_IsAnImmutableRecord()
    {
        var r = ManagedComponentImmutabilityProbe.Walk();
        _out.WriteLine($"{r.Types} recorded managed component types; {r.Violations.Count} not immutable ({Existing.Count} allowed, CE-4046)");
        foreach (var v in r.Violations) _out.WriteLine($"  {v.Type}: {string.Join("; ", v.Reasons)}");
        Assert.True(r.Types > 15, $"only {r.Types} recorded managed types walked — the probe is not finding the tree.");

        var found = r.Violations.Where(v => !Singletons.ContainsKey(v.Type)).ToList();
        var fresh = found.Where(v => !Existing.Contains(v.Type)).ToList();
        Assert.True(fresh.Count == 0,
            $"{fresh.Count} recorded managed component(s) can be changed in place — the recorder would miss the change (CE-4046). Make each "
            + "an immutable record (init-only members, IReadOnly*/Immutable collections; change it with `with` + Set), move per-frame state "
            + "to an unmanaged struct, or mark it NoReplay if replay does not need it:\n  "
            + string.Join("\n  ", fresh.Select(v => $"{v.Type}: {string.Join("; ", v.Reasons)}")));

        var stillMutable = found.Select(v => v.Type).ToHashSet();
        var stale = Existing.Where(t => !stillMutable.Contains(t)).OrderBy(t => t).ToList();
        Assert.True(stale.Count == 0,
            $"{stale.Count} type(s) in the shrink-only list are immutable now (or gone) — remove them from Existing:\n  " + string.Join("\n  ", stale));
    }

    /// <summary>⭐ The probe's own red-proof: what it calls immutable and what it does not.</summary>
    [Fact]
    public void TheProbe_TellsAnImmutableRecordFromAMutableClass()
    {
        Assert.Empty(ManagedComponentImmutabilityProbe.Reasons(typeof(GoodRecord)));
        var bad = ManagedComponentImmutabilityProbe.Reasons(typeof(BadClass));
        Assert.Contains("not a record", bad);
        Assert.Contains("settable field Count", bad);
        Assert.Contains("settable property Name", bad);
        Assert.Contains(bad, r => r.StartsWith("mutable collection Items"));
        Assert.Contains(ManagedComponentImmutabilityProbe.Reasons(typeof(RecordWithAList)), r => r.StartsWith("mutable collection Ids"));
    }

    private sealed record GoodRecord(string Name, ImmutableArray<int> Ids, IReadOnlyList<float> Speeds) { public int Version { get; init; } }
    private sealed class BadClass { public int Count; public string Name { get; set; } = ""; public readonly List<int> Items = new(); }
    private sealed record RecordWithAList(List<int> Ids);
}
