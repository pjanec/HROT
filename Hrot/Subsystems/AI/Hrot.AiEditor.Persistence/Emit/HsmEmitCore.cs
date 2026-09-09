using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using Hrot.AiEditor.Persistence.Hsm;

namespace Hrot.AiEditor.Persistence.Emit;

/// <summary>
/// Deterministic C# emitter for HSM assets operating on the persisted DTO.
/// Design §6.1: netstandard2.0 emit core — no editor/net8/ImGui reference.
/// Takes an <see cref="HsmAssetDto"/> and returns the C# string for
/// <c>CreateBuilder()</c> + the <c>[HsmDefinition]</c> thunk + the <c>[HsmLayout]</c> method.
/// Output is byte-identical to HsmFluentEmitter.Emit(model) when given
/// mapper.ToDto(model).
/// </summary>
public static class HsmEmitCore
{
    private const string LayoutNamespace = "Hrot.Editor.AiShared.Layout";
    private const string Indent          = "    ";

    /// <summary>Emits the complete .cs file content for the given HSM asset DTO.</summary>
    /// <remarks>
    /// Output includes the <c>[HsmLayout]</c> method.
    /// Byte-identical to <c>HsmFluentEmitter.Emit(model)</c> when given
    /// <c>mapper.ToDto(model)</c>.  Used by the editor adapter + BATCH-02 gate.
    /// </remarks>
    public static string Emit(HsmAssetDto dto)
    {
        return EmitInternal(dto, includeLayout: true);
    }

    /// <summary>
    /// Emits the topology core (.cs file content) for the given HSM asset DTO,
    /// EXCLUDING the <c>[HsmLayout]</c> method.
    /// Design §6.2: generated <c>.g.cs</c> = <c>CreateBuilder()</c> + <c>[HsmDefinition]</c> thunk only.
    /// Layout lives in JSON; read by the future JSON loader (PU-301).
    /// </summary>
    public static string EmitTopologyCore(HsmAssetDto dto)
        => EmitTopologyCore(dto, sizeResolver: null);

    /// <summary>
    /// ⭐ <c>E7b</c> overload: an optional struct-size resolver, so a transition bound to a
    /// struct-typed managed variable can bake that variable's packed offset into its action key.
    /// Primitive-typed variables need no resolver and are unaffected.
    /// </summary>
    public static string EmitTopologyCore(HsmAssetDto dto, System.Func<string, int?>? sizeResolver)
    {
        return EmitInternal(dto, includeLayout: false, sizeResolver);
    }

    /// <summary>Core emitter: shared implementation for both <see cref="Emit"/> and <see cref="EmitTopologyCore"/>.</summary>
    private static string EmitInternal(HsmAssetDto dto, bool includeLayout,
        System.Func<string, int?>? sizeResolver = null)
    {
        var sb = new StringBuilder();
        var usings = includeLayout ? CollectUsings(dto) : CollectUsingsTopologyOnly(dto);

        // Header
        sb.AppendLine(AiEmitCoreBase.BuildHeader(dto.AssetId));

        // Usings
        foreach (var ns in usings)
        {
            if (ns.Length == 0)
                sb.AppendLine(); // blank line between system and non-system groups
            else
                sb.AppendLine($"using {ns};");
        }

        sb.AppendLine();

        string targetNs = string.IsNullOrEmpty(dto.TargetNamespace)
            ? "Hrot.AI.Behaviors.Machines"
            : dto.TargetNamespace;
        string className = SanitizeIdentifier(dto.Name);

        sb.AppendLine($"namespace {targetNs};");
        sb.AppendLine();
        sb.AppendLine($"public static class {className}");
        sb.AppendLine("{");

        EmitCreateBuilder(sb, dto, sizeResolver);
        sb.AppendLine();
        EmitCompile(sb, dto);

        if (includeLayout)
        {
            sb.AppendLine();
            EmitLayout(sb, dto);
        }

        sb.AppendLine("}");

        return sb.ToString();
    }

    // ---- Using collection ----

