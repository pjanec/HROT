using System;
using System.Text;
using System.Threading;
using Hrot.Editor;

namespace Hrot.ClusterRunner.Integration.Tests.Eqs;

/// <summary>Shared by the constructed-editor rails: pump the editor's frames until a condition holds (e.g. the run gate is open — <c>CE-1442</c>: a fresh host is closed until its view has heard the cluster).</summary>
internal static class EditorPump
{
    internal static void Pump(EditorSubsystem editor, Func<bool> until, double timeoutSeconds, string what)
    {
        var end = DateTime.UtcNow.AddSeconds(timeoutSeconds);
        var trace = new StringBuilder(); var next = DateTime.UtcNow;
        while (!until())
        {
            if (DateTime.UtcNow > next) { trace.Append("\n  ").Append(Diag(editor)); next = DateTime.UtcNow.AddSeconds(6); }
            if (DateTime.UtcNow > end) throw new TimeoutException($"timed out ({timeoutSeconds:0} s) waiting for: {what}; {Diag(editor)}{trace}");
            editor.Update(0.016f);
            Thread.Sleep(10);
        }
    }

    internal static string Diag(EditorSubsystem e)
        => $"viewState={e.NodeForTest?.ClusterSlave.View.State} followed={e.NodeForTest?.ClusterSlave.View.HasFollowedState} mode={e.TimeTransportForTest?.Mode} master={e.ClusterMasterForTest?.CurrentClusterState} pending={e.RunChoiceForTest?.Pending?.Text} checking={e.RunChoiceForTest?.IsCheckingUnsaved} held={e.ClusterMasterForTest?.HeldLoadRequestId} notice={e.ClusterMasterForTest?.LoadNotice} roster={e.ClusterMasterForTest?.NodeRoster.ActiveNodes.Count} slave={e.NodeForTest?.ClusterSlave.LocalStateIdForTest}";

    internal static void PumpFor(EditorSubsystem editor, double seconds)
    {
        var end = DateTime.UtcNow.AddSeconds(seconds);
        while (DateTime.UtcNow < end) { editor.Update(0.016f); Thread.Sleep(10); }
    }
}
