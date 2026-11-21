using System;
using Fdp.Core.Logging;

namespace Hrot.Common.Infrastructure;

/// <summary>
/// ⭐ <c>CE-11026</c> (<c>docs/designs/IG/DESIGN_Linked_Client_Director.md</c> §2b, R-11009) — a LINKED CLIENT: a program outside this process that presents the world this node simulates
/// (a viewer connected through an extension) and that the node's loads must wait for. Product-neutral: nothing here names what the client is.
/// <para>This is the world-readiness half; the members for film frames and sound arrive with <c>CE-11018</c>. The two calls are in this order on every load that changes the world:
/// <see cref="BeginWorld"/> at the load's commit, then <see cref="IsWorldReady"/> polled until it says true or reports a failure.</para>
/// </summary>
public interface ILinkedClient
{
    /// <summary>What the client calls itself, for the logs.</summary>
    string Name { get; }

    /// <summary>
    /// The load <paramref name="loadId"/> committed its world: the client's side moves on to a NEW world now (and remembers the id against it), so a poll right after this cannot read the OLD world as ready.
    /// Called once per load, at its commit, on the frame thread.
    /// </summary>
    void BeginWorld(Guid loadId);

    /// <summary>
    /// True when every part of the client that asks to be waited for presents the world of <paramref name="loadId"/> (true at once when none asks, or for an id <see cref="BeginWorld"/> never saw); false before the new world is
    /// even announced. <paramref name="failure"/> is the reason a part failed to load it (a non-null value ends the wait and fails the load with it), <paramref name="step"/> what a part is loading now (for the node's heartbeat).
    /// Polled on the frame thread; cheap.
    /// </summary>
    bool IsWorldReady(Guid loadId, out string? failure, out string? step);

    // ── ⭐ CE-11018 (§2b, §2c) — the film: Begin, every Frame in order, End. All on the frame thread; each call returns at once with a task the director waits on. ──

    /// <summary>
    /// True when the client can render film frames now (connected, its world ready). Read at a job's <see cref="BeginFilm"/> only: a job never changes source part way.
    /// A client that renders no film (only presents the world) returns false — the job's frames stay the map's.
    /// </summary>
    bool CanFilm => false;

    /// <summary>A film job starts (<see cref="FilmJob"/>); the task completes when the client is warmed up for it, or FAULTS with the reason it cannot take it
    /// (the job then fails with that reason).</summary>
    System.Threading.Tasks.Task BeginFilm(FilmJob job) => System.Threading.Tasks.Task.FromException(new NotSupportedException($"{Name} renders no film"));

    /// <summary>One frame of the job (in <see cref="FilmFrame.Index"/> order, none skipped). The task completes when the frame is done (a scene frame's PNG written);
    /// it FAULTS with the reason when the client could not do it — the job fails with that reason.</summary>
    System.Threading.Tasks.Task Frame(FilmFrame frame) => System.Threading.Tasks.Task.FromException(new NotSupportedException($"{Name} renders no film"));

    /// <summary>The job ended (<paramref name="outcome"/>; called once for every <see cref="BeginFilm"/>, a failed or stopped job included). The task's result is the client's
    /// sound track of the job (<see cref="FilmTrack"/>), or null when it made none.</summary>
    System.Threading.Tasks.Task<FilmTrack?> EndFilm(FilmJob job, FilmJobOutcome outcome) => System.Threading.Tasks.Task.FromResult<FilmTrack?>(null);
}

/// <summary>
/// ⭐ <c>CE-11026</c> — the node's registry of its linked client, one per node (<c>HrotNodeContext.LinkedClients</c>, every node gets one by construction). A capability that connects a client
/// <see cref="Offer"/>s it and <see cref="Withdraw"/>s it when it goes away; the load phase reads <see cref="Current"/>. <b>One client at a time:</b> a second <see cref="Offer"/> replaces the first, with a log line
/// (the node waits for the one it was last given). Thread-safe: written by a capability at composition or connection, read on the frame thread; the read is one volatile load and allocates nothing.
/// </summary>
public sealed class LinkedClients
{
    private readonly object _gate = new();
    private volatile ILinkedClient? _current;

    /// <summary>The client the node's loads wait for now; null = none.</summary>
    public ILinkedClient? Current => _current;

    /// <summary>Makes <paramref name="client"/> the node's linked client, replacing (and logging) one already offered.</summary>
    public void Offer(ILinkedClient client)
    {
        ArgumentNullException.ThrowIfNull(client);
        lock (_gate)
        {
            var old = _current;
            if (old != null && !ReferenceEquals(old, client))
                FdpLog<LinkedClients>.Info("[LinkedClient] '{0}' replaces '{1}' as the node's linked client.", client.Name, old.Name);
            _current = client;
        }
    }

    /// <summary>Removes <paramref name="client"/> when it is the current one (a client already replaced is left alone).</summary>
    public void Withdraw(ILinkedClient client)
    {
        lock (_gate)
        {
            if (ReferenceEquals(_current, client)) _current = null;
        }
    }
}
