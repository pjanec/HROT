using Xunit;

namespace Hrot.Editor.AiShared.Tests.Identity;

/// <summary>
/// ⭐⭐⭐ <b><c>CE-099</c> — the serial collection for every test that mutates
/// <see cref="Hrot.Editor.AiShared.AssetRoots.ConfiguredRoot"/>.</b>
///
/// <para>🔴🔴 <b>Measured <c>2026-08-27</c>, and it was a REAL race, not a test-authoring slip.</b>
/// <c>AssetRoots.Configure</c> writes a <b>process-global static</b>. xUnit runs distinct test classes in
/// PARALLEL by default *(one implicit collection each)* ⇒ ⛔ two classes that both call it interleave, and
/// each one's careful save/restore is clobbered by the other's.</para>
///
/// <para>📌 <b>How it surfaced.</b> <c>TheRootReportingPolicyIsOneImplementationTests</c> passed 5/5
/// FILTERED and reddened in the full suite: its <i>"a configured root ⇒ silent"</i> arm saw
/// <c>ConfiguredRoot == null</c>, because <c>TheDeployedNodeFindsItsAssetsTests</c> calls
/// <c>Configure(null)</c> at four points and had run in between. ⚠⚠ <b>That older class has been racing
/// since ruling 67 landed</b> — with nothing to collide with, it simply never lost.</para>
///
/// <para>⭐⭐ <b>Why a shared COLLECTION and not a lock.</b> Tests in one xUnit collection never run in
/// parallel with each other, so membership IS the fix; <c>DisableParallelization</c> additionally keeps the
/// collection from overlapping other collections, in case a third class starts touching the static from
/// elsewhere in the assembly. ⛔ A lock inside each class would have to be found and used by every future
/// author — 📌 the same reason <c>PanelSnapshotTestCollection</c> exists for the other process-global in
/// this assembly.</para>
///
/// <para>🔒 <b>If you add a test that calls <c>AssetRoots.Configure</c>, put it in this collection.</b>
/// ⚠ A green filtered run is NOT evidence that it is safe in the suite — that is exactly the trap this
/// class documents, and it is the third time this programme has hit an existing serial-collection
/// convention it had not joined.</para>
///
/// <para>📄 Design: <c>docs/DESIGN_Subsystem_Composition_Unification.md</c> §5c.15.</para>
/// </summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class AssetRootsTestCollection
{
    public const string Name = "AssetRoots.ConfiguredRoot (process-global)";
}
