using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Nodes;
using Fdp.Core;
using Fdp.Toolkit.Blueprints;
using Fdp.Toolkit.Blueprints.Components;
using Hrot.Diagnostics.Breakpoints;

namespace Hrot.Editor.DebugApi
{
    /// <summary>
    /// <b>Group O — variable addressing (MX1).</b> "The watch, over HTTP": address a variable by the
    /// same tuple a watch row uses — <c>(entity, assetId, variablePath)</c> — instead of by raw
    /// component and byte offset.
    ///
    /// <para><b>Everything here ROUTES; nothing here re-implements.</b> The reads come from
    /// <c>IBlueprintDebugSession.CaptureLiveState</c> (which hands back DECODED field values), the
    /// address from <c>ResolveWorkingStateField</c>, the staging from
    /// <c>TryWriteWorkingStateField</c>, the pending (yellow) state from
    /// <c>IStagedWrites.TryGetPending</c>, the byte image from <c>ComponentBytes</c>, and the
    /// entity→asset discovery from <c>BlueprintTierSummary</c>. ⛔ A second resolver would be worse
    /// than untested: the API could report a value the editor's own panel never staged.</para>
    ///
    /// <para>⚠ <b>Consumes the variable model, never changes it.</b> Every type named above is read
    /// through its existing public seam; no file of the frozen variable/Details area is touched.</para>
    /// </summary>
    public sealed partial class DebugApiService
    {
        // ── discovery ─────────────────────────────────────────────────────────

        /// <summary>
        /// The blueprints actually attached to an entity, read from its blackboard slot table — the
        /// same scan the Entity Inspector uses. This is what makes the endpoints usable by an agent
        /// that knows an entity id and nothing else: without it, every call would need an asset Guid
        /// nobody can guess.
        /// </summary>
        private List<SlotSummary> AttachedBlueprints(Entity entity)
        {
            var slots = new List<SlotSummary>();
            if (_blueprintRegistry is null) return slots;

            unsafe
            {
                if (_world.HasComponent<BlueprintBlackboard1024>(entity))
                {
                    ref var bb = ref _world.GetComponentRW<BlueprintBlackboard1024>(entity);
                    BlueprintTierSummary.AppendSlots(
                        (byte*)Unsafe.AsPointer(ref Unsafe.As<BlueprintBlackboard1024, byte>(ref bb)),
                        _blueprintRegistry, slots);
                }
                if (_world.HasComponent<BlueprintBlackboard4096>(entity))
                {
                    ref var bb = ref _world.GetComponentRW<BlueprintBlackboard4096>(entity);
                    BlueprintTierSummary.AppendSlots(
                        (byte*)Unsafe.AsPointer(ref Unsafe.As<BlueprintBlackboard4096, byte>(ref bb)),
                        _blueprintRegistry, slots);
                }
                if (_world.HasComponent<BlueprintBlackboard16384>(entity))
                {
                    ref var bb = ref _world.GetComponentRW<BlueprintBlackboard16384>(entity);
                    BlueprintTierSummary.AppendSlots(
                        (byte*)Unsafe.AsPointer(ref Unsafe.As<BlueprintBlackboard16384, byte>(ref bb)),
                        _blueprintRegistry, slots);
                }
            }
            return slots;
        }

