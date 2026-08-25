using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Fdp.Core;
using Fdp.Toolkit.Replication.Components;
using Fdp.Toolkit.Replication.Services;
using Xunit;

namespace Hrot.Editor.Tests;

/// <summary>
/// ⭐⭐⭐ <b><c>BP-508</c> — there is ONE resolver from a runtime network id to an <c>Entity</c>, and no
/// fifth copy can appear unnoticed.</b>
/// 📄 <c>DESIGN_Variable_Watch_Pinning.md</c> §8 ② · §9 *(⛔ "a FIFTH <c>FindEntityByNetworkId</c>")</b> ·
/// 📌 <c>R-77</c> *(count corrected: there were FOUR, not two)</b>.
///
/// <para>⚠⚠ <b>Why the second rail here is a SOURCE SCAN, said plainly.</b> "There is exactly one
/// implementation" is a STRUCTURAL claim about the repository, ⛔ not a behaviour of any object — no
/// constructed object can be asked it. ⭐ A scan is the honest instrument for that one claim, and it is
/// the only thing here that is not a behavioural assertion. ⛔ It does NOT stand in for the behaviour
/// rails above it.</para>
/// </summary>
public sealed class ThereIsOneNetworkIdResolverTests
{
    // ══ the BEHAVIOUR — what the four copies now share ══════════════════════════

    /// <summary>
    /// ⭐⭐ <b>The best-of-four behaviour, asserted:</b> the filtered scan finds the right entity, and the
    /// three "answer, don't throw" cases every former copy disagreed about are pinned.
    /// <para>⚠ A DECOY with a different id is present because a resolver that returned the first
    /// networked entity it saw would pass a single-entity rail.</para>
    /// </summary>
    [Fact]
    public void TheResolverFindsTheEntityAndAnswersRatherThanThrowing()
    {
        using var repo = new EntityRepository();
        repo.RegisterComponent<NetworkIdentity>();

        var wanted = repo.CreateEntity();
        repo.SetComponent(wanted, new NetworkIdentity(4242));
        var decoy = repo.CreateEntity();
        repo.SetComponent(decoy, new NetworkIdentity(4243));
        repo.CreateEntity();                                  // ⚠ no NetworkIdentity at all

        Assert.Equal(wanted, NetworkIdResolver.FindEntityByNetworkId(repo, 4242));
        Assert.Equal(decoy,  NetworkIdResolver.FindEntityByNetworkId(repo, 4243));

        // ⛔ Three answers, no exceptions — the three the four copies did NOT agree on.
        Assert.Equal(Entity.Null, NetworkIdResolver.FindEntityByNetworkId(repo, 99999));   // absent
        Assert.Equal(Entity.Null, NetworkIdResolver.FindEntityByNetworkId(repo, 0));       // 0 is never valid
        Assert.Equal(Entity.Null, NetworkIdResolver.FindEntityByNetworkId(null, 4242));    // no world yet
    }

    /// <summary>
    /// ⚠ <b>A negative id is refused too</b> — ⭐ <c>NetworkIdentity.Value</c> is a <c>long</c>, so a
    /// caller can hand one in, and "id ≤ 0 means no identity" must hold on both sides of zero.
    /// </summary>
    [Fact]
    public void ANonPositiveIdIsNeverAMatch()
    {
        using var repo = new EntityRepository();
        repo.RegisterComponent<NetworkIdentity>();

        var e = repo.CreateEntity();
        repo.SetComponent(e, new NetworkIdentity(-5));

        Assert.Equal(Entity.Null, NetworkIdResolver.FindEntityByNetworkId(repo, -5));
    }

    // ══ the STRUCTURE — no fifth copy ═══════════════════════════════════════════

