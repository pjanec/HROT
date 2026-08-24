using System;
using System.IO;
using Xunit;

namespace Hrot.Editor.Tests;

/// <summary>
/// <b>The AI-debug API gets the dependencies the composition root already holds.</b>
///
/// <para><b>Why a TEXT rail.</b> <c>EditorSubsystem</c> cannot be constructed headless, so no
/// behavioural rail can see this class of defect: every other rail builds its own composition root
/// and therefore cannot observe what the real one forgot. Reading the file is weaker than running it
/// and honest about that — it is also the only thing that can fail when a dependency stops being
/// passed. (Same shape, and same reasoning, as
/// <c>TheBlueprintLiveWriteLandsTests.TheCompositionRootHandsBlueprintALiveWriter</c>.)</para>
///
/// <para><b>What it caught.</b> MX1 (Group O — variable addressing) shipped and answered "No
/// blueprint debug session is available in this editor" for every call: the editor built the session
/// ~400 lines earlier, handed BTree's and HSM's sessions to <c>DebugApiService</c>, and did not hand
/// it Blueprint's. A held dependency that is not passed is the silent-default defect — the parameter
/// is optional so tests and lightweight hosts need not supply it, which is exactly what makes the
/// omission invisible in a running editor.</para>
/// </summary>
public sealed class DebugApiCompositionTests
{
    [Theory]
    // Group O reads and stages variables through the blueprint debug session's resolver. Without it
    // every /entities/{id}/variable* call refuses.
    [InlineData("blueprintSession:", "Group O (variables) answers 'no blueprint debug session' for every call")]
    // BlueprintTierSummary needs the registry to turn a blackboard slot's int id into the asset Guid
    // the session addresses variables by. Without it no entity resolves to an asset at all.
    [InlineData("blueprintRegistry:", "an entity's attached blueprints cannot be resolved to an asset")]
    // MX4a: GET /behaviors emits each behaviour's param schema from BehaviorDefinition.ParamsDtoType.
    [InlineData("behaviorRegistry:", "GET /behaviors cannot report any behaviour's parameter schema")]
    // The staged-write queue is also what the pending (yellow) flag is read from.
    [InlineData("bpManager:", "breakpoints and the pending-write flag are both unavailable")]
    // HN-029: the editor's own one-node ClusterMaster sits on _orchestrationBus, so the editor CAN
    // request a cluster transition. Without the hand-off POST /scenario/load/live answers
    // NOT_SUPPORTED_HERE(scenario.load) in the EDITOR — the mode that has always been able to load.
    [InlineData("requestTransition:", "POST /scenario/load/live refuses in the editor")]
    public void TheDebugApiServiceIsHandedItsDependencies(string argument, string consequence)
    {
        var call = DebugApiServiceConstruction();

        Assert.True(
            call.Contains(argument, StringComparison.Ordinal),
            $"EditorSubsystem builds DebugApiService without {argument} — {consequence}. "
          + "The editor holds the dependency at this point, so this is a missing hand-off, not a "
          + "deliberate default.");
    }

    /// <summary>The text of the <c>new DebugApiService(…)</c> call in the editor's composition root.</summary>
    private static string DebugApiServiceConstruction()
    {
        var text = File.ReadAllText(RepoFile("Hrot/Subsystems/Hrot.Editor/EditorSubsystem.cs"));

        int start = text.IndexOf("new Hrot.Editor.DebugApi.DebugApiService(", StringComparison.Ordinal);
        Assert.True(start > 0,
            "EditorSubsystem no longer constructs DebugApiService — the AI-debug API has no service "
          + "at all, and every capability endpoint answers 503.");

        // Up to this call's closing ");" — never into whatever follows it.
        int end = text.IndexOf(");", start, StringComparison.Ordinal);
        Assert.True(end > start, "Could not find the end of the DebugApiService construction.");
        return text[start..end];
    }

    private static string RepoFile(string relative)
    {
        var dir = AppContext.BaseDirectory;
        for (int i = 0; i < 12 && dir != null; i++)
        {
            var candidate = Path.Combine(dir, relative);
            if (File.Exists(candidate)) return candidate;
            dir = Directory.GetParent(dir)?.FullName;
        }
        throw new FileNotFoundException($"Could not locate '{relative}' above {AppContext.BaseDirectory}.");
    }
}