        /// <summary>
        /// Resolves the <c>asset</c> query parameter against what the entity actually carries. Accepts
        /// the asset Guid or the blueprint NAME, and — when the entity carries exactly one blueprint —
        /// omitting it entirely, which is the common case and the one an agent reaches for first.
        /// </summary>
        private bool TryResolveAsset(
            Entity entity, string? asset, out SlotSummary slot, out string error)
        {
            slot  = default;
            error = "";

            var attached = AttachedBlueprints(entity);
            if (attached.Count == 0)
            {
                error = _blueprintRegistry is null
                    ? "This editor has no blueprint registry wired into the debug API, so variables cannot be addressed."
                    : "This entity carries no blueprint blackboard, so it has no blueprint variables.";
                return false;
            }

            if (string.IsNullOrWhiteSpace(asset))
            {
                if (attached.Count == 1) { slot = attached[0]; return true; }
                error = $"This entity carries {attached.Count} blueprints "
                      + $"({string.Join(", ", attached.ConvertAll(s => s.Name))}); "
                      + "name one with ?asset=<name|assetId>.";
                return false;
            }

            if (Guid.TryParse(asset, out var assetId))
            {
                foreach (var candidate in attached)
                    if (candidate.AssetId == assetId) { slot = candidate; return true; }
                error = $"Blueprint asset '{asset}' is not attached to this entity. "
                      + $"Attached: {string.Join(", ", attached.ConvertAll(s => s.Name))}.";
                return false;
            }

            foreach (var candidate in attached)
                if (string.Equals(candidate.Name, asset, StringComparison.OrdinalIgnoreCase))
                { slot = candidate; return true; }

            error = $"No blueprint named '{asset}' is attached to this entity. "
                  + $"Attached: {string.Join(", ", attached.ConvertAll(s => s.Name))}.";
            return false;
        }

        // ── reads ─────────────────────────────────────────────────────────────

        /// <summary>
        /// GET /entities/{networkId}/variables?asset= — every variable of the entity's blueprint, with
        /// its live value and whether a staged write is still pending on it (the watch row's yellow).
        /// Must run on the main thread.
        /// </summary>
        public (JsonNode? result, string? error, string? hintCategory) GetEntityVariables(
            long networkId, string? asset)
        {
            if (!_entityMap.TryGetEntity(networkId, out var entity))
                return (null, $"Entity {networkId} not found.", DebugApiHints.Entity);

            if (_blueprintSession is null)
                return (null, "No blueprint debug session is available in this editor.", DebugApiHints.Variable);

            if (!TryResolveAsset(entity, asset, out var slot, out var assetError))
                return (null, assetError, DebugApiHints.Variable);

            var snapshot = _blueprintSession.CaptureLiveState(entity, slot.AssetId);
            if (snapshot is null)
                return (null,
                    $"No live state for blueprint '{slot.Name}' on entity {networkId} — "
                    + "the blueprint may not be compiled into this run.",
                    DebugApiHints.Variable);

            var variables = new JsonArray();
            foreach (var field in snapshot.FieldValues)
                variables.Add(DescribeVariable(entity, slot, field.Key, field.Value));

            return (new JsonObject
            {
                ["networkId"] = networkId,
                ["asset"]     = slot.Name,
                ["assetId"]   = slot.AssetId.ToString("D"),
                ["dispatch"]  = snapshot.Dispatch.ToString(),
                ["variables"] = variables,
            }, null, null);
        }

        /// <summary>
        /// GET /entities/{networkId}/variable?asset=&amp;path= — one variable, its live value, and its
        /// pending (staged-but-not-yet-applied) value if a write is queued.
        /// Must run on the main thread.
        /// </summary>
        public (JsonNode? result, string? error, string? hintCategory) GetEntityVariable(
            long networkId, string? asset, string? path)
        {
            if (string.IsNullOrWhiteSpace(path))
                return (null, "path is required — the variable's name.", DebugApiHints.Variable);

            if (!_entityMap.TryGetEntity(networkId, out var entity))
                return (null, $"Entity {networkId} not found.", DebugApiHints.Entity);

            if (_blueprintSession is null)
                return (null, "No blueprint debug session is available in this editor.", DebugApiHints.Variable);

            if (!TryResolveAsset(entity, asset, out var slot, out var assetError))
                return (null, assetError, DebugApiHints.Variable);

            var snapshot = _blueprintSession.CaptureLiveState(entity, slot.AssetId);
            if (snapshot is null)
                return (null,
                    $"No live state for blueprint '{slot.Name}' on entity {networkId}.",
                    DebugApiHints.Variable);

            if (!snapshot.FieldValues.TryGetValue(path!, out var value))
                return (null,
                    $"Blueprint '{slot.Name}' has no live variable '{path}'. "
                    + $"List them with GET /entities/{networkId}/variables?asset={slot.Name}.",
                    DebugApiHints.Variable);

            var dto = DescribeVariable(entity, slot, path!, value);
            dto["networkId"] = networkId;
            dto["asset"]     = slot.Name;
            dto["assetId"]   = slot.AssetId.ToString("D");
            return (dto, null, null);
        }