    /// <summary>
    /// Collects usings for the full file (includes <c>Hrot.Editor.AiShared.Layout</c> for the
    /// <c>[HsmLayout]</c> method).  Used by <see cref="Emit"/>.
    /// </summary>
    private static IReadOnlyList<string> CollectUsings(HsmAssetDto dto)
    {
        var set = new HashSet<string>
        {
            "System",
            "System.Numerics",
            "Fhsm.Compiler",
            "Fhsm.Kernel.Attributes",
            "Fhsm.Kernel.Data",
            LayoutNamespace,
        };

        // Collect namespaces from action/guard FQNs in states, transitions, global transitions.
        foreach (var s in dto.States)
        {
            AddNsFromFqn(set, s.OnEntryAction);
            AddNsFromFqn(set, s.OnExitAction);
            AddNsFromFqn(set, s.ActivityAction);
            AddNsFromFqn(set, s.TimerAction);
        }
        foreach (var t in dto.Transitions)
        {
            AddNsFromFqn(set, t.GuardFunction);
            AddNsFromFqn(set, t.ActionFunction);
        }
        foreach (var gt in dto.GlobalTransitions)
        {
            AddNsFromFqn(set, gt.GuardFunction);
            AddNsFromFqn(set, gt.ActionFunction);
        }

        return AiEmitCoreBase.SortUsings(set);
    }

    /// <summary>
    /// Collects usings for the topology-core-only file (excludes
    /// <c>Hrot.Editor.AiShared.Layout</c> — no <c>[HsmLayout]</c> method, no
    /// <c>System.Numerics</c> since that is only used in layout).
    /// Used by <see cref="EmitTopologyCore"/>.
    /// </summary>
    private static IReadOnlyList<string> CollectUsingsTopologyOnly(HsmAssetDto dto)
    {
        var set = new HashSet<string>
        {
            "System",
            // System.Numerics is only needed for Vector2 in the layout method.
            // NOTE: LayoutNamespace intentionally excluded — no [HsmLayout] in topology core.
            "Fhsm.Compiler",
            "Fhsm.Kernel.Attributes",
            "Fhsm.Kernel.Data",
        };

        // Collect namespaces from action/guard FQNs in states, transitions, global transitions.
        foreach (var s in dto.States)
        {
            AddNsFromFqn(set, s.OnEntryAction);
            AddNsFromFqn(set, s.OnExitAction);
            AddNsFromFqn(set, s.ActivityAction);
            AddNsFromFqn(set, s.TimerAction);
        }
        foreach (var t in dto.Transitions)
        {
            AddNsFromFqn(set, t.GuardFunction);
            AddNsFromFqn(set, t.ActionFunction);
        }
        foreach (var gt in dto.GlobalTransitions)
        {
            AddNsFromFqn(set, gt.GuardFunction);
            AddNsFromFqn(set, gt.ActionFunction);
        }

        return AiEmitCoreBase.SortUsings(set);
    }

    private static void AddNsFromFqn(HashSet<string> set, string? fqn)
    {
        if (string.IsNullOrEmpty(fqn)) return;
        string f = fqn!;
        int last   = f.LastIndexOf('.');
        if (last <= 0) return;
        int second = f.LastIndexOf('.', last - 1);
        if (second > 0)
            set.Add(f.Substring(0, second));
        else
            set.Add(f.Substring(0, last));
    }

    // ---- CreateBuilder ----

