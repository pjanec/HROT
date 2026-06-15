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
        bridge.Should().Contain("__parseParams = static (string json, byte* memory) =>",
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
    /// When NO variable has DefaultValueJson, ParseParams must NOT be emitted —
    /// the bridge is identical to pre-DEBT-AIB-013 output (null ParseParams at runtime).
    /// </summary>
    [Fact]
    public void ManagedAsset_NoVariableHasDefault_DoesNotEmitParseParams()
    {
        var dto    = MakeNoDefaultsDto();
        var bridge = BTreeBridgeEmitCore.EmitBridge(dto);

        bridge.Should().NotContain("__parseParams",
            "when no variable has DefaultValueJson, ParseParams must not be emitted");
        bridge.Should().NotContain("ParseParams  = __parseParams",
            "when no variable has DefaultValueJson, ParseParams must not be set in the initializer");
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
    /// When only the FIRST variable has DefaultValueJson (second has null), only the
    /// first variable's write must appear in the ParseParams lambda.
    /// </summary>
    [Fact]
    public void ManagedAsset_OnlyFirstVarHasDefault_EmitsOnlyFirstWrite()
    {
        // alpha has a default; beta does NOT.
        var dto    = MakeTwoVarDto(alphaDefault: "{\"Value\":7}", betaDefault: null);
        var bridge = BTreeBridgeEmitCore.EmitBridge(dto);

        bridge.Should().Contain("__parseParams = static (string json, byte* memory) =>",
            "ParseParams unsafe lambda must be emitted when at least one variable has DefaultValueJson");

        bridge.Should().Contain("memory + 0",
            "alpha must be written at offset 0");

        bridge.Should().NotContain("memory + 4",
            "beta has no DefaultValueJson — its write must NOT appear in ParseParams");
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
}
