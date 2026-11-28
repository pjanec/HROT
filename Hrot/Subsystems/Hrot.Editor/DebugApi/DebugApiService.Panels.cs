using System;
using System.Collections.Generic;
using System.Text.Json.Nodes;
using Fdp.Diagnostics.Contracts.Panels;
using Fdp.Toolkit.Diagnostics.Gizmos;

namespace Hrot.Editor.DebugApi
{
    /// <summary>
    /// <b>Group T — the panel snapshot, read over HTTP (MX9).</b> The UI made machine-readable without
    /// pixels: every instrumented panel builds a whole view-model each frame, renders only from it, and
    /// publishes it to <c>PanelSnapshot</c>; this group is that snapshot's first consumer.
    ///
    /// <para><b>Read-only, and deliberately thin.</b> No logic lives here that the panels do not already
    /// own — a second interpretation of what a panel shows would be a second answer to "what is on
    /// screen", and the wrong one would be wrong invisibly.</para>
    /// </summary>
    public sealed partial class DebugApiService
    {
        // ⭐ S1d-1 — the gizmo dump's frame with every HUD run removed (grown, never shrunk)
        private DebugPrimitive[] _unscopedFrame = Array.Empty<DebugPrimitive>();

        /// <summary>
        /// GET /panels — what is instrumented, what actually published this frame, and the kinds map a
        /// cross-host conformance check groups by.
        /// </summary>
        /// <remarks>
        /// <b>Both lists, always.</b> "Instrumented but silent" and "not instrumented at all" are
        /// different facts and an agent must be able to tell them apart: collapsing them turns "the
        /// assertion found nothing" into "the UI showed nothing", which is the false green the two-set
        /// design exists to prevent.
        /// </remarks>
        public JsonNode GetPanels()
        {
            var registered = new JsonArray();
            var registeredIds = new List<string>(PanelSnapshot.RegisteredPanels);
            registeredIds.Sort(StringComparer.Ordinal);   // deterministic — a diff must not reorder
            foreach (var id in registeredIds) registered.Add(id);

            var captured = new JsonArray();
            var capturedIds = new List<string>(PanelSnapshot.CapturedPanels);
            capturedIds.Sort(StringComparer.Ordinal);
            foreach (var id in capturedIds) captured.Add(id);

            // kind → the live addresses of that kind. Only captured panels can be grouped: a kind is a
            // property of the view-model, and a panel that has not published has not stated one.
            var kinds = new JsonObject();
            foreach (var id in capturedIds)
            {
                var kind = PanelSnapshot.TryGet(id)?.PanelKind;
                if (string.IsNullOrWhiteSpace(kind)) continue;
                if (kinds[kind] is not JsonArray bucket)
                {
                    bucket = new JsonArray();
                    kinds[kind] = bucket;
                }
                bucket.Add(id);
            }

            return new JsonObject
            {
                // Capture is off in production — with it off, `captured` is empty for a reason that has
                // nothing to do with the UI, so the flag is reported rather than left to be inferred.
                ["captureEnabled"] = PanelSnapshot.CaptureEnabled,
                ["registered"]     = registered,
                ["captured"]       = captured,
                ["kinds"]          = kinds,
                // ⭐⭐⭐ CORRECTED 2026-08-23 — THIS FIELD WAS TELLING CALLERS THE OPPOSITE OF THE TRUTH.
                //   ⛔ It said "not cleared per frame", describing the state BEFORE MX-006 landed. 📐
                //      MX-006 built exactly the captured-only clear this text calls unavailable, and
                //      EditorSubsystem.Update calls PanelSnapshot.ClearCaptured() every frame.
                //   ⚠⚠ An API that describes its own semantics wrongly is worse than one that says
                //      nothing: a caller reasons "a stale entry may be a closed window" and writes an
                //      ignore-list for a staleness that cannot occur.
                //   ⭐ The real contract, and the one thing a caller must know: the captured set is a
                //      SINGLE FRAME, and the reader sees the PREVIOUS complete frame (the job queue
                //      drains before the clear — see EditorSubsystem.Update).
                ["staleness"]      = "captured is a single frame: it is cleared at the top of every frame "
                                   + "and refilled as panels draw. An out-of-band reader sees the previous "
                                   + "COMPLETE frame, so act, step a tick, then read.",
            };
        }

