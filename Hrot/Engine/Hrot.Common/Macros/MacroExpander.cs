using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;
using Fdp.Core;
using Fdp.Toolkit.Behavior;
using Fdp.Toolkit.NetworkSpawning;
using Fdp.Toolkit.NetworkSpawning.Events;
using Fdp.Toolkit.Replication;
using Fdp.Toolkit.Scenario;
using Hrot.Core.Network;

namespace Hrot.Common.Macros
{
    /// <summary>
    /// ⭐⭐ Macros S3a ④ — THE EXPANDER (docs/DESIGN_Scenario_Macros.md §15, SR-146 / SR-155 / SR-167 / SR-169 / SR-185): turns ONE macro
    /// instance into the ordinary creation requests of every entity it makes, at every nesting level, from the FILES — before anything exists.
    /// <para>
    /// Per macro file: ③ <see cref="ParamBindings"/> paths on the file's entity JSON → the scenario extractor (the ONE file reader) with
    /// two passes — ① ② <see cref="ParamBind"/> BEFORE the file's own references resolve, <see cref="Relocation"/> (authored anchor →
    /// instance anchor) AFTER — then each request is made a macro child: GUID derived <c>macro:&lt;file key&gt;</c> from its instance's
    /// GUID, TRANSIENT, <see cref="MacroOrigin"/>, owned by the instance's owner, its <see cref="ParamBindings"/> dropped (SR-168: only a
    /// transient child loses them). A child that is itself an instance is flattened recursively (its transform already moved — the
    /// transforms COMPOSE) and created <see cref="MacroExpansionState.Done"/>, so the request path never expands it again.
    /// </para>
    /// ⛔ Pure: it publishes nothing. A failure — a missing file, an unknown arg, a cycle, depth over <see cref="MaxDepth"/>, a pinned macro
    /// moved, a reference naming nothing — comes back as the reason, naming the path; nothing is half-built.
    /// </summary>
    public sealed class MacroExpander
    {
        /// <summary>The deepest nesting allowed (SR-149).</summary>
        public const int MaxDepth = 8;

        private readonly IMacroLibrary _library;
        private readonly IScenarioEntityExtractor _extractor;
        private readonly ScenarioSerializer _serializer;
        private readonly ScenarioBehaviorRemapper? _behaviorRemapper;

        public MacroExpander(IMacroLibrary library, IScenarioEntityExtractor extractor, ScenarioSerializer serializer,
                             ScenarioBehaviorRemapper? behaviorRemapper = null)
        {
            _library = library ?? throw new ArgumentNullException(nameof(library));
            _extractor = extractor ?? throw new ArgumentNullException(nameof(extractor));
            _serializer = serializer ?? throw new ArgumentNullException(nameof(serializer));
            _behaviorRemapper = behaviorRemapper;
        }

        /// <summary>The library this expander reads (a probe for the forwarding rail).</summary>
        public IMacroLibrary Library => _library;

        /// <summary>The outcome: every child request (all levels), or the reason nothing may be created.</summary>
        /// <param name="MaxShift">⭐ SR-182 — the top macro's <c>Placement.MaxShift</c> (or its default): the farthest placement may move a child.</param>
        /// <param name="Relocatable">⭐ SR-156 — <c>false</c> for a pinned macro: it stands where it was authored, so it is not placed.</param>
        public sealed record Result(IReadOnlyList<EntityCreationRequest> Children, string? Failure,
                                    float MaxShift = ScenarioHeader.DefaultMaxShift, bool Relocatable = true)
        {
            public bool Ok => Failure is null;
        }

        private sealed class Refused : Exception { public Refused(string m) : base(m) { } }

        /// <param name="instance">The instance's <see cref="MacroInstance"/>.</param>
        /// <param name="instanceNetworkId">The instance entity's network id (its children's <see cref="MacroOrigin.Instance"/>).</param>
        /// <param name="instanceGuid">The instance's stable GUID — every child GUID derives from it (SR-167).</param>
        /// <param name="owner">The instance's owner; every child gets it.</param>
        /// <param name="anchor">The instance's place and heading (its <c>SimTransform</c>, SR-152).</param>
        /// <param name="outside">Where an arg's <c>@Key</c> / GUID resolves — the world OUTSIDE the macro (SR-58); <c>null</c> = args hold no references.</param>
        /// <param name="idAllocator">Fresh network ids for the children (SR-151).</param>
        /// <param name="variant">⭐ macros S3b B2 — the run's variant (<see cref="ScenarioVariants"/>): presence and one-of-N inside the files, salted
        /// by each instance's GUID. 0 = as authored.</param>
        public Result Expand(MacroInstance instance, int instanceNetworkId, Guid instanceGuid, NodeId owner, SimTransform anchor,
                             IRefScope? outside, INetworkIdAllocator idAllocator, int variant = 0)
        {
            var children = new List<EntityCreationRequest>();
            var top = new ScenarioHeader?[1];
            try
            {
                Flatten(instance, instanceNetworkId, instanceGuid, owner, anchor, outside, idAllocator, new List<string>(), children, top, variant);
                return new Result(children, null, top[0]?.EffectiveMaxShift ?? ScenarioHeader.DefaultMaxShift, top[0]?.Relocatable ?? true);
            }
            catch (Refused r) { return new Result(Array.Empty<EntityCreationRequest>(), r.Message); }
            catch (Exception ex) when (ex is InvalidOperationException or System.Text.Json.JsonException or ArgumentException or NotSupportedException)
            { return new Result(Array.Empty<EntityCreationRequest>(), $"macro '{instance.Macro}': {ex.Message}"); }
        }

