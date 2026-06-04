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
    public static string Emit(HsmAssetDto dto)
    {
        var sb = new StringBuilder();
        var usings = CollectUsings(dto);

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

        EmitCreateBuilder(sb, dto);
        sb.AppendLine();
        EmitCompile(sb, dto);
        sb.AppendLine();
        EmitLayout(sb, dto);

        sb.AppendLine("}");

        return sb.ToString();
    }

    // ---- Using collection ----

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

    private static void EmitCreateBuilder(StringBuilder sb, HsmAssetDto dto)
    {
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
        var allActions = CollectActions(dto);
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
            foreach (var topState in userTopLevel)
                EmitTopLevelState(sb, dto, topState, stableIdToState, pad, eventIdMap);
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

    private static void EmitTopLevelState(
        StringBuilder sb, HsmAssetDto dto,
        StateNodeDto state,
        Dictionary<Guid, StateNodeDto> stableIdToState,
        string pad,
        Dictionary<string, ushort> eventIdMap)
    {
        var outgoing = dto.Transitions
            .Where(t => t.SourceStableId == state.StableId)
            .ToList();

        var children = state.ChildStableIds
            .Where(id => stableIdToState.ContainsKey(id))
            .Select(id => stableIdToState[id])
            .ToList();

        bool needsVar = children.Count > 0 || outgoing.Count > 0;
        string varName = MakeVarName(state.Name);

        string decl = $"builder.State({QuoteStr(state.Name)}, stableId: new Guid({QuoteStr(state.StableId.ToString("D"))}))";
        var config   = BuildStateConfig(state, eventIdMap);

        if (needsVar)
            sb.Append($"{pad}var {varName} = {decl}");
        else
            sb.Append($"{pad}{decl}");

        EmitConfigChain(sb, config, pad + "    ");
        sb.AppendLine(";");

        foreach (var child in children)
            EmitChildCall(sb, dto, child, stableIdToState, varName, pad, depth: 2, eventIdMap);

        foreach (var t in outgoing)
            EmitTransitionCall(sb, stableIdToState, varName, t, pad, eventIdMap);
    }

    private static void EmitChildCall(
        StringBuilder sb, HsmAssetDto dto,
        StateNodeDto child,
        Dictionary<Guid, StateNodeDto> stableIdToState,
        string parentVar, string pad, int depth,
        Dictionary<string, ushort> eventIdMap)
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

        bool hasBody = config.Count > 0 || children.Count > 0 || outgoing.Count > 0;

        if (!hasBody)
        {
            sb.AppendLine($"{pad}{parentVar}.Child({QuoteStr(child.Name)}, {lambdaParam} => {{ }}, stableId: new Guid({stableGuid}));");
        }
        else
        {
            sb.AppendLine($"{pad}{parentVar}.Child({QuoteStr(child.Name)}, {lambdaParam} =>");
            sb.AppendLine($"{pad}{{");

            if (config.Count > 0)
            {
                sb.Append($"{innerPad}{lambdaParam}");
                foreach (var c in config)
                    sb.Append(c);
                sb.AppendLine(";");
            }

            foreach (var grandchild in children)
                EmitChildCall(sb, dto, grandchild, stableIdToState, lambdaParam, innerPad, depth + 1, eventIdMap);

            foreach (var t in outgoing)
                EmitTransitionCall(sb, stableIdToState, lambdaParam, t, innerPad, eventIdMap);

            sb.AppendLine($"{pad}}}, stableId: new Guid({stableGuid}));");
        }
    }

    private static void EmitTransitionCall(
        StringBuilder sb,
        Dictionary<Guid, StateNodeDto> stableIdToState,
        string stateVar, TransitionNodeDto t, string pad,
        Dictionary<string, ushort> eventIdMap)
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
            chain += $".Action({QuoteStr(t.ActionFunction!)})";
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

        sb.AppendLine($"{Indent}{Indent}.Build();");
    }

    // ---- Helpers ----

    private static string QuoteStr(string s) => $"\"{s}\"";
    private static string BoolStr(bool b) => b ? "true" : "false";

    private static string FormatFloat(float f) =>
        f.ToString("R", CultureInfo.InvariantCulture) + "f";

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

    private static List<string> CollectActions(HsmAssetDto dto)
    {
        var set = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var s in dto.States)
        {
            if (s.OnEntryAction  != null) set.Add(s.OnEntryAction);
            if (s.OnExitAction   != null) set.Add(s.OnExitAction);
            if (s.ActivityAction != null) set.Add(s.ActivityAction);
            if (s.TimerAction    != null) set.Add(s.TimerAction);
        }
        foreach (var t in dto.Transitions)
            if (t.ActionFunction != null) set.Add(t.ActionFunction);
        foreach (var gt in dto.GlobalTransitions)
            if (gt.ActionFunction != null) set.Add(gt.ActionFunction);
        return new List<string>(set);
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