        /// <summary>
        /// GET /panels/{panelId} — that panel's dumped view-model.
        /// </summary>
        /// <remarks>
        /// The two failure modes answer differently on purpose: a panel nobody instrumented is a
        /// different problem from one that is instrumented and simply has not drawn (its window is
        /// closed), and only the second is fixed by opening a window.
        /// </remarks>
        public (JsonNode? result, string? error, string? hintCategory) GetPanel(string? panelId)
        {
            if (string.IsNullOrWhiteSpace(panelId))
                return (null, "A panel id is required. List them with GET /panels.", DebugApiHints.Panel);

            // ⭐ CE-11045 (HL-1b) — the map feed's panel (<host>/_gizmo) is published only while the gizmo group runs: reading it is a read of the gizmo feed (same timed reference).
            if (panelId!.EndsWith("/" + Fdp.Diagnostics.Contracts.Panels.GizmoFrameViewModel.Kind, StringComparison.Ordinal)) GizmoLease?.Touch();
            var vm = PanelSnapshot.TryGet(panelId!);
            if (vm is null)
            {
                bool instrumented = PanelSnapshot.RegisteredPanels.Contains(panelId!);
                return (null, instrumented
                    ? $"Panel '{panelId}' is instrumented but has published no model — its window is "
                      + "probably closed, or no frame has drawn it since capture was enabled."
                    : PanelSnapshot.CaptureEnabled
                        ? $"No panel '{panelId}' is instrumented. List what exists with GET /panels."
                        : $"No panel '{panelId}' has published a model, and capture is DISABLED — "
                          + "nothing will publish until it is on.",
                    DebugApiHints.Panel);
            }

            return (new JsonObject
            {
                ["panelId"]   = vm.PanelId,
                ["panelKind"] = vm.PanelKind,
                ["model"]     = vm.Dump(),
            }, null, null);
        }

        /// <summary>
        /// GET /panels/_gizmo — the map/gizmo feed: this frame's debug primitives, the same snapshot one
        /// layer down from the panels.
        /// </summary>
        /// <remarks>
        /// <c>DebugPrimitive</c> is a 64-byte explicit-layout union whose fields OVERLAP by shape, so it
        /// is projected per shape rather than serialized wholesale — a blanket dump would emit whichever
        /// field happened to alias the bytes and read as data.
        /// </remarks>
        /// <summary>⭐ CE-11045 — the headless editor's timed reference on its gizmo gate (null: a windowed editor, or a host whose perspective coordinator holds the gate).</summary>
        public Func<Hrot.Presentation.DebugApi.GizmoReadLease?>? GizmoLeaseProvider { get; set; }
        private Hrot.Presentation.DebugApi.GizmoReadLease? GizmoLease => GizmoLeaseProvider?.Invoke();

        private DebugPrimitive[] _hudBound = Array.Empty<DebugPrimitive>(), _hudUnbound = Array.Empty<DebugPrimitive>();

        /// <summary>
        /// ⭐⭐ Simulated devices S1e-2 (DESIGN_Simulated_Devices §5a, the checks' <c>hud</c> cue) — the HUD run a view bound to <paramref name="source"/> (a
        /// network id) draws in the PUBLISHED frame (<see cref="Hrot.Editor.Director.HudRuns"/> — <c>HudScopes.Keep</c>): its texts, its size; −1 with no gizmo
        /// feed. ⭐ Touches the headless editor's gizmo gate (CE-11045) on every read, so a verifier that reads each sample keeps the projectors drawing.
        /// </summary>
        public int HudRun(long source, List<string> texts)
        {
            if (_gizmoFeed is not { } buffer) return -1;
            GizmoLease?.Touch();
            return Hrot.Editor.Director.HudRuns.Read(buffer.ReaderFrame(), source, texts, ref _hudBound, ref _hudUnbound);
        }

        /// <summary>⭐ S1e-2 — the checks' <c>field</c> cue's <c>memory</c>: the unit's <c>[UnitMemory]</c> struct by name, read on THIS host's brain world.</summary>
        public JsonObject? UnitMemoryOf(int networkId, string type)
            => _entityMap.TryGetEntity(networkId, out var e) ? Hrot.Editor.Director.UnitMemoryJson.Read(_world, e, type) : null;

        private Hrot.Editor.Director.ScriptedStation? _scriptedStation;

        /// <summary>⭐ S1e-2 — this node's station as the checks drive it (<c>do: possess / view / control</c>), on the brain world; possessing through
        /// <see cref="PostPossess"/> (the HTTP possess: the map's own station then sends no frames that would race it, CE-1464).</summary>
        public Hrot.Editor.Director.ScriptedStation ScriptedStation
            => _scriptedStation ??= new Hrot.Editor.Director.ScriptedStation(() => _world, id => PostPossess(id).error);

