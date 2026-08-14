using System.Runtime.InteropServices;
using Fdp.Core;
using Fdp.Core.Logging;
using Fdp.Toolkit.Behavior;
using Fdp.Toolkit.Blueprints.Components;
using Fdp.Toolkit.Blueprints.Partitioning;
using Hrot.Blueprints.Core.Assets;
using Hrot.Blueprints.Editor.Host;
using Hrot.Blueprints.Editor.NodeDrawers;
using NLog;
using NLog.Config;
using NodeEditor.Core.Commands;
using NodeEditor.Core.Interfaces;
using NodeEditor.Primitives;

namespace Hrot.Blueprints.Tests.Integration;

/// <summary>
/// <b>The fix this covers: two pin caches, one cleared per block, one not.</b>
///
/// <para>
/// <c>Stage5_Schedule</c> keeps <c>_pinValueCache</c> (per-block, cleared at every block boundary)
/// and <c>_statementPinCache</c> (cross-block, never cleared — for a value a statement-scheduled
/// node materializes exactly once as a real local). Eight statement-scheduled node outputs used to
/// write ONLY the per-block cache (<c>SetVariableNode</c>'s <c>Value</c> out-pin wrote neither). A
/// consumer in the SAME block still found the value; a consumer on the far side of a <c>Branch</c>
/// found nothing in either cache and no case in <c>ResolveNodeOutput</c>, so it fell into the
/// <c>default:</c> arm and silently printed <c>0</c>/<c>false</c> — a clean build, no diagnostic.
/// </para>
///
/// <para>
/// ⭐ <b>The one property every test here is built around:</b> producer and consumer MUST sit on
/// opposite sides of a <c>Branch</c>. A same-block test passes against the OLD (buggy) code too and
/// proves nothing about which cache served the read. Test 1 below is the one exception — kept
/// deliberately same-block as a baseline so a Branch-side failure in test 2 can be read as "the
/// cross-block path specifically," not "SetVariable's Value pin never worked."
/// </para>
///
/// <para>
/// ⚠ Composed entirely through <see cref="AuthoringPath"/> (real palette registry, real command
/// sink, real Details sessions) — never by hand-assembling node/pin objects — so these tests also
/// prove the EDITOR half of the fix, not merely the compiler's lowering.
/// </para>
/// </summary>
[Collection("DebugProbe")]
public sealed class CrossBlockDataOutTests
{
    /// <summary>
    /// Force-loads Hrot.AI.Behaviors before any <see cref="AuthoringPath.Open"/> call builds the
    /// palette registry. <c>ReflectionComponentTypeProvider</c> (backing
    /// <c>ComponentPaletteEntries.GetComponentEntries</c>) only discovers already-loaded assemblies,
    /// and a <c>ProjectReference</c> alone does not force the CLR to load it — mirrors
    /// <c>CollectionWritePinParityTests</c>' identical static-ctor trick.
    /// ⭐ Batch 52: superseded by <c>TestAssemblyModuleInit</c>; kept as a local guard because the
    /// central one fails silently. A new test class needs nothing of its own.
    /// </summary>
    static CrossBlockDataOutTests()
    {
        _ = typeof(Hrot.AI.Behaviors.BpFixedListDemo).Assembly;
    }

    /// <summary>
    /// FC-0's <c>[BlueprintWritable]</c> reference component (InlineArray "Items" buffer + curated
    /// <c>BpFixedListDemoOps</c> accessors). Used for BOTH collection tests (4 and 5) so one
    /// force-loaded assembly and one component covers both — <c>BpCollectionDemo</c> is deliberately
    /// NOT usable here: it ships write accessors but is NOT <c>[BlueprintWritable]</c> (the
    /// gate-1-vs-gate-2 discovery case), so a wire into a <see cref="CollectionWriteNode"/> would
    /// land but never bake.
    /// </summary>
    private const string FixedListFqn = "Hrot.AI.Behaviors.BpFixedListDemo";

    // ── Test 1: headline defect, same-block baseline ────────────────────────────────────────────

