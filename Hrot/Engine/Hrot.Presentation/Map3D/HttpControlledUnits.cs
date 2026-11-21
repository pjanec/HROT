using System.Collections.Concurrent;

namespace Hrot.UI.Common.Map3D;

/// <summary>
/// ⭐⭐ <c>CE-1464</c> (docs/DESIGN_Entity_Control.md §8d) — the units this process possessed over HTTP (<c>POST /entities/{id}/possess</c>), by
/// network id. The station still FOLLOWS the replicated <c>PossessedBy</c> (the view goes into first person, §8d), but it SENDS NO control frames
/// for such a unit: the HTTP client drives it with <c>POST …/control</c>. 📌 Both senders count their own <c>seq</c> and the brain keeps the newest
/// (<c>Possession.cs</c>, M-6), so a windowed station sending a frame per rendered frame outran every HTTP frame — the unit never fired.
/// <para>Marked by the HTTP possess, cleared by the HTTP release and when the station leaves the unit (lost, released, switched) — so a unit the
/// operator later possesses through his own UI is his again. Read on the UI thread, written on the main thread (the API's job queue): a
/// concurrent set, no lock.</para>
/// </summary>
public static class HttpControlledUnits
{
    private static readonly ConcurrentDictionary<int, byte> s_units = new();

    /// <summary>The HTTP possess of <paramref name="networkId"/> was accepted: the station stops sending frames for it.</summary>
    public static void Mark(int networkId) => s_units[networkId] = 0;

    /// <summary>The unit is no longer driven over HTTP.</summary>
    public static void Clear(int networkId) => s_units.TryRemove(networkId, out _);

    /// <summary>Is <paramref name="networkId"/> driven over HTTP (⇒ the station sends nothing for it)?</summary>
    public static bool IsMarked(int networkId) => s_units.ContainsKey(networkId);
}
