using System;
using System.Numerics;
using System.Text.Json.Nodes;
using Fdp.Core;
using Fdp.Toolkit.Behavior.Components;
using Fdp.Toolkit.Blueprints;
using Fdp.Toolkit.Blueprints.Events;

namespace Hrot.Editor.DebugApi
{
    /// <summary>
    /// <b>Group Q — blueprint hot-attach (MX2)</b> and <b>Group R — the entity state dump (MX3)</b>.
    ///
    /// <para>Both are deliberately thin: Q publishes the lifecycle events the runtime already consumes,
    /// and R re-reads components <c>GET /entities/{id}</c> already returns. Neither owns any semantics —
    /// what they buy is that an agent can say "run this blueprint" and "where is it" without knowing the
    /// event ids or digging through component JSON.</para>
    /// </summary>
    public sealed partial class DebugApiService
    {
        // ── Group Q — blueprint hot-attach ────────────────────────────────────

        /// <summary>
        /// GET /blueprints — every blueprint this editor has compiled, by name.
        /// </summary>
        /// <remarks>
        /// Attach takes a NAME, so the names have to be discoverable: without this an agent would have
        /// to guess, and the attach endpoint's own error is the wrong place to learn a vocabulary.
        /// </remarks>
        public (JsonNode? result, string? error, string? hintCategory) GetBlueprints()
        {
            if (_blueprintRegistry is null)
                return (null, "This editor has no blueprint registry wired into the debug API.",
                        DebugApiHints.Blueprint);

            var items = new JsonArray();
            foreach (var (id, def) in _blueprintRegistry.GetAll())
            {
                if (def is null) continue;
                items.Add(new JsonObject
                {
                    ["blueprintId"] = id,
                    ["name"]        = def.Name,
                    ["assetId"]     = def.AssetId.ToString("D"),
                    ["kind"]        = def.Kind.ToString(),
                    ["stateSize"]   = def.StateSize,
                    // Only an Instance blueprint can be attached to an entity — saying so here saves a
                    // round trip through a refusal.
                    ["attachable"]  = def.Kind == BlueprintDispatchKind.Instance,
                });
            }

            return (new JsonObject { ["count"] = items.Count, ["blueprints"] = items }, null, null);
        }

        /// <summary>
        /// POST /entities/{networkId}/attach-blueprint {blueprint, paramsJson?} — attach an Instance
        /// blueprint to a running entity. Must run on the main thread.
        /// </summary>
        /// <remarks>
        /// Publishes the event the runtime already consumes (<c>BlueprintEventIngressSystem</c>, Input
        /// phase) rather than reaching into the blackboard: the attach allocates a slot, may promote the
        /// blackboard tier, and runs the params pipeline — all of which the ingress system owns.
        /// <para>It therefore takes effect on the NEXT tick, not on this response, and the reply says so.</para>
        /// </remarks>
        public (JsonNode? result, string? error, string? hintCategory) AttachBlueprint(
            long networkId, string? blueprint, string? paramsJson)
        {
            if (!TryResolveBlueprint(networkId, blueprint, out var entity, out int blueprintId,
                                     out var def, out var error, out var hint))
                return (null, error, hint);

            if (def!.Kind != BlueprintDispatchKind.Instance)
                return (null,
                    $"Blueprint '{def.Name}' is {def.Kind}-dispatch and cannot be attached to an entity — "
                    + "only Instance blueprints occupy a slot. List attachable ones with GET /blueprints.",
                    DebugApiHints.Blueprint);

            _world.Bus.PublishManaged(new AttachInstanceBlueprintEvent
            {
                Entity      = entity,
                BlueprintId = blueprintId,
                ParamsJson  = string.IsNullOrWhiteSpace(paramsJson) ? null : paramsJson,
            });

            return (new JsonObject
            {
                ["networkId"]   = networkId,
                ["blueprint"]   = def.Name,
                ["blueprintId"] = blueprintId,
                ["attached"]    = true,
                ["note"]        = "Queued. The ingress system applies it on the next tick; read it back "
                                + $"with GET /entities/{networkId}/variables.",
            }, null, null);
        }