    /// <summary>
    /// ⭐ <b>The user's defect, as a value — same-block baseline.</b> <c>Set Count</c> then
    /// immediately <c>Print</c> its <c>Value</c> out-pin, no <c>Branch</c> between them. Ticks twice
    /// and asserts the literal reaches the log both times, never a default.
    ///
    /// <para>
    /// A literal (not an increment) feeds <c>SetVariable</c>: wiring <c>GetVariable(Count) + 1</c>
    /// back into <c>SetVariable</c> would show 11-then-... vs. a flat 11-then-11, but the property
    /// under test is "the pin is not silently defaulted," which a flat non-zero value already
    /// proves — see the task note that a literal is an acceptable substitute for an increment.
    /// </para>
    /// </summary>
    [Fact]
    public void SetVariable_ValueOutPin_SameBlock_ReachesLog_TwiceNotDefault()
    {
        var messages = RunToLog(() =>
        {
            var doc = OpenDoc("XBlk_Headline");
            var countVar = RequireVariable(doc, "Count", "System.Int32");

            var setVar = AuthoringPath.AddNode(doc.Sink, doc.Graph, "SetVariable",
                new Dictionary<string, object?> { ["VariableId"] = countVar.Id.ToString() });

            var print = AuthoringPath.AddNode(doc.Sink, doc.Graph, "PrintString");
            var printSession = (PrintStringNodeSession)AuthoringPath.Details(doc, print);
            printSession.SetFormatForTest("count={Count}");
            doc.Model.RebuildAndNotify();

            var entry = doc.Graph.Nodes.First(n => n is EventEntryNode);
            var ret   = doc.Graph.Nodes.First(n => n is ReturnNode);
            AuthoringPath.Link(doc, entry,  "Out", setVar, "In");
            AuthoringPath.Link(doc, setVar, "Out", print,  "In");
            AuthoringPath.Link(doc, print,  "Out", ret,    "In");

            var literal = AuthoringPath.AddNode(doc.Sink, doc.Graph, "LiteralInt");
            ((LiteralNode)literal).ValueJson = "11";
            AuthoringPath.Link(doc, literal, "Value", setVar, "Value");

            // The pin under test: SetVariable's data-OUT "Value" pin, not the variable itself.
            // ⚠ SetVariableNode projects TWO pins both labeled "Value" (data-in AND data-out) --
            // AuthoringPath.Pin() matches by label only and returns the first (the data-IN one), so
            // a plain AuthoringPath.Link here would silently wire in->in and get refused. LinkOutToIn
            // disambiguates by direction instead.
            LinkOutToIn(doc, setVar, "Value", print, "Count");

            return doc.Asset;
        }, ticks: 2);

        Assert.Equal(2, messages.Count(m => m.Contains("count=11")));
        Assert.DoesNotContain(messages, m => m.Contains("count=0"));
    }

    // ── Test 2: the same, across a Branch — the test that actually distinguishes the caches ──────

