using System;

namespace Hrot.ExCon.Observer;

/// <summary>
/// ⭐ CE-277(c3) — ExCon's tiny persistable "observer" state: the console's camera vantage point.
///
/// <para>ExCon has NO ECS world, so it has nothing entity-shaped to put in a scenario. This is the minimal
/// REAL state it can save — deliberately an <b>intentionally-incompatible</b> scenario format
/// (<c>$meta.docType = "ExCon.Observer"</c>, not <c>Hrot.Scenario</c>) — so the distributed-save merge routes
/// it to <c>foreign/</c> verbatim and pushes it back to ExCon on load, never trying to merge it into the ECS
/// scenario (§4c). The <see cref="InstanceMarker"/> is unique per process, so a fresh instance can PROVE it
/// restored this state from the file (its marker becomes the saved one) rather than kept its own default.</para>
/// </summary>
public sealed class ExConObserverState
{
    /// <summary>Camera vantage — fake but plausible console state. Distinctive default so it is recognisable in the file.</summary>
    public float CameraX { get; set; } = 111f;
    public float CameraY { get; set; } = 222f;
    public float CameraZ { get; set; } = 333f;

    /// <summary>Unique per PROCESS. Saved into the file; on load a fresh instance's marker becomes the saved one,
    /// which is the value-level proof that the foreign part round-tripped.</summary>
    public string InstanceMarker { get; set; } = Guid.NewGuid().ToString("N").Substring(0, 8);

    /// <summary>False on a fresh instance; set true after ExCon restores this state from a loaded scenario's
    /// foreign slice — the flag-level proof that load actually ran (get_panel shows it).</summary>
    public bool RestoredFromScenario { get; set; }
}