        /// <summary>
        /// POST /entities/{networkId}/detach-blueprint {blueprint} — detach an Instance blueprint.
        /// Must run on the main thread.
        /// </summary>
        public (JsonNode? result, string? error, string? hintCategory) DetachBlueprint(
            long networkId, string? blueprint)
        {
            if (!TryResolveBlueprint(networkId, blueprint, out var entity, out int blueprintId,
                                     out var def, out var error, out var hint))
                return (null, error, hint);

            _world.Bus.Publish(new RemoveInstanceBlueprintEvent
            {
                Entity      = entity,
                BlueprintId = blueprintId,
            });

            return (new JsonObject
            {
                ["networkId"]   = networkId,
                ["blueprint"]   = def!.Name,
                ["blueprintId"] = blueprintId,
                ["detached"]    = true,
                ["note"]        = "Queued. The ingress system applies it on the next tick.",
            }, null, null);
        }

        /// <summary>
        /// Resolves the entity and the blueprint together — by name, by asset Guid, or by the raw int id.
        /// </summary>
        private bool TryResolveBlueprint(
            long networkId, string? blueprint, out Entity entity, out int blueprintId,
            out BlueprintDefinition? def, out string error, out string? hintCategory)
        {
            entity      = default;
            blueprintId = 0;
            def         = null;
            error       = "";
            hintCategory = null;

            if (!_entityMap.TryGetEntity(networkId, out entity))
            {
                error = $"Entity {networkId} not found.";
                hintCategory = DebugApiHints.Entity;
                return false;
            }

            if (string.IsNullOrWhiteSpace(blueprint))
            {
                error = "blueprint is required — its name, asset Guid, or numeric blueprintId.";
                hintCategory = DebugApiHints.Blueprint;
                return false;
            }

            if (_blueprintRegistry is null)
            {
                error = "This editor has no blueprint registry wired into the debug API.";
                hintCategory = DebugApiHints.Blueprint;
                return false;
            }

            // A Guid or an int addresses the definition directly; anything else is a name.
            if (Guid.TryParse(blueprint, out var assetId))
                blueprintId = BlueprintIdHash.Compute(assetId);
            else if (int.TryParse(blueprint, out var numeric))
                blueprintId = numeric;

            if (blueprintId != 0 && _blueprintRegistry.TryGetById(blueprintId, out def) && def != null)
                return true;

            foreach (var (id, candidate) in _blueprintRegistry.GetAll())
            {
                if (candidate is null) continue;
                if (!string.Equals(candidate.Name, blueprint, StringComparison.OrdinalIgnoreCase)) continue;
                blueprintId = id;
                def         = candidate;
                return true;
            }

            error = $"No blueprint '{blueprint}' is compiled into this run. "
                  + "List what exists with GET /blueprints.";
            hintCategory = DebugApiHints.Blueprint;
            return false;
        }

        // ── Group G, completed — resuming after a breakpoint hit ──────────────

        /// <summary>
        /// POST /breakpoints/continue and /breakpoints/step — resume the debugger after a hit.
        /// Must run on the main thread.
        /// </summary>
        /// <remarks>
        /// <b>Why this had to exist.</b> The API could arm a breakpoint and let it fire, but had no way
        /// to resume: only the editor's own UI could. That is worse than an inconvenience, because the
        /// staged-write drain is gated on the debugger NOT being rewound
        /// (<c>ResumeAndDrainSystem</c>) — so once anything hit a breakpoint, every later live variable
        /// write was accepted, queued, and never applied. Deleting the breakpoint does not resume;
        /// these do, and continuing is also what drains the queue.
        /// </remarks>
        public (JsonNode? result, string? error, string? hintCategory) ContinueFromBreakpoint(bool step)
        {
            if (_bpManager is null)
                return (null, "This editor has no data-breakpoint manager, so there is nothing to resume.",
                        DebugApiHints.Breakpoint);

            bool wasPaused = _bpManager.IsPaused;
            if (step) _bpManager.RequestStep();
            else      _bpManager.RequestContinue();

            return (new JsonObject
            {
                ["wasPaused"] = wasPaused,
                ["action"]    = step ? "step" : "continue",
                ["isPaused"]  = _bpManager.IsPaused,
                // Resuming is also what applies anything staged while the debugger was stopped.
                ["note"]      = wasPaused
                    ? "Resumed. Any staged live writes queued while stopped drain from here."
                    : "The debugger was not stopped; nothing to resume.",
            }, null, null);
        }

