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
    }
}
