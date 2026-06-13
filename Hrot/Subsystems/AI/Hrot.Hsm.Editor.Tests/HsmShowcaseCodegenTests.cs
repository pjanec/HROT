using System;
using FluentAssertions;
using Xunit;

namespace Hrot.Hsm.Editor.Tests;

/// <summary>
/// Runtime-execution guard for the generated HsmShowcase machine.
/// The editor's registrar calls <c>HsmShowcase.Compile()</c> at boot (via
/// HsmShowcaseRegistrar.Register); a generated builder that references a GoTo target
/// before it is declared throws "Target state '…' not found" and crashes the editor
/// on boot. Compiling the asset is NOT enough — the builder must actually RUN.
/// This test executes the generated builder to catch that class of failure.
/// </summary>
public sealed class HsmShowcaseCodegenTests
{
    [Fact]
    public void HsmShowcase_CreateBuilder_DoesNotThrow()
    {
        var ex = Record.Exception(() => Hrot.AI.Behaviors.Machines.HsmShowcase.CreateBuilder());
        ex.Should().BeNull(
            "the generated builder must resolve every GoTo target (no forward references) — " +
            "otherwise the editor crashes at boot when the registrar runs");
    }

    [Fact]
    public void HsmShowcase_Compile_DoesNotThrow_AndProducesBlob()
    {
        var ex = Record.Exception(() => Hrot.AI.Behaviors.Machines.HsmShowcase.Compile());
        ex.Should().BeNull("Compile() is what the registrar invokes at editor boot");

        var blob = Hrot.AI.Behaviors.Machines.HsmShowcase.Compile();
        blob.Should().NotBeNull();
    }
}