    /// <summary>
    /// ⭐⭐ <b>The test that distinguishes the per-block cache from the cross-block one.</b> Identical
    /// to the baseline above except a <c>Branch</c> (literal <c>true</c> condition) now sits between
    /// <c>SetVariable</c> and <c>Print</c>, so they schedule into DIFFERENT blocks. Against the OLD
    /// code, <c>_pinValueCache</c> is cleared at the block boundary the Branch introduces and
    /// <c>ResolveNodeOutput</c> has no case for <see cref="SetVariableNode"/>'s <c>Value</c> pin, so
    /// the consumer would silently read <c>IrOp_Const("default")</c> — <c>count=0</c> — with a clean
    /// build and no diagnostic. Fixed code answers from <c>_statementPinCache</c> instead.
    /// </summary>
    [Fact]
    public void SetVariable_ValueOutPin_AcrossBranch_ReachesLog_NotDefault()
    {
        var messages = RunToLog(() =>
        {
            var doc = OpenDoc("XBlk_SetVarBranch");
            var countVar = RequireVariable(doc, "Count", "System.Int32");

            var setVar = AuthoringPath.AddNode(doc.Sink, doc.Graph, "SetVariable",
                new Dictionary<string, object?> { ["VariableId"] = countVar.Id.ToString() });

            var branch  = AuthoringPath.AddNode(doc.Sink, doc.Graph, "Branch");
            var litTrue = AuthoringPath.AddNode(doc.Sink, doc.Graph, "LiteralBool");
            ((LiteralNode)litTrue).ValueJson = "true";
            AuthoringPath.Link(doc, litTrue, "Value", branch, "Condition");

            var print = AuthoringPath.AddNode(doc.Sink, doc.Graph, "PrintString");
            var printSession = (PrintStringNodeSession)AuthoringPath.Details(doc, print);
            printSession.SetFormatForTest("count={Count}");
            doc.Model.RebuildAndNotify();

            var entry = doc.Graph.Nodes.First(n => n is EventEntryNode);
            var ret   = doc.Graph.Nodes.First(n => n is ReturnNode);
            AuthoringPath.Link(doc, entry,  "Out",  setVar, "In");
            AuthoringPath.Link(doc, setVar, "Out",  branch, "In");
            AuthoringPath.Link(doc, branch, "True", print,  "In");   // ⭐ the block boundary
            AuthoringPath.Link(doc, print,  "Out",  ret,    "In");

            var literal = AuthoringPath.AddNode(doc.Sink, doc.Graph, "LiteralInt");
            ((LiteralNode)literal).ValueJson = "11";
            AuthoringPath.Link(doc, literal, "Value", setVar, "Value");

            LinkOutToIn(doc, setVar, "Value", print, "Count");   // ⚠ see the direction note in test 1

            return doc.Asset;
        }, ticks: 1);

        Assert.Contains(messages, m => m.Contains("count=11"));
        Assert.DoesNotContain(messages, m => m.Contains("count=0"));
    }

    // ── Test 3: SetShared's "Written" out-pin across a Branch ───────────────────────────────────

    /// <summary>
    /// <see cref="SetSharedNode"/>'s <c>Written</c> out-pin (bool), wired across a <c>Branch</c> into
    /// a Print String. Fixed code answers <c>true</c> from <c>_statementPinCache</c>; the old code's
    /// gap here (BOTH caches were already missing this write in the unfixed state the task
    /// describes) would have silently printed <c>false</c>.
    /// </summary>
    [Fact]
    public void SetShared_WrittenOutPin_AcrossBranch_ReachesLog_NotFalse()
    {
        const string slotName = "XBlkSlot";

        var messages = RunToLog(() =>
        {
            var doc = OpenDoc("XBlk_SetShared");

            var setShared    = AuthoringPath.AddNode(doc.Sink, doc.Graph, "SetShared");
            var sharedSession = (SetSharedNodeSession)AuthoringPath.Details(doc, setShared);
            sharedSession.SetVariableIdForTest(slotName);
            sharedSession.SetSharedTypeIdForTest("System.Int32");
            doc.Model.RebuildAndNotify();

            var branch  = AuthoringPath.AddNode(doc.Sink, doc.Graph, "Branch");
            var litTrue = AuthoringPath.AddNode(doc.Sink, doc.Graph, "LiteralBool");
            ((LiteralNode)litTrue).ValueJson = "true";
            AuthoringPath.Link(doc, litTrue, "Value", branch, "Condition");

            var print = AuthoringPath.AddNode(doc.Sink, doc.Graph, "PrintString");
            var printSession = (PrintStringNodeSession)AuthoringPath.Details(doc, print);
            printSession.SetFormatForTest("w={Written}");
            doc.Model.RebuildAndNotify();

            var entry = doc.Graph.Nodes.First(n => n is EventEntryNode);
            var ret   = doc.Graph.Nodes.First(n => n is ReturnNode);
            AuthoringPath.Link(doc, entry,     "Out",  setShared, "In");
            AuthoringPath.Link(doc, setShared, "Out",  branch,    "In");
            AuthoringPath.Link(doc, branch,    "True", print,     "In");   // ⭐ the block boundary
            AuthoringPath.Link(doc, print,     "Out",  ret,       "In");

            var literal = AuthoringPath.AddNode(doc.Sink, doc.Graph, "LiteralInt");
            ((LiteralNode)literal).ValueJson = "5";
            // ⚠ BlueprintGraphModel.ResolvePinDisplayLabel relabels SetShared's data-in "Value" pin
            // with the slot name for display (Get/SetShared: "VariableId is already the shared
            // field's slot name -- show it on the pin") -- AuthoringPath.Pin() matches by that
            // display Label, not the pin's underlying Name, so the wire target is the slot name.
            AuthoringPath.Link(doc, literal, "Value", setShared, slotName);

            AuthoringPath.Link(doc, setShared, "Written", print, "Written");

            return doc.Asset;
        },
        ticks: 1,
        // ⚠ Not a caching concern: BlueprintSharedState.TrySetShared legitimately fails (Written =
        // false) unless the entity-scoped slot is provisioned first -- mirrors
        // MultiPinSetSharedTests.AttachSharedSlot, which must run AFTER AttachBlueprint (the
        // partition tier component only exists once the blueprint is attached).
        afterAttach: (fixture, entity) => AttachIntSharedSlot(fixture.World, entity, slotName));

        Assert.Contains(messages, m => m.Contains("w=True"));
        Assert.DoesNotContain(messages, m => m.Contains("w=False"));
    }

