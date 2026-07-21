using System.Runtime.InteropServices;
using ComTypes = System.Runtime.InteropServices.ComTypes;

namespace Hrot.Blueprints.Editor.Host;

/// <summary>
/// Editor punch-list #6 — opens a source file at a specific line in a <b>running Visual Studio</b>
/// (devenv) instance, via the Windows Running Object Table (ROT) + the VS <c>DTE</c> automation object.
///
/// <para>
/// We enumerate every registered <c>!VisualStudio.DTE.*</c> instance, prefer the one whose open
/// solution directory contains the target file (so the right window is used when several VS windows
/// are open), and drive <c>ItemOperations.OpenFile</c> + <c>Selection.GotoLine</c> on it. DTE is
/// accessed via <c>dynamic</c> (late-bound IDispatch) so no EnvDTE NuGet reference is needed.
/// </para>
///
/// <para>Windows-only; every failure path returns <c>false</c> so the caller can fall back to a CLI launch.</para>
/// </summary>
internal static class VisualStudioDteOpener
{
    [DllImport("ole32.dll")]
    private static extern int GetRunningObjectTable(int reserved, out ComTypes.IRunningObjectTable prot);

    [DllImport("ole32.dll")]
    private static extern int CreateBindCtx(int reserved, out ComTypes.IBindCtx ppbc);

    private const uint RPC_E_CALL_REJECTED = 0x8001010A;

    /// <summary>
    /// Tries to open <paramref name="filePath"/> at <paramref name="line"/> in a running VS instance.
    /// Returns true only when a VS instance actually accepted the command.
    /// </summary>
    public static bool TryOpenAtLine(string filePath, int line)
    {
        if (!OperatingSystem.IsWindows() || string.IsNullOrEmpty(filePath)) return false;

        try
        {
            string fileFull = NormalizePath(filePath);
            object? best = null;      // DTE whose solution contains the file
            object? anyRunning = null; // first DTE with any solution open (fallback)

            foreach (var dte in EnumerateDteInstances())
            {
                try
                {
                    anyRunning ??= dte;
                    string? slnDir = SolutionDir((dynamic)dte);
                    if (slnDir != null && fileFull.StartsWith(slnDir, StringComparison.OrdinalIgnoreCase))
                    {
                        best = dte;
                        break;
                    }
                }
                catch (COMException) { /* instance busy/spinning up — skip */ }
            }

            var target = best ?? anyRunning;
            if (target == null) return false;

            return ExecuteJump((dynamic)target, fileFull, line);
        }
        catch
        {
            return false;
        }
    }

    private static string? SolutionDir(dynamic dte)
    {
        try
        {
            string? full = dte.Solution?.FullName as string;
            if (string.IsNullOrEmpty(full)) return null;
            var dir = Path.GetDirectoryName(Path.GetFullPath(full));
            return dir == null ? null : dir.TrimEnd('\\') + "\\";
        }
        catch
        {
            return null;
        }
    }

    private static bool ExecuteJump(dynamic dte, string filePath, int line)
    {
        const int maxRetries = 5;
        for (int i = 0; i < maxRetries; i++)
        {
            try
            {
                dte.ItemOperations.OpenFile(filePath);
                object? sel = dte.ActiveDocument?.Selection;
                if (sel != null && line > 0)
                    ((dynamic)sel).GotoLine(line, true);
                try { dte.MainWindow.Activate(); } catch { /* non-fatal */ }
                return true;
            }
            catch (COMException ex) when ((uint)ex.HResult == RPC_E_CALL_REJECTED)
            {
                System.Threading.Thread.Sleep(200); // VS busy (building/menu open) — back off and retry
            }
            catch (COMException)
            {
                return false;
            }
        }
        return false;
    }

    private static IEnumerable<object> EnumerateDteInstances()
    {
        if (GetRunningObjectTable(0, out var rot) != 0) yield break;

        rot.EnumRunning(out var enumMoniker);
        enumMoniker.Reset();

        var monikers = new ComTypes.IMoniker[1];
        while (enumMoniker.Next(1, monikers, IntPtr.Zero) == 0)
        {
            var moniker = monikers[0];
            if (moniker == null) continue;

            string? name = null;
            try
            {
                CreateBindCtx(0, out var bindCtx);
                moniker.GetDisplayName(bindCtx, null, out name);
            }
            catch (COMException) { }

            if (name == null || !name.StartsWith("!VisualStudio.DTE.", StringComparison.Ordinal))
                continue;

            object? obj = null;
            try { rot.GetObject(moniker, out obj); }
            catch (COMException) { }

            if (obj != null) yield return obj;
        }
    }

    private static string NormalizePath(string p)
    {
        try { return Path.GetFullPath(p); }
        catch { return p; }
    }
}
