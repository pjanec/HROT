using Xunit;

namespace Hrot.Blueprints.Tests.Editor;

/// <summary>
/// ⭐⭐⭐ <b>Serialises every test class in THIS assembly that touches the process-global
/// <c>PanelSnapshot</c> singleton.</b>
///
/// <para>⛔⛔ <b>The race is real, not theoretical — it was MEASURED in the AiShared assembly during the
/// `U-obs-5` sweep</b> *(a filtered run of two new rail classes went red)*: xunit runs different test
/// CLASSES in parallel by default, and two classes independently flipping
/// <c>PanelSnapshot.CaptureEnabled</c> interleave — one turns it off in its <c>Dispose</c> while the other
/// is between its <c>Register</c> call and its assertion, so a rail that should see a captured model sees
/// <see langword="null"/>.</para>
///
/// <para>⚠⚠ <b>This assembly's two rail classes had the SAME latent race and had simply not lost the coin
/// toss yet</b> — the Blueprints suite was green at 3920 passing while carrying it. ⇒ ⭐ fixed here
/// deliberately rather than waiting for the flake to appear in a future run and be misread as a real
/// regression. 📌 That misreading is exactly what `B101c` warns about: establish the DIRECTION before
/// adjusting a red rail.</para>
///
/// <para>⭐ Tests WITHIN one class already run sequentially, so a class that resets the singleton in its
/// constructor and <c>Dispose</c> is safe against itself. ⛔ It is only cross-CLASS parallelism that bites.</para>
/// </summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class PanelSnapshotTestCollection
{
    public const string Name = "PanelSnapshot serial (Blueprints)";
}
