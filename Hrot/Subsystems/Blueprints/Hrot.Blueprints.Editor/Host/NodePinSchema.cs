using System.Reflection;
using Hrot.Blueprints.Core.Assets;
using Hrot.Blueprints.Core.Compiler.Catalogs;
using Hrot.Blueprints.Editor.NodeDrawers;

namespace Hrot.Blueprints.Editor.Host;

/// <summary>
/// Canonical per-kind pin schema for Blueprint nodes.
/// <para>
/// Loaded <c>.bp.json</c> assets store <c>"Pins": []</c> (the compiler does not
/// require persisted pins).  This class resolves the authoritative pin list for a
/// given <see cref="Node"/> instance so the editor projection can hydrate pins for
/// the canvas without mutating the asset or the serialization format.
/// </para>
/// <para>
/// Resolution order:
/// <list type="number">
///   <item>If <paramref name="node"/>.Pins is non-empty (test-builder assets), return it as-is.</item>
///   <item>Try the <see cref="NodeKindRegistry"/> via <paramref name="registry"/> —
///     call <c>CreateInstance().Pins</c> on the matching descriptor.</item>
///   <item>Fall back to the built-in table below for kinds that are not in the registry
///     (core compiler kinds that appear in the JSON test fixtures).</item>
/// </list>
/// </para>
/// <para>
/// All projected pins (exec + data) are the pins the <b>compiler actually consumes</b>
/// for that kind (verified against <c>Stage2_Validate</c>/<c>Stage4_TypeResolve</c>/
/// <c>Stage5_Schedule</c>); a wired data pin is therefore meaningful.  Pins remain
/// projection-only — nothing is persisted to the asset or to disk.
/// </para>
/// </summary>
internal static class NodePinSchema
{
    /// <summary>
    /// Returns the canonical <see cref="Pin"/> list for <paramref name="node"/>.
    /// The returned pins have freshly generated (non-stable) GUIDs.  The caller's
    /// two-pass GUID-binding step must replace them with the real GUIDs from
    /// incident links before projecting <see cref="BlueprintPinModel"/> instances.
    /// </summary>
    /// <param name="node">The asset node to build pins for.</param>
    /// <param name="registry">Optional node-kind registry for registry-backed pin schemas.</param>
    /// <param name="asset">
    /// Optional owning asset; when non-null, Get/Set variable node Value pins are typed
    /// from the declared variable type rather than defaulting to <c>System.Object</c>.
    /// </param>
    /// <param name="channelCommands">
    /// Optional channel-command catalog.  When non-null it is used to resolve the parameter
    /// data-IN pins of a <see cref="ChannelCommandNode"/> from the matching
    /// <see cref="ChannelCommandCatalogEntry.ParamsTypeFqn"/> (the compiler's source of truth,
    /// Stage2_Validate §V_ChannelCommandReferences / Stage5_Schedule §ChannelCommand).
    /// When null (or the action/params type cannot be resolved) the node falls back to
    /// exec-only, matching the prior behavior.
    /// </param>
    public static IReadOnlyList<Pin> GetCanonicalPins(
        Node node,
        NodeKindRegistry?       registry        = null,
        BlueprintAsset?         asset           = null,
        IChannelCommandCatalog? channelCommands = null)
    {
        // Pass 0: asset already has pins (builder-created test assets).
        if (node.Pins.Count > 0)
            return node.Pins;

        // Pass 1: registry descriptor.
        if (registry != null)
        {
            var kindName = node.GetType().Name; // e.g. "WhenNode", "ReadEqsResultNode"
            var descriptor = registry.TryGet(kindName);
            if (descriptor == null)
            {
                // Also try without the "Node" suffix (registry keys like "When", "ReadEqsResult").
                var shortName = kindName.EndsWith("Node")
                    ? kindName[..^4]
                    : kindName;
                descriptor = registry.TryGet(shortName);
            }
            if (descriptor != null)
            {
                try
                {
                    var instance = descriptor.CreateInstance();
                    if (instance.Pins.Count > 0)
                        return instance.Pins;
                }
                catch { /* fallthrough to built-in table */ }
            }
        }

        // Pass 2: built-in fallback table for core compiler node kinds.
        return node switch
        {
            EventEntryNode      => ExecOnly("Out"),
            ReturnNode          => ExecOnly("In"),
            BranchNode          => BranchPins(),
            SequenceNode        => SequencePins(),
            FunctionCallNode fc => FunctionCallPins(fc),
            GetVariableNode gv  => GetVariablePins(gv, ResolveVariableTypeId(gv.VariableId, asset)),
            SetVariableNode sv  => SetVariablePins(sv, ResolveVariableTypeId(sv.VariableId, asset)),
            LiteralNode lt      => LiteralPins(lt),
            CastNode ca         => CastPins(ca),
            LatentDelayNode     => LatentDelayPins(),
            ChannelCommandNode cc => ChannelCommandPins(cc, channelCommands),
            WaitForChannelNode  => ExecInOut(),
            WaitForEventNode    => ExecInOut(),
            CallCustomEventNode cce => CallCustomEventPins(cce, asset),
            CallPeerBlueprintNode => CallPeerBlueprintPins(),
            CallEventDispatcherNode => ExecInOut(),
            BindEventDispatcherNode => ExecInOut(),
            ArrayMakeNode am    => ArrayMakePins(am),
            ArrayGetNode        => ArrayGetPins(),

            // Newer node kinds whose full pin schemas are in the registry;
            // if they reach here with empty pins just give them exec in/out.
            WhenNode            => ExecInOut(),
            ReadEqsResultNode   => Array.Empty<Pin>(),
            SpawnEqsSensorNode  => ExecInOut(),
            ScoreDecisionNode   => ScoreDecisionPins(),
            ReadRankedResultNode => ReadRankedResultPins(),
            PartitionElementsNode => ExecInOut(),
            AssignRolesNode     => ExecInOut(),
            AdvancePhaseNode    => ExecInOut(),
            AcquireSlotNode     => ExecInOut(),

            _ => Array.Empty<Pin>(),
        };
    }