        private void Flatten(MacroInstance mi, int instanceId, Guid instanceGuid, NodeId owner, SimTransform anchor, IRefScope? outside,
                             INetworkIdAllocator ids, List<string> path, List<EntityCreationRequest> into, ScenarioHeader?[] top, int variant)
        {
            string here = path.Count == 0 ? mi.Macro : string.Join(" / ", path) + " / " + mi.Macro;
            if (path.Contains(mi.Macro, StringComparer.Ordinal))
                throw new Refused($"macro cycle: {here}");
            if (path.Count >= MaxDepth)
                throw new Refused($"{here}: macros nested deeper than {MaxDepth}");
            if (!_library.TryRead(mi.Macro, out var json, out var why))
                throw new Refused(path.Count == 0 ? why : $"{string.Join(" / ", path)}: {why}");

            var dom = JsonNode.Parse(json) as JsonObject ?? throw new Refused($"{here}: the file is not a JSON object");
            var header = ScenarioHeaderJson.Read(dom);
            if (header is null || !header.IsMacro)
                throw new Refused($"{here}: the file is not a macro (its header has no Kind \"macro\")");
            if (path.Count == 0) top[0] = header;

            // ── params (SR-28 / SR-149 / SR-161): the header's defaults ▸ the instance's args, an undeclared arg refused, the file's
            //    ParamBindings applied — ⭐ the ONE routine a scenario load uses too (ScenarioParams, SR-41)
            ScenarioParams.Prepared bound;
            try { bound = ScenarioParams.Prepare(dom, header, mi.ArgsJson, outside is null ? null : new RefResolve(outside), $"macro '{here}'", variant, instanceGuid); }
            catch (ScenarioParams.ArgsRefused r) { throw new Refused(r.Message); }
            var source = bound.Source;

            // ── relocation (SR-127 / SR-152 / SR-156): authored anchor → instance anchor; a pinned macro must stand at its anchor
            var authored = header.Anchor ?? new MacroAnchor(WorldPoint.Zero, default);
            var to = anchor.Position;
            var toHeading = new Bearing(anchor.Rotation.HeadingDeg);
            LeafApplier? relocate = null;
            if (header.Relocatable)
                relocate = new Relocation(authored.Position, authored.Heading, to, toHeading);
            else if (Distance2(authored.Position, to) > 0.25f || MathF.Abs(Turn(authored.Heading.Degrees, toHeading.Degrees)) > 1f)
                throw new Refused($"{here}: macro '{mi.Macro}' is pinned at its anchor ({authored.Position}, {authored.Heading.Degrees:0}°) and may not be placed elsewhere");

            var requests = _extractor.Extract(_serializer, dom.ToJsonString(), ids, _behaviorRemapper,
                                              new ExtractionPasses(BeforeResolve: new ParamBind(source), AfterResolve: relocate));

            foreach (var r in requests)
            {
                var comps = r.InitialComponents is null ? new List<object>() : new List<object>(r.InitialComponents);
                Guid local = Guid.Empty;
                for (int i = comps.Count - 1; i >= 0; i--)
                {
                    switch (comps[i])
                    {
                        case EntityGuid g: local = g.Value; comps.RemoveAt(i); break;
                        case ParamBindings: comps.RemoveAt(i); break;          // SR-168: a transient child never needs them
                        case MacroExpansionStatus: comps.RemoveAt(i); break;    // set below for a nested instance
                    }
                }
                if (local == Guid.Empty)
                    throw new Refused($"{here}: an entity of the file has no key (EntityGuid)");
                var childGuid = EntityGuidDerivation.Derive(instanceGuid, "macro:" + local.ToString("D"));   // SR-167
                comps.Add(new EntityGuid(childGuid));
                comps.Add(new MacroOrigin { Instance = new EntityRef(instanceId), Local = local });

                // a nested instance: flatten it NOW from its file (its transform is already moved, so the transforms compose — SR-155)
                if (comps.OfType<MacroInstance>().FirstOrDefault() is { } nested)
                {
                    var nestedAnchor = comps.OfType<SimTransform>().FirstOrDefault();
                    path.Add(mi.Macro);
                    try { Flatten(nested, r.PreAllocatedNetworkId, childGuid, owner, nestedAnchor, outside, ids, path, into, top, variant); }
                    finally { path.RemoveAt(path.Count - 1); }
                    comps.Add(new MacroExpansionStatus { State = MacroExpansionState.Done });   // E-2: never expanded again
                }

                into.Add(new EntityCreationRequest
                {
                    RequestId               = Guid.NewGuid(),
                    OwnerAppInstanceId      = (int)owner,
                    TkbType                 = r.TkbType,
                    DisType                 = r.DisType,
                    InitialComponents       = comps,
                    PreAllocatedNetworkId   = r.PreAllocatedNetworkId,
                    ChildComponentOverrides = r.ChildComponentOverrides,
                    IsScenarioLoad          = true,    // a scenario-shaped file: its subordinates are its own entities (CE-3324)
                    IsTransient             = true,    // SR-169 — a macro child is never saved
                    SpawnHeight             = r.SpawnHeight,
                    InitType                = r.InitType,
                });
            }
        }

        private static float Distance2(WorldPoint a, WorldPoint b) { float dx = a.X - b.X, dy = a.Y - b.Y; return dx * dx + dy * dy; }

        private static float Turn(float from, float to) { float d = (to - from) % 360f; if (d > 180f) d -= 360f; if (d < -180f) d += 360f; return d; }
    }
}