        public (JsonNode? result, string? error, string? hintCategory) GetGizmoFrame(int max = 500)
        {
            // ⭐⭐ BP-487 — `_gizmoFeed`, NOT `_primitiveBuffer`: on a cluster host the buffer belongs to the
            //    ACTIVE PERSPECTIVE's subsystem, not to the API service. See its remarks.
            var buffer = _gizmoFeed;
            if (buffer is null)
                // ⭐⭐ CE-259am — this used to assert "this host has no debug primitive buffer", which on
                //    --mode replaybrowser was FALSE: the host had one and was filling it every frame, while
                //    the subsystem contributed no provider. The explainer distinguishes the two causes.
                return (null, "No gizmo feed: " + GizmoFeedAbsenceReason(), DebugApiHints.Panel);

            // ⭐ CE-11045 (HL-1) — a HEADLESS editor draws no gizmo nobody reads: this read takes a timed reference (5 s) on the map's gizmo gate (GizmoReadLease).
            //   The FIRST read after an idle spell finds the frame published while the gate was shut (empty); the next pulled tick draws, so a second read returns the
            //   primitives. `drawingStarted` says which case this was. A windowed editor / a cluster perspective has no lease: always drawing / its viewer holds the gate.
            bool drawingStarted = GizmoLease?.Touch() ?? false;
            // ⭐ POC 2 P-6 — the published frame when the kernel ticks on a worker; ⭐ S1d-1 (R3-4) — bound to no source: every HUD run skipped whole
            //   (S1e's `hud` cue reads a run through a view bound to its source, never through this dump)
            var frame = Fdp.Toolkit.Diagnostics.Gizmos.HudScopes.Unscoped(buffer.ReaderFrame(), ref _unscopedFrame);
            var items = new JsonArray();

            int emitted = Math.Min(frame.Length, Math.Max(1, max));
            for (int i = 0; i < emitted; i++)
                items.Add(DescribePrimitive(frame[i]));

            return (new JsonObject
            {
                ["count"]      = frame.Length,
                ["dropped"]    = buffer.DroppedCount,
                // Truncation is REPORTED, never silent: a reader that cannot tell a full frame from a
                // clipped one would take "no more primitives" from an arbitrary cap.
                ["emitted"]    = emitted,
                ["drawingStarted"] = drawingStarted,   // ⭐ CE-11045 — true: this read opened a headless host's gizmo gate; read again for the primitives
                ["truncated"]  = emitted < frame.Length,
                ["primitives"] = items,
            }, null, null);
        }

        /// <summary>One primitive, projected by its shape — see <see cref="GetGizmoFrame"/>'s remarks.</summary>
        private static JsonObject DescribePrimitive(in DebugPrimitive p)
        {
            var node = new JsonObject
            {
                ["shape"] = p.Shape.ToString(),
                ["space"] = p.Space.ToString(),
                ["layer"] = p.DebugLayer,
                ["color"] = $"#{p.Color.R:X2}{p.Color.G:X2}{p.Color.B:X2}{p.Color.A:X2}",
            };

            switch (p.Shape)
            {
                case DebugPrimitiveShape.Line:
                    node["from"] = Vec3(p.LineStart);
                    node["to"]   = Vec3(p.LineEnd);
                    break;

                case DebugPrimitiveShape.Arrow:
                    node["from"] = Vec3(p.ArrowFrom);
                    node["to"]   = Vec3(p.ArrowTo);
                    break;

                case DebugPrimitiveShape.Sphere:
                    node["center"] = Vec3(p.SphereCenter);
                    node["radius"] = p.SphereRadius;
                    break;

                case DebugPrimitiveShape.Box2D:
                    node["center"]   = new JsonObject { ["x"] = p.BoxCenterX, ["y"] = p.BoxCenterY };
                    node["extent"]   = new JsonObject { ["x"] = p.BoxExtentX, ["y"] = p.BoxExtentY };
                    node["angleDeg"] = p.BoxAngleDeg;
                    break;

                case DebugPrimitiveShape.Text:
                    node["at"]   = new JsonObject { ["x"] = p.TextX, ["y"] = p.TextY };
                    node["text"] = p.TextContent.ToString();
                    break;

                case DebugPrimitiveShape.SpatialAnchor:
                    node["networkId"] = p.StructNetworkId;
                    break;

                default:
                    // A shape this projection does not model yet is reported as itself rather than as
                    // aliased bytes — the name is true, and inventing fields would not be.
                    node["note"] = "no field projection for this shape yet";
                    break;
            }

            return node;
        }

        private static JsonObject Vec3(System.Numerics.Vector3 v)
            => new() { ["x"] = v.X, ["y"] = v.Y, ["z"] = v.Z };
    }
}