    // ── variable type resolution ─────────────────────────────────────────────

    /// <summary>
    /// Look up the <c>TypeId</c> for a variable by its string id (e.g. <c>"var:abc123"</c>
    /// or plain GUID string) from the asset's variable list.
    /// Returns <c>"System.Object"</c> when the variable is not found or the asset is null.
    /// </summary>
    private static string ResolveVariableTypeId(string variableId, BlueprintAsset? asset)
    {
        if (asset == null || string.IsNullOrEmpty(variableId))
            return "System.Object";

        // CanvasRenderer.PlaceVariableNode passes the raw My-Blueprint item-id which may be
        // in the form "var:<Guid>" (as built by BlueprintMyBlueprintModel.BuildVariableItems).
        // Strip the "var:" prefix before parsing.
        var idStr = variableId.StartsWith("var:", StringComparison.OrdinalIgnoreCase)
            ? variableId[4..]
            : variableId;

        if (Guid.TryParse(idStr, out var guid))
        {
            var decl = asset.Variables.FirstOrDefault(v => v.Id == guid);
            if (decl != null && !string.IsNullOrEmpty(decl.Type?.TypeId))
                return decl.Type.TypeId;
        }

        return "System.Object";
    }

    // ── per-kind schema helpers ───────────────────────────────────────────────

    /// <summary>A single exec pin in the given direction.</summary>
    private static IReadOnlyList<Pin> ExecOnly(string direction)
        => new[] { MakeExec(direction == "In" ? "In" : "Out", direction) };

    /// <summary>Exec-in + exec-out, named "In" and "Out".</summary>
    private static IReadOnlyList<Pin> ExecInOut()
        => new[]
        {
            MakeExec("In",  "In"),
            MakeExec("Out", "Out"),
        };

    /// <summary>
    /// Branch: exec In + True/False exec outs + a <c>Condition</c> (System.Boolean) data-IN.
    /// Stage5_Schedule.ScheduleBranchNode reads the first non-exec data-IN pin as the
    /// branch condition (falling back to a <c>false</c> const when unconnected), so the
    /// Condition pin is compiler-consumed.
    /// </summary>
    private static IReadOnlyList<Pin> BranchPins()
        => new[]
        {
            MakeExec("In",    "In"),
            MakeExec("True",  "Out"),
            MakeExec("False", "Out"),
            MakeData("Condition", "In", "System.Boolean"),
        };

