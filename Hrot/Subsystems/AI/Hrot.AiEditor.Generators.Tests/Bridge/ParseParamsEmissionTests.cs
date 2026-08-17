using System;
using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Hrot.AiEditor.Persistence.BTree;
using Hrot.AiEditor.Persistence.Emit;
using Xunit;

namespace Hrot.AiEditor.Generators.Tests.Bridge;

/// <summary>
/// DEBT-AIB-013 fix tests — ParseParams emission in <see cref="BTreeBridgeEmitCore"/>.
///
/// Verifies that when a managed BTree asset has variables with non-null DefaultValueJson,
/// the emitted bridge source contains a <c>ParseParams =</c> delegate that:
/// 1. Deserializes each default JSON literal into the correct DTO type.
/// 2. Writes each DTO at its bin-packed byte offset (non-first variable must target non-zero offset).
///
/// Uses known types (System.Int32, System.Single) from BTreeBlackboardPackHelper.KnownSizes
/// so the test is self-contained and does not require Roslyn/struct-size resolution.
/// </summary>
public sealed class ParseParamsEmissionTests
{
    // ── Fixture helpers ─────────────────────────────────────────────────────────

    /// <summary>
    /// Builds a minimal managed BTree DTO with two Int32 variables, each with a DefaultValueJson.
    /// Packed layout (natural alignment):
    ///   alpha: System.Int32, size=4, align=4 → offset 0
    ///   beta:  System.Int32, size=4, align=4 → offset 4
    /// </summary>
    private static BehaviorTreeAssetDto MakeTwoVarDto(
        string? alphaDefault = "{\"Value\":42}",
        string? betaDefault  = "{\"Value\":99}")
    {
        return new BehaviorTreeAssetDto
        {
            AssetId           = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000001"),
            Name              = "TestParseParams",
            TargetNamespace   = "Test.Ns",
            BlackboardTypeName = "Fdp.Toolkit.Behavior.Components.BrainBlackboard",
            ContextTypeName   = "Fdp.Toolkit.Behavior.BTreeContext",
            Blackboard        = new BlackboardBlockDto
            {
                Managed   = true,
                TypeName  = "Fdp.Toolkit.Behavior.Components.BrainBlackboard",
                Variables = new List<BlackboardVariableDto>
                {
                    new BlackboardVariableDto
                    {
                        Name             = "alpha",
                        Type             = new BlackboardTypeRefDto { TypeId = "System.Int32" },
                        DefaultValueJson = alphaDefault,
                    },
                    new BlackboardVariableDto
                    {
                        Name             = "beta",
                        Type             = new BlackboardTypeRefDto { TypeId = "System.Int32" },
                        DefaultValueJson = betaDefault,
                    },
                }
            }
        };
    }

    /// <summary>
    /// Builds a DTO with one variable carrying DefaultValueJson (System.Single at offset 0).
    /// </summary>
    private static BehaviorTreeAssetDto MakeSingleVarDto(string? defaultJson = "{\"X\":3.14}")
    {
        return new BehaviorTreeAssetDto
        {
            AssetId            = Guid.Parse("bbbbbbbb-0000-0000-0000-000000000001"),
            Name               = "TestSingleDefault",
            TargetNamespace    = "Test.Ns",
            BlackboardTypeName = "Fdp.Toolkit.Behavior.Components.BrainBlackboard",
            ContextTypeName    = "Fdp.Toolkit.Behavior.BTreeContext",
            Blackboard         = new BlackboardBlockDto
            {
                Managed   = true,
                TypeName  = "Fdp.Toolkit.Behavior.Components.BrainBlackboard",
                Variables = new List<BlackboardVariableDto>
                {
                    new BlackboardVariableDto
                    {
                        Name             = "only",
                        Type             = new BlackboardTypeRefDto { TypeId = "System.Int32" },
                        DefaultValueJson = defaultJson,
                    },
                }
            }
        };
    }

    /// <summary>
    /// Builds a managed DTO where NO variable has DefaultValueJson.
    /// ParseParams must NOT be emitted for this case.
    /// </summary>
    private static BehaviorTreeAssetDto MakeNoDefaultsDto()
    {
        return new BehaviorTreeAssetDto
        {
            AssetId            = Guid.Parse("cccccccc-0000-0000-0000-000000000001"),
            Name               = "TestNoDefaults",
            TargetNamespace    = "Test.Ns",
            BlackboardTypeName = "Fdp.Toolkit.Behavior.Components.BrainBlackboard",
            ContextTypeName    = "Fdp.Toolkit.Behavior.BTreeContext",
            Blackboard         = new BlackboardBlockDto
            {
                Managed   = true,
                TypeName  = "Fdp.Toolkit.Behavior.Components.BrainBlackboard",
                Variables = new List<BlackboardVariableDto>
                {
                    new BlackboardVariableDto
                    {
                        Name             = "x",
                        Type             = new BlackboardTypeRefDto { TypeId = "System.Int32" },
                        DefaultValueJson = null,
                    },
                }
            }
        };
    }