    private static void EmitCreateBuilder(StringBuilder sb, HsmAssetDto dto,
        System.Func<string, int?>? sizeResolver = null)
    {
        // ⭐⭐ E7b — the packed offsets of the managed blackboard's inline params, computed once for
        //    the whole asset. An unbound transition never touches this map, so an asset with no
        //    ExpressionTargetField emits byte-identically.
        var paramOffsets = HsmParamOffsets(dto, sizeResolver);

        sb.AppendLine($"{Indent}public static HsmBuilder CreateBuilder()");
        sb.AppendLine($"{Indent}{{");

        string pad = Indent + Indent;

        sb.AppendLine($"{pad}var builder = new HsmBuilder({QuoteStr(dto.Name)});");

        // Build event-id map from the DTO.
        // EventId is stored in the DTO for byte-identical emit (from original builder calls).
        // For assets without stored IDs (EventId==0), fall back to sequential assignment.
        var eventIdMap = new Dictionary<string, ushort>(StringComparer.Ordinal);
        ushort fallbackId = 1;
        foreach (var ev in dto.Events)
        {
            ushort id = ev.EventId != 0 ? ev.EventId : fallbackId;
            eventIdMap[ev.Name] = id;
            if (ev.EventId == 0) fallbackId++;
        }

        // Events emitted in original order (sorted by EventId to match HsmFluentEmitter's
        // AllEvents.OrderBy(e => e.EventId) behavior)
        if (dto.Events.Count > 0)
        {
            sb.AppendLine();
            // Sort by the EventId (matching original emitter: asset.AllEvents.OrderBy(e => e.EventId))
            var sortedEvents = new System.Collections.Generic.List<EventDefinitionDto>(dto.Events);
            sortedEvents.Sort((a, b) =>
            {
                ushort idA = a.EventId != 0 ? a.EventId : (eventIdMap.TryGetValue(a.Name, out var va) ? va : (ushort)0);
                ushort idB = b.EventId != 0 ? b.EventId : (eventIdMap.TryGetValue(b.Name, out var vb) ? vb : (ushort)0);
                return idA.CompareTo(idB);
            });
            foreach (var ev in sortedEvents)
            {
                ushort id = eventIdMap.TryGetValue(ev.Name, out var eid) ? eid : (ushort)0;
                sb.AppendLine(
                    $"{pad}builder.Event({QuoteStr(ev.Name)}, {id}, {ev.PayloadSize}," +
                    $" {BoolStr(ev.IsIndirect)}, {BoolStr(ev.IsDeferrable)});");
            }
        }

        // RegisterAction calls (alphabetical)
        var allActions = CollectActions(dto, paramOffsets);
        if (allActions.Count > 0)
        {
            sb.AppendLine();
            foreach (var a in allActions)
                sb.AppendLine($"{pad}builder.RegisterAction({QuoteStr(a)});");
        }

        // RegisterGuard calls (alphabetical)
        var allGuards = CollectGuards(dto);
        if (allGuards.Count > 0)
        {
            sb.AppendLine();
            foreach (var g in allGuards)
                sb.AppendLine($"{pad}builder.RegisterGuard({QuoteStr(g)});");
        }

        // Build state lookup
        var stableIdToState = dto.States.ToDictionary(s => s.StableId);

        // Mirror original emitter: asset.RootState.Children[0] is the compiler-inserted "__Root".
        // Its children are the user-visible top-level states.
        // In the DTO: the state with no parentStableId (or parentStableId not in dto.States) is
        // the compiler root. Emit its children as top-level states, skipping __Root itself.
        var compilerRoot = dto.States
            .FirstOrDefault(s => !s.ParentStableId.HasValue ||
                                 !stableIdToState.ContainsKey(s.ParentStableId.Value));

        var userTopLevel = compilerRoot != null
            ? compilerRoot.ChildStableIds
                .Where(id => stableIdToState.ContainsKey(id))
                .Select(id => stableIdToState[id])
                .ToList()
            : new System.Collections.Generic.List<StateNodeDto>();

        if (userTopLevel.Count > 0)
        {
            sb.AppendLine();

            // VE-DEBT-006: two-pass emission for the WHOLE hierarchy (top-level + nested).
            // Pass 1 declares every state (vars + config + Child(...) calls) WITHOUT any
            // transition; Pass 2 emits every transition once all states exist. This makes
            // forward / cross-hierarchy GoTo targets safe — HsmBuilder.GoTo resolves the
            // target eagerly, so a transition to a later-declared state would otherwise throw
            // "Target state not found" and crash the editor at boot (the registrar runs Compile()).
            //
            // Nested transition-bearing states are not visible at method-body scope (they live
            // behind a Child(...) lambda parameter sbN). So we pre-declare a method-body-scope
            // StateBuilder variable for each and capture the lambda parameter into it; Pass 2 then
            // references that variable.
            var stateVarNames     = new Dictionary<Guid, string>();
            var usedVarNames      = new HashSet<string>(StringComparer.Ordinal);
            var nestedCaptureVars = new System.Collections.Generic.List<string>();

            // Allocate builder-variable names. A state needs a variable when:
            //   - (top-level) it has children or outgoing transitions — declared inline as `var x = builder.State(...)`;
            //   - (nested)    it has outgoing transitions — pre-declared + captured from inside its Child(...) lambda.
            void AllocateVars(StateNodeDto state, bool isTopLevel)
            {
                var childStates = state.ChildStableIds
                    .Where(id => stableIdToState.ContainsKey(id))
                    .Select(id => stableIdToState[id])
                    .ToList();
                bool hasOutgoing = dto.Transitions.Any(t => t.SourceStableId == state.StableId);
                bool needsVar    = isTopLevel ? (childStates.Count > 0 || hasOutgoing) : hasOutgoing;

                if (needsVar)
                {
                    string varName = AllocVarName(state.Name, usedVarNames);
                    stateVarNames[state.StableId] = varName;
                    if (!isTopLevel)
                        nestedCaptureVars.Add(varName);
                }

                foreach (var child in childStates)
                    AllocateVars(child, isTopLevel: false);
            }

            foreach (var topState in userTopLevel)
                AllocateVars(topState, isTopLevel: true);

            // Pre-declare nested capture variables at method-body scope (assigned inside Child lambdas).
            foreach (var v in nestedCaptureVars)
                sb.AppendLine($"{pad}StateBuilder {v} = null!;");
            if (nestedCaptureVars.Count > 0)
                sb.AppendLine();

            // Pass 1: declarations only (no transitions).
            var pendingTransitions = new System.Collections.Generic.List<(string VarName, TransitionNodeDto T)>();
            foreach (var topState in userTopLevel)
                EmitTopLevelStateDecl(sb, dto, topState, stableIdToState, pad, eventIdMap, pendingTransitions, stateVarNames);

            // Pass 2: emit transitions after all states are declared (avoids GoTo forward-ref error).
            // Each state's own transitions are appended consecutively in document order, so the
            // per-state TransitionNode order (and thus the compiled blob) is unchanged.
            foreach (var (varName, t) in pendingTransitions)
                EmitTransitionCall(sb, stableIdToState, varName, t, pad, eventIdMap, paramOffsets);
        }

        // Global transitions sorted by EventId (matching original emitter: OrderBy(g => g.EventId))
        if (dto.GlobalTransitions.Count > 0)
        {
            sb.AppendLine();
            var sortedGlobal = dto.GlobalTransitions
                .OrderBy(g =>
                {
                    if (g.EventName != null && eventIdMap.TryGetValue(g.EventName, out var gId))
                        return (int)gId;
                    return int.MaxValue;
                })
                .ToList();
            foreach (var gt in sortedGlobal)
            {
                string evRef     = gt.EventName ?? "";
                string targetName = stableIdToState.TryGetValue(gt.TargetStableId, out var tgt)
                    ? tgt.Name : "???";
                sb.AppendLine(
                    $"{pad}builder.GlobalTransition({QuoteStr(evRef)}, {QuoteStr(targetName)}," +
                    $" visualId: new Guid({QuoteStr(gt.VisualId.ToString("D"))}));");
            }
        }

        sb.AppendLine();
        sb.AppendLine($"{pad}return builder;");
        sb.AppendLine($"{Indent}}}");
    }