        // ── Group R — the entity state dump ───────────────────────────────────

        /// <summary>
        /// GET /entities/{networkId}/state — the well-known fields parsed out, so an assertion reads
        /// <c>state.position.x</c> instead of digging through the component dump.
        /// Must run on the main thread.
        /// </summary>
        /// <remarks>
        /// A convenience over <c>GET /entities/{id}</c>, and only that: every value here is read from the
        /// same component the full dump returns. A field whose component the entity does not carry is
        /// OMITTED rather than defaulted — a zero position would be indistinguishable from the origin.
        /// </remarks>
        public (JsonNode? result, string? error, string? hintCategory) GetEntityState(long networkId)
        {
            if (!_entityMap.TryGetEntity(networkId, out var entity))
                return (null, $"Entity {networkId} not found.", DebugApiHints.Entity);

            var state = new JsonObject
            {
                ["networkId"] = networkId,
                ["alive"]     = _world.IsAlive(entity),
            };

            if (_world.HasComponent<SimTransform>(entity))
            {
                ref readonly var transform = ref _world.GetComponentRO<SimTransform>(entity);
                state["position"] = Vec3(transform.Position);
                state["rotation"] = Euler(transform.Rotation);
            }

            if (_world.HasComponent<SimVelocity>(entity))
            {
                ref readonly var velocity = ref _world.GetComponentRO<SimVelocity>(entity);
                state["velocity"] = Vec3(velocity.Linear);
                // The scalar is what a "did it move?" assertion actually wants, and computing it here
                // means every caller computes it the same way.
                state["speed"]    = velocity.Linear.Length();
            }

            if (_world.HasComponent<BehaviorState>(entity))
            {
                ref readonly var behavior = ref _world.GetComponentRO<BehaviorState>(entity);
                var node = new JsonObject
                {
                    ["hash"]      = behavior.ActiveBehaviorHash,
                    ["brainTier"] = behavior.BrainTier,
                };
                if (_behaviorRegistry is not null
                    && _behaviorRegistry.TryGetName(behavior.ActiveBehaviorHash, out var name))
                    node["name"] = name;
                state["behavior"] = node;
            }

            // ⚠ "grounded" is in the design's field list and is NOT here: measured, this engine has no
            // ground-contact component to read it from. Deriving it from the position would be a guess
            // wearing a fact's name — MX-007 records it rather than inventing one.
            return (state, null, null);
        }

        private static JsonObject Euler(Quaternion q)
        {
            // Yaw-pitch-roll in degrees, in SimTransform's own stated convention (Z-up, yaw 0 = east).
            var m = Matrix4x4.CreateFromQuaternion(q);
            float yaw   = MathF.Atan2(m.M12, m.M11);
            float pitch = MathF.Asin(Math.Clamp(-m.M13, -1f, 1f));
            float roll  = MathF.Atan2(m.M23, m.M33);
            const float ToDeg = 180f / MathF.PI;
            return new JsonObject
            {
                ["yawDeg"]   = yaw * ToDeg,
                ["pitchDeg"] = pitch * ToDeg,
                ["rollDeg"]  = roll * ToDeg,
            };
        }
    }
}
