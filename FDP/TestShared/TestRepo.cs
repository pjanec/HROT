using System;
using Fdp.Toolkit.Orchestration;

namespace Fdp.Testing
{
    /// <summary>
    /// ⭐⭐⭐ <b>The ONE test-side answer to "where is the repository root?"</b> (<c>CE-3343</c>). Built ON the one production walk-up,
    /// <see cref="ContentLayout.TryFindSourceTree"/> (<c>HROT.sln</c> beside an <c>assets/</c> folder) — ⛔ never a second walk-up: ~50 test
    /// files each carried a private copy (different markers, different failure modes), and a ban rail
    /// (<c>TheTestsHaveOneRepoRootWalkUpTests</c>) now fails the build of a new one.
    /// <para>⭐ ONE source, linked into every test project that needs it (<c>&lt;Compile Include … Link&gt;</c>, like
    /// <c>FlakyFactAttribute</c>) — there is no shared test-helper assembly. <c>internal</c> so two linked assemblies never clash.
    /// Every test project references <c>Fdp.Toolkits</c> (transitively), which is what <see cref="ContentLayout"/> needs.</para>
    /// <para>⚠ Searches upward from <see cref="AppContext.BaseDirectory"/> ONLY (the test binary's folder), not the working directory —
    /// the walk every private copy did; <c>ContentLayout.TryFindSourceTree()</c>'s own default would try the working directory first.</para>
    /// </summary>
    internal static class TestRepo
    {
        private static readonly Lazy<string?> Resolved = new Lazy<string?>(() => ContentLayout.TryFindSourceTree(AppContext.BaseDirectory));

        /// <summary>The repository root (the folder holding <c>HROT.sln</c> beside <c>assets/</c>). Throws a clear message when there is none.</summary>
        public static string Root => Resolved.Value ?? throw NotFound();

        /// <summary>The repository root, or <c>null</c> when there is none — for the few callers that have a fallback (never for a rail that scans sources: use <see cref="Root"/>).</summary>
        public static string? TryFind() => Resolved.Value;

        private static InvalidOperationException NotFound() =>
            new InvalidOperationException(
                "The repository root was not found above the test binary (" + AppContext.BaseDirectory + "): no folder holds " +
                ContentLayout.RepositoryMarkerFileName + " beside an '" + ContentLayout.AssetsDirectoryName + "' folder. " +
                "This test reads repository sources and cannot run without them.");
    }
}