    /// <summary>
    /// Pass 1 of the two-pass state emission (VE-DEBT-006 compilable + boot-safe output):
    /// emits the state variable declaration + config chain + children, but NOT transitions.
    /// Transitions (top-level AND nested) are collected in <paramref name="pendingTransitions"/>
    /// for Pass 2, so every GoTo target is already declared when it is resolved.
    /// </summary>
    private static void EmitTopLevelStateDecl(
        StringBuilder sb, HsmAssetDto dto,
        StateNodeDto state,
        Dictionary<Guid, StateNodeDto> stableIdToState,
        string pad,
        Dictionary<string, ushort> eventIdMap,
        System.Collections.Generic.List<(string VarName, TransitionNodeDto T)> pendingTransitions,
        Dictionary<Guid, string> stateVarNames)
    {
        var outgoing = dto.Transitions
            .Where(t => t.SourceStableId == state.StableId)
            .ToList();

        var children = state.ChildStableIds
            .Where(id => stableIdToState.ContainsKey(id))
            .Select(id => stableIdToState[id])
            .ToList();

        // A pre-allocated var name exists iff this state has children or outgoing transitions.
        stateVarNames.TryGetValue(state.StableId, out var varName);
        bool needsVar = varName != null;

        string decl = $"builder.State({QuoteStr(state.Name)}, stableId: new Guid({QuoteStr(state.StableId.ToString("D"))}))";
        var config   = BuildStateConfig(state, eventIdMap);

        if (needsVar)
            sb.Append($"{pad}var {varName} = {decl}");
        else
            sb.Append($"{pad}{decl}");

        EmitConfigChain(sb, config, pad + "    ");
        sb.AppendLine(";");

        foreach (var child in children)
            EmitChildCall(sb, dto, child, stableIdToState, varName!, pad, depth: 2, eventIdMap, pendingTransitions, stateVarNames);

        // Collect transitions for Pass 2 (not emitted here to avoid forward-ref errors).
        foreach (var t in outgoing)
            pendingTransitions.Add((varName!, t));
    }

