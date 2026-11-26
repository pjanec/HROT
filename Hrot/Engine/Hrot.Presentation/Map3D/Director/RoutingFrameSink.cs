using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Hrot.Common.Infrastructure;
using LinkedFrame = Hrot.Common.Infrastructure.FilmFrame;
using LinkedGraphic = Hrot.Common.Infrastructure.FilmGraphic;   // ⚠ Director.FilmGraphic (a take's graphic) is a different type

namespace Hrot.UI.Common.Map3D.Director
{
    /// <summary>
    /// ⭐⭐ <c>CE-11018</c> (docs/designs/IG/DESIGN_Linked_Client_Director.md §2a–§2c, Q113-B / D-2 / D-3 / D-4) — the ONE frame sink of the director on every host
    /// (<see cref="DirectorPack.Attach"/> builds it; the runner, the stills and the render queue all hold this one): it chooses, at each job's <see cref="Begin"/>, where
    /// the job's SCENE frames go — the node's linked client (<see cref="LinkedClients.Current"/>, when the job's frames are <see cref="FilmFrames.Auto"/> and the client
    /// <see cref="ILinkedClient.CanFilm"/>), else the map (<see cref="FilmFrameSink"/>, as before). A job never switches part way.
    /// <list type="bullet">
    ///   <item>a scene frame for the client goes through the SAME capture mailbox as a map frame: the map's film layer places its camera for the shot and hands back the
    ///     pose it would have rendered (<see cref="FilmCaptureRequest.PoseOnly"/>) — the client gets exactly the map's camera (D-3); a 2-D view is refused with the reason;</item>
    ///   <item>a CARD is always the map's picture (D-4); the client gets a <see cref="FilmFrameKind.Silence"/> frame for each card frame, a <see cref="FilmFrameKind.Repeat"/>
    ///     for each slow-motion repeat — every film frame index reaches it, in order (the calls are chained, so a frame waiting for its pose never overtakes);</item>
    ///   <item>a frame the client FAULTS fails the job (the director's capture wait throws its reason).</item>
    /// </list>
    /// </summary>
    public sealed class RoutingFrameSink : IFrameSink
    {
        /// <summary>The reason a 2-D view cannot be filmed by a linked client.</summary>
        public const string TwoDRefused = "this take shows the 2-D map, which a linked client cannot render — render the job with frames: map";

        private readonly IFrameSink _map;
        private readonly Func<LinkedClients?>? _clients;
        private ILinkedClient? _client;
        private Guid _job;
        private Task _tail = Task.CompletedTask;
        private FilmCamera _lastCamera;
        private double _lastSim;

        /// <param name="map">The node's own map sink (<see cref="FilmFrameSink"/>).</param>
        /// <param name="clients">The node's linked-client registry (<c>HrotNodeContext.LinkedClients</c>), read at each job's Begin; null = a host with none (the map only).</param>
        public RoutingFrameSink(IFrameSink map, Func<LinkedClients?>? clients)
        {
            _map = map ?? throw new ArgumentNullException(nameof(map));
            _clients = clients;
        }

        /// <summary>The registry this sink reads (rails: the host's own, forwarded).</summary>
        public LinkedClients? Clients => _clients?.Invoke();

        /// <summary>The linked client the CURRENT job's scene frames go to; null = the map (or no job).</summary>
        public ILinkedClient? Routed => _client;

        /// <summary>⭐ CE-11067 — where the last job's frames went, in words (see <see cref="IFrameSink.Route"/>).</summary>
        public string? Route { get; private set; }

        public Task Begin(FilmJob job)
        {
            if (job == null) throw new ArgumentNullException(nameof(job));
            _job = job.JobId;
            _tail = Task.CompletedTask;
            var current = _clients?.Invoke()?.Current;
            _client = job.Frames == FilmFrames.Auto && current is { CanFilm: true } c ? c : null;
            Route = _client != null ? $"the linked client '{_client.Name}'"
                : job.Frames != FilmFrames.Auto ? "HROT's map (frames: map)"
                : current != null ? $"HROT's map: the linked client '{current.Name}' cannot film now"
                : "HROT's map (no linked client)";
            return _client != null ? Chain(() => _client.BeginFilm(job)) : _map.Begin(job);
        }