    private static IReadOnlyList<Pin> SequencePins()
        => new[]
        {
            MakeExec("In",    "In"),
            MakeExec("Then0", "Out"),
            MakeExec("Then1", "Out"),
        };

    /// <summary>
    /// LatentDelay: exec In/Out + a <c>Duration</c> (System.Single) data-IN.
    /// Stage5_Schedule.BuildLatentDelayOp resolves the first non-exec data-IN pin as the
    /// delay seconds (defaulting to <c>0f</c> when unconnected).
    /// </summary>
    private static IReadOnlyList<Pin> LatentDelayPins()
        => new[]
        {
            MakeExec("In",  "In"),
            MakeExec("Out", "Out"),
            MakeData("Duration", "In", "System.Single"),
        };

    /// <summary>
    /// ScoreDecision: exec In/Out + a <c>WinningOptionId</c> (System.Byte) data-OUT.
    /// Stage5_Schedule caches the score result on the out pin named "WinningOptionId".
    /// </summary>
    private static IReadOnlyList<Pin> ScoreDecisionPins()
        => new[]
        {
            MakeExec("In",  "In"),
            MakeExec("Out", "Out"),
            MakeData("WinningOptionId", "Out", "System.Byte"),
        };

    /// <summary>
    /// ReadRankedResult: three data-OUT pins corresponding to the fields of the emitted result
    /// struct declared in <c>InstanceEmitter.EmitReadRankedResultHelpers</c> (lines 539-541):
    /// <c>public bool IsValid; public long Entity; public float Score;</c>.
    /// Stage5_Schedule.cs:1049-1062 iterates these data-OUT pins by name and emits
    /// <c>IrOp_FieldRead(helperResult2, outPin.Name, fieldType)</c> for each; pin names must
    /// therefore match the struct field names exactly.  No data-IN pins — Rank is a node field
    /// baked at compile time (Stage5_Schedule.cs:1039).
    /// </summary>
    private static IReadOnlyList<Pin> ReadRankedResultPins()
        => new[]
        {
            MakeData("IsValid", "Out", "System.Boolean"),
            MakeData("Entity",  "Out", "System.Int64"),
            MakeData("Score",   "Out", "System.Single"),
        };

    /// <summary>
    /// CallCustomEvent: exec In + exec Out + one data-IN pin per custom-event parameter in
    /// declaration order, typed from <c>param.Type.TypeId</c> (fallback: <c>System.Object</c>).
    /// Grounded in Stage5_Schedule.cs:695-703: <c>ResolveAllDataInputs(node, stmts)</c> maps
    /// all non-exec data-IN pins positionally to the raised event's parameters
    /// (<c>IrOp_RaiseCustomEvent(idx, inputVals)</c>).
    /// The event is matched by <c>Guid.TryParse(EventId) &amp;&amp; events[i].Id == guid</c>
    /// (Stage5_Schedule.cs:1157-1159 — primary key is <see cref="CustomEventDecl.Id"/>, with
    /// a Name fallback at line 1160).
    /// Graceful fallback to exec-only when: asset is null, EventId does not parse to a Guid,
    /// no matching <see cref="CustomEventDecl"/> found, or the event has zero parameters.
    /// </summary>
    private static IReadOnlyList<Pin> CallCustomEventPins(CallCustomEventNode cce, BlueprintAsset? asset)
    {
        var execPins = new[]
        {
            MakeExec("In",  "In"),
            MakeExec("Out", "Out"),
        };

        if (asset == null)
            return execPins;

        if (!Guid.TryParse(cce.EventId, out var eventGuid))
            return execPins;

        var decl = asset.CustomEvents.FirstOrDefault(e => e.Id == eventGuid);
        if (decl == null || decl.Parameters.Count == 0)
            return execPins;

        var pins = new List<Pin>(2 + decl.Parameters.Count);
        pins.AddRange(execPins);
        foreach (var param in decl.Parameters)
        {
            var typeId = string.IsNullOrEmpty(param.Type?.TypeId) ? "System.Object" : param.Type.TypeId;
            pins.Add(MakeData(param.Name, "In", typeId));
        }
        return pins;
    }