    private static void EmitChildCall(
        StringBuilder sb, HsmAssetDto dto,
        StateNodeDto child,
        Dictionary<Guid, StateNodeDto> stableIdToState,
        string parentVar, string pad, int depth,
        Dictionary<string, ushort> eventIdMap,
        System.Collections.Generic.List<(string VarName, TransitionNodeDto T)> pendingTransitions,
        Dictionary<Guid, string> stateVarNames)
    {
        string stableGuid  = QuoteStr(child.StableId.ToString("D"));
        string lambdaParam = $"sb{depth}";
        string innerPad    = pad + "    ";
        var config = BuildStateConfig(child, eventIdMap);

        var children = child.ChildStableIds
            .Where(id => stableIdToState.ContainsKey(id))
            .Select(id => stableIdToState[id])
            .ToList();

        var outgoing = dto.Transitions
            .Where(t => t.SourceStableId == child.StableId)
            .ToList();

        // A nested state gets a capture variable iff it has outgoing transitions (allocated
        // by AllocateVars). Capturing the lambda parameter into it lets Pass 2 attach the
        // transitions at method-body scope after every state is declared.
        bool hasCaptureVar = stateVarNames.TryGetValue(child.StableId, out var captureVar);

        bool hasBody = config.Count > 0 || children.Count > 0 || hasCaptureVar;

        if (!hasBody)
        {
            sb.AppendLine($"{pad}{parentVar}.Child({QuoteStr(child.Name)}, {lambdaParam} => {{ }}, stableId: new Guid({stableGuid}));");
        }
        else
        {
            sb.AppendLine($"{pad}{parentVar}.Child({QuoteStr(child.Name)}, {lambdaParam} =>");
            sb.AppendLine($"{pad}{{");

            // Capture this child's builder so Pass 2 can attach its outgoing transitions.
            if (hasCaptureVar)
                sb.AppendLine($"{innerPad}{captureVar} = {lambdaParam};");

            if (config.Count > 0)
            {
                sb.Append($"{innerPad}{lambdaParam}");
                foreach (var c in config)
                    sb.Append(c);
                sb.AppendLine(";");
            }

            foreach (var grandchild in children)
                EmitChildCall(sb, dto, grandchild, stableIdToState, lambdaParam, innerPad, depth + 1, eventIdMap, pendingTransitions, stateVarNames);

            // Transitions are deferred to Pass 2 (referenced via captureVar) — no inline GoTo here.

            sb.AppendLine($"{pad}}}, stableId: new Guid({stableGuid}));");
        }

        // Collect this child's outgoing transitions for Pass 2 (captureVar is non-null when outgoing exists).
        foreach (var t in outgoing)
            pendingTransitions.Add((captureVar!, t));
    }

    private static void EmitTransitionCall(
        StringBuilder sb,
        Dictionary<Guid, StateNodeDto> stableIdToState,
        string stateVar, TransitionNodeDto t, string pad,
        Dictionary<string, ushort> eventIdMap,
        IReadOnlyDictionary<string, int> paramOffsets)
    {
        string onCall = t.EventName != null
            ? $"{stateVar}.On({QuoteStr(t.EventName)})"
            : $"{stateVar}.On({(eventIdMap.TryGetValue(t.EventName ?? "", out var id) ? (int)id : 0)})";

        string targetName = stableIdToState.TryGetValue(t.TargetStableId, out var tgt)
            ? tgt.Name : "???";
        string chain =
            $".GoTo({QuoteStr(targetName)}, visualId: new Guid({QuoteStr(t.VisualId.ToString("D"))}))";

        if (!string.IsNullOrEmpty(t.GuardFunction))
            chain += $".Guard({QuoteStr(t.GuardFunction!)})";
        if (!string.IsNullOrEmpty(t.ActionFunction))
            chain += $".Action({QuoteStr(EffectiveActionName(t.ActionFunction!, t.ExpressionTargetField, paramOffsets))})";
        if (t.Priority != 0)
            chain += $".Priority({t.Priority})";

        sb.AppendLine($"{pad}{onCall}{chain};");
    }

    // ---- Compile ----

