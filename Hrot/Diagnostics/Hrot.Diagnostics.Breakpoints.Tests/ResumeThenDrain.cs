using Fdp.Core;
using Fdp.ModuleHost.Abstractions;

namespace Hrot.Diagnostics.Breakpoints.Tests;

/// <summary>
/// ⭐⭐⭐ <b><c>W5</c> — the two steps a resume used to be ONE of.</b>
/// 📄 <c>DESIGN_Staged_Live_Write.md</c> §6's <c>W5</c> row · <c>DESIGN_Time_Architecture.md</c> §10;
/// 📌 <c>R-63</c>.
///
/// <para>🔴 <b>What changed under these rails.</b> <c>RequestStep</c>/<c>RequestContinue</c> used to
/// <b>restore the post-tick snapshot AND drain the staged mutations</b>. ⭐ <c>W5</c> removed the drain:
/// the kernel's <c>PreFrame</c> <c>ResumeAndDrainSystem</c> is the ONE implementation *(ruling 9)*, and
/// a toolbar pause never calls either request method, so a drain that lived only there could never
/// apply that designer's edit *(<c>R-126</c>: a PULL, so no path can forget to raise it)*.</para>
///
/// <para>⭐⭐ <b>These rails' CLAIMS are unchanged</b> — last-write-wins, the N+1 boundary, the surgical
/// byte range, managed routing. ⛔ Only the step that performs the drain moved, so each site names the
/// two steps instead of one. ⚠ <b>Re-expressed, never deleted</b>: an assertion about the drain's
/// semantics is still exactly as valuable as it was.</para>
///
/// <para>⚠ <b>Why this calls <see cref="IStagedWrites.DrainInto"/> and not a kernel.</b> Most of these
/// rails have no <c>ModuleHostKernel</c> — they drive the manager and the repository directly. ⭐ The
/// seam is the same object the kernel calls, so the rail exercises the production drain; ⛔ it just
/// does not also re-test the kernel's scheduling, which
/// <c>TheToolbarPauseWriteLandsTests</c> does through a real kernel.</para>
/// </summary>
internal static class ResumeThenDrain
{
    /// <summary>⭐ Continue, then let the tick loop's drain run. ⛔ Two steps since <c>W5</c>.</summary>
    public static void ContinueAndDrain(this DataBreakpointManager manager, EntityRepository repo)
    {
        manager.RequestContinue();
        ((IStagedWrites)manager).DrainInto(repo);
    }

    /// <summary>⭐ Step, then let the tick loop's drain run. ⛔ Two steps since <c>W5</c>.</summary>
    public static void StepAndDrain(this DataBreakpointManager manager, EntityRepository repo)
    {
        manager.RequestStep();
        ((IStagedWrites)manager).DrainInto(repo);
    }
}