        /// <summary>
        /// One variable as the API reports it: name, CLR type, live value, and the pending staged
        /// value when one is queued.
        /// </summary>
        /// <remarks>
        /// <c>pending</c> is the machine half of the editor's yellow: true means a staged write for
        /// this exact address has not been drained yet, so <c>value</c> is still the OLD number. It is
        /// read through the same <c>IStagedWrites</c> queue the panel paints from, at the same
        /// address the write staged at — anything else could yellow a row nobody wrote.
        /// </remarks>
        private JsonObject DescribeVariable(Entity entity, SlotSummary slot, string path, object? value)
        {
            var dto = new JsonObject
            {
                ["path"]  = path,
                ["type"]  = value?.GetType().Name ?? "unknown",
                ["value"] = ToJson(value),
            };

            var address = ResolveVariableAddress(entity, slot.AssetId, path);
            if (address is null)
            {
                // Readable but not addressable: the dispatch kind's layout is not one the resolver
                // maps (only AiPrimitive and Instance are), so it can be neither staged nor yellowed.
                dto["writable"] = false;
                dto["pending"]  = false;
                return dto;
            }

            dto["writable"] = true;
            dto["pending"]  = false;

            if (_bpManager is Fdp.ModuleHost.Abstractions.IStagedWrites staged
                && value is not null
                && staged.TryGetPending(
                       entity,
                       ComponentTypeRegistry.GetId(address.ComponentType),
                       address.ComponentOffsetBytes,
                       out var pendingBytes)
                && pendingBytes.Length == address.SizeBytes)
            {
                dto["pending"]      = true;
                dto["pendingValue"] = ToJson(DecodeBytes(pendingBytes, value.GetType()));
            }

            return dto;
        }

        // ── the write ─────────────────────────────────────────────────────────