    // ── Tests ───────────────────────────────────────────────────────────────────

    /// <summary>
    /// When both variables carry DefaultValueJson, the bridge must contain an unsafe block
    /// that captures a ParseParams lambda, and the BehaviorDefinition initializer must
    /// reference it via <c>ParseParams = __parseParams,</c>.
    /// The second variable (beta) must be written at offset 4, NOT offset 0.
    /// </summary>
    [Fact]
    public void ManagedAsset_BothVarsHaveDefault_EmitsParseParamsWithBothOffsets()
    {
        var dto    = MakeTwoVarDto();
        var bridge = BTreeBridgeEmitCore.EmitBridge(dto);

        // The unsafe block that captures the lambda must be present.
        bridge.Should().Contain("__parseParams = static (string json, byte* memory, global::Fdp.Core.EntityRepository world, global::Fdp.Core.Entity self, global::Fdp.Toolkit.Behavior.IHostVariableAccess? host) =>",
            "bridge must emit an unsafe lambda assigned to __parseParams");

        // The BehaviorDefinition initializer must reference the local.
        bridge.Should().Contain("ParseParams  = __parseParams,",
            "BehaviorDefinition initializer must reference the __parseParams local");

        // First variable: offset 0
        bridge.Should().Contain("memory + 0",
            "alpha (first variable) must be written at offset 0");

        // Second variable: offset 4 (Int32 = 4 bytes, natural alignment → no padding)
        bridge.Should().Contain("memory + 4",
            "beta (second variable, Int32 after Int32) must be written at offset 4, not 0");

        // Both JSON literals must appear (embedded quotes escaped as \" in the C# string literal).
        // The emitted literal looks like: "{\"Value\":42}"
        bridge.Should().Contain("{\\\"Value\\\":42}",
            "alpha default JSON literal (with escaped quotes) must be baked into the lambda");
        bridge.Should().Contain("{\\\"Value\\\":99}",
            "beta default JSON literal (with escaped quotes) must be baked into the lambda");
    }

    /// <summary>
    /// The JSON literal `{"Value":42}` contains double-quotes that must be
    /// properly escaped as `\"` in the emitted C# string literal.
    /// </summary>
    [Fact]
    public void ManagedAsset_DefaultJson_IsProperlyEscaped()
    {
        var dto    = MakeSingleVarDto("{\"X\":3.14}");
        var bridge = BTreeBridgeEmitCore.EmitBridge(dto);

        // The emitted C# literal must use \" escaping for embedded quotes.
        // e.g. the line: JsonSerializer.Deserialize<int>("{\"X\":3.14}")
        bridge.Should().Contain("\\\"X\\\"",
            "double-quotes in DefaultValueJson must be escaped as \\\" in the C# string literal");
    }

    /// <summary>
    /// ⭐⭐⭐ <b><c>DEBT-AIB-021</c> defect (b), INVERTED DELIBERATELY (Batch 70).</b>
    ///
    /// <para>
    /// 🔴 <b>This test used to assert the defect.</b> It read: <i>"when NO variable has
    /// DefaultValueJson, ParseParams must not be emitted"</i> — which is exactly why an asset whose
    /// variables carry no defaults <b>could never be overridden at all</b>. ⛔ Fixing the "ignores the
    /// json arg" half alone would have left those assets untouched, and this green test would have
    /// hidden it.
    /// </para>
    ///
    /// <para>
    /// ⭐ The guard is now <b>≥1 packed managed variable</b>, not ≥1 default. ⚠ The old intent —
    /// <i>"non-managed assets are unchanged"</i> — survives in
    /// <c>NonManagedAsset_DoesNotEmitParseParams</c> below, which is where it belonged.
    /// </para>
    /// </summary>
    [Fact]
    public void ManagedAsset_NoVariableHasDefault_StillEmitsParseParams_ForTheOverlay()
    {
        var dto    = MakeNoDefaultsDto();
        var bridge = BTreeBridgeEmitCore.EmitBridge(dto);

        bridge.Should().Contain("__parseParams",
            "an asset with packed managed variables must be overridable even with no baked defaults");
        bridge.Should().Contain("ParseParams  = __parseParams,",
            "and the definition must actually carry it");
        bridge.Should().Contain("JsonDocument.Parse(json)",
            "the overlay is what makes it useful without defaults");
    }

