using Fdp.Toolkit.Behavior.Demo;
using Fdp.Toolkit.Blueprints;
using Hrot.Blueprints.Core.Assets;
using Hrot.Blueprints.Core.Compiler;
using Hrot.Blueprints.Core.Compiler.Catalogs;
using Hrot.Blueprints.Core.Compiler.Diagnostics;
using Hrot.Blueprints.Core.Compiler.Stages;
using Hrot.Blueprints.Editor.Host;
using Hrot.Blueprints.Editor.NodeDrawers;
using Hrot.Blueprints.Tests.Builders;
using NodeEditor.Core.Interfaces;
using NodeEditor.Primitives;

namespace Hrot.Blueprints.Tests.Compiler;

/// <summary>
/// ENUM-SAMPLE focused verification suite.
/// Confirms the end-to-end enum pipeline: catalog entry → NodePinSchema Stance pin →
/// global:: TypeId → compile → (global::FQN)N in generated C#.
/// All tests are headless; no ImGui required.
/// </summary>
public sealed class EnumSampleTests
{
    // ── Compile options (mirrors the standard DefaultOptions used in AiPrimitiveEmitGoldenTests) ─

    private static CompileOptions DefaultOptions() =>
        new CompileOptions(
            Mode:              CompilerMode.Debug,
            NodeRegistry:      BuiltInNodeRegistry.Instance,
            TypeRegistry:      StaticTypeRegistry.Instance,
            EngineEvents:      BuiltInEngineEventCatalog.Instance,
            ChannelCommands:   BuiltInChannelCommandCatalog.Instance,
            WaitPrimitives:    BuiltInWaitPrimitiveCatalog.Instance,
            SiblingSignatures: Array.Empty<BlueprintSignature>());

    // ── 1. Catalog contains DemoEnumAction ────────────────────────────────────

    [Fact]
    public void Catalog_ContainsDemoEnumAction_OnLocomotionChannel()
    {
        var entries = BuiltInChannelCommandCatalog.Instance.GetEntries();
        var entry   = entries.FirstOrDefault(e => e.Name == "DemoEnumAction");

        Assert.NotNull(entry);
        Assert.Equal("DemoEnumAction",                                entry!.Name);
        Assert.Equal("Fdp.Toolkit.Behavior.Components.LocomotionChannel", entry.ChannelTypeFqn);
        Assert.Equal(99,                                              entry.ActionId);
        Assert.Equal("Fdp.Toolkit.Behavior.Demo.DemoEnumActionParams", entry.ParamsTypeFqn);
    }

    // ── 2. Per-action palette has an entry for DemoEnumAction (AN4) ──────────

    [Fact]
    public void Palette_ContainsDemoEnumAction_Entry()
    {
        var entries = BlueprintNodePaletteEntries
            .ChannelCommandEntries(BuiltInChannelCommandCatalog.Instance)
            .ToList();

        Assert.Contains(entries, e =>
            e.Kind        == "ChannelCommand:LocomotionChannel:DemoEnumAction"
            && e.Category == "Channel/Locomotion");
    }

    // ── 3. NodePinSchema projects Stance pin with global:: TypeId (AN6) ──────

    [Fact]
    public void NodePinSchema_DemoEnumAction_ProjectsStancePinWithGlobalPrefix()
    {
        var node = new ChannelCommandNode
        {
            ChannelType = "LocomotionChannel",
            ActionId    = "DemoEnumAction",
        };

        var pins = NodePinSchema.GetCanonicalPins(
            node,
            channelCommands: BuiltInChannelCommandCatalog.Instance);

        // Exec In + Exec Out always present.
        Assert.Contains(pins, p => p.Name == "In"  && p.IsExec && p.Direction == "In");
        Assert.Contains(pins, p => p.Name == "Out" && p.IsExec && p.Direction == "Out");

        // Stance pin: data-IN with global:: TypeId (AN6 stamping by ReflectDataMembers).
        var stancePin = pins.FirstOrDefault(p =>
            !p.IsExec && p.Direction == "In" && p.Name == "Stance");
        Assert.NotNull(stancePin);
        Assert.Equal("global::Fdp.Toolkit.Behavior.Demo.DemoStance",
                     stancePin!.TypeRef?.TypeId);

        // TargetPos and Repeat also projected.
        var dataNames = pins.Where(p => !p.IsExec && p.Direction == "In")
                            .Select(p => p.Name)
                            .ToHashSet();
        Assert.Contains("TargetPos", dataNames);
        Assert.Contains("Repeat",    dataNames);

        // Total data-IN pins: TargetPos, Stance, Repeat = 3.
        Assert.Equal(3, dataNames.Count);
    }