    /// <summary>
    /// CallPeerBlueprint: exec In + exec Out + a single <c>Return</c> data-OUT pin
    /// (<c>System.Object</c> wildcard, resolved by Stage4 from incident links).
    /// Grounded in Stage5_Schedule.cs:661:
    /// <c>var outPin = node.Pins.FirstOrDefault(p =&gt; !p.IsExec &amp;&amp; p.Direction == "Out")</c>;
    /// the compiler always reads the first data-OUT pin as the return slot, then caches the peer
    /// function's return value on it (Stage5_Schedule.cs:664-672).
    /// <para>
    /// TODO(BATCH-03): add one data-IN pin per peer function parameter (Stage5_Schedule.cs:660
    /// calls <c>ResolveAllDataInputs</c> to consume all data-IN pins as call arguments).  Deferred
    /// because it requires resolving the peer blueprint's exported function signature (the
    /// graph-signature work tracked in BATCH-03).
    /// </para>
    /// </summary>
    private static IReadOnlyList<Pin> CallPeerBlueprintPins()
        => new[]
        {
            MakeExec("In",  "In"),
            MakeExec("Out", "Out"),
            MakeData("Return", "Out", "System.Object"),
        };

    /// <summary>
    /// ArrayGet: exec In/Out + <c>Array</c> data-IN (first data-IN, the source array),
    /// <c>Index</c> (System.Int32) data-IN, and <c>Element</c> data-OUT.
    /// Stage4_TypeResolve uses the first non-exec data-IN pin as the array and the
    /// first non-exec data-OUT pin as the element; element/array CLR type is a compile-time
    /// wildcard (System.Object here) resolved from incident links.  <c>Array</c> must be the
    /// first data-IN pin so Stage4 picks it (not Index) as the array input.
    /// </summary>
    private static IReadOnlyList<Pin> ArrayGetPins()
        => new[]
        {
            MakeExec("In",  "In"),
            MakeExec("Out", "Out"),
            MakeData("Array",   "In",  "System.Object"),
            MakeData("Index",   "In",  "System.Int32"),
            MakeData("Element", "Out", "System.Object"),
        };

    /// <summary>
    /// ArrayMake: exec In/Out + a small fixed set of element data-IN pins ("0","1") typed
    /// from <see cref="ArrayMakeNode.ElementTypeId"/> (or System.Object) + an <c>Array</c>
    /// data-OUT.  Stage4_TypeResolve infers the array type from the first non-exec data-IN
    /// pin's type and writes it onto the first non-exec data-OUT pin.  Dynamic element-count
    /// tracking is out of scope; two element slots is a sensible default.
    /// </summary>
    private static IReadOnlyList<Pin> ArrayMakePins(ArrayMakeNode am)
    {
        var elemType = string.IsNullOrEmpty(am.ElementTypeId) ? "System.Object" : am.ElementTypeId;
        return new[]
        {
            MakeExec("In",  "In"),
            MakeExec("Out", "Out"),
            MakeData("0",     "In",  elemType),
            MakeData("1",     "In",  elemType),
            MakeData("Array", "Out", elemType + "[]"),
        };
    }

    /// <summary>
    /// FunctionCall: exec In/Out (only when <see cref="FunctionCallNode.IsPure"/> is false)
    /// plus one data-IN pin per method parameter (in declaration order, matching
    /// Stage5_Schedule.ResolveAllDataInputs) and a single <c>Return</c> data-OUT pin when the
    /// method has a non-void return type.  The target <see cref="Type"/> is resolved by FQN
    /// across loaded assemblies; when the type/method cannot be found the node degrades
    /// gracefully to exec-only (pure → empty), matching the prior behavior.
    /// </summary>
    private static IReadOnlyList<Pin> FunctionCallPins(FunctionCallNode fc)
    {
        var pins = new List<Pin>();
        if (!fc.IsPure)
        {
            pins.Add(MakeExec("In",  "In"));
            pins.Add(MakeExec("Out", "Out"));
        }

        var method = ResolveMethod(fc.TargetTypeId, fc.MethodName);
        if (method == null)
            return pins; // graceful fallback: exec-only (or empty for pure).

        foreach (var param in method.GetParameters())
        {
            var pt = param.ParameterType;
            if (pt.IsByRef) pt = pt.GetElementType() ?? pt;
            pins.Add(MakeData(param.Name ?? "arg", "In", pt.FullName ?? pt.Name));
        }

        if (method.ReturnType != typeof(void))
            pins.Add(MakeData("Return", "Out",
                method.ReturnType.FullName ?? method.ReturnType.Name));

        return pins;
    }

