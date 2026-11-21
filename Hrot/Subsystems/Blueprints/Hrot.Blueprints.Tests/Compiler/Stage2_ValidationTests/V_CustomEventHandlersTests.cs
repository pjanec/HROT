using Fdp.Toolkit.Blueprints;
using Hrot.Blueprints.Core.Assets;
using Hrot.Blueprints.Core.Compiler;
using Hrot.Blueprints.Core.Compiler.Catalogs;
using Hrot.Blueprints.Core.Compiler.Diagnostics;
using Hrot.Blueprints.Core.Compiler.Stages;
using Hrot.Blueprints.Tests.Builders;

namespace Hrot.Blueprints.Tests.Compiler;

/// <summary>
/// BP-12c — a declared custom event is only half a custom event.
///
/// <para>
/// The declaration gives the call node its argument pins; the body is an Event graph of the same
/// name, from which <c>InstanceEmitter</c> emits <c>Event_{Name}(…)</c>. Calling an event with no
/// such graph — or one whose input list has a different arity — produces generated C# that does not
/// compile, and until this validator that surfaced only as a Roslyn error naming a method the
/// designer never wrote.
/// </para>
/// </summary>
public sealed class V_CustomEventHandlersTests
{
    private static CompileOptions DefaultOptions() =>
        new CompileOptions(
            Mode:              CompilerMode.Debug,
            NodeRegistry:      BuiltInNodeRegistry.Instance,
            TypeRegistry:      StaticTypeRegistry.Instance,
            EngineEvents:      BuiltInEngineEventCatalog.Instance,
            ChannelCommands:   BuiltInChannelCommandCatalog.Instance,
            WaitPrimitives:    BuiltInWaitPrimitiveCatalog.Instance,
            SiblingSignatures: Array.Empty<BlueprintSignature>());

    private static IReadOnlyList<Diagnostic> Validate(BlueprintAsset asset)
    {
        var sink = new DiagnosticSink();
        Stage2_Validate.Run(asset, new ValidationContext(sink, DefaultOptions()));
        return sink.All;
    }

    // ── BP1407: declared, called, unhandled ───────────────────────────────────

    [Fact]
    [CoversDiagnosticCode("BP1407")]
    public void CallToDeclaredEventWithNoHandlerGraph_EmitsBP1407()
    {
        var asset = BlueprintAssetBuilder
            .Instance("NoHandler")
            .WithCustomEvent("OnFire")
            .WithGraph("Tick", g => g.Entry().CallCustomEvent("OnFire").Return())
            .Build();

        var diags = Validate(asset);

        var d = Assert.Single(diags, x => x.Code == DiagnosticCodes.BP1407);
        Assert.True(d.IsError);
        Assert.Contains("Event_OnFire", d.Message);
    }

    /// <summary>
    /// The shape the new create path produces on its own: a declaration nobody calls yet. That must
    /// stay silent — otherwise clicking "Custom Events +" would immediately break the build, and
    /// there is no way to author the paired Event graph in the editor yet (BP-24).
    /// </summary>
    [Fact]
    public void DeclaredButNeverCalled_IsSilent()
    {
        var asset = BlueprintAssetBuilder
            .Instance("DeclOnly")
            .WithCustomEvent("OnFire")
            .WithGraph("Tick", g => g.Entry().Return())
            .Build();

        Assert.DoesNotContain(Validate(asset), d => d.Code == DiagnosticCodes.BP1407);
    }

    [Fact]
    public void CallWithMatchingHandlerGraph_IsSilent()
    {
        var asset = BlueprintAssetBuilder
            .Instance("Handled")
            .WithCustomEvent("OnFire")
            .WithGraph("Tick", g => g.Entry().CallCustomEvent("OnFire").Return())
            .WithEventGraph("OnFire", g => g.Entry().Return())
            .Build();

        var diags = Validate(asset);

        Assert.DoesNotContain(diags, d => d.Code == DiagnosticCodes.BP1407);
        Assert.DoesNotContain(diags, d => d.Code == DiagnosticCodes.BP1408);
    }

    /// <summary>
    /// A GUID reference resolves the same way a name does — the editor's picker writes the GUID
    /// form, so it is the shape that actually ships.
    /// </summary>
    [Fact]
    public void GuidReference_ResolvesTheSameAsAName()
    {
        var asset = BlueprintAssetBuilder
            .Instance("ByGuid")
            .WithCustomEvent("OnFire")
            .WithGraph("Tick", g => g.Entry().CallCustomEvent("OnFire").Return())
            .Build();

        // Rewrite the call to the canonical GUID form the BP-07 picker writes.
        var call = asset.Graphs
            .SelectMany(g => g.Nodes)
            .OfType<CallCustomEventNode>()
            .Single();
        call.EventId = asset.CustomEvents[0].Id.ToString("D");

        Assert.Contains(Validate(asset), d => d.Code == DiagnosticCodes.BP1407);
    }

    /// <summary>
    /// An unresolvable id is BP1403's business, not this validator's — one bad reference must not
    /// produce two diagnostics saying different things.
    /// </summary>
    [Fact]
    public void UnknownEventId_IsLeftToBP1403()
    {
        var asset = BlueprintAssetBuilder
            .Instance("Unknown")
            .WithCustomEvent("OnFire")
            .WithGraph("Tick", g => g.Entry().CallCustomEvent("NotDeclared").Return())
            .Build();

        var diags = Validate(asset);

        Assert.Contains(diags, d => d.Code == DiagnosticCodes.BP1403);
        Assert.DoesNotContain(diags, d => d.Code == DiagnosticCodes.BP1407);
    }

    // ── BP1408: handler arity mismatch ────────────────────────────────────────

    [Fact]
    [CoversDiagnosticCode("BP1408")]
    public void HandlerGraphWithWrongArity_EmitsBP1408()
    {
        var asset = BlueprintAssetBuilder
            .Instance("Arity")
            .WithCustomEvent("OnHit", ("Damage", typeof(float)))
            .WithGraph("Tick", g => g.Entry().CallCustomEvent("OnHit").Return())
            .WithEventGraph("OnHit", g => g.Entry().Return())   // takes no inputs
            .Build();

        var diags = Validate(asset);

        var d = Assert.Single(diags, x => x.Code == DiagnosticCodes.BP1408);
        Assert.True(d.IsError);
        Assert.DoesNotContain(diags, x => x.Code == DiagnosticCodes.BP1407);
    }

    [Fact]
    public void HandlerGraphWithMatchingArity_IsSilent()
    {
        var asset = BlueprintAssetBuilder
            .Instance("ArityOk")
            .WithCustomEvent("OnHit", ("Damage", typeof(float)))
            .WithGraph("Tick", g => g.Entry().CallCustomEvent("OnHit").Return())
            .WithEventGraph("OnHit", g => g.WithInput("Damage", "System.Single").Entry().Return())
            .Build();

        Assert.DoesNotContain(Validate(asset), d => d.Code == DiagnosticCodes.BP1408);
    }
}
