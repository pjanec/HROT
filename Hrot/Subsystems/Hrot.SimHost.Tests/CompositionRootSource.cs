namespace Hrot.SimHost.Tests
{
    /// <summary>
    /// ⭐⭐ <b>The shared instrument for composition-root SOURCE scans.</b>
    ///
    /// <para>📌 <c>CE-156</c> is why <see cref="StripComments"/> exists at all: a rail asserted
    /// <c>Assert.Contains("TkbTranslatorSet.Base", src)</c> over RAW source and stayed green on a
    /// <b>comment</b> — the prose explaining that the call had MOVED. Two hosts were green for the wrong
    /// reason. ⇒ ⛔ <b>every source scan strips comments first</b>, or it eventually asserts the presence
    /// of the sentence describing its own obsolescence.</para>
    ///
    /// <para>⭐ Extracted from <c>MapPresentationParityRails</c> when a second rail family needed the same
    /// two helpers — ⛔ routed rather than copied, so the <c>CE-156</c> fix cannot rot in one copy while
    /// the other keeps passing on comments.</para>
    /// </summary>
    internal static class CompositionRootSource
    {
        /// <summary>
        /// ⭐ Removes line and block comments so a source scan asserts CODE, never prose.
        /// ⚠ Deliberately crude — a composition-root heuristic, not a C# parser. It does not understand
        /// string literals, which is acceptable here: none of the tokens these rails scan for appear
        /// inside one, and a false RED from that would be loud rather than silent.
        /// </summary>
        internal static string StripComments(string src)
        {
            src = System.Text.RegularExpressions.Regex.Replace(
                src, @"/\*.*?\*/", " ", System.Text.RegularExpressions.RegexOptions.Singleline);
            return System.Text.RegularExpressions.Regex.Replace(
                src, @"//[^\n]*", " ");
        }

        /// <summary>
        /// Repo-root-relative source read; the scan is the only way to see a composition root's local
        /// registration set. ⛔ Throws rather than returning empty if the target moved — an absent file
        /// must be a loud RED, never a vacuous pass.
        /// </summary>
        internal static string ReadRepoSource(string relativePath)
        {
            var dir = new System.IO.DirectoryInfo(System.AppContext.BaseDirectory);
            while (dir != null && !System.IO.Directory.Exists(System.IO.Path.Combine(dir.FullName, "docs")))
                dir = dir.Parent;
            if (dir == null)
                throw new System.InvalidOperationException(
                    "Could not locate the repository root (no ancestor directory contains 'docs').");

            var path = System.IO.Path.Combine(
                dir.FullName, relativePath.Replace('/', System.IO.Path.DirectorySeparatorChar));
            if (!System.IO.File.Exists(path))
                throw new System.IO.FileNotFoundException(
                    $"expected {path} to exist — the rail's target moved.", path);
            return System.IO.File.ReadAllText(path);
        }

        /// <summary>
        /// ⭐⭐⭐ <b><c>CE-260</c> — does this source CONSTRUCT <paramref name="typeName"/>, however it
        /// spells the type?</b> Use this instead of <c>src.Contains("new Foo(")</c>.
        /// </summary>
        /// <remarks>
        /// <para><b>📌 The blindness this exists to remove.</b> 🔎 Found by the "H - ui" session on their
        /// own rail and re-measured here on two of mine: a source-scan that asserts the literal string
        /// <c>"new EntityRotatorGizmo"</c> is <b>GREEN over a file that writes
        /// <c>new Hrot.ScenarioEditor.Gizmos.EntityRotatorGizmo(</c></b> — fully qualified. Their rail had
        /// been green over the very duplicate it forbids ever since <c>CE-051</c>.</para>
        ///
        /// <para>⛔⛔ <b>The direction is what makes it dangerous.</b> A POSITIVE assertion that pins a
        /// spelling merely goes red when someone requalifies — annoying, visible, fixable. ⭐ A NEGATIVE
        /// one — <c>Assert.False(src.Contains(...))</c> — goes <b>SILENTLY GREEN</b>, which is the whole
        /// defect class this repo keeps paying for: a rail that cannot tell "absent" from "spelled
        /// differently".</para>
        ///
        /// <para>⚠ <b>Honest about what this is NOT.</b> It is still TEXT, not the compiler: an alias
        /// (<c>using Bus = Fdp.Core.FdpEventBus;</c> then <c>new Bus()</c>) still slips past, and so does
        /// a factory that hides the construction. ⭐ It closes the QUALIFICATION hole, which is the one
        /// that actually bit — ⛔ it does not turn a text scan into a semantic one. For that the answer is
        /// Roslyn, and the rail should say so rather than imply coverage it has not got.</para>
        /// </remarks>
        /// <summary>
        /// ⭐⭐ Macros debt A7 — <b>a host's EFFECTIVE composition</b>: its root file, comments stripped, PLUS every bootstrapper it hands its
        /// NODE build to, followed transitively. 📌 <c>CE-1446</c> (<c>e405d3662</c>) moved CGF's and the editor's node build out of
        /// <c>CgfSubsystem.cs</c> / <c>EditorSubsystem.cs</c> into <c>CgfNodeBootstrapper</c> / <c>EditorNodeBootstrapper</c>, which delegate to
        /// the shared <c>Hrot.SimHost.NodeBootstrapper</c> — and seven rails reading the root file alone went red on a composition that was
        /// right. ⭐ ONE follower here, so no rail keeps its own copy of the delegation rules.
        /// ⚠ The UNION of the files: a positive check finds the call wherever it moved; a negative check sees every file the host composes with.
        /// </summary>
        internal static string ReadEffectiveRoot(string relativePath)
        {
            var seen = new System.Collections.Generic.HashSet<string>(System.StringComparer.Ordinal);
            var all = new System.Text.StringBuilder();
            void Follow(string path)
            {
                if (!seen.Add(path)) return;
                var code = StripComments(ReadRepoSource(path));
                all.Append(code).Append('\n');
                foreach (var (type, file) in Delegates)
                    if (ConstructsType(code, type) || code.Contains(type + " _nodeBootstrapper = new(", System.StringComparison.Ordinal)
                                                    || code.Contains(type + "? _bootstrapper", System.StringComparison.Ordinal))
                        Follow(file);
                // ⭐ CE-4270 (§4.1ag) — a host that builds its pack through the ONE shared spawning step wires what that step wires.
                if (code.Contains("HostSpawning.Register(", System.StringComparison.Ordinal))
                    Follow(HostSpawningFile);
                // ⭐ CE-4270 B9 — a host that resolves the ONE role table composes what that table composes.
                if (code.Contains("RoleCapabilityTable.Plan(", System.StringComparison.Ordinal))
                    Follow(RoleTableFile);
            }
            Follow(relativePath);
            return all.ToString();
        }

        /// <summary>⭐ CE-4270 — the ONE shared spawning step (where the pack is built and scheduled for every host that calls it).</summary>
        internal const string HostSpawningFile = "Hrot/Engine/Hrot.EcsNode/Hosting/HostSpawning.cs";

        /// <summary>⭐ CE-4270 B9 — the ONE role table (CGF, the fused server and the editor resolve their declared role against it).</summary>
        internal const string RoleTableFile = "Hrot/Subsystems/Hrot.CGF/RoleCapabilityTable.cs";

        /// <summary>The node bootstrappers a composition root may hand its node build to (type → file).</summary>
        private static readonly (string Type, string File)[] Delegates =
        {
            ("CgfNodeBootstrapper",            "Hrot/Subsystems/Hrot.CGF/CgfNodeBootstrapper.cs"),
            ("EditorNodeBootstrapper",         "Hrot/Subsystems/Hrot.Editor/EditorNodeBootstrapper.cs"),
            ("Hrot.SimHost.NodeBootstrapper",  "Hrot/Engine/Hrot.EcsNode/NodeBootstrapper.cs"),
        };

        internal static bool ConstructsType(string src, string typeName)
        {
            // new  [Optional.Dotted.Qualification.]TypeName  followed by ( or <
            string pattern =
                "\\bnew\\s+(?:[A-Za-z_][A-Za-z0-9_]*\\s*\\.\\s*)*"
                + System.Text.RegularExpressions.Regex.Escape(typeName)
                + "\\s*[(<]";
            return System.Text.RegularExpressions.Regex.IsMatch(src, pattern);
        }
    }
}