    // ── Test 4: CollectionWrite's "Ok" out-pin across a Branch ──────────────────────────────────

    /// <summary>
    /// <see cref="CollectionWriteNode"/>'s <c>Ok</c> out-pin (bool), wired across a <c>Branch</c>.
    ///
    /// <para>
    /// ⚠ <b>The node CREATION half is hand-built, not picker-driven — flagged deliberately.</b> The
    /// real "select component type" gesture
    /// (<see cref="GetComponentNodeSession.SetComponentTypeFqnForTest"/>, i.e.
    /// <c>ApplyComponentTypeFqn</c>) reflects BOTH scalar fields AND collections and appends them
    /// together; for <c>BpFixedListDemo</c> that includes its InlineArray buffer field
    /// (<c>Items</c>, CLR type <c>BpFixedListDemo+Buffer</c>) as a plain scalar pin, which the
    /// compiler's <c>StaticTypeRegistry</c> cannot resolve (<c>BP1500</c>) — a pre-existing gap in
    /// <c>ComponentFieldReflector</c>/<c>ApplyComponentTypeFqn</c> unrelated to the fix under test.
    /// So the <see cref="GetComponentNode"/> here is built with ONLY the collection decl baked,
    /// mirroring <c>BlueprintCommandSinkTests.AddWritableCollectionSource</c> — the repo's existing
    /// helper for this exact shape. The WIRE step below is still 100% production code:
    /// <c>AuthoringPath.Link</c> → <c>BlueprintCommandSink.TryBakeCollectionConsumer</c>'s real
    /// write-accessor bake, the same bake a designer's canvas wire triggers. This means the test does
    /// NOT cover the "pick BpFixedListDemo from the Get Component picker" gesture itself.
    /// </para>
    /// </summary>
    [Fact]
    public void CollectionWrite_OkOutPin_AcrossBranch_ReachesLog_NotFalse()
    {
        var messages = RunToLog(() =>
        {
            var doc = OpenDoc("XBlk_CollWrite");

            var getComp = AddWritableItemsCollectionSource(doc);

            var collWrite = AuthoringPath.AddNode(doc.Sink, doc.Graph, "Component.Collection.Add");

            // Bakes ComponentTypeFqn/WriteAccessorFqn/ElementTypeFqn onto collWrite.
            AuthoringPath.Link(doc, getComp, "Items", collWrite, "Collection");

            var litVal = AuthoringPath.AddNode(doc.Sink, doc.Graph, "LiteralInt");
            ((LiteralNode)litVal).ValueJson = "42";
            AuthoringPath.Link(doc, litVal, "Value", collWrite, "Value");

            var branch  = AuthoringPath.AddNode(doc.Sink, doc.Graph, "Branch");
            var litTrue = AuthoringPath.AddNode(doc.Sink, doc.Graph, "LiteralBool");
            ((LiteralNode)litTrue).ValueJson = "true";
            AuthoringPath.Link(doc, litTrue, "Value", branch, "Condition");

            var print = AuthoringPath.AddNode(doc.Sink, doc.Graph, "PrintString");
            var printSession = (PrintStringNodeSession)AuthoringPath.Details(doc, print);
            printSession.SetFormatForTest("ok={Ok}");
            doc.Model.RebuildAndNotify();

            var entry = doc.Graph.Nodes.First(n => n is EventEntryNode);
            var ret   = doc.Graph.Nodes.First(n => n is ReturnNode);
            AuthoringPath.Link(doc, entry,     "Out",  collWrite, "In");
            AuthoringPath.Link(doc, collWrite, "Out",  branch,    "In");
            AuthoringPath.Link(doc, branch,    "True", print,     "In");   // ⭐ the block boundary
            AuthoringPath.Link(doc, print,     "Out",  ret,       "In");

            AuthoringPath.Link(doc, collWrite, "Ok", print, "Ok");

            return doc.Asset;
        },
        ticks: 1,
        setup: (fixture, entity) =>
        {
            // CollectionWrite is self-only and write-if-present (HasComponent-guarded) -- the
            // entity needs the real component attached or "Ok" is legitimately false, not a defect.
            fixture.World.RegisterComponent<Hrot.AI.Behaviors.BpFixedListDemo>();
            fixture.World.AddComponent(entity, default(Hrot.AI.Behaviors.BpFixedListDemo));
        });

        Assert.Contains(messages, m => m.Contains("ok=True"));
        Assert.DoesNotContain(messages, m => m.Contains("ok=False"));
    }

