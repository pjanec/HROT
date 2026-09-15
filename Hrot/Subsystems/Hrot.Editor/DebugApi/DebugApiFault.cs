using System;
using System.Diagnostics;
using System.Text;
using System.Text.Json.Nodes;

namespace Hrot.Editor.DebugApi
{
    /// <summary>
    /// ⭐⭐⭐ <c>CE-190</c> — <b>an exception that reaches the envelope must say WHERE IT HAPPENED.</b>
    /// </summary>
    /// <remarks>
    /// <para>🔒 <b>User, <c>2026-09-05</c>:</b> <i>"ex.message in mcp response is not enough, can we add
    /// source tracking (where the exception happened?)"</i></para>
    ///
    /// <para>📌 <b>The defect this fixes.</b> Both 500 paths in <see cref="DebugApiHost"/> reported
    /// <c>ex.Message</c> and nothing else. For the exception types that actually show up mid-refactor that
    /// is worthless: a <c>NullReferenceException</c> reads as <i>"Object reference not set to an instance of
    /// an object."</i> — no type, no method, no file, no line. The agent on the other end of MCP then has a
    /// failure it cannot locate, and the next move is a manual bisect of the whole route.</para>
    ///
    /// <para>⭐⭐ <b>Two outputs, because two consumers read two different things</b> — measured, not
    /// assumed, in <c>tools/ai-debug-mcp/src/index.mjs</c>:</para>
    /// <list type="number">
    ///   <item><b><see cref="OneLine"/> → the envelope's <c>error</c> string.</b> <c>:215</c> does
    ///   <c>const msg = envelope?.error</c> and that string becomes the <c>McpToolError</c> message — for
    ///   some callers it is ALL they ever see. ⇒ it must carry the type and the throw site inline, not just
    ///   the message.</item>
    ///   <item><b><see cref="Describe"/> → the envelope's <c>fault</c> object.</b> <c>:296</c> spreads the
    ///   whole server envelope into the MCP error payload (<c>...(envelope || {})</c>), so a new field
    ///   arrives verbatim with <b>no node-side change</b>. That is where the full frame list and the inner
    ///   chain go.</item>
    /// </list>
    ///
    /// <para>⚠ <b>File and line depend on PDBs being next to the assembly.</b> A dev build has them and the
    /// site reads <c>DebugApiService.cs:1234</c>; without them it degrades to
    /// <c>Type.Method +IL_0042</c>. ⛔ It never degrades to nothing — an unlocatable failure is the thing
    /// this exists to prevent.</para>
    /// </remarks>
    internal static class DebugApiFault
    {
        /// <summary>How many frames of the stack to report. Enough to see the caller chain, bounded so a
        /// deep recursion cannot make the response enormous.</summary>
        private const int MaxFrames = 24;

        /// <summary>How far down the <see cref="Exception.InnerException"/> chain to walk.</summary>
        private const int MaxInnerDepth = 5;

        /// <summary>
        /// The one-line summary for the envelope's <c>error</c>: <c>Type: message @ File.cs:line in Method</c>.
        /// </summary>
        internal static string OneLine(Exception ex)
        {
            Exception e = Unwrap(ex);
            Exception origin = Innermost(e);

            var sb = new StringBuilder();
            sb.Append(origin.GetType().FullName).Append(": ").Append(origin.Message);

            string? site = SiteOf(origin);
            if (site != null)
                sb.Append(" @ ").Append(site);

            // When the exception was wrapped, say so — otherwise the site looks like it contradicts the
            // type the caller's own catch would have seen.
            if (!ReferenceEquals(origin, e))
                sb.Append(" (wrapped in ").Append(e.GetType().Name).Append(')');

            return sb.ToString();
        }

        /// <summary>
        /// The structured <c>fault</c> object: type, message, throw site, frames, and the inner chain.
        /// </summary>
        internal static JsonObject Describe(Exception ex)
        {
            Exception e = Unwrap(ex);
            Exception origin = Innermost(e);

            var o = new JsonObject
            {
                ["type"]    = origin.GetType().FullName,
                ["message"] = origin.Message,
                ["site"]    = SiteOf(origin),
                ["frames"]  = Frames(origin),
            };

            // The wrapper's own type is worth keeping: an AggregateException or a TargetInvocationException
            // tells you the call crossed a Task or a reflection boundary, which changes where you look.
            if (!ReferenceEquals(origin, ex))
                o["wrappedIn"] = ex.GetType().FullName;

            JsonArray chain = InnerChain(e);
            if (chain.Count > 0)
                o["inner"] = chain;

            return o;
        }