    private static void EmitCompile(StringBuilder sb, HsmAssetDto dto)
    {
        sb.AppendLine($"{Indent}[HsmDefinition({QuoteStr(dto.Name)}, AssetId = {QuoteStr(dto.AssetId.ToString("D"))})]");
        sb.AppendLine($"{Indent}public static HsmDefinitionBlob Compile() => CreateBuilder().Build().Compile();");
    }

    // ---- Layout ----

    private static void EmitLayout(StringBuilder sb, HsmAssetDto dto)
    {
        sb.AppendLine($"{Indent}[HsmLayout({QuoteStr(dto.AssetId.ToString("D"))})]");
        sb.AppendLine($"{Indent}public static HsmEditorLayout Layout() => new HsmEditorLayoutBuilder()");
        sb.AppendLine(
            $"{Indent}{Indent}.Canvas(new Vector2({FormatFloat(dto.Canvas.PanX)}," +
            $" {FormatFloat(dto.Canvas.PanY)}), {FormatFloat(dto.Canvas.Zoom)})");

        // States sorted by StableId (D-format) lexicographic
        foreach (var s in dto.States.OrderBy(x => x.StableId.ToString("D"), StringComparer.Ordinal))
        {
            string guidStr = QuoteStr(s.StableId.ToString("D"));
            string pos     = $"new Vector2({FormatFloat(s.X)}, {FormatFloat(s.Y)})";

            sb.Append($"{Indent}{Indent}.State({guidStr}, {pos}");
            if (s.SizeOverrideX.HasValue && s.SizeOverrideY.HasValue)
                sb.Append($", sizeOverride: new Vector2({FormatFloat(s.SizeOverrideX.Value)}, {FormatFloat(s.SizeOverrideY.Value)})");
            if (!string.IsNullOrEmpty(s.Comment))
                sb.Append($", comment: {QuoteStr(s.Comment!)}");
            if (s.IsCollapsed)
                sb.Append(", collapsed: true");
            if (!string.IsNullOrEmpty(s.ColorOverride))
                sb.Append($", color: {QuoteStr(s.ColorOverride!)}");
            sb.AppendLine(")");
        }

        // Transitions sorted by VisualId (D-format) lexicographic
        foreach (var t in dto.Transitions.OrderBy(x => x.VisualId.ToString("D"), StringComparer.Ordinal))
        {
            string guidStr = QuoteStr(t.VisualId.ToString("D"));
            string waypoints;
            if (t.Waypoints.Count == 0)
            {
                waypoints = "Array.Empty<System.Numerics.Vector2>()";
            }
            else
            {
                var pts = string.Join(", ",
                    t.Waypoints.Select(wp => $"new Vector2({FormatFloat(wp.X)}, {FormatFloat(wp.Y)})"));
                waypoints = $"new Vector2[] {{ {pts} }}";
            }

            sb.Append($"{Indent}{Indent}.Transition({guidStr}, {waypoints}");
            if (!string.IsNullOrEmpty(t.Comment))
                sb.Append($", comment: {QuoteStr(t.Comment!)}");
            sb.AppendLine(")");
        }

        // Regions sorted by StableId (D-format) lexicographic
        foreach (var r in dto.Regions.OrderBy(x => x.StableId.ToString("D"), StringComparer.Ordinal))
        {
            string guidStr = QuoteStr(r.StableId.ToString("D"));
            const string zeroPos = "new Vector2(0f, 0f)";

            sb.Append($"{Indent}{Indent}.Region({guidStr}, {r.RegionIndex}, {zeroPos}");
            if (!string.IsNullOrEmpty(r.Comment))
                sb.Append($", comment: {QuoteStr(r.Comment!)}");
            sb.AppendLine(")");
        }

        var conflictSuppressions = dto.Suppressions.Conflict
            .OrderBy(s => s.VariableName)
            .ThenBy(s => s.WriterPairKey)
            .ToList();
        foreach (var sup in conflictSuppressions)
        {
            sb.AppendLine($"{Indent}{Indent}.SuppressBlackboardConflict(\"{sup.VariableName}\", \"{sup.WriterPairKey}\")");
        }

        var unusedSuppressions = dto.Suppressions.Unused.OrderBy(s => s).ToList();
        foreach (var sup in unusedSuppressions)
        {
            sb.AppendLine($"{Indent}{Indent}.SuppressUnusedWarning(\"{sup}\")");
        }

        // ⭐ W7b (§9.4) -- per-VARIABLE "allow concurrent writes". Sorted like its neighbours so the
        //   emitted layout method stays deterministic; omitted entirely when empty, so every existing
        //   asset emits byte-identically.
        foreach (var allowed in (dto.Suppressions.ConcurrentWritesAllowed ?? new()).OrderBy(s => s))
        {
            sb.AppendLine($"{Indent}{Indent}.AllowConcurrentWrites(\"{allowed}\")");
        }

        sb.AppendLine($"{Indent}{Indent}.Build();");
    }

