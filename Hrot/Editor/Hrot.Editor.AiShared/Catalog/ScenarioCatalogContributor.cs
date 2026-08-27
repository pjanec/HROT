using System.Security.Cryptography;
using System.Text;
namespace Hrot.Editor.AiShared.Catalog;

/// <summary>
/// Projects the editor-side scenario list into the asset catalog as
/// <see cref="AssetKind.Scenario"/> entries.
/// </summary>
/// <remarks>
/// <para>⭐⭐⭐ <b><c>CE-053</c> — MOVED to <c>Hrot.Editor.AiShared</c>, and the doc it replaces was
/// WRONG.</b> It read: *"this class lives in <c>Hrot.Editor</c> … because it depends on the editor-side
/// scenario list (<c>IEditorLogic.AvailableScenarios</c>)."* 📐 Measured <c>2026-08-26</c>: it depends on a
/// <see cref="Func{TResult}"/> and names no host type at all. ⇒ ⛔ **the stated layering reason did not
/// exist**, and it is the same over-claim <c>AssetPickActionRouter</c>'s doc made before <c>CE-049</c>
/// lifted it.</para>
///
/// <para>🔴🔴 <b>What that cost, measured from the user's <c>--mode cgf</c> visual check:</b> CGF's asset
/// catalog had NO scenario contributor, so it held zero <c>AssetKind.Scenario</c> entries ⇒
/// <c>File/Edit/Open Scenario</c>, <c>File/Live/Load Scenario</c> and <c>File/Open Asset</c>'s Scenario tab
/// were all **EMPTY** — three of the six reported symptoms, one root. ⚠ <c>CE-049</c> wired CGF's picker
/// but never gave its catalog anything to show.</para>
/// <para>
/// <b>AssetId derivation:</b> each scenario's <see cref="IEditableAsset.AssetId"/> is
/// a deterministic <see cref="Guid"/> computed as SHA256(UTF8(relpath))[:16], so
/// <c>FindByAssetId</c> is stable across enumerations and restarts.
/// </para>
/// <para>
/// <b>ContributorChanged:</b> fired by <see cref="Refresh"/> only when the projected
/// list has changed since the last enumeration. This avoids spurious catalog rebuilds
/// when the editor-side scenario list is polled but unchanged.
/// </para>
/// </remarks>
public sealed class ScenarioCatalogContributor : IAssetCatalogContributor
{
    private readonly Func<IReadOnlyList<string>> _scenarioListSource;
    private string[] _lastList = Array.Empty<string>();

    /// <summary>
    /// Creates a new <see cref="ScenarioCatalogContributor"/>.
    /// </summary>
    /// <param name="scenarioListSource">
    /// A delegate that returns the current list of available scenario relative paths.
    /// In production this is <c>() => editorLogic.AvailableScenarios</c>;
    /// in tests a lambda over a mutable list.
    /// </param>
    public ScenarioCatalogContributor(Func<IReadOnlyList<string>> scenarioListSource)
    {
        _scenarioListSource = scenarioListSource
            ?? throw new ArgumentNullException(nameof(scenarioListSource));
    }

    // ── IAssetCatalogContributor ──────────────────────────────────────────

    /// <inheritdoc />
    public AssetKind Kind => AssetKind.Scenario;

    /// <inheritdoc />
    /// <returns><see langword="null"/> — scenarios have no Assets root (§16).</returns>
    public string? BaseFolder => null;

    /// <inheritdoc />
    public event Action? ContributorChanged;

    /// <summary>
    /// Enumerates the current scenario list as <see cref="IEditableAsset"/> instances.
    /// One asset per scenario; the asset <c>Name</c> is the relative path verbatim
    /// (may contain <c>/</c>).
    /// </summary>
    public IReadOnlyList<IEditableAsset> Enumerate()
    {
        var scenarios = _scenarioListSource();
        _lastList = scenarios.ToArray();

        var result = new IEditableAsset[scenarios.Count];
        for (int i = 0; i < scenarios.Count; i++)
            result[i] = new ScenarioEditableAsset(scenarios[i]);

        return result;
    }

    /// <summary>
    /// Re-checks the scenario list source and fires <see cref="ContributorChanged"/>
    /// when the list differs from the last enumeration. Does nothing when unchanged.
    /// </summary>
    public void Refresh()
    {
        var current = _scenarioListSource();
        if (!SequenceEqual(_lastList, current))
        {
            _lastList = current.ToArray();
            ContributorChanged?.Invoke();
        }
    }

    // ── Helpers ───────────────────────────────────────────────────────────

    private static bool SequenceEqual(string[] a, IReadOnlyList<string> b)
    {
        if (a.Length != b.Count)
            return false;
        for (int i = 0; i < a.Length; i++)
            if (!string.Equals(a[i], b[i], StringComparison.Ordinal))
                return false;
        return true;
    }

    // ── Nested asset type ─────────────────────────────────────────────────

    private sealed class ScenarioEditableAsset : IEditableAsset
    {
        public ScenarioEditableAsset(string name)
        {
            Name = name;
            AssetId = DeriveAssetId(name);
        }

        public Guid AssetId { get; }
        public string Name { get; }
        public AssetKind Kind => AssetKind.Scenario;
        public string SourceFilePath => "";
        public bool IsDirty => false;
        public bool IsEditorOwned => false;

#pragma warning disable CS0067
        public event Action? Changed;
#pragma warning restore CS0067

        /// <summary>
        /// Deterministic <see cref="Guid"/> from a scenario relative path:
        /// SHA256(UTF8(relpath)) → first 16 bytes.
        /// </summary>
        internal static Guid DeriveAssetId(string relPath)
        {
            var hash = SHA256.HashData(Encoding.UTF8.GetBytes(relPath));
            return new Guid(hash.AsSpan(0, 16));
        }
    }
}