        /// <summary>
        /// POST /entities/{networkId}/variable {asset, path, value} — STAGE a write through the same
        /// path the Details editor uses. The value is applied by the kernel's drain at the next
        /// advancing tick, so the read reports <c>pending: true</c> until then.
        /// Must run on the main thread.
        /// </summary>
        /// <remarks>
        /// Running is not a reason to refuse, it is a reason to stage (R-126) — so there is no
        /// run-state gate here. The refusals that remain are all data-shaped: no entity, no session,
        /// a name that does not resolve, a value that will not convert, a width that does not match.
        /// The width check is the corruption gate (Q32 §2.1): a payload wider than the field overruns
        /// its neighbour on a blackboard three subsystems share.
        /// </remarks>
        public (JsonNode? result, string? error, string? hintCategory) StageEntityVariable(
            long networkId, string? asset, string? path, JsonNode? value)
        {
            if (string.IsNullOrWhiteSpace(path))
                return (null, "path is required — the variable's name.", DebugApiHints.Variable);
            if (value is null)
                return (null, "value is required.", DebugApiHints.Variable);

            if (!_entityMap.TryGetEntity(networkId, out var entity))
                return (null, $"Entity {networkId} not found.", DebugApiHints.Entity);

            if (_blueprintSession is null)
                return (null, "No blueprint debug session is available in this editor.", DebugApiHints.Variable);

            if (!TryResolveAsset(entity, asset, out var slot, out var assetError))
                return (null, assetError, DebugApiHints.Variable);

            // The CURRENT value is what names the field's type — the snapshot hands back decoded
            // values, so their runtime type IS the field's. That keeps the API free of a second
            // layout table it would have to keep in step with the compiler's.
            var snapshot = _blueprintSession.CaptureLiveState(entity, slot.AssetId);
            object? current = null;
            if (snapshot is null || !snapshot.FieldValues.TryGetValue(path!, out current) || current is null)
                return (null,
                    $"Blueprint '{slot.Name}' has no live variable '{path}' to write. "
                    + $"List them with GET /entities/{networkId}/variables?asset={slot.Name}.",
                    DebugApiHints.Variable);

            var address = ResolveVariableAddress(entity, slot.AssetId, path!);
            if (address is null)
                return (null,
                    $"Variable '{path}' has no live address on this entity — its blueprint's dispatch "
                    + "kind has no staged-write layout, or its compiled layout is out of date.",
                    DebugApiHints.Variable);

            var fieldType = current.GetType();
            object? converted;
            try
            {
                converted = JsonSerializer.Deserialize(value.ToJsonString(), fieldType, DebugApiPatchOptions);
            }
            catch (Exception ex)
            {
                return (null,
                    $"Cannot read {value.ToJsonString()} as {fieldType.Name} for variable '{path}': {ex.Message}",
                    DebugApiHints.Variable);
            }
            if (converted is null)
                return (null, $"Value for '{path}' converted to null, which cannot be staged.", DebugApiHints.Variable);

            var bytes = ComponentBytes.Of(converted, ComponentBytes.SizeOf(fieldType));
            if (bytes.Length != address.SizeBytes)
                return (null,
                    $"Internal size mismatch staging '{path}': {bytes.Length} bytes for a "
                    + $"{address.SizeBytes}-byte field. Refused rather than risk the neighbouring value.",
                    DebugApiHints.Variable);

            bool staged = _blueprintSession.TryWriteWorkingStateField(
                entity, address.ComponentType, address.ComponentOffsetBytes, bytes);

            if (!staged)
                return (null,
                    "This editor has no staged-write target, so the edit has nowhere to go. "
                    + "That is a missing capability on this host, not a property of the variable.",
                    DebugApiHints.Variable);

            // ⚠ The drain is gated on the debugger not being REWOUND, so a write staged while stopped
            //   at a breakpoint is queued and goes nowhere until someone resumes. Reporting
            //   `staged: true` and nothing else would be the silent-discard the pending flag exists to
            //   prevent — so the reply says whether this write can actually land. (MX-009.)
            bool stopped = _bpManager?.IsPaused ?? false;

            var result = new JsonObject
            {
                ["networkId"] = networkId,
                ["asset"]     = slot.Name,
                ["assetId"]   = slot.AssetId.ToString("D"),
                ["path"]      = path,
                ["staged"]    = true,
                ["pending"]   = true,
                ["willDrain"] = !stopped,
                ["note"]      = stopped
                    ? "Staged, but the debugger is stopped at a breakpoint and the drain is gated on "
                    + "resuming — POST /breakpoints/continue, or this write stays pending forever."
                    : "Staged. It lands on the next advancing tick; until then the read reports the "
                    + "old value with pending: true.",
            };
            return (result, null, null);
        }

        // ── shared plumbing ───────────────────────────────────────────────────

        /// <summary>
        /// NAME → (component, component-absolute offset, size), through the session's own resolver —
        /// the SAME address the write stages at and the pending read asks about. Null means the
        /// variable has no live address (unmapped dispatch kind, unattached blueprint, stale layout).
        /// </summary>
        private Hrot.Blueprints.Core.Debug.WorkingStateFieldRef? ResolveVariableAddress(
            Entity entity, Guid assetId, string path)
            => _blueprintSession?.ResolveWorkingStateField(entity, assetId, path);

        /// <summary>
        /// The inverse of <see cref="ComponentBytes.Of"/> for a staged payload: reads the managed byte
        /// image back into a boxed value so a pending write can be REPORTED, not just flagged.
        /// </summary>
        private static object? DecodeBytes(byte[] bytes, Type type)
        {
            try
            {
                unsafe
                {
                    fixed (byte* src = bytes)
                        return System.Runtime.InteropServices.Marshal.PtrToStructure((IntPtr)src, type);
                }
            }
            catch
            {
                // A type whose marshalled layout differs from its managed one (bool is the classic)
                // would decode wrongly; saying nothing beats reporting a wrong number.
                return null;
            }
        }

        /// <summary>Boxed value → JSON, through the same options the rest of the API serializes with.</summary>
        private static JsonNode? ToJson(object? value)
        {
            if (value is null) return null;
            try
            {
                return JsonSerializer.SerializeToNode(value, value.GetType(), DebugApiDumpOptions);
            }
            catch
            {
                return JsonValue.Create(value.ToString());
            }
        }
    }
}