        public Task Frame(string path, int width, int height, int supersample, SubtitleFrame? subtitle) => _map.Frame(path, width, height, supersample, subtitle);

        public Task Frame(string path, int width, int height, int supersample, FilmOverlay overlay) => _map.Frame(path, width, height, supersample, overlay);

        public Task Frame(string path, int width, int height, int supersample, FilmOverlay overlay, FilmFrameContext context)
        {
            var client = _client;
            if (client == null || context.JobId != _job) return _map.Frame(path, width, height, supersample, overlay, context);
            if (FilmStudio.TwoD) return Task.FromException(new InvalidOperationException(TwoDRefused));
            // the map's film layer places the camera for THIS frame (it serves on its next drawn frame) — then the client renders it, after every earlier frame
            var pose = FilmStudio.Capture(new FilmCaptureRequest(path, width, height, supersample, PoseOnly: true));
            float fade = FilmStudio.Fade;   // the dip of THIS frame — read now, not when the chain gets to it
            return Chain(async () =>
            {
                var served = await pose.ConfigureAwait(false);
                _lastCamera = served.Camera;
                _lastSim = served.SimTime;
                await client.Frame(Linked(FilmFrameKind.Scene, context, served.SimTime, served.Camera, width, height, supersample, path, overlay, fade)).ConfigureAwait(false);
            });
        }

        /// <summary>A card is the map's picture, always (D-4).</summary>
        public Task Card(string path, int width, int height, string text) => _map.Card(path, width, height, text);

        public Task Advance(FilmFrameKind kind, FilmFrameContext context)
        {
            var client = _client;
            if (client == null || context.JobId != _job) return _map.Advance(kind, context);
            return Chain(() => client.Frame(Linked(kind, context, _lastSim, kind == FilmFrameKind.Repeat ? _lastCamera : default, 0, 0, 1, "", FilmOverlay.None, 0f)));
        }

        public Task<FilmTrack?> End(FilmJob job, FilmJobOutcome outcome)
        {
            var client = _client;
            _client = null;
            if (client == null || job.JobId != _job) return _map.End(job, outcome);
            var tail = _tail;
            _tail = Task.CompletedTask;
            // after every frame sent (a failed frame does not stop the end: the client is told how the job ended either way)
            return tail.ContinueWith(_ => client.EndFilm(job, outcome), TaskScheduler.Default).Unwrap();
        }

        private Task Chain(Func<Task> next)
        {
            // ⚠ a faulted frame ends the chain: later calls fault with the SAME reason (the job fails once, with the first reason)
            var t = _tail.ContinueWith(prev => prev.IsFaulted ? prev : next(), TaskScheduler.Default).Unwrap();
            _tail = t;
            return t;
        }

        private LinkedFrame Linked(FilmFrameKind kind, FilmFrameContext c, double sim, FilmCamera camera, int width, int height, int supersample, string path, FilmOverlay overlay, float fade)
        {
            FilmSubtitle? sub = overlay.Subtitle is { } s ? new FilmSubtitle(s.Text, (FilmSubtitlePosition)(byte)s.Position, s.Alpha) : null;
            var graphics = overlay.Graphics.Count == 0 ? Array.Empty<LinkedGraphic>() : new LinkedGraphic[overlay.Graphics.Count];
            for (int i = 0; i < graphics.Length; i++)
            {
                var g = overlay.Graphics[i];
                graphics[i] = new LinkedGraphic((FilmGraphicKind)(byte)g.Kind, g.Points, g.Radius, g.Text, g.Colour, g.Alpha);
            }
            return new LinkedFrame(c.JobId, c.Index, kind, sim, c.FilmRate, c.Step, camera, width, height, supersample, path, sub, graphics,
                                   fade, c.LayersOn, c.LayersOff);
        }
    }
}
