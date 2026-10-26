using System;
using System.Collections.Generic;
using System.Text.Json.Nodes;
using Fdp.Core;
using Fdp.Toolkit.Diagnostics;
using Fdp.Toolkit.ReplayBrowser;
using Fdp.Toolkit.Scenario;
using Hrot.Editor.DebugApi;
using Hrot.UI.Common.Map3D.Director;

namespace Hrot.Editor.Director
{
    /// <summary>
    /// ⭐ <c>CE-4045</c> H1a (docs/DESIGN_Scenario_Regression.md §10-1, R-4085) — a finished live run's recording, played forward by the
    /// product's own player, read by the verifier. ⭐ An interface so the rails compare with no recording file.
    /// </summary>
    public interface IRecordedRun : IDisposable
    {
        /// <summary>Plays forward to the FIRST recorded frame whose <c>GlobalTime.FrameNumber</c> is at least <paramref name="simFrame"/>
        /// (the recording keeps every rendered frame, paused ones too — CE-4053 — so a sim frame is found by its own number, never by
        /// wall time). False when the recording ends first or has no such sim frame (it skipped it).</summary>
        bool AdvanceToSimFrame(long simFrame);

        /// <summary>The sim frame the playback stands at (−1 before the first frame, or with no clock).</summary>
        long SimFrame { get; }

        /// <summary>The playback world, as a verify world (read only).</summary>
        IVerifyWorld World { get; }
    }

    /// <summary>
    /// ⭐ H1a — <see cref="ReplayBrowserContext"/> (→ <c>PlaybackController</c> / <c>PlaybackSystem.ApplyFrame</c>, the SAME player the
    /// product's <c>ReplayModule</c> drives) over the file the live run wrote. ⚠ Runs in the process that recorded it, so every component
    /// type's assembly is loaded (CE-4050 cannot bite).
    /// </summary>
    public sealed class RecordedRun : IRecordedRun
    {
        private readonly ReplayBrowserContext _ctx = new();
        private readonly RecordedRunWorld _world;
        private bool _started;

        /// <param name="serializer">The editor's scenario serializer: the dump is the one <c>GET /entities/{id}</c> writes (CE-171).</param>
        /// <param name="behaviourName">A behaviour's name by its hash (the <c>task</c> cue).</param>
        public RecordedRun(string path, ScenarioSerializer? serializer, Func<int, string?> behaviourName)
        {
            _ctx.LoadRecording(path);
            if (_ctx.Playback is null || _ctx.Playback.TotalFrames == 0)
            {
                _ctx.Dispose();
                throw new InvalidOperationException($"the recording {path} could not be played (no frames, or it failed to load — see the log)");
            }
            _world = new RecordedRunWorld(_ctx.SandboxRepo, serializer, behaviourName);
        }

        public int TotalFrames => _ctx.Playback?.TotalFrames ?? 0;

        public long SimFrame => _ctx.SandboxRepo.HasSingletonUnmanaged<GlobalTime>() ? _ctx.SandboxRepo.GetSingletonUnmanaged<GlobalTime>().FrameNumber : -1;

        public IVerifyWorld World => _world;

        public bool AdvanceToSimFrame(long simFrame)
        {
            if (!_started) { _ctx.SeekToFrame(0, suppressHistory: true); _started = true; }
            while (SimFrame < simFrame)
                if (!_ctx.StepForward(suppressHistory: true)) return false;
            return SimFrame == simFrame;
        }

        public void Dispose() => _ctx.Dispose();
    }

    /// <summary>
    /// ⭐ H1a — the playback world as a verify world: names through <see cref="VerifyWorldNames"/> (as the editor's), components through the
    /// SAME extraction and dump options as <c>GET /entities/{id}</c> — so a recorded snapshot and a live one differ only where the worlds do.
    /// ⛔ Read only: a recording is not edited, and its shots and blasts are the timeline's Combat lanes (RH-5), not side stores.
    /// </summary>
    internal sealed class RecordedRunWorld : IVerifyWorld
    {
        private readonly EntityRepository _repo;
        private readonly EntityStateExtractionService _extraction;
        private readonly Func<int, string?> _behaviourName;

        public RecordedRunWorld(EntityRepository repo, ScenarioSerializer? serializer, Func<int, string?> behaviourName)
        {
            _repo = repo ?? throw new ArgumentNullException(nameof(repo));
            _behaviourName = behaviourName ?? throw new ArgumentNullException(nameof(behaviourName));
            _extraction = new EntityStateExtractionService(repo, null, serializer);
            Units = new WorldUnits(() => _repo);
        }

        public EntityRepository? World => _repo;
        public IShotWorld Units { get; }
        public IReadOnlyList<(long Id, string Name)> Named() => VerifyWorldNames.Named(_repo);
        public string? NameOfEntityIndex(int index) => VerifyWorldNames.NameOfEntityIndex(_repo, index);
        public string? BehaviourName(int hash) => _behaviourName(hash);

        public JsonObject? Components(long networkId)
            => _extraction.ExtractEntities(new List<long> { networkId }) is { Count: > 0 } dumps
                ? DebugApiService.DumpToJsonNode(dumps[0])["Components"] as JsonObject
                : null;

        public JsonArray Shots() => new();
        public JsonArray Blasts() => new();
        public string? SetComponent(long networkId, string component, JsonObject patch) => "a recording is read only";
    }
}
