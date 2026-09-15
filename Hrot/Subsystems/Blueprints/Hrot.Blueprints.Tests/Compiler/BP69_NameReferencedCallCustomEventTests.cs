using Fdp.Toolkit.Blueprints;
using Hrot.Blueprints.Core.Assets;
using Hrot.Blueprints.Core.Compiler;
using Hrot.Blueprints.Core.Compiler.Catalogs;
using Hrot.Blueprints.Core.Compiler.Stages;
using Hrot.Blueprints.Editor.Host;
using Hrot.Blueprints.Editor.NodeDrawers;
using Xunit;
using BlueprintDispatchKind = Hrot.Blueprints.Core.Assets.BlueprintDispatchKind;
using BlueprintTypeRef      = Hrot.Blueprints.Core.Assets.BlueprintTypeRef;
using ParameterDecl         = Hrot.Blueprints.Core.Assets.ParameterDecl;

namespace Hrot.Blueprints.Tests.Compiler;

/// <summary>
/// <b>BP-69 — a name-referenced <c>CallCustomEvent</c> must keep its argument pins.</b>
///
/// <para>
/// <c>CallCustomEventNode.EventId</c> accepts two forms: the declaration's GUID (what the editor's
/// picker writes) and a bare <c>Name</c> (hand-authored JSON, and the shape the test builders use).
/// <c>Stage5.FindCustomEventIndex</c>, <c>Stage2.V_ValueNodeReferences</c>,
/// <c>V_CustomEventHandlers</c> and BP-12b's rename path all honour both — but
/// <c>NodePinSchema.CallCustomEventPins</c> and <c>Stage0_Rehydrate.EnrichCallCustomEventPins</c>
/// each bailed on <c>!Guid.TryParse</c>. So a name-referenced call to an event WITH parameters got
/// exec-only pins and emitted <c>Event_X(ref s, view, ecb, self, time)</c> against a handler that
/// declares some ⇒ <b>CS7036 with no BP diagnostic</b>.
/// </para>
/// <para>
/// ⚠ <b>BP1408 cannot catch this</b>, which is why it survived: it compares the declaration's
/// Parameters against the handler graph's Inputs, and those two agree. The mismatch is at the
/// <em>call node's pins</em> — a third list nothing compared. So the decisive test here is the
/// end-to-end one (<see cref="NameReferencedCall_WithParameters_EmitsArgumentsAndCompiles"/>), not
/// the pin-count ones.
/// </para>
/// </summary>
public sealed class BP69_NameReferencedCallCustomEventTests
{
    private const string EventName = "OnDamaged";

    private static CompileOptions Options() => new(
        Mode:              CompilerMode.Debug,
        NodeRegistry:      BuiltInNodeRegistry.Instance,
        TypeRegistry:      StaticTypeRegistry.Instance,
        EngineEvents:      BuiltInEngineEventCatalog.Instance,
        ChannelCommands:   BuiltInChannelCommandCatalog.Instance,
        WaitPrimitives:    BuiltInWaitPrimitiveCatalog.Instance,
        SiblingSignatures: Array.Empty<BlueprintSignature>());

    /// <summary>Two typed parameters, so a dropped argument list is unmistakable.</summary>
    private static List<ParameterDecl> TwoParams() => new()
    {
        new ParameterDecl
        {
            Id = Guid.NewGuid(), Name = "Amount",
            Type = new BlueprintTypeRef { TypeId = "System.Single" },
        },
        new ParameterDecl
        {
            Id = Guid.NewGuid(), Name = "Critical",
            Type = new BlueprintTypeRef { TypeId = "System.Boolean" },
        },
    };

