using System.Collections.Generic;
using System.Text.Json.Nodes;
using Hrot.UI.Common.Map3D;
using Hrot.UI.Common.Map3D.Director;

namespace Hrot.Editor.Director
{
    /// <summary>
    /// ⭐ <c>CE-4061</c> (docs/DESIGN_Director_Timeline_Editor.md §11h, CR-1) — what an <see cref="IRunExtension"/> runs instead of a take
    /// (<see cref="PlayRequest.Plan"/>). The runner knows no kind of plan; the editor's verifier is one (<c>VerifyOptions</c>).
    /// </summary>
    public abstract record RunPlan;

    /// <summary>⭐ <c>CE-4061</c> — what an extension judged an item to be (<see cref="ItemReport.Result"/>); written into the report under
    /// <see cref="Kind"/>.</summary>
    public interface IRunResult
    {
        string Kind { get; }
        bool Passed { get; }
        JsonObject ToJson();
    }

    /// <summary>⭐ <c>CE-4061</c> — the take an extension's item plays (no camera when it is not a film) and the sim time its run time counts from.</summary>
    public sealed record RunStart(Take Take, double T0);

    /// <summary>
    /// ⭐ <c>CE-4061</c> — what an extension may read and ask of the <see cref="DirectorRunner"/> it is attached to. The runner gives itself:
    /// one item at a time, main thread only.
    /// </summary>
    public interface IRunContext
    {
        IDirectorHost Host { get; }
        WorldUnits Units { get; }
        PlayRequest Request { get; }
        ReelItem Item { get; }
        ItemReport Report { get; }
        /// <summary>Run time: the host's sim time less the item's start.</summary>
        double Rel { get; }
        /// <summary>The item fails with <paramref name="why"/> and the run goes on to the next item.</summary>
        void Fail(string why);
        /// <summary>The extension's item is judged: release it, then the next item, or the end.</summary>
        void EndItem();
        /// <summary>The runner hands its frames to <see cref="IRunExtension.Tick"/> until <see cref="ResumePlaying"/> or <see cref="EndItem"/>.</summary>
        void EnterExtensionPhase();
        /// <summary>Back to stepping the item (its clock restarts).</summary>
        void ResumePlaying();
    }

    /// <summary>
    /// ⭐ <c>CE-4061</c> (§11h) — the ONE seam through which a host adds a kind of run to the film runner: the editor's scenario verifier
    /// (checks, golden, R1, the replay and review checks). The runner calls it at fixed points of an item and owns everything else (the
    /// load, the preview, the stepping rule, the camera, capture). ⛔ The runner names no verify type — so it can be shared (P1).
    /// </summary>
    public interface IRunExtension
    {
        /// <summary>Once, when the runner is built.</summary>
        void Attach(IRunContext context);

        /// <summary>The name a status shows for <see cref="DirectorMode.Plan"/> (<c>"Verify"</c>).</summary>
        string ModeName { get; }

        /// <summary>The name a status shows while the runner is in <see cref="DirectorPhase.Extension"/>.</summary>
        string PhaseName { get; }

        /// <summary>Why <paramref name="request"/> cannot run (empty: it can) — in place of loading a take for each item.</summary>
        IReadOnlyList<string> Validate(PlayRequest request);

        /// <summary>The item loads LIVE (it records) instead of into the preview.</summary>
        bool LoadsLive(PlayRequest request);

        /// <summary>Before each item's load.</summary>
        void BeforeLoad();

        /// <summary>A live load (<see cref="LoadsLive"/>); <see cref="IsLoaded"/> says when it is there.</summary>
        void Load(string scenario);
        bool IsLoaded { get; }

        /// <summary>Each frame once the item is loaded and paused, until it returns the take to play (null: not yet, or it failed).</summary>
        RunStart? BeginItem();

        /// <summary>Each playing frame, after the anchors are observed.</summary>
        void OnFrame(double rel);

        /// <summary>Each playing frame before the step: true takes no step this frame (the extension may have taken the frames over).</summary>
        bool HoldsStep(double rel, long retired, long issued);

        /// <summary>Just before the frame's ONE step.</summary>
        void BeforeStep(double rel);

        /// <summary>The item's run is over (still <c>playing</c>): judge it. True when the extension took the end over — it has called, or
        /// will call, <see cref="IRunContext.EndItem"/>.</summary>
        bool Finish(ItemReport report);

        /// <summary>Each frame of <see cref="DirectorPhase.Extension"/>.</summary>
        void Tick();

        /// <summary>A new item begins: forget the last.</summary>
        void Reset();

        /// <summary>The run stops (or failed): release what is open.</summary>
        void Stop();
    }
}
