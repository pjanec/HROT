using System;
using System.Runtime.InteropServices;
using Fdp.Core;
using Fdp.Toolkit.Blueprints;
using Hrot.Blueprints.Core.Assets;
using Hrot.Blueprints.Tests.Builders;
using Xunit;

namespace Hrot.Blueprints.Tests.Compiler;

/// <summary>
/// G2 (R1 + R2): proves a compiled <b>Library-blueprint function</b> is runtime-invocable by name
/// through the new <see cref="BlueprintDefinition.Functions"/> table + <see cref="LibraryFunctionDelegate"/>.
/// <para>
/// Before G2, a Library-kind definition carried no callable delegate (only Instance dispatch did), so
/// a blueprint-authored parameter resolver could not be dispatched at all. The registrar now populates
/// <c>Functions["&lt;graph&gt;"]</c> with an adapter that marshals blittable inputs into the emitted
/// static method and writes its return value back out — the runtime seam the resolver path builds on.
/// </para>
/// </summary>
public sealed class LibraryFunction_InvokeTests : IDisposable
{
    private readonly BlueprintTestFixture _fixture = new();

    public void Dispose() => _fixture.Dispose();

    [Fact]
    public void LibraryFunction_IsRuntimeInvocable_ByName_ThroughFunctionsTable()
    {
        // A Library asset with one Function graph that returns NodeStatus.Success.
        var asset = BlueprintAssetBuilder
            .Library("Stage8Lib")
            .WithGraph("Add", g => g.Entry().Return(NodeStatus.Success))
            .Build();

        _fixture.CompileAndLoad(asset); // compile → load → registrar populates the live BlueprintRegistry

        Assert.True(_fixture.Registry.TryGetByName("Stage8Lib", out var def),
            "Library blueprint should be registered.");
        Assert.Equal(Fdp.Toolkit.Blueprints.BlueprintDispatchKind.Library, def!.Kind);

        // R2: the Function graph is exposed in the Functions table, keyed by graph name.
        Assert.True(def.Functions.ContainsKey("Add"),
            "Library function 'Add' must be registered in the Functions table (G2 R2).");

        // R1: invoke it through the LibraryFunctionDelegate seam and read its NodeStatus return.
        var entity = _fixture.CreateEntity();
        Span<byte> outputs = stackalloc byte[sizeof(int)]; // Fbt.NodeStatus is int-sized
        def.Functions["Add"](ReadOnlySpan<byte>.Empty, outputs, _fixture.View, entity, 0f);

        var status = MemoryMarshal.Read<Fbt.NodeStatus>(outputs);
        Assert.Equal(Fbt.NodeStatus.Success, status);
    }
}
