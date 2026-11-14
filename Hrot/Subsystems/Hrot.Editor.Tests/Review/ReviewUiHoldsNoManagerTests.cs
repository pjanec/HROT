using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Fdp.Toolkit.ReplayBrowser.Federation;
using Xunit;

namespace Hrot.Editor.Tests.Review;

/// <summary>
/// ⭐ CE-4496 R-2 — acceptance ④ of docs/DESIGN_Threaded_Review_Source.md §3: <b>no review UI type references <see cref="FederatedReplayManager"/></b> — not in a field, a property, a constructor or
/// method parameter, a return type, nor inside a generic argument (<c>Func&lt;FederatedReplayManager?&gt;</c> was the old shape). The UI reads the published <see cref="ReviewReadModel"/> and commands through
/// <see cref="IReviewCommands"/>; only the hosts (ReviewSource, ReplayBrowserSubsystem) hold the manager.
/// 🔴 Red-proof: give <c>ByNodeSection</c> back a <c>Func&lt;FederatedReplayManager?&gt;</c> property ⇒ this rail names it.
/// </summary>
public sealed class ReviewUiHoldsNoManagerTests
{
    private const BindingFlags All = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;

    /// <summary>Every review UI type: the Replay Browser's panels (and the window that hosts the Federation panel), the event timeline's federated cursor, the review lane / search / kit, and the editor's Source surfaces.</summary>
    private static IEnumerable<Type> ReviewUiTypes()
    {
        var presentation = typeof(Fdp.Presentation.Panels.ReplayBrowser.FederationPanel).Assembly;
        foreach (var t in presentation.GetTypes())
            if (t.Namespace is "Fdp.Presentation.Panels.ReplayBrowser" or "Fdp.Presentation.Windows.ReplayBrowser") yield return t;
        yield return typeof(Fdp.Presentation.Panels.Timeline.FederatedReplayCursor);

        foreach (var t in typeof(Hrot.Editor.AiShared.Review.ReplicaWatchLane).Assembly.GetTypes())
            if (t.Namespace == "Hrot.Editor.AiShared.Review") yield return t;

        yield return typeof(Hrot.Editor.Review.ReviewUi);
        yield return typeof(Hrot.Editor.Review.ReviewSourceStatusSection);
        yield return typeof(Hrot.Editor.Review.ReviewSourceSwitch);
        yield return typeof(Hrot.Editor.Review.ReviewSourceFace);
        yield return typeof(Hrot.Editor.Review.ReviewSourceToolbarCombo);
        yield return typeof(Hrot.Editor.Review.ReviewFederationWindowHost);
    }

    private static bool Mentions(Type? t, HashSet<Type> seen)
    {
        if (t is null || !seen.Add(t)) return false;
        if (t == typeof(FederatedReplayManager)) return true;
        if (t.HasElementType && Mentions(t.GetElementType(), seen)) return true;
        if (t.IsGenericType) foreach (var a in t.GetGenericArguments()) if (Mentions(a, seen)) return true;
        return false;
    }

    private static bool Mentions(Type? t) => Mentions(t, new HashSet<Type>());

    [Fact]
    public void NoReviewUiType_HoldsOrTakesTheFederationManager()
    {
        var types = ReviewUiTypes().Distinct().ToArray();
        Assert.True(types.Length >= 20, "the scan sees the review UI (anti-vacuity): " + types.Length);
        Assert.Contains(typeof(Fdp.Presentation.Panels.ReplayBrowser.ByNodeSection), types);
        Assert.Contains(typeof(Hrot.Editor.AiShared.Review.ReplicaWatchLane), types);

        var hits = new List<string>();
        foreach (var t in types)
        {
            foreach (var f in t.GetFields(All)) if (Mentions(f.FieldType)) hits.Add($"{t.Name}.{f.Name} (field)");
            foreach (var p in t.GetProperties(All)) if (Mentions(p.PropertyType)) hits.Add($"{t.Name}.{p.Name} (property)");
            foreach (var c in t.GetConstructors(All))
                foreach (var a in c.GetParameters()) if (Mentions(a.ParameterType)) hits.Add($"{t.Name}..ctor({a.Name})");
            foreach (var m in t.GetMethods(All))
            {
                if (Mentions(m.ReturnType)) hits.Add($"{t.Name}.{m.Name} (returns)");
                foreach (var a in m.GetParameters()) if (Mentions(a.ParameterType)) hits.Add($"{t.Name}.{m.Name}({a.Name})");
            }
        }
        Assert.True(hits.Count == 0, "review UI types that still reference FederatedReplayManager:\n  " + string.Join("\n  ", hits));
    }

    [Fact]
    public void TheScan_FindsAManagerInsideAGenericArgument()   // the rail's own red-proof: the old Func<FederatedReplayManager?> shape is caught
    {
        Assert.True(Mentions(typeof(Func<FederatedReplayManager?>)));
        Assert.True(Mentions(typeof(FederatedReplayManager[])));
        Assert.False(Mentions(typeof(Func<ReviewReadModel?>)));
    }
}
