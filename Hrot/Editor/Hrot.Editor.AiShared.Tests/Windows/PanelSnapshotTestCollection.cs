using Xunit;

namespace Hrot.Editor.AiShared.Tests;

/// <summary>
/// ⭐⭐⭐ <b>U-obs-5 — serializes every test class in this assembly that touches the process-global
/// <c>PanelSnapshot</c> singleton.</b>
///
/// <para>⛔⛔ <b>MEASURED, not theoretical.</b> xunit runs different test classes (their default,
/// one-class-one-collection) in PARALLEL. Two classes that each reset
/// <c>PanelSnapshot.CaptureEnabled</c> in their constructor/<c>Dispose</c> can interleave: one flips it
/// to <c>false</c> between another's <c>CaptureEnabled = true</c> and its
/// <c>PanelSnapshot.Register</c> call, and a rail that should see a captured model sees <c>null</c>
/// instead. 📌 Reproduced 2026-08-22 running only two of these classes together via
/// <c>--filter</c>.</para>
///
/// <para>⭐ Every test class in this assembly that reads or writes <c>PanelSnapshot</c> statics carries
/// <c>[Collection(Name)]</c>, so xunit runs them one at a time instead of concurrently. ⚠ Tests WITHIN
/// one class remain parallel-safe as before — this only orders class against class.</para>
/// </summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class PanelSnapshotTestCollection
{
    public const string Name = "PanelSnapshot serial (AiShared)";
}