    // ── Test 5: negative control — ForEach body locals must not leak across the fix ────────────────

    /// <summary>
    /// ⚠ <b>Negative control.</b> <see cref="ComponentForEachNode"/>'s <c>CurrentItem</c> is a
    /// loop-local living inside the emitted <c>for</c> body's braces; the scheduler deliberately
    /// removes it from the (per-block) cache after scheduling the body so nothing outside the loop
    /// can reference an out-of-scope C# local. This test exists to confirm the eight-node fix above
    /// did not accidentally widen that removal's scope or add ForEach's pins to
    /// <c>_statementPinCache</c> (which would leak the local past the closing brace). Asserts only
    /// that the graph still compiles clean — <c>ComponentForEachNode</c> is not one of the fixed
    /// nodes, so this is a regression guard, not a reproduction of the original defect.
    /// </summary>
    [Fact]
    public void ComponentForEach_BodyLocals_StayInScope_CompilesClean()
    {
        var doc = OpenDoc("XBlk_ForEach");

        // ⚠ Hand-built GetComponentNode -- see the doc comment on the CollectionWrite test above for
        // why (BpFixedListDemo's InlineArray buffer field breaks the real picker's combined
        // scalar+collection bake with an unrelated BP1500).
        var getComp = AddWritableItemsCollectionSource(doc);

        var forEach = AuthoringPath.AddNode(doc.Sink, doc.Graph, "Component.ForEach");
        AuthoringPath.Link(doc, getComp, "Items", forEach, "Collection");   // bakes accessor FQNs

        var print = AuthoringPath.AddNode(doc.Sink, doc.Graph, "PrintString");
        var printSession = (PrintStringNodeSession)AuthoringPath.Details(doc, print);
        printSession.SetFormatForTest("item={CurrentItem}");
        doc.Model.RebuildAndNotify();

        var entry = doc.Graph.Nodes.First(n => n is EventEntryNode);
        var ret   = doc.Graph.Nodes.First(n => n is ReturnNode);
        AuthoringPath.Link(doc, entry,   "Out",       forEach, "In");
        AuthoringPath.Link(doc, forEach, "Body",      print,   "In");   // CurrentItem consumed INSIDE the body
        AuthoringPath.Link(doc, forEach, "Completed", ret,     "In");

        AuthoringPath.Link(doc, forEach, "CurrentItem", print, "CurrentItem");

        var result = AuthoringPath.Generate(doc.Asset);

        Assert.True(result.Clean, $"Compile failed:{Environment.NewLine}{result.Report()}");
    }