    private static IReadOnlyList<Pin> GetVariablePins(GetVariableNode gv, string typeId)
        => new[]
        {
            MakeData("Value", "Out", typeId),
        };

    private static IReadOnlyList<Pin> SetVariablePins(SetVariableNode sv, string typeId)
        => new[]
        {
            MakeExec("In",    "In"),
            MakeExec("Out",   "Out"),
            MakeData("Value", "In",  typeId),
            MakeData("Value", "Out", typeId),
        };

    private static IReadOnlyList<Pin> LiteralPins(LiteralNode lt)
        => new[]
        {
            MakeData("Value", "Out", string.IsNullOrEmpty(lt.TypeId) ? "System.Object" : lt.TypeId),
        };

    private static IReadOnlyList<Pin> CastPins(CastNode ca)
        => new[]
        {
            MakeExec("In",  "In"),
            MakeExec("Out", "Out"),
            MakeData("In",  "In",  "System.Object"),
            MakeData("Out", "Out", string.IsNullOrEmpty(ca.TargetTypeId) ? "System.Object" : ca.TargetTypeId),
        };

    // ── ChannelCommand parameter resolution (DYNAMIC) ─────────────────────────

    /// <summary>
    /// ChannelCommand: exec In/Out + parameter data-IN pins resolved from the channel-command
    /// catalog.  The matching <see cref="ChannelCommandCatalogEntry"/> is found exactly the way
    /// Stage2_Validate.V_ChannelCommandReferences matches it:
    /// <c>LastSegment(ChannelTypeFqn) == node.ChannelType &amp;&amp; entry.Name == node.ActionId</c>.
    /// The entry's <see cref="ChannelCommandCatalogEntry.ParamsTypeFqn"/> is resolved to a CLR
    /// <see cref="Type"/> across loaded assemblies and projected as:
    /// <list type="bullet">
    ///   <item>one data-IN pin per public instance field/property when the params type is a
    ///     decomposable struct/class (e.g. <c>AimAndFireParams</c> → Target, CooldownSeconds);</item>
    ///   <item>a single data-IN pin (named after the type's short name) when the params type is a
    ///     primitive/enum (e.g. <c>System.Int32</c>) — Stage5 consumes channel-command data-IN
    ///     pins by <c>(Name, value)</c>, so one value pin is meaningful.</item>
    /// </list>
    /// Unknown action / unresolvable params type / null catalog → exec-only (no throw).
    /// </summary>
    private static IReadOnlyList<Pin> ChannelCommandPins(
        ChannelCommandNode cc, IChannelCommandCatalog? channelCommands)
    {
        var pins = new List<Pin>
        {
            MakeExec("In",  "In"),
            MakeExec("Out", "Out"),
        };

        if (channelCommands == null)
            return pins;

        ChannelCommandCatalogEntry? entry;
        try
        {
            entry = channelCommands.GetEntries().FirstOrDefault(e =>
                LastSegment(e.ChannelTypeFqn) == cc.ChannelType
                && e.Name == cc.ActionId);
        }
        catch
        {
            return pins; // catalog failure → exec-only.
        }

        if (entry == null || string.IsNullOrEmpty(entry.ParamsTypeFqn))
            return pins; // unknown action → exec-only.

        var paramsType = ResolveType(entry.ParamsTypeFqn);
        if (paramsType == null)
        {
            // Type not loadable: still surface a single typed param pin so the wire is meaningful.
            pins.Add(MakeData(LastSegment(entry.ParamsTypeFqn), "In", entry.ParamsTypeFqn));
            return pins;
        }

        var members = ReflectDataMembers(paramsType);
        if (members.Count == 0)
        {
            // Primitive/enum/opaque params: a single value pin typed as the params type.
            pins.Add(MakeData(LastSegment(entry.ParamsTypeFqn), "In", entry.ParamsTypeFqn));
            return pins;
        }

        foreach (var (name, typeFqn) in members)
            pins.Add(MakeData(name, "In", typeFqn));

        return pins;
    }

