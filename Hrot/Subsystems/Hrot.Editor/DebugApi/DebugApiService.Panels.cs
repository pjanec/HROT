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
        public (JsonNode? result, string? error, string? hintCategory) GetGizmoFrame(int max = 500)
        {
            // ⭐⭐ BP-487 — `_gizmoFeed`, NOT `_primitiveBuffer`: on a cluster host the buffer belongs to the
            //    ACTIVE PERSPECTIVE's subsystem, not to the API service. See its remarks.
            var buffer = _gizmoFeed;
            if (buffer is null)
                return (null, "This host has no debug primitive buffer for the active perspective, so there "
                            + "is no gizmo feed. Check GET /capabilities for panels.gizmo — ExCon draws no "
                            + "gizmos, so its perspective legitimately has none.",
                        DebugApiHints.Panel);

            var frame = buffer.GetFrame();
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