    // ── harness ───────────────────────────────────────────────────────────────────────────────────

    private static AuthoringPath.Document OpenDoc(string name) =>
        AuthoringPath.Open(AuthoringPath.NewAsset(name, BlueprintDispatchKind.Instance));

    /// <summary>Declares an ordinary scalar variable through the editor's real create path.</summary>
    private static VariableDecl RequireVariable(AuthoringPath.Document doc, string name, string typeId) =>
        BlueprintDocumentFactory.CreateVariable(doc.Asset, name, typeId)
            ?? throw new InvalidOperationException($"CreateVariable rejected '{name}'.");

    /// <summary>
    /// A <see cref="GetComponentNode"/> over <see cref="FixedListFqn"/> carrying ONLY its <c>Items</c>
    /// collection decl (no scalar fields) — mirrors <c>BlueprintCommandSinkTests
    /// .AddWritableCollectionSource</c> exactly (same FQNs). See the doc comment on
    /// <see cref="CollectionWrite_OkOutPin_AcrossBranch_ReachesLog_NotFalse"/> for why this bypasses
    /// the real "pick component type" Details session rather than driving it.
    /// </summary>
    private static Node AddWritableItemsCollectionSource(AuthoringPath.Document doc)
    {
        var node = new GetComponentNode
        {
            Id               = Guid.NewGuid(),
            ComponentTypeFqn = FixedListFqn,
            Fields = new List<ComponentFieldDecl>
            {
                new()
                {
                    Name             = "Items",
                    IsCollection     = true,
                    ElementTypeId    = "System.Int32",
                    CountAccessorFqn = "Hrot.AI.Behaviors.Brains.BpFixedListDemoOps.Count",
                    ItemAccessorFqn  = "Hrot.AI.Behaviors.Brains.BpFixedListDemoOps.Item",
                },
            },
        };
        doc.Graph.Nodes.Add(node);
        doc.Model.RebuildAndNotify();
        return node;
    }

    /// <summary>
    /// Wires <paramref name="fromNode"/>'s OUTPUT pin named <paramref name="fromPin"/> into
    /// <paramref name="toNode"/>'s INPUT pin named <paramref name="toPin"/>, disambiguating by
    /// direction — needed wherever a node projects two pins sharing one label (e.g.
    /// <see cref="SetVariableNode"/>'s data-in AND data-out both labeled "Value"), where
    /// <see cref="AuthoringPath.Pin"/>'s label-only lookup would silently grab the wrong one.
    /// </summary>
    private static void LinkOutToIn(
        AuthoringPath.Document doc, Node fromNode, string fromPin, Node toNode, string toPin)
    {
        var fromP = FindPinByDirection(doc, fromNode, fromPin, wantOutput: true);
        var toP   = FindPinByDirection(doc, toNode, toPin, wantOutput: false);
        var result = doc.Sink.Apply(new GraphCommand.AddLink(IdGenerator.NewLinkId(), fromP.Id, toP.Id));
        if (!result.Success)
            throw new InvalidOperationException(
                $"The editor refused {fromNode.GetType().Name}.{fromPin} -> "
                + $"{toNode.GetType().Name}.{toPin}: {result.Message}");
    }

    private static IPinModel FindPinByDirection(
        AuthoringPath.Document doc, Node node, string pinName, bool wantOutput)
    {
        var model = doc.Model.FindNode(new NodeId(node.Id))
            ?? throw new InvalidOperationException($"Node {node.Id} is not in the graph model.");

        foreach (var pin in model.Pins)
        {
            if (!string.Equals(pin.Label, pinName, StringComparison.OrdinalIgnoreCase)) continue;
            bool isOutput = string.Equals(pin.Direction.ToString(), "Output", StringComparison.OrdinalIgnoreCase);
            if (isOutput == wantOutput) return pin;
        }
        throw new InvalidOperationException(
            $"No {(wantOutput ? "Output" : "Input")} pin '{pinName}' on {node.GetType().Name}. Projected: "
            + string.Join(", ", model.Pins.Select(p => $"{p.Label}({p.Direction})")));
    }

