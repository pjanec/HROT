using System;
using System.Linq;
using System.Reflection;
using Fdp.Core;
using Fdp.Toolkit.Blueprints;
using FluentAssertions;
using Hrot.AI.Behaviors.Brains;
using Xunit;

namespace Hrot.AiEditor.Generators.Tests.Demos;

/// <summary>
/// BP-112 proof: the FIRST <c>Dispatch: Library</c> asset ever to pass through the real Roslyn source
/// generator (<c>Hrot.Blueprints.Generators.BlueprintIncrementalGenerator</c>).
///
/// <para>
/// ⚠ <b>Why this file exists at all.</b> The generator suite was already gated, but every fixture in it
/// was an <c>AiPrimitive</c> HillAssault2 asset, so the Library emit path — the
/// <c>LibraryFunctionDelegate</c> adapters in <c>CSharpEmitter.EmitLibraryFunctionAdapter</c> — had
/// never been compiled by it. It emitted <c>MemoryMarshal.Write(outputs, ref __r)</c>, and
/// <c>MemoryMarshal.Write&lt;T&gt;</c> declares that parameter <c>in T</c>, so every Library asset
/// raised <b>CS9191</b>. Every project here sets <c>TreatWarningsAsErrors</c>, so that warning failed
/// the whole solution build: the editor shipped a create-a-Function-Library path that bricked
/// <c>dotnet build</c> for anyone who pulled the asset. In-process Roslyn tests
/// (<c>BlueprintTestFixture.CompileAndLoad</c>) could not catch it — they do not treat warnings as
/// errors.
/// </para>
///
/// <para>
/// ⭐ The real regression lock is <c>Assets/Blueprints/LibraryFunctionsDemo.bp.json</c> itself: it is an
/// <c>AdditionalFiles</c> entry of <c>Hrot.AI.Behaviors</c>, so a regression in the Library emit path
/// fails the build outright, before any test runs. What this file adds is the other half — that the
/// adapters do not merely <i>compile</i> but marshal correctly in both directions.
/// </para>
///
/// <para>
/// The fixture covers all three branches of <c>EmitLibraryFunctionAdapter</c>:
/// <list type="bullet">
/// <item><c>Combine</c> — one output → the single <c>Write(outputs, in __r)</c> that broke.</item>
/// <item><c>Offsets</c> — two outputs → the BP-73 sequential <c>Write(outputs.Slice(__oo), in __outN)</c> walk.</item>
/// <item><c>Noop</c> — zero outputs → the <c>NodeStatus</c> status-return shape.</item>
/// </list>
/// </para>
/// </summary>
public sealed class LibraryFunctionsDemo_ProofTests
{
    private static readonly Assembly BehaviorsAssembly = typeof(DemoAiPrimitiveNodes).Assembly;

    /// <summary>
    /// Locates the generated class by name pattern rather than hardcoding the BlueprintId hash baked
    /// into it (mirrors <c>HillAssault2_CalculateSegments_ProofTests.FindGeneratedBlueprintType</c>).
    /// </summary>
    private static Type FindGeneratedType(string prefix)
    {
        var type = BehaviorsAssembly.GetTypes()
            .SingleOrDefault(t =>
                t.Namespace == "Hrot.AI.Behaviors.Generated"
                && t.Name.StartsWith(prefix, StringComparison.Ordinal)
                && t.Name.EndsWith("_Bp", StringComparison.Ordinal));
        type.Should().NotBeNull(
            $"LibraryFunctionsDemo.bp.json must compile via the real Roslyn source generator into a " +
            $"Hrot.AI.Behaviors.Generated.{prefix}*_Bp class");
        return type!;
    }