    private static BlueprintAsset MakeAsset(out CustomEventDecl decl, string eventIdOnCallNode)
    {
        decl = new CustomEventDecl
        {
            Id = Guid.NewGuid(), Name = EventName, Parameters = TwoParams(),
        };

        // Caller graph: Entry -> CallCustomEvent -> Return, EXEC-WIRED so the call node is actually
        // scheduled and a call site is emitted. (An unwired call node is unreachable and emits
        // nothing — the first draft of this test made that mistake and passed against the bug.)
        // The call node is PIN-LESS: the on-disk shape Stage 0 rehydrates, and the only path the
        // enricher runs on.
        var entryId = Guid.NewGuid();
        var callId  = Guid.NewGuid();
        var retId   = Guid.NewGuid();
        var entryEx = Guid.NewGuid();
        // Deterministic pin ids, so AssignDirection binds them by name (see the node comment).
        var callExIn  = DeterministicIds.PinId(callId, "In",  "In");
        var callExOut = DeterministicIds.PinId(callId, "Out", "Out");
        var retEx   = Guid.NewGuid();

        var caller = new Graph
        {
            Id = Guid.NewGuid(), Name = "Tick", Kind = GraphKind.Function,
            Nodes = new List<Node>
            {
                new EventEntryNode
                {
                    Id = entryId,
                    Pins = new List<Pin>
                    {
                        new() { Id = entryEx, Name = "ExecOut", Direction = "Out",
                                IsExec = true, TypeRef = new() },
                    },
                },
                // PIN-LESS on purpose. The exec links below carry DETERMINISTIC pin GUIDs, which
                // Stage0.AssignDirection binds by NAME — so the wiring survives the enricher adding
                // data-In pins, exactly as it does for a real editor-created asset.
                new CallCustomEventNode { Id = callId, EventId = eventIdOnCallNode },
                new ReturnNode
                {
                    Id = retId,
                    Pins = new List<Pin>
                    {
                        new() { Id = retEx, Name = "ExecIn", Direction = "In",
                                IsExec = true, TypeRef = new() },
                    },
                },
            },
            Links = new List<Link>
            {
                new() { FromNodeId = entryId, FromPinId = entryEx,
                        ToNodeId = callId, ToPinId = callExIn },
                new() { FromNodeId = callId, FromPinId = callExOut,
                        ToNodeId = retId,  ToPinId = retEx },
            },
        };

        // The handler: an Event graph named after the declaration, with matching Inputs.
        var handler = new Graph
        {
            Id = Guid.NewGuid(), Name = EventName, Kind = GraphKind.Event,
            Inputs = decl.Parameters
                .Select(p => new ParameterDecl { Id = p.Id, Name = p.Name, Type = p.Type })
                .ToList(),
            Nodes = new List<Node> { new EventEntryNode { Id = Guid.NewGuid(), EventTypeId = "" } },
        };

        return new BlueprintAsset
        {
            AssetId      = Guid.NewGuid(),
            Name         = "Bp69Asset",
            Dispatch     = BlueprintDispatchKind.Instance,
            CustomEvents = new List<CustomEventDecl> { decl },
            Graphs       = new List<Graph> { caller, handler },
            Header       = new Header(),
        };
    }

    private static CallCustomEventNode CallNode(BlueprintAsset a)
        => a.Graphs[0].Nodes.OfType<CallCustomEventNode>().Single();

    // =====================================================================
    // Editor projection
    // =====================================================================

    [Fact]
    public void EditorProjection_NameReferencedCall_ProjectsOneDataInPerParameter()
    {
        var asset = MakeAsset(out _, eventIdOnCallNode: EventName);   // the NAME form

        var pins = NodePinSchema.GetCanonicalPins(
            CallNode(asset), registry: null, asset: asset);

        var dataIn = pins.Where(p => !p.IsExec && p.Direction == "In").ToList();
        Assert.Equal(2, dataIn.Count);                       // BP-69: was 0
        Assert.Equal("Amount",   dataIn[0].Name);
        Assert.Equal("Critical", dataIn[1].Name);
        Assert.Equal("System.Single",  dataIn[0].TypeRef.TypeId);
        Assert.Equal("System.Boolean", dataIn[1].TypeRef.TypeId);
    }

    [Fact]
    public void EditorProjection_GuidReferencedCall_StillProjectsTheSamePins()
    {
        var asset = MakeAsset(out var decl, eventIdOnCallNode: "placeholder");
        CallNode(asset).EventId = decl.Id.ToString();          // the GUID form

        var pins = NodePinSchema.GetCanonicalPins(
            CallNode(asset), registry: null, asset: asset);

        Assert.Equal(2, pins.Count(p => !p.IsExec && p.Direction == "In"));
    }

    [Fact]
    public void EditorProjection_UnresolvableEventId_FallsBackToExecOnly()
    {
        var asset = MakeAsset(out _, eventIdOnCallNode: "NoSuchEvent");

        var pins = NodePinSchema.GetCanonicalPins(
            CallNode(asset), registry: null, asset: asset);

        Assert.Empty(pins.Where(p => !p.IsExec));   // graceful, unchanged
    }

    // =====================================================================
    // Compiler projection (Stage 0)
    // =====================================================================

