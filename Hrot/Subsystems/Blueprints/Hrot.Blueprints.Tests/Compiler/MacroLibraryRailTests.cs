using Hrot.Blueprints.Core.Assets;
using Hrot.Blueprints.Core.Compiler;
using Hrot.Blueprints.Core.Compiler.Catalogs;
using Hrot.Blueprints.Core.Compiler.Diagnostics;
using BlueprintDispatchKind = Hrot.Blueprints.Core.Assets.BlueprintDispatchKind;

namespace Hrot.Blueprints.Tests.Compiler;

/// <summary>
/// BP-82 / Q25-C2 — a <b>macro library</b>: a Library asset that declares macros and no functions.
///
/// <para>
/// ⭐ <b>The shape the macro feature was built to allow, and the one <c>BP5001</c> rejected.</b> Such
/// an asset has zero Function graphs by design — every call site lives in the assets that consume it,
/// and expansion happens there.
/// </para>
///
/// <para>
/// ⚠ <b>Why it needed a change at all, given macros were already "skipped".</b> Stage 5 skips macro
/// graphs because they are declarations rather than compilation targets, and <c>IrGraphKind</c> has no
/// Macro member — so by lowering time a macro library and an empty library are <b>indistinguishable</b>.
/// The skip that made macros safe everywhere else is exactly what made this rule wrong.
/// </para>
/// </summary>
public sealed class MacroLibraryRailTests
{
    private static CompileOptions DefaultOptions() => new(
        Mode:              CompilerMode.Debug,
        NodeRegistry:      BuiltInNodeRegistry.Instance,
        TypeRegistry:      StaticTypeRegistry.Instance,
        EngineEvents:      BuiltInEngineEventCatalog.Instance,
        ChannelCommands:   BuiltInChannelCommandCatalog.Instance,
        WaitPrimitives:    BuiltInWaitPrimitiveCatalog.Instance,
        SiblingSignatures: Array.Empty<BlueprintSignature>());

    private static Pin P(string name, string dir, bool isExec) => new()
    {
        Id = Guid.NewGuid(), Name = name, Direction = dir, IsExec = isExec,
        TypeRef = new BlueprintTypeRef(),
    };

    /// <summary>Entry → Return: the minimum a graph needs to schedule.</summary>
    private static Graph Body(string name, GraphKind kind)
    {
        var entry = new EventEntryNode { Id = Guid.NewGuid(), EventTypeId = "" };
        var eOut  = P("Out", "Out", true); entry.Pins.Add(eOut);
        var ret   = new ReturnNode { Id = Guid.NewGuid() };
        var rIn   = P("In", "In", true); ret.Pins.Add(rIn);

        return new Graph
        {
            Id = Guid.NewGuid(), Name = name, Kind = kind,
            Nodes = { entry, ret },
            Links = { new Link { FromNodeId = entry.Id, FromPinId = eOut.Id, ToNodeId = ret.Id, ToPinId = rIn.Id } },
        };
    }

    private static BlueprintAsset Library(string name, params Graph[] graphs) => new()
    {
        AssetId  = Guid.NewGuid(), Name = name,
        Dispatch = BlueprintDispatchKind.Library,
        Graphs   = graphs.ToList(), Header = new Header(),
    };

    private static string[] Codes(BlueprintAsset asset)
        => new BlueprintCompiler().Compile(asset, DefaultOptions())
            .Diagnostics.Select(d => d.Code).ToArray();

    // ────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// ⭐ <b>The rail.</b> A Library declaring only macros compiles without <c>BP5001</c>.
    /// </summary>
    [Fact]
    public void MacroOnlyLibrary_IsNotReportedAsExposingNothing()
    {
        var asset = Library("MacroLib", Body("AimFire", GraphKind.Macro));

        Assert.DoesNotContain(DiagnosticCodes.BP5001, Codes(asset));
    }

    /// <summary>
    /// ⚠ The rail must not swallow the real case. A Library with neither functions nor macros still
    /// exposes nothing, and saying so is the whole point of the diagnostic.
    /// </summary>
    [Fact]
    public void LibraryWithNeitherFunctionsNorMacros_StillReportsBP5001()
    {
        var asset = Library("EmptyLib");

        Assert.Contains(DiagnosticCodes.BP5001, Codes(asset));
    }

    /// <summary>A Library with functions is unaffected, macros or not.</summary>
    [Fact]
    public void LibraryWithFunctions_IsUnaffected()
    {
        var withBoth = Library("MixedLib",
            Body("Helper", GraphKind.Function), Body("AimFire", GraphKind.Macro));

        Assert.DoesNotContain(DiagnosticCodes.BP5001, Codes(withBoth));
    }

    /// <summary>
    /// 📌 <c>BP9001</c> needed no narrowing, and this pins the reason: a latent node inside a macro
    /// DECLARATION reaches no IR graph at all, so the library-latency rule cannot see it. It is
    /// flagged where it actually lands — spliced into the graph that called the macro.
    /// </summary>
    [Fact]
    public void LatentNodeInsideAMacroDeclaration_DoesNotTripTheLibraryLatencyRule()
    {
        var macro = Body("Waiter", GraphKind.Macro);
        var delay = new LatentDelayNode { Id = Guid.NewGuid() };
        delay.Pins.Add(P("In", "In", true));
        delay.Pins.Add(P("Out", "Out", true));
        macro.Nodes.Add(delay);

        var asset = Library("LatentMacroLib", macro);

        Assert.DoesNotContain(DiagnosticCodes.BP9001, Codes(asset));
    }
}
