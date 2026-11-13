using System;
using Fdp.Toolkit.Orchestration;

namespace Hrot.Common.Infrastructure;

/// <summary>
/// ⭐ CE-3446 — the 3-D map's build as the SIM side (the cluster load's map step) reads it on a threaded node: the map is the UI's (it reads A and
/// uploads meshes), so its state rides up as an immutable snapshot (<see cref="StagedPresentation.AddUplink{T}"/>) and <see cref="Request3D"/>
/// goes to the UI thread with the next pull. 📐 Measured: the player's load step polled <c>Map3DBuild.IsBuilt</c> from the sim worker — 17 guard
/// hits (A read by the sim side) in one multiplayer cycle.
/// </summary>
public sealed class StagedMapBuildStatus : IMapBuildStatus
{
    private sealed record Snapshot(bool Present, bool IsBuilt, string? Failure, string Step);
    private static readonly Snapshot NotYet = new(true, false, null, "");

    private readonly StagedPresentation _presentation;
    private readonly Func<IMapBuildStatus?> _ui;
    private readonly Func<Snapshot?> _latest;
    private readonly Action<Fdp.Core.EntityRepository> _request;

    private StagedMapBuildStatus(StagedPresentation presentation, Func<IMapBuildStatus?> ui)
    {
        _presentation = presentation;
        _ui = ui;
        _latest = presentation.AddUplink<Snapshot>(last =>
        {
            var s = ui();
            bool present = s != null, built = s?.IsBuilt ?? false;
            string? failure = s?.Failure; string step = s?.Step ?? "";
            return last is not null && last.Present == present && last.IsBuilt == built && ReferenceEquals(last.Failure, failure) && ReferenceEquals(last.Step, step)
                ? null : new Snapshot(present, built, failure, step);
        });
        _request = _ => _ui()?.Request3D();   // cached: a request allocates nothing
    }

    /// <summary>The sim side's status source over the UI's <paramref name="ui"/>: null when the UI has no map (the load step then waits for none).</summary>
    public static Func<IMapBuildStatus?> Over(StagedPresentation presentation, Func<IMapBuildStatus?> ui)
    {
        var staged = new StagedMapBuildStatus(presentation, ui);
        return () => staged.Current.Present ? staged : null;
    }

    private Snapshot Current => _latest() ?? NotYet;   // before the first UI frame: a map that is not built yet

    public bool IsBuilt => Current.IsBuilt;
    public string? Failure => Current.Failure;
    public string Step => Current.Step;
    public void Request3D() => _presentation.Staging.Post(_request);
}