    // ---- Helpers ----

    private static string QuoteStr(string s) => $"\"{s}\"";
    private static string BoolStr(bool b) => b ? "true" : "false";

    private static string FormatFloat(float f) =>
        f.ToString("R", CultureInfo.InvariantCulture) + "f";

    /// <summary>
    /// Returns a unique camelCase builder-variable name for <paramref name="stateName"/>,
    /// suffixing with a counter on collision. Returns <see cref="MakeVarName"/> verbatim when
    /// the name is unused, so non-colliding output is byte-identical to the prior emitter.
    /// </summary>
    private static string AllocVarName(string stateName, HashSet<string> used)
    {
        string baseName = MakeVarName(stateName);
        string name = baseName;
        int n = 2;
        while (used.Contains(name))
            name = baseName + n++.ToString(CultureInfo.InvariantCulture);
        used.Add(name);
        return name;
    }

    private static string MakeVarName(string stateName)
    {
        var sb   = new StringBuilder(stateName.Length);
        bool cap = false;
        foreach (char c in stateName)
        {
            if (!char.IsLetterOrDigit(c) && c != '_')
            {
                cap = true;
                continue;
            }
            if (sb.Length == 0)
                sb.Append(char.ToLowerInvariant(c));
            else if (cap)
                sb.Append(char.ToUpperInvariant(c));
            else
                sb.Append(c);
            cap = false;
        }
        string name = sb.ToString();
        if (name.Length == 0 || char.IsDigit(name[0]))
            name = "_" + name;
        return name;
    }

    private static string SanitizeIdentifier(string name)
    {
        var sb = new StringBuilder(name.Length);
        foreach (char c in name)
        {
            if (char.IsLetterOrDigit(c) || c == '_')
                sb.Append(c);
            else
                sb.Append('_');
        }
        string result = sb.ToString();
        if (result.Length == 0 || char.IsDigit(result[0]))
            result = "_" + result;
        return result;
    }

    private static List<string> BuildStateConfig(StateNodeDto s, Dictionary<string, ushort> eventIdMap)
    {
        var parts = new List<string>();
        if (s.IsInitial)     parts.Add(".Initial()");
        if (s.IsHistory)     parts.Add(".History()");
        if (s.IsDeepHistory) parts.Add(".DeepHistory()");
        if (s.IsParallel)    parts.Add(".Parallel()");
        if (s.IsFinal)       parts.Add(".Final()");
        if (s.OnEntryAction  != null) parts.Add($".OnEntry({QuoteStr(s.OnEntryAction)})");
        if (s.OnExitAction   != null) parts.Add($".OnExit({QuoteStr(s.OnExitAction)})");
        if (s.ActivityAction != null) parts.Add($".Activity({QuoteStr(s.ActivityAction)})");
        if (s.TimerAction    != null) parts.Add($".TimerAction({QuoteStr(s.TimerAction)})");
        // Deferred events in ascending ID order (matching HsmFluentEmitter: OrderBy(id => id))
        var deferredIds = s.DeferredEventNames
            .Where(name => eventIdMap.ContainsKey(name))
            .Select(name => eventIdMap[name])
            .OrderBy(id => id)
            .ToList();
        foreach (var eventId in deferredIds)
            parts.Add($".DeferEvent({eventId})");
        return parts;
    }

    private static void EmitConfigChain(StringBuilder sb, List<string> config, string continuationPad)
    {
        foreach (var c in config)
        {
            sb.AppendLine();
            sb.Append($"{continuationPad}{c}");
        }
    }