    [Theory]
    [InlineData(true)]    // GUID form
    [InlineData(false)]   // Name form — this is the one that was broken
    public void Stage0_ProjectsArgumentPins_ForEitherEventIdForm(bool useGuid)
    {
        var asset = MakeAsset(out var decl, eventIdOnCallNode: EventName);
        if (useGuid) CallNode(asset).EventId = decl.Id.ToString();

        Stage0_Rehydrate.Run(asset, Options());

        var dataIn = CallNode(asset).Pins
            .Where(p => !p.IsExec && p.Direction == "In")
            .ToList();
        Assert.Equal(2, dataIn.Count);
        Assert.Equal("Amount",   dataIn[0].Name);
        Assert.Equal("Critical", dataIn[1].Name);
    }

    // =====================================================================
    // The decisive one: the seam BP1408 cannot see
    // =====================================================================

    /// <summary>
    /// End-to-end. The emitted call must pass as many arguments as <c>Event_OnDamaged</c> declares.
    /// Before BP-69 this produced <c>Event_OnDamaged(ref s, view, ecb, self, time)</c> against a
    /// 2-parameter handler — valid-looking IR, no BP diagnostic, and a CS7036 at Roslyn.
    /// </summary>
    [Fact]
    public void NameReferencedCall_WithParameters_EmitsArgumentsAndCompiles()
    {
        var asset = MakeAsset(out _, eventIdOnCallNode: EventName);

        var result = new BlueprintCompiler().Compile(asset, Options());

        Assert.True(result.Succeeded,
            "Compile failed: " + string.Join(", ",
                result.Diagnostics.Select(d => $"{d.Code}:{d.Message}")));

        var src = result.GeneratedSource!;

        // The handler method takes the two declared parameters...
        Assert.Contains("float Amount", src);
        Assert.Contains("bool Critical", src);

        // ...so every INVOCATION must supply two arguments after the engine-context prefix.
        //
        // ⚠ Assert on the invocation, not on `Event_OnDamaged(` — that also matches the method
        // DECLARATION, and an earlier draft of this test did exactly that and passed against the
        // bug. The argument-less invocation below is the literal text the defect produced.
        Assert.DoesNotContain($"Event_{EventName}(ref s, view, ecb, self, time);", src);

        // And the invocation the caller graph produces must carry both arguments. Nothing is wired
        // to them here, so they come through as declared `default(T)` temps (BP-69's companion fix
        // in ResolveDataPin) — never as bare undeclared identifiers, which would be CS0103.
        var invocations = src.Split('\n')
            .Select(l => l.Trim())
            .Where(l => l.StartsWith($"Event_{EventName}(", StringComparison.Ordinal))
            .ToList();
        Assert.NotEmpty(invocations);
        foreach (var call in invocations)
        {
            var argsAfterTime = call.Substring(call.IndexOf("time", StringComparison.Ordinal));
            Assert.NotEqual("time);", argsAfterTime);
            Assert.Equal(2, argsAfterTime.Split(',').Length - 1);   // two arguments follow `time`
        }
    }

    /// <summary>
    /// BP-69's companion fix, general to every unwired data pin: <c>ResolveDataPin</c> now emits a
    /// <b>declared</b> <c>default(T)</c> statement instead of a bare dummy <c>IrValue</c>.
    /// <para>
    /// Without it, fixing BP-69 would merely have traded CS7036 for **CS0103** — the argument pins
    /// now exist, and an unwired one produced <c>Event_X(..., __t0, __t1)</c> with no <c>var __t0</c>
    /// anywhere. That is a lateral move, not a fix: both are Roslyn errors no BP diagnostic explains.
    /// </para>
    /// </summary>
    [Fact]
    public void UnwiredArgumentPins_EmitDeclaredDefaults_NotUndeclaredTemps()
    {
        var asset = MakeAsset(out _, eventIdOnCallNode: EventName);

        var result = new BlueprintCompiler().Compile(asset, Options());
        Assert.True(result.Succeeded);
        var src = result.GeneratedSource!;

        // Every temp the generated code READS must also be DECLARED in it.
        var used = System.Text.RegularExpressions.Regex
            .Matches(src, @"__t\d+")
            .Select(m => m.Value)
            .Distinct()
            .ToList();
        Assert.NotEmpty(used);   // the fixture must actually exercise temps

        foreach (var t in used)
        {
            Assert.True(
                src.Contains($"var {t} =", StringComparison.Ordinal)
                || src.Contains($"ref var {t} =", StringComparison.Ordinal),
                $"{t} is used but never declared — that is CS0103 with only a BP4001 warning "
                + "to explain it (BP-69/BP-71's shape).");
        }
    }
}