    /// <summary>
    /// A non-managed asset (Managed=false) must not emit ParseParams regardless of variables.
    /// </summary>
    [Fact]
    public void NonManagedAsset_DoesNotEmitParseParams()
    {
        var dto = new BehaviorTreeAssetDto
        {
            AssetId            = Guid.Parse("dddddddd-0000-0000-0000-000000000001"),
            Name               = "NonManaged",
            TargetNamespace    = "Test.Ns",
            BlackboardTypeName = "Fdp.Toolkit.Behavior.Components.BrainBlackboard",
            ContextTypeName    = "Fdp.Toolkit.Behavior.BTreeContext",
            Blackboard         = new BlackboardBlockDto
            {
                Managed   = false,   // not managed
                TypeName  = "HandWrittenType",
                Variables = new List<BlackboardVariableDto>()
            }
        };

        var bridge = BTreeBridgeEmitCore.EmitBridge(dto);

        bridge.Should().NotContain("__parseParams",
            "non-managed asset must not emit ParseParams");
    }

    /// <summary>
    /// ⭐⭐ <b>UPDATED DELIBERATELY (Batch 70) — the two steps are now distinguishable.</b>
    ///
    /// <para>
    /// 🔴 This test used to assert <c>NotContain("memory + 4")</c>, i.e. <i>"a variable with no default
    /// is never written"</i>. ⭐ <b>That is still true of STEP 1</b> — only <c>alpha</c> is baked — but
    /// it is <b>no longer true of the lambda as a whole</b>, because step 2's overlay can write
    /// <c>beta</c> when the caller supplies it. ⇒ the assertion moved from "is this offset absent" to
    /// <b>"which STEP writes it"</b>, which is the thing that actually matters.
    /// </para>
    /// </summary>
    [Fact]
    public void ManagedAsset_OnlyFirstVarHasDefault_BakesOnlyThatOne_ButBothAreOverridable()
    {
        // alpha has a default; beta does NOT.
        var dto    = MakeTwoVarDto(alphaDefault: "{\"Value\":7}", betaDefault: null);
        var bridge = BTreeBridgeEmitCore.EmitBridge(dto);

        bridge.Should().Contain("__parseParams = static (string json, byte* memory, global::Fdp.Core.EntityRepository world, global::Fdp.Core.Entity self, global::Fdp.Toolkit.Behavior.IHostVariableAccess? host) =>");

        // ⭐ Step 1 bakes ONLY alpha: its default literal appears, beta's does not exist to appear.
        bridge.Should().Contain("{\\\"Value\\\":7}",
            "alpha's baked default must be present");
        bridge.Should().Contain("memory + 0",
            "alpha must be written at offset 0");

        // ⭐⭐ Step 2 makes BOTH overridable -- that is the point of defect (b)'s fix.
        bridge.Should().Contain("case \"alpha\":");
        bridge.Should().Contain("case \"beta\":");
        bridge.Should().Contain("memory + 4",
            "beta has no default, but the overlay must still be able to write it");
    }