    /// <summary>
    /// Returns the public instance fields and read/write properties of <paramref name="type"/>
    /// as <c>(Name, TypeFqn)</c> pairs, in declaration order.  Returns an empty list for
    /// primitives, enums and types with no decomposable members.
    /// </summary>
    private static List<(string Name, string TypeFqn)> ReflectDataMembers(Type type)
    {
        var result = new List<(string, string)>();
        if (type.IsPrimitive || type.IsEnum || type == typeof(string))
            return result;

        try
        {
            foreach (var field in type.GetFields(BindingFlags.Public | BindingFlags.Instance))
                result.Add((field.Name, field.FieldType.FullName ?? field.FieldType.Name));

            foreach (var prop in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                if (prop.GetIndexParameters().Length > 0) continue; // skip indexers
                if (!prop.CanRead) continue;
                result.Add((prop.Name, prop.PropertyType.FullName ?? prop.PropertyType.Name));
            }
        }
        catch
        {
            return new List<(string, string)>();
        }

        return result;
    }

    // ── reflection helpers ────────────────────────────────────────────────────

    /// <summary>
    /// Resolves a <see cref="Type"/> by its fully-qualified name across all loaded assemblies.
    /// Tries <see cref="Type.GetType(string)"/> first, then scans
    /// <see cref="AppDomain.CurrentDomain"/>.  Returns null when not found.
    /// </summary>
    private static Type? ResolveType(string fqn)
    {
        if (string.IsNullOrEmpty(fqn)) return null;

        var direct = Type.GetType(fqn, throwOnError: false);
        if (direct != null) return direct;

        foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
        {
            try
            {
                var t = asm.GetType(fqn, throwOnError: false);
                if (t != null) return t;
            }
            catch
            {
                // Ignore assemblies that fail type resolution.
            }
        }
        return null;
    }

    /// <summary>
    /// Resolves a method by declaring-type FQN and method name across loaded assemblies.
    /// Returns the first public/non-public, static/instance method matching
    /// <paramref name="methodName"/>; null when the type or method cannot be found.
    /// </summary>
    private static MethodInfo? ResolveMethod(string targetTypeId, string methodName)
    {
        if (string.IsNullOrEmpty(targetTypeId) || string.IsNullOrEmpty(methodName))
            return null;

        var type = ResolveType(targetTypeId);
        if (type == null) return null;

        try
        {
            return type.GetMethods(
                    BindingFlags.Public | BindingFlags.NonPublic |
                    BindingFlags.Static | BindingFlags.Instance)
                .FirstOrDefault(m => m.Name == methodName);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>Returns the last dotted segment of <paramref name="fqn"/> (e.g. "LocomotionChannel").</summary>
    private static string LastSegment(string fqn)
    {
        if (string.IsNullOrEmpty(fqn)) return fqn;
        var idx = fqn.LastIndexOf('.');
        return idx >= 0 && idx < fqn.Length - 1 ? fqn[(idx + 1)..] : fqn;
    }

    // ── primitive factory helpers ─────────────────────────────────────────────

    private static Pin MakeExec(string name, string direction) => new()
    {
        Id        = Guid.NewGuid(),
        Name      = name,
        Direction = direction,
        IsExec    = true,
        TypeRef   = new BlueprintTypeRef(),
    };

    private static Pin MakeData(string name, string direction, string typeId) => new()
    {
        Id        = Guid.NewGuid(),
        Name      = name,
        Direction = direction,
        IsExec    = false,
        TypeRef   = new BlueprintTypeRef { TypeId = typeId },
    };
}