    // ── 4. BlueprintEnumValueProvider resolves DemoStance members (AN6) ──────

    [Fact]
    public void EnumValueProvider_ResolvesDemoStance_Members()
    {
        var provider = new BlueprintEnumValueProvider_Accessor();
        var key      = new TypeKey("global::Fdp.Toolkit.Behavior.Demo.DemoStance");
        var values   = provider.GetValues(key);

        Assert.Equal(3, values.Count);

        var names = values.Select(v => v.DisplayName).ToHashSet();
        Assert.Contains("Standing",  names);
        Assert.Contains("Crouching", names);
        Assert.Contains("Prone",     names);

        // Values should be 0, 1, 2 (DemoStance : int backing).
        var longs = values.Select(v => v.Value).OrderBy(x => x).ToList();
        Assert.Equal(new long[] { 0, 1, 2 }, longs);
    }

    // ── 5. StaticTypeRegistry accepts global:: enum TypeId (AN2) ─────────────

    [Fact]
    public void StaticTypeRegistry_AcceptsGlobalPrefixedEnumTypeId()
    {
        // BlueprintTypeRef only carries TypeId; the registry recognises the "global::" sentinel.
        var typeRef = new BlueprintTypeRef
        {
            TypeId = "global::Fdp.Toolkit.Behavior.Demo.DemoStance",
        };

        bool resolved = StaticTypeRegistry.Instance.TryResolve(typeRef, out var irType);

        Assert.True(resolved, "global::-prefixed enum TypeId must resolve (AN2).");
        Assert.True(irType.IsUnmanaged,  "Enum IrTypeRef must be unmanaged.");
        Assert.Equal(4, irType.SizeBytes);
        // IrTypeRef.FullName must be UNPREFIXED (no global::global:: on emit).
        Assert.Equal("Fdp.Toolkit.Behavior.Demo.DemoStance", irType.FullName);
    }

    // ── 6. Blueprint compiles successfully (exec-only path; Stance unset) ────

    [Fact]
    public void DemoEnumAction_BlueprintCompiles_WithNoErrors()
    {
        // Build a minimal AiPrimitive that uses DemoEnumAction (exec-wired only; no data pins
        // set, so Stage3 MaterializeDefaultPinLiterals will apply a zero default for Stance).
        var asset = BlueprintAssetBuilder
            .AiPrimitive("DemoEnumActionTest")
            .WithHostings(AiPrimitiveHosting.BTreeAction)
            .WithGraph("Main", g => g
                .Entry()
                .ChannelCommand("LocomotionChannel", "DemoEnumAction")
                .Return())
            .Build();

        var opts   = DefaultOptions();
        var result = new BlueprintCompiler().Compile(asset, opts);

        Assert.True(result.Succeeded,
            $"Compile failed: {string.Join(", ", result.Diagnostics.Where(d => d.IsError).Select(d => $"{d.Code}: {d.Message}"))}");
        Assert.False(result.Diagnostics.Any(d => d.IsError),
            "No compile errors expected.");
    }

    // ── 7. Generated source contains global::FQN.MemberName (ENUM-NAME) ──────
    //
    // Strategy: build the asset via BlueprintAssetBuilder (passes Stage2 correctly), then
    // patch the ChannelCommand node's Pins list directly so the Stance pin with
    // DefaultValue="Crouching" (member NAME, ENUM-NAME) is present before Stage3 runs.
    // This lets us skip the Stage2 re-run (already tested in test 6) and directly exercise
    // Stage3→7 with the enum default literal.