    /// <summary>
    /// E1/E3 regression: authoring a default value on a <b>composed blueprint AiPrimitive</b> node
    /// (the Static Parameters panel writes the JSON onto the auto-created Params variable) must bake
    /// that value into ParseParams exactly like any other managed variable. This is the composed
    /// analogue of <see cref="ManagedAsset_BothVarsHaveDefault_EmitsParseParamsWithBothOffsets"/> —
    /// the generic default-value baking is proven for scalar KnownSizes types, but never for a
    /// composed Params struct (offset 0, size from the schema resolver), which is exactly where the
    /// cross-generator offset/size subtleties live.
    ///
    /// Proves that for such a node the bridge simultaneously emits:
    /// 1. the layout-drift guard (predicted size 8 vs reflected),
    /// 2. the AiPrimitive TickCore thunk, and
    /// 3. a ParseParams write of the authored default into the composed Params at offset 0.
    /// </summary>
    [Fact]
    public void ComposedAiPrimitiveNode_WithAuthoredDefault_BakesParamsIntoParseParams()
    {
        const string paramsTypeId = "Hrot.AI.Behaviors.Generated.ParamDemo_CEFE162F_Bp+Params";
        const string wsTypeId     = "Hrot.AI.Behaviors.Generated.ParamDemo_CEFE162F_Bp+WorkingState";
        const string tickCoreFqn  = "Hrot.AI.Behaviors.Generated.ParamDemo_CEFE162F_Bp.TickCore";
        const string defaultJson  = "{\"Threshold\":5,\"FlagA\":false,\"FlagB\":true}";

        var dto = new BehaviorTreeAssetDto
        {
            AssetId            = Guid.Parse("eeeeeeee-0000-0000-0000-000000000001"),
            Name               = "TestComposedDefault",
            TargetNamespace    = "Test.Ns",
            BlackboardTypeName = "Fdp.Toolkit.Behavior.Components.BrainBlackboard",
            ContextTypeName    = "Fdp.Toolkit.Behavior.BTreeContext",
            Blackboard         = new BlackboardBlockDto
            {
                Managed   = true,
                TypeName  = "Fdp.Toolkit.Behavior.Components.BrainBlackboard",
                Variables = new List<BlackboardVariableDto>
                {
                    new BlackboardVariableDto
                    {
                        Name             = "bpParams",
                        Type             = new BlackboardTypeRefDto { TypeId = paramsTypeId },
                        DefaultValueJson = defaultJson,
                        IsAutoManaged    = true,
                    },
                },
            },
            Nodes = new List<BTreeNodeDto>
            {
                new BTreeActionNodeDto
                {
                    VisualId = Guid.Parse("eeeeeeee-0000-0000-0000-0000000000aa"),
                    Action = new BTreeActionPayloadDto
                    {
                        MethodFqn             = tickCoreFqn,
                        ExpressionTargetField = "bpParams",
                        DelegateShape         = BTreeDelegateShapeDto.AiPrimitiveTickCore,
                        WorkingStateTypeId    = wsTypeId,
                    },
                },
            },
        };

        // The composed Params struct is not a KnownSize; supply the schema-derived size (8 bytes).
        var bridge = BTreeBridgeEmitCore.EmitBridge(dto, typeId => typeId.Contains("Params") ? 8 : (int?)null);

        // (1) Layout-drift guard for the composed Params (predicted 8).
        bridge.Should().Contain(
            "global::System.Runtime.InteropServices.Marshal.SizeOf<global::Hrot.AI.Behaviors.Generated.ParamDemo_CEFE162F_Bp.Params>() != 8",
            "the composed node must still emit its predicted-vs-reflected layout-drift guard");

        // (2) AiPrimitive thunk dispatching to the blueprint's TickCore.
        bridge.Should().Contain($"actionRegistry.Register(\"{tickCoreFqn}@0@",
            "the composed AiPrimitive action must register its baked-offset thunk");

        // (3) ParseParams bakes the authored default into the composed Params at offset 0.
        bridge.Should().Contain("__parseParams = static (string json, byte* memory, global::Fdp.Core.EntityRepository world, global::Fdp.Core.Entity self, global::Fdp.Toolkit.Behavior.IHostVariableAccess? host) =>",
            "an authored default on the composed Params variable must emit a ParseParams lambda");
        bridge.Should().Contain(
            "global::System.Text.Json.JsonSerializer.Deserialize<global::Hrot.AI.Behaviors.Generated.ParamDemo_CEFE162F_Bp.Params>(",
            "the authored default must deserialize into the composed Params struct type");
        bridge.Should().Contain("global::System.Runtime.CompilerServices.Unsafe.Write(memory + 0",
            "the composed Params default must be written at its baked offset 0");
        bridge.Should().Contain("{\\\"Threshold\\\":5,\\\"FlagA\\\":false,\\\"FlagB\\\":true}",
            "the authored JSON literal (escaped) must be baked into the ParseParams lambda");
        bridge.Should().Contain("ParseParams  = __parseParams,",
            "the BehaviorDefinition initializer must reference the __parseParams local");
    }

    /// <summary>
    /// The emitted ParseParams lambda must use System.Text.Json.JsonSerializer.Deserialize
    /// and System.Runtime.CompilerServices.Unsafe.Write (fully qualified, global::).
    /// It must also be wrapped in an unsafe block for the byte* parameter to be legal.
    /// </summary>
    [Fact]
    public void ManagedAsset_WithDefault_EmitsFullyQualifiedJsonSerializerAndUnsafeWrite()
    {
        var dto    = MakeSingleVarDto();
        var bridge = BTreeBridgeEmitCore.EmitBridge(dto);

        bridge.Should().Contain("global::System.Text.Json.JsonSerializer.Deserialize",
            "ParseParams must use fully-qualified JsonSerializer");

        bridge.Should().Contain("global::System.Runtime.CompilerServices.Unsafe.Write",
            "ParseParams must use fully-qualified Unsafe.Write");

        // The unsafe block must wrap the lambda capture.
        bridge.Should().Contain("unsafe",
            "ParseParams lambda (byte* parameter) must be declared inside an unsafe block");

        // The fully-qualified delegate type must be declared for the local variable.
        bridge.Should().Contain("global::Fdp.Toolkit.Behavior.ParseParamsDelegate? __parseParams",
            "the local variable must be typed as ParseParamsDelegate?");
    }