    /// <summary>
    /// ⭐⭐⭐ <b>THE ANTI-FIFTH RAIL — and writing it found MORE than <c>R-77</c> had recorded.</b>
    ///
    /// <para>⛔ It matches the LOOKUP SHAPE *(a <c>NetworkIdentity</c> read whose <c>.Value</c> is
    /// compared with <c>==</c>)</b> rather than the old method NAME, ⚠ because a fifth copy is most likely
    /// to be written INLINE — which a name scan misses entirely.</para>
    ///
    /// <para>🔴🔴 <b>And that is exactly what it found, <c>2026-08-25</c>.</b> The design counted FOUR
    /// named <c>FindEntityByNetworkId</c> methods. 📐 This scan found the shape in more places, including
    /// <b>three inline loops in <c>EditorSubsystem.cs</c> itself</b> — the same file that also held a
    /// named copy. ⭐ Those three are routed through the resolver by this batch; ⚠ the remainder are NOT,
    /// and each is named in <see cref="AllowedNonResolverLookups"/> with the reason.</para>
    ///
    /// <para>⭐⭐ <b>The allow-list is the point, not a weakness.</b> It is the MEASURED set, so a NEW
    /// inline lookup reddens this rail — ⛔ whereas asserting the aspirational answer would have meant
    /// quarantining the rail until an out-of-lane cleanup landed, and a quarantined rail catches nothing.
    /// ⭐ Removing an entry is how the follow-up cleanups prove themselves.</para>
    ///
    /// <para>⚠ <b>What this rail cannot see</b>, so nobody over-trusts it: a copy using <c>Equals</c>, a
    /// component alias, or a helper indirection. ⭐ It catches the shape every real copy had — ⛔ it is a
    /// tripwire, not a proof.</para>
    /// </summary>
    [Fact]
    public void NoNewInlineNetworkIdLookupAppears()
    {
        var root = RepoRoot();
        Assert.NotNull(root);

        var found = new List<string>();

        foreach (var file in Directory.EnumerateFiles(root!, "*.cs", SearchOption.AllDirectories))
        {
            if (file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}") ||
                file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"))
                continue;

            var rel = Path.GetRelativePath(root!, file).Replace('\\', '/');
            if (rel == ResolverPath || AllowedNonResolverLookups.ContainsKey(rel)) continue;

            // ⚠ Test assemblies assert on ids constantly and legitimately; this rail is about production.
            if (rel.Contains(".Tests/")) continue;

            foreach (var line in File.ReadLines(file))
            {
                if (!line.Contains("NetworkIdentity>(") || !line.Contains(".Value ==")) continue;
                found.Add(rel);
                break;
            }
        }

        Assert.Empty(found);
    }

    /// <summary>⭐ The resolver's own home — the one file that is SUPPOSED to contain the shape.</summary>
    private const string ResolverPath =
        "FDP/Toolkits/Fdp.Toolkits/Replication/Services/NetworkIdResolver.cs";

    /// <summary>
    /// ⚠⚠ <b>The MEASURED remainder, each with why it is not routed by this batch.</b>
    /// ⭐ Every entry is a follow-up, ⛔ not an exemption on principle — and deleting an entry is how that
    /// follow-up proves itself.
    /// </summary>
    private static readonly Dictionary<string, string> AllowedNonResolverLookups = new()
    {
        // ⭐⭐ NOT a lookup at all, and this is BP-512's whole point: the predicate asks "is THIS entity
        //    id N?" of the entity it was HANDED, in O(1). ⛔ A resolver call here would be the wrong fix —
        //    it runs once per active entity per tracker per tick.
        ["Hrot/Diagnostics/Hrot.Diagnostics.Breakpoints/DataBreakpointManager.cs"] =
            "BP-512: an is-this-entity test on the entity it was handed, not a lookup",

        // ⛔ A DIFFERENT shape: it scans PartMetadata and compares the PARENT's id, i.e. "find the child
        //    whose parent is N" — not "find the entity with id N". Routing it would need a second seam.
        ["Hrot/Subsystems/Hrot.SimHost/Systems/EqsResultUpdateSystem.cs"] =
            "parent-of lookup over PartMetadata, not an entity-by-id lookup",
        ["Hrot/Network/Hrot.Network.NED/CGF/EqsResultIngressTranslator.cs"] =
            "parent-of lookup over PartMetadata, not an entity-by-id lookup",

        // ⚠ These two are the SAME FILE NAME in two assemblies — a ruling-9 duplicate in its own right,
        //   and both are outside the UI lane. Routing one and not the other would make them diverge.
        ["Hrot/Subsystems/Hrot.SimHost/Systems/MissionControlBehaviorParamsHelper.cs"] =
            "out of lane; duplicated with the Hrot.Core copy — reconcile both together",
        ["Hrot/Engine/Hrot.Core/Systems/Common/MissionControlBehaviorParamsHelper.cs"] =
            "out of lane; duplicated with the Hrot.SimHost copy — reconcile both together",
    };

    /// <summary>⭐ Walks up to the checkout root, the same probe <c>CuratedScenarios</c> and
    /// <c>LayoutPaths</c> use. ⚠ <c>null</c> outside a checkout, and the rail says so rather than
    /// silently passing on an empty scan.</summary>
    private static string? RepoRoot()
    {
        var dir = AppContext.BaseDirectory;
        for (int i = 0; i < 12 && !string.IsNullOrEmpty(dir); i++)
        {
            if (File.Exists(Path.Combine(dir, "IOS-IG-SimHost.sln"))) return dir;
            dir = Directory.GetParent(dir)?.FullName;
        }
        return null;
    }
}
