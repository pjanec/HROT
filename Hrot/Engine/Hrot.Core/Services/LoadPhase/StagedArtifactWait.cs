using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Hrot.Map.Common.ClusterLoad;

/// <summary>
/// ⭐⭐ <c>L6</c> — <b>the bounded wait for an artifact the staging copy is still delivering.</b>
///
/// <para>🔴 <b>The race, measured <c>2026-09-18</c> in one <c>--mode all</c> load:</b> the staging copy
/// started at <c>T+0</c>, the content step was dispatched at <c>T+6 ms</c>, the files were fanned out at
/// <c>T+55 ms</c> and the nodes acknowledged them at <c>T+118 ms</c>. ⇒ <b>the step that consumes the
/// artifacts runs while they are still arriving.</b></para>
///
/// <para>⭐ The scenario step always survived this because it retried; the knowledge-base and terrain
/// steps did NOT retry, and — before the names moved onto the message — they read a missing header and
/// concluded <i>"this scenario names none"</i>, which is legal, silent, and indistinguishable from the
/// truth. Now that a name arrives on the message, the same race surfaces as a <b>missing ARTIFACT</b>
/// instead: loud, but potentially spurious. This closes that gap without touching the transaction saga.</para>
///
/// <para>⛔⛔ <b>What this deliberately is NOT.</b> It does not make the orchestrator order the content
/// step after the staging acknowledgements — that is the complete fix, it restructures the two-phase
/// trajectory that every transition shares (live, edit, preview, replay, idle), and it is recorded as
/// deferred rather than attempted alongside a refactor of the node side.
/// 📄 <c>docs/DESIGN_Cluster_Load_Phase.md</c> §2.4, <c>L6</c>.</para>
/// </summary>
internal static class StagedArtifactWait
{
    /// <summary>⭐ Total budget. Matches the scenario step's, so the three parts fail on the same clock.</summary>
    internal const int MaxAttempts = 100;
    internal const int DelayMs     = 20;

    /// <summary>
    /// ⭐ Waits for <paramref name="path"/> to appear, up to the budget.
    /// Returns <c>true</c> when it exists; <c>false</c> when the budget ran out and the caller should
    /// report a missing artifact LOUDLY.
    /// </summary>
    internal static async Task<bool> ForFileAsync(string path, CancellationToken ct)
    {
        for (int attempt = 0; attempt < MaxAttempts; attempt++)
        {
            if (File.Exists(path)) return true;
            await Task.Delay(DelayMs, ct).ConfigureAwait(false);
        }
        return File.Exists(path);
    }
}
