using System;
using System.Collections.Generic;
using System.IO;
using Fdp.Toolkit.NetworkSpawning;
using Xunit;

namespace Hrot.Editor.Tests;

/// <summary>
/// ⭐⭐⭐ <b><c>CE-203</c> <c>E2</c> — there is ONE production <c>SequentialIdAllocator</c>, and the editor
/// still issues the id it always did.</b>
/// 📄 <c>DESIGN_Subsystem_Composition_Unification.md</c> §4.1y <c>E2</c> ·
/// <c>DESIGN_Deterministic_Network_Ids.md</c> §4b/§11.
///
/// <para>📐 <b>There were THREE copies.</b> <c>Hrot.Core.Network.SequentialIdAllocator</c> (the shared one,
/// which <c>OfflineNetworkFactory.CreateIdAllocator</c> already returned for every offline host),
/// <c>EditorSubsystem</c>'s private nested one, and <c>EditorHarness</c>'s test copy. ⛔ The shared class's
/// own remarks record that the first two DISAGREED — <c>Reset(1000)</c> issued <c>1001</c> there and
/// <c>1000</c> in the editor — which <c>HN-037</c> had to correct one level down. ⇒ this is the divergence
/// a second implementation buys, written down by the code itself.</para>
///
/// <para>⚠ <b>Two halves, and the structural one does not stand in for the behavioural one.</b> "Exactly one
/// implementation" is a claim about the REPOSITORY that no constructed object can be asked; "the first id is
/// still 1000" is a claim about behaviour that a source scan cannot see. Both are needed, and the shape is
/// <see cref="ThereIsOneNetworkIdResolverTests"/>'s.</para>
/// </summary>
public sealed class ThereIsOneSequentialIdAllocatorTests
{
    // ══ the BEHAVIOUR — the editor's first id is unchanged ══════════════════════

    /// <summary>
    /// ⭐⭐⭐ <b>THE RAIL THAT MAKES <c>E2</c> BEHAVIOUR-PRESERVING.</b> The editor's deleted allocator
    /// started at <c>1000</c> and POST-incremented; the shared one starts at <c>1</c> and PRE-increments.
    /// ⇒ the adoption is only safe because <c>Reset(WorldBase)</c> is stated on the OBSERVABLE — <i>"after
    /// this returns, the next id issued is <c>startId</c>"</i> — so one call reproduces the old sequence
    /// exactly.
    /// <para>⛔ Asserting only the FIRST id would pass for an allocator that then repeated itself, so the
    /// second and third are pinned too.</para>
    /// </summary>
    [Fact]
    public void TheOfflineFactorysAllocator_ResetToWorldBase_IssuesTheEditorsOldSequence()
    {
        INetworkIdAllocator alloc =
            new OfflineNetworkFactory().CreateIdAllocator("EditorAllocator", skipRoutingWait: true);

        alloc.Reset(WorldIdAuthority.WorldBase);

        Assert.Equal(1000, alloc.AllocateId());
        Assert.Equal(1001, alloc.AllocateId());
        Assert.Equal(1002, alloc.AllocateId());
    }

    /// <summary>
    /// ⭐⭐ <b>And it is the SHARED class</b>, not another copy the factory happens to return.
    /// ⚠ Also that it is <see cref="IRestorableIdAllocator"/>: the preview dry-run bracket
    /// (<c>PreviewParticipants.IdAllocator</c>) type-TESTS for that capability and silently degrades when
    /// it is absent — exactly the silent-default shape. The deleted editor copy implemented it, so losing
    /// it here would have disabled preview id restoration with no error at all.
    /// </summary>
    [Fact]
    public void ItIsTheSharedClass_AndItCanStillRestoreItsPreviewPosition()
    {
        INetworkIdAllocator alloc =
            new OfflineNetworkFactory().CreateIdAllocator("EditorAllocator", skipRoutingWait: true);

        Assert.IsType<Hrot.Core.Network.SequentialIdAllocator>(alloc);

        var restorable = Assert.IsAssignableFrom<IRestorableIdAllocator>(alloc);
        alloc.Reset(WorldIdAuthority.WorldBase);

        object? mark = restorable.CaptureIssuingPosition();
        Assert.NotNull(mark);
        Assert.Equal(1000, alloc.AllocateId());
        Assert.Equal(1001, alloc.AllocateId());

        restorable.RestoreIssuingPosition(mark!);
        Assert.Equal(1000, alloc.AllocateId());   // the preview's ids repeat exactly
    }

    // ══ the STRUCTURE — no second production declaration ════════════════════════

    /// <summary>
    /// ⭐⭐⭐ <b>THE ANTI-SECOND-COPY RAIL.</b> A new private allocator is the easiest thing in the world to
    /// write when a host needs one offline, and the last one silently disagreed with the shared class for
    /// months.
    /// <para>⚠ <b>What it cannot see</b>, so nobody over-trusts it: a copy under a different NAME. ⭐ It
    /// catches the shape the two real copies had — ⛔ a tripwire, not a proof.</para>
    /// <para>⭐ Test assemblies are exempt on purpose: <c>EditorHarness</c>'s copy is a test double and is
    /// not a production implementation. ⛔ It is still a third copy, and it is named here so the exemption
    /// is a decision rather than an oversight.</para>
    /// </summary>
    [Fact]
    public void OnlyOneProductionFileDeclaresASequentialIdAllocator()
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
            if (rel == SharedAllocatorPath) continue;
            if (rel.Contains(".Tests/")) continue;   // test doubles are not production implementations

            foreach (var line in File.ReadLines(file))
            {
                if (!line.Contains("class SequentialIdAllocator")) continue;
                found.Add(rel);
                break;
            }
        }

        Assert.Empty(found);
    }

    /// <summary>⭐ The one file that is SUPPOSED to declare it.</summary>
    private const string SharedAllocatorPath = "Hrot/Engine/Hrot.Core/Network/SequentialIdAllocator.cs";

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