    [Fact]
    public void DemoEnumAction_GeneratedSource_ContainsMemberQualifiedName()
    {
        // 1. Use the builder to get a structurally valid AiPrimitive asset.
        var asset = BlueprintAssetBuilder
            .AiPrimitive("DemoEnumLiteralTest")
            .WithHostings(AiPrimitiveHosting.BTreeAction)
            .WithGraph("Main", g => g
                .Entry()
                .ChannelCommand("LocomotionChannel", "DemoEnumAction")
                .Return())
            .Build();

        // 2. Locate the ChannelCommand node and inject the Stance pin with DefaultValue.
        //    The builder produces nodes with explicit Pins (exec pins via RegisterNode), so
        //    we add our data pin directly.  Stage3 picks up DefaultValue from existing pins.
        //    ENUM-NAME: store the member name "Crouching" (value 1) not the integer "1".
        var graph  = asset.Graphs.First();
        var ccNode = graph.Nodes.OfType<ChannelCommandNode>().First();

        ccNode.Pins.Add(new Pin
        {
            Id           = Guid.NewGuid(),
            Name         = "Stance",
            Direction    = "In",
            IsExec       = false,
            TypeRef      = new BlueprintTypeRef
            {
                TypeId = "global::Fdp.Toolkit.Behavior.Demo.DemoStance",
            },
            DefaultValue = "Crouching",   // member NAME (ENUM-NAME); emits global::...DemoStance.Crouching
        });

        // 3. Run Stage3–7 (Stage2 validation already passed in test 6; we skip it here to
        //    avoid BP1025 being re-evaluated on the mutated asset).
        var opts = DefaultOptions();
        var sink = new DiagnosticSink();
        var ctx  = new ValidationContext(sink, opts);

        var norm         = Stage3_Normalize.Run(asset, ctx);
        var typed        = Stage4_TypeResolve.Run(norm, ctx);
        var ir           = Stage5_Schedule.Run(typed, ctx);
        var low          = Stage6_Lower.Run(ir, CompilerMode.Debug, sink);
        var (src, _)     = Stage7_Emit.Run(low, CompilerMode.Debug, sink);

        // No emit errors expected.
        Assert.False(sink.HasErrors,
            $"Emit errors: {string.Join(", ", sink.All.Where(d => d.IsError).Select(d => $"{d.Code}: {d.Message}"))}");

        // ENUM-NAME: the emitted source must contain the member-qualified name.
        // Stage3_Normalize.FormatDefaultLiteral emits: global::Fdp.Toolkit.Behavior.Demo.DemoStance.Crouching
        Assert.Contains("global::Fdp.Toolkit.Behavior.Demo.DemoStance.Crouching", src,
            StringComparison.Ordinal);

        // No double-prefix: TypeId is "global::FQN" → emitted as "global::FQN.Member" (one global::).
        Assert.DoesNotContain("global::global::", src, StringComparison.Ordinal);
    }

    // ── 7b. Integer-stored default still emits the cast (backward compat) ────

    [Fact]
    public void DemoEnumAction_IntegerDefault_StillEmitsCast()
    {
        // Old assets or fallback paths may store the integer "1" instead of the member name.
        // ENUM-NAME backward-compat: FormatDefaultLiteral detects a pure-integer string and
        // emits the cast form (global::FQN)N instead of the dot-qualified name.
        var asset = BlueprintAssetBuilder
            .AiPrimitive("DemoEnumIntFallbackTest")
            .WithHostings(AiPrimitiveHosting.BTreeAction)
            .WithGraph("Main", g => g
                .Entry()
                .ChannelCommand("LocomotionChannel", "DemoEnumAction")
                .Return())
            .Build();

        var graph  = asset.Graphs.First();
        var ccNode = graph.Nodes.OfType<ChannelCommandNode>().First();

        ccNode.Pins.Add(new Pin
        {
            Id           = Guid.NewGuid(),
            Name         = "Stance",
            Direction    = "In",
            IsExec       = false,
            TypeRef      = new BlueprintTypeRef
            {
                TypeId = "global::Fdp.Toolkit.Behavior.Demo.DemoStance",
            },
            DefaultValue = "1",   // integer string — backward compat path
        });

        var opts = DefaultOptions();
        var sink = new DiagnosticSink();
        var ctx  = new ValidationContext(sink, opts);

        var norm         = Stage3_Normalize.Run(asset, ctx);
        var typed        = Stage4_TypeResolve.Run(norm, ctx);
        var ir           = Stage5_Schedule.Run(typed, ctx);
        var low          = Stage6_Lower.Run(ir, CompilerMode.Debug, sink);
        var (src, _)     = Stage7_Emit.Run(low, CompilerMode.Debug, sink);

        Assert.False(sink.HasErrors,
            $"Emit errors: {string.Join(", ", sink.All.Where(d => d.IsError).Select(d => $"{d.Code}: {d.Message}"))}");

        // Backward-compat: integer cast form must still compile.
        Assert.Contains("(global::Fdp.Toolkit.Behavior.Demo.DemoStance)1", src,
            StringComparison.Ordinal);
        Assert.DoesNotContain("global::global::", src, StringComparison.Ordinal);
    }

    // ── Helper: thin accessor for BlueprintEnumValueProvider (internal) ───────

    /// <summary>
    /// Thin wrapper that constructs a <c>BlueprintEnumValueProvider</c> for test use.
    /// The class is <c>internal</c>; Hrot.Blueprints.Tests has InternalsVisibleTo via the Editor project.
    /// </summary>
    private sealed class BlueprintEnumValueProvider_Accessor : IEnumValueProvider
    {
        private readonly BlueprintEnumValueProvider _inner = new();
        public IReadOnlyList<EnumValueEntry> GetValues(TypeKey t)
            => _inner.GetValues(t);
        public int GetMaxInlineValues() => _inner.GetMaxInlineValues();
    }
}