        // ── the pieces ────────────────────────────────────────────────────────

        /// <summary>
        /// A single-inner <see cref="AggregateException"/> is Task plumbing, not information.
        /// </summary>
        /// <remarks>
        /// ⭐ <see cref="MainThreadJobQueue"/> completes its jobs with <c>TrySetException</c>, so every fault
        /// raised on the main thread arrives here wrapped. ⛔ Reporting the site of the aggregate would point
        /// at the await, never at the bug. ⚠ A MULTI-inner aggregate is left alone — collapsing it would
        /// discard real branches.
        /// </remarks>
        private static Exception Unwrap(Exception ex)
            => ex is AggregateException agg && agg.InnerExceptions.Count == 1
                ? Unwrap(agg.InnerExceptions[0])
                : ex;

        /// <summary>The deepest cause — where the failure actually started.</summary>
        private static Exception Innermost(Exception ex)
        {
            Exception cur = ex;
            for (int i = 0; i < MaxInnerDepth && cur.InnerException != null; i++)
                cur = cur.InnerException;
            return cur;
        }

        /// <summary>
        /// The throw site: the first frame carrying a file, else the first frame at all.
        /// </summary>
        /// <remarks>
        /// ⚠ Frame 0 of an exception's own trace IS the throw point. ⭐ We still scan for the first frame
        /// with file info rather than taking frame 0 blindly: a throw from a framework method (a
        /// <c>JsonSerializer</c> internal, say) has no source, and the first OUR-code frame is what the
        /// reader can actually open.
        /// </remarks>
        private static string? SiteOf(Exception ex)
        {
            var trace = new StackTrace(ex, fNeedFileInfo: true);
            StackFrame[] frames = trace.GetFrames();
            if (frames.Length == 0)
                return null;

            foreach (StackFrame f in frames)
            {
                string? file = f.GetFileName();
                if (!string.IsNullOrEmpty(file))
                    return $"{ShortFile(file)}:{f.GetFileLineNumber()} in {MethodOf(f)}";
            }

            return MethodOf(frames[0]) + IlSuffix(frames[0]);
        }

        private static JsonArray Frames(Exception ex)
        {
            var arr = new JsonArray();
            var trace = new StackTrace(ex, fNeedFileInfo: true);

            int n = 0;
            foreach (StackFrame f in trace.GetFrames())
            {
                if (n++ >= MaxFrames) { arr.Add("… truncated"); break; }

                string? file = f.GetFileName();
                arr.Add(string.IsNullOrEmpty(file)
                    ? MethodOf(f) + IlSuffix(f)
                    : $"{MethodOf(f)} ({ShortFile(file)}:{f.GetFileLineNumber()})");
            }

            return arr;
        }

        private static JsonArray InnerChain(Exception ex)
        {
            var arr = new JsonArray();
            Exception? cur = ex.InnerException;

            for (int i = 0; i < MaxInnerDepth && cur != null; i++, cur = cur.InnerException)
            {
                arr.Add(new JsonObject
                {
                    ["type"]    = cur.GetType().FullName,
                    ["message"] = cur.Message,
                    ["site"]    = SiteOf(cur),
                });
            }

            return arr;
        }

        private static string MethodOf(StackFrame f)
        {
            var m = f.GetMethod();
            if (m == null) return "<unknown>";
            string declaring = m.DeclaringType?.Name ?? "<global>";
            return declaring + "." + m.Name;
        }

        private static string IlSuffix(StackFrame f)
        {
            int il = f.GetILOffset();
            return il == StackFrame.OFFSET_UNKNOWN ? "" : $" +IL_{il:X4}";
        }

        /// <summary>
        /// The file name plus its immediate directory — enough to disambiguate the many same-named files in
        /// this repo without pasting an absolute build-machine path into every error.
        /// </summary>
        private static string ShortFile(string path)
        {
            string name = System.IO.Path.GetFileName(path);
            string? dir  = System.IO.Path.GetFileName(System.IO.Path.GetDirectoryName(path) ?? "");
            return string.IsNullOrEmpty(dir) ? name : dir + "/" + name;
        }
    }
}