    private static List<string> CollectActions(
        HsmAssetDto dto, IReadOnlyDictionary<string, int> paramOffsets)
    {
        var set = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var s in dto.States)
        {
            if (s.OnEntryAction  != null) set.Add(s.OnEntryAction);
            if (s.OnExitAction   != null) set.Add(s.OnExitAction);
            if (s.ActivityAction != null) set.Add(s.ActivityAction);
            if (s.TimerAction    != null) set.Add(s.TimerAction);
        }
        // ⭐⭐ E7b — the SAME resolution the transition itself emits. ⛔ If these two disagreed the
        //    builder would register one name and the transition would address another, which is
        //    exactly the silent TryGetValue miss E6 was.
        foreach (var t in dto.Transitions)
            if (t.ActionFunction != null)
                set.Add(EffectiveActionName(t.ActionFunction, t.ExpressionTargetField, paramOffsets));
        foreach (var gt in dto.GlobalTransitions)
            if (gt.ActionFunction != null) set.Add(gt.ActionFunction);
        return new List<string>(set);
    }

    // ---- E7b: expression-target binding ----------------------------------------

    /// <summary>
    /// ⭐⭐⭐ <b><c>E7b</c> — the action name a bound transition is addressed by:
    /// <c>{ActionFqn}@{byteOffset}</c> when <paramref name="expressionTargetField"/> names a packed
    /// managed variable; the bare FQN otherwise.</b>
    ///
    /// <para>
    /// 🔴🔴 <b>What this closes.</b> <c>ExpressionTargetField</c> — <i>"the blackboard field that
    /// receives the expression result of <c>ActionFunction</c>"</i> — round-tripped
    /// (<c>HsmAssetMapper</c>), was maintained by the command sink, was counted as a reference by
    /// <c>HsmAsset.CountNodesReferencingVariable</c>, and was treated as a WRITER STYLE by
    /// <c>HsmValidator</c> rule 9 — and appeared <b>zero times</b> in either emit core. ⛔ A producer
    /// with no consumer: the binding could never reach the blob, so there were no bytes to write.
    /// </para>
    ///
    /// <para>
    /// ⭐⭐ <b>The mechanism already existed and was unreachable.</b> <c>HsmActionGenerator</c> emits a
    /// per-binding thunk for every <c>[SharedAiAction]</c> — it projects the bound field at its byte
    /// offset and calls the method — and registers it under the compound key
    /// <c>HsmActionKey.ForCompoundKey</c>. ⚠ Nothing ever produced a compound key on the ASSET side,
    /// so those registrations were addressable by nobody.
    /// </para>
    ///
    /// <para>
    /// ⚠⚠ <b>The <c>"@"</c> spelling is MIRRORED from <c>HsmActionKey.CompoundKeyName</c>, and the
    /// mirror is forced:</b> this assembly is netstandard2.0 and deliberately references nothing, so
    /// it cannot call the Roslyn-hosted analyzer. ⇒ <b>the drift is the defect</b> — same shape as
    /// <c>HsmActionKey.Compute</c> mirroring <c>HsmFlattener.ComputeHash</c>, and
    /// <c>BehaviorParameterSizeAnalyzer</c>'s inlined size constant. ⭐ An agreement test compares the
    /// two sides across the wall rather than restating either.
    /// </para>
    /// </summary>
    internal static string EffectiveActionName(
        string actionFqn, string? expressionTargetField, IReadOnlyDictionary<string, int> paramOffsets)
    {
        if (string.IsNullOrEmpty(expressionTargetField)) return actionFqn;
        if (!paramOffsets.TryGetValue(expressionTargetField!, out int byteOffset)) return actionFqn;
        return actionFqn + "@" + byteOffset;   // MIRROR of HsmActionKey.CompoundKeyName
    }

    /// <summary>
    /// ⭐ The managed blackboard's inline param offsets by variable name — <b>the shared packer's
    /// own answer</b> (<see cref="BTreeBlackboardPackHelper"/>), so an expression target and the
    /// <c>ParseParams</c> that writes it can never land on different bytes. Empty for a non-managed
    /// blackboard, for <c>State</c>-role-only variables, or when a type cannot be sized.
    /// </summary>
    private static IReadOnlyDictionary<string, int> HsmParamOffsets(
        HsmAssetDto dto, System.Func<string, int?>? sizeResolver)
    {
        var map = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var f in HsmBridgeEmitCore.PackParamsFor(dto, sizeResolver))
            map[f.Name] = f.ByteOffset;
        return map;
    }

    private static List<string> CollectGuards(HsmAssetDto dto)
    {
        var set = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var t in dto.Transitions)
            if (t.GuardFunction != null) set.Add(t.GuardFunction);
        foreach (var gt in dto.GlobalTransitions)
            if (gt.GuardFunction != null) set.Add(gt.GuardFunction);
        return new List<string>(set);
    }
}