    /// <summary>
    /// Runs <paramref name="compose"/> to build+save+reload an asset, compiles it through real
    /// Roslyn, attaches to a fresh entity (running <paramref name="setup"/> BEFORE compile/attach --
    /// e.g. to register+add a real ECS component -- and <paramref name="afterAttach"/> AFTER
    /// <c>AttachBlueprint</c> -- e.g. to provision a shared-state slot, which needs the blueprint's
    /// blackboard partition to already exist), ticks <paramref name="ticks"/> frames, and returns
    /// every captured AI.Behavior log message — mirrors <c>AuthoringPathRunValueTests.Tick</c>'s
    /// NLog capture pattern (register the rule, restore <c>LogManager.Configuration</c> in
    /// <c>finally</c>, clear <see cref="AiBehaviorLogTarget.SharedInstance"/> before and after).
    /// </summary>
    private static IReadOnlyList<string> RunToLog(
        Func<BlueprintAsset> compose,
        int ticks,
        Action<BlueprintTestFixture, Entity>? setup = null,
        Action<BlueprintTestFixture, Entity>? afterAttach = null)
    {
        var previousConfig = LogManager.Configuration;
        AiBehaviorLogTarget.SharedInstance.Clear();
        try
        {
            var config = new LoggingConfiguration();
            config.AddRule(LogLevel.Trace, LogLevel.Fatal, AiBehaviorLogTarget.SharedInstance, "AI.Behavior*");
            LogManager.Configuration = config;

            var authored = compose();
            var asset = AuthoringPath.SaveAndReload(authored);

            using var fixture = new BlueprintTestFixture(
                new BlueprintTestFixtureOptions { VerifyAlcUnloadOnDispose = false });

            var entity = fixture.CreateEntity();
            setup?.Invoke(fixture, entity);

            fixture.CompileAndLoad(asset);
            fixture.AttachBlueprint(asset, entity);
            afterAttach?.Invoke(fixture, entity);
            for (int i = 0; i < ticks; i++)
                fixture.TickFrame(0.016f);

            return AiBehaviorLogTarget.SharedInstance.GetMessages().Select(m => m.Message).ToList();
        }
        finally
        {
            AiBehaviorLogTarget.SharedInstance.Clear();
            LogManager.Configuration = previousConfig;
        }
    }

    /// <summary>
    /// Provisions an entity-scoped <c>int</c> shared-state slot named <paramref name="slotName"/> —
    /// mirrors <c>MultiPinSetSharedTests.AttachSharedSlot</c> (same
    /// <c>BlueprintBlackboardPartitions.TryAttach</c> call, generalized off <c>MultiPinShared</c> to
    /// a plain <c>int</c>). Must run AFTER <c>AttachBlueprint</c> — the BB1024 tier component this
    /// reads only exists once the blueprint has been attached.
    /// </summary>
    private static unsafe void AttachIntSharedSlot(EntityRepository world, Entity entity, string slotName)
    {
        int slotKey = StatefulBTreeActionBinder.ComputeStatefulSlotKey(
            Guid.Empty, StatefulSlotScope.Entity, Guid.Empty, slotName);
        uint expectedHash = unchecked(
            StatefulBTreeActionBinder.ComputeTypeNameHash(typeof(int).FullName ?? "") ^ (uint)Marshal.SizeOf<int>());

        ref var tier = ref world.GetComponentRW<BlueprintBlackboard1024>(entity);
        fixed (byte* mem = tier.Memory)
        {
            bool ok = BlueprintBlackboardPartitions.TryAttach(
                mem, slotKey, Marshal.SizeOf<int>(), expectedHash, out _);
            if (!ok)
                throw new InvalidOperationException($"TryAttach for shared slot '{slotName}' failed.");
        }
    }
}