    /// <summary>
    /// Runs the generated <c>[BlueprintRegistrar]</c> into a staging buffer and returns the staged
    /// <see cref="BlueprintDefinition"/> — the same path <c>AiHotReloadCoordinator</c> drives at runtime.
    /// </summary>
    private static BlueprintDefinition StageDefinition()
    {
        var bpType       = FindGeneratedType("LibraryFunctionsDemo_");
        var registrarType = FindGeneratedType("BlueprintRegistrar_LibraryFunctionsDemo_");

        var register = registrarType.GetMethod("Register", BindingFlags.Public | BindingFlags.Static);
        register.Should().NotBeNull("the generated registrar must expose a static Register method");

        var staging = new BlueprintRegistry().BeginStaging();
        register!.Invoke(null, new object[] { staging });

        int blueprintId = (int)bpType.GetField("BlueprintId", BindingFlags.Public | BindingFlags.Static)!
            .GetRawConstantValue()!;

        staging.StagedBlueprintIds.Should().Contain(blueprintId,
            "the registrar must stage the Library definition under the class's own BlueprintId");

        var def = staging.GetType()
            .GetField("Definitions", BindingFlags.NonPublic | BindingFlags.Instance)!
            .GetValue(staging) as System.Collections.Generic.Dictionary<int, BlueprintDefinition>;
        return def![blueprintId];
    }

    private static LibraryFunctionDelegate Function(string name)
    {
        var def = StageDefinition();
        def.Kind.Should().Be(BlueprintDispatchKind.Library,
            "the asset declares Dispatch: Library and the registrar must carry that through");
        def.Functions.Should().ContainKey(name,
            "a Library definition must expose each Function graph as an invocable adapter");
        return def.Functions[name];
    }

    [Fact]
    public void LibraryAsset_IsCompiledByTheRealSourceGenerator()
    {
        // The narrow claim BP-112 turned on: a Library asset reaches the generator at all.
        var bpType = FindGeneratedType("LibraryFunctionsDemo_");

        bpType.GetMethod("Combine", BindingFlags.Public | BindingFlags.Static)
            .Should().NotBeNull("a Library function graph is emitted as a public static method");
        bpType.GetMethod("Offsets", BindingFlags.Public | BindingFlags.Static)!
            .ReturnType.Should().Be(typeof(ValueTuple<int, int>),
                "BP-73: a two-output function graph returns an unnamed ValueTuple");
        bpType.GetMethod("Noop", BindingFlags.Public | BindingFlags.Static)!
            .ReturnType.Should().Be(typeof(Fbt.NodeStatus),
                "a zero-output Library function graph returns NodeStatus");
    }

    [Fact]
    public void SingleOutputAdapter_MarshalsInputsAndWritesTheReturnValue()
    {
        var combine = Function("Combine");

        Span<byte> inputs  = stackalloc byte[sizeof(int) * 2];
        Span<byte> outputs = stackalloc byte[sizeof(int)];
        BitConverter.TryWriteBytes(inputs, 3);
        BitConverter.TryWriteBytes(inputs.Slice(sizeof(int)), 4);

        combine(inputs, outputs, view: null!, self: default, time: 0f);

        BitConverter.ToInt32(outputs).Should().Be(7,
            "Combine(3, 4) = 3 + 4; the adapter must unpack both inputs in declaration order and " +
            "write the return value back through MemoryMarshal.Write");
    }

    [Fact]
    public void MultiOutputAdapter_WritesEachOutputSequentially()
    {
        var offsets = Function("Offsets");

        Span<byte> inputs  = stackalloc byte[sizeof(int)];
        Span<byte> outputs = stackalloc byte[sizeof(int) * 2];
        BitConverter.TryWriteBytes(inputs, 10);

        offsets(inputs, outputs, view: null!, self: default, time: 0f);

        // BP-73: written element by element, NOT as a blitted ValueTuple — so both land where the
        // reader's Unsafe.SizeOf<T> walk expects them.
        BitConverter.ToInt32(outputs).Should().Be(11, "Plus = V + 1");
        BitConverter.ToInt32(outputs.Slice(sizeof(int))).Should().Be(9, "Minus = V - 1");
    }

    [Fact]
    public void ZeroOutputAdapter_WritesTheStatusReturn()
    {
        var noop = Function("Noop");

        // NodeStatus is `enum NodeStatus : byte`, so the adapter writes exactly one byte.
        Span<byte> outputs = stackalloc byte[sizeof(int)];

        noop(ReadOnlySpan<byte>.Empty, outputs, view: null!, self: default, time: 0f);

        ((Fbt.NodeStatus)outputs[0]).Should().Be(Fbt.NodeStatus.Success,
            "a Library function graph declaring no outputs returns NodeStatus, and the adapter " +
            "writes it out like any other return value");
    }
}
