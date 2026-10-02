using Xunit;

namespace Hrot.Blueprints.Tests.Editor.Frame;

/// <summary>
/// ⭐⭐⭐ <b>Batch 100 (<c>100a</c>) — every frame rail runs in THIS collection, and the reason is not
/// tidiness.</b>
///
/// <para>⛔⛔ <b>Raylib is not re-entrant.</b> Two concurrent <c>InitWindow</c> calls in one process
/// crash the test host — ⚠ and a crashed host truncates the run, so the pass/fail counts differ
/// between runs. 📌 That is exactly the shape of <c>BP-337</c> and <c>DEBT-AIB-030</c>, both of which
/// cost this programme whole batches of confusion. ⛔ <b>Do not add a frame rail outside this
/// collection.</b></para>
///
/// <para>⭐ <see cref="Hrot.Editor.UiFrameRail.UiFrameHarness"/> also holds a process-wide semaphore.
/// ⚠ <b>That is a belt, not a replacement</b> — it serialises rails that would otherwise crash, but it
/// cannot stop xUnit from timing them out while they queue. ⭐ The collection is what makes them
/// orderly; the semaphore is what makes a mistake survivable.</para>
/// </summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class UiFrameCollection
{
    public const string Name = "ui-frame-rail";
}