    // ── DEBT-AIB-021 (Batch 70): the overlay's four decided behaviours ──────────

    /// <summary>
    /// ⭐⭐⭐ <b>The ORDER is the ruling.</b> 📄 <c>DESIGN_Parameter_Model.md</c> §3.2 — <i>"defaults are
    /// baked, scenario JSON overlays them, runtime wins."</i> ⇒ step 1's baked write must appear
    /// BEFORE step 2's overlay in the emitted body, ⛔ or the default would clobber the override and it
    /// would read as a resolver bug.
    /// </summary>
    [Fact]
    public void DefaultsAreBakedBeforeTheOverlayRuns()
    {
        var bridge = BTreeBridgeEmitCore.EmitBridge(MakeTwoVarDto());

        int baked   = bridge.IndexOf("Step 1", StringComparison.Ordinal);
        int overlay = bridge.IndexOf("Step 2", StringComparison.Ordinal);

        baked.Should().BeGreaterThan(-1);
        overlay.Should().BeGreaterThan(baked, "the overlay must run AFTER the baked defaults");
    }

    /// <summary>
    /// ⭐ <b>Every packed variable gets an overlay arm</b>, so overriding ONE leaves the others at
    /// their baked defaults — the switch simply never reaches them.
    /// </summary>
    [Fact]
    public void EveryPackedVariable_GetsItsOwnOverlayArm()
    {
        var bridge = BTreeBridgeEmitCore.EmitBridge(MakeTwoVarDto());

        bridge.Should().Contain("case \"alpha\":");
        bridge.Should().Contain("case \"beta\":");
        bridge.Should().Contain("__prop.Value.GetRawText()",
            "each arm deserializes only its own element -- that is what makes it an OVERLAY");
    }

    /// <summary>
    /// ⭐⭐ <b>A DECISION test: an unknown key is IGNORED, not an error.</b>
    ///
    /// <para>
    /// ⛔ <b>Not an oversight — it matches the CURATED path.</b>
    /// <c>JsonSerializer.Deserialize&lt;TDto&gt;</c> drops unmapped members unless
    /// <c>UnmappedMemberHandling</c> says otherwise, and ruling 9 says one mechanism gets one
    /// behaviour. ⚠ Pinned here so a later batch does not "fix" it into a throw.
    /// </para>
    /// </summary>
    [Fact]
    public void AnUnknownKey_IsIgnoredRatherThanAnError()
    {
        var bridge = BTreeBridgeEmitCore.EmitBridge(MakeTwoVarDto());

        bridge.Should().Contain("default: break;",
            "an unknown key must fall through silently, matching the curated path");
        bridge.Should().NotContain("Unknown parameter",
            "no diagnostic is emitted for an unknown key -- that is the decision");
    }

    /// <summary>
    /// ⭐ <b>Empty or null json ⇒ defaults only</b>, which is the shipped behaviour and must stay
    /// byte-identical for every asset nobody overrides.
    /// </summary>
    [Fact]
    public void EmptyJson_SkipsTheOverlayEntirely()
    {
        var bridge = BTreeBridgeEmitCore.EmitBridge(MakeTwoVarDto());

        bridge.Should().Contain("if (!string.IsNullOrWhiteSpace(json))",
            "no json means no overlay -- the baked defaults stand alone");
    }

    /// <summary>
    /// ⛔ <b>Malformed json THROWS, deliberately.</b> <c>BehaviorIngressSystem</c> parses into a stack
    /// shadow and commits only on success, so a throw is what leaves the entity on its old behaviour.
    /// ⚠ Swallowing would hand it a successful-looking all-zero params region — the same reasoning
    /// <c>BehaviorParams.FromJson</c> records for <c>G1</c>.
    /// </summary>
    [Fact]
    public void MalformedJson_IsNotSwallowed()
    {
        var bridge = BTreeBridgeEmitCore.EmitBridge(MakeTwoVarDto());

        bridge.Should().Contain("JsonDocument.Parse(json)");
        bridge.Should().NotContain("catch",
            "the emitted lambda must not swallow a parse failure -- parse-before-commit depends on it");
    }
}
