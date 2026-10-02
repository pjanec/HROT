using System.Reflection;
using Hrot.Editor.AiShared;
using Hrot.Editor.AiShared.Browser;
using Hrot.Editor.AiShared.Recipes;
using Xunit;

namespace Hrot.Editor.Tests.Browser;

/// <summary>
/// ⭐⭐⭐ <b><c>CE-049</c> (Axis-C E2 item ②) — rails for the ONE create-core.</b>
/// 📄 <c>docs/DESIGN_Cgf_Asset_Picker_Shell_Slice.md</c> §2 *(the inventory row that found the
/// duplicate)* / §3 ②. Ruling <b>9</b>.
///
/// <para>🔴 <b>What these rails protect.</b> <see cref="AssetCreateController"/> replaced TWO
/// near-verbatim copies — <c>EditorSubsystem.CreateAssetCore</c> and <c>CgfSubsystem.AssetShellCreate</c>
/// — which had already <b>drifted in three places</b>. ⚠ A dedup is only worth doing if the surviving
/// implementation is pinned; otherwise the next host re-derives it and the count goes back to two.</para>
///
/// <para>⭐⭐ <b>The ORDER rails are the load-bearing ones.</b> The four composition facts *(BUG-A6's
/// source-dir write · assembly-then-JSON contributor refresh · id only once catalogued)* are exactly what
/// a re-derivation gets wrong, and each is invisible in a smoke test: getting them wrong yields a created
/// file plus a <b>silently unaddressable id</b>, ⛔ never an exception.</para>
/// </summary>
public sealed class TheCreateCoreIsOneImplementationTests
{
    // ── the smallest fakes that let the controller run ───────────────────────

    private sealed class Asset : IEditableAsset
    {
        public Guid AssetId { get; init; } = Guid.NewGuid();
        public string Name { get; init; } = "";
        public AssetKind Kind { get; init; }
        public string SourceFilePath { get; init; } = "";
        public bool IsDirty => false;
        public bool IsEditorOwned => true;
#pragma warning disable CS0067
        public event Action? Changed;
#pragma warning restore CS0067
    }

    private sealed class Service : INewAssetService
    {
        public readonly List<(IEditableAsset? Recipe, string Name, string RelPath)> Calls = new();
        public AssetKind Kind { get; init; }
        public IEditableAsset? Minted;

        public IReadOnlyList<IEditableAsset> AvailableRecipes()
            => new IEditableAsset[] { new Asset { Name = "Blank", Kind = Kind } };

        public bool IsBlankTemplate(IEditableAsset recipe) => recipe.Name == "Blank";

        public IEditableAsset CreateNew(IEditableAsset? recipe, string name, string relPath)
        {
            Calls.Add((recipe, name, relPath));
            return Minted ??= new Asset { Name = name, Kind = Kind };
        }
    }

    /// <summary>Records the ORDER of every side effect, which is what these rails assert.</summary>
    private sealed class Trace
    {
        public readonly List<string> Steps = new();
        public bool CatalogueAfterJsonRefresh;
        private bool _jsonRefreshed;

        public void Mint()                     => Steps.Add("mint");
        public void WriteBlueprint(string p)   => Steps.Add($"write:{p}");
        public void RefreshAssembly()          => Steps.Add("refresh:assembly");
        public void RefreshJson(AssetKind k)   { Steps.Add($"refresh:json:{k}"); _jsonRefreshed = true; }
        public void Find()                     { Steps.Add("find"); CatalogueAfterJsonRefresh = _jsonRefreshed; }
        public void Open()                     => Steps.Add("open");
    }

    private static (AssetCreateController C, Service S, Trace T) Build(
        AssetKind kind, bool catalogueResolves = true, string? blueprintRoot = null)
    {
        var service = new Service { Kind = kind };
        var trace   = new Trace();

        var controller = new AssetCreateController(
            services:               new Dictionary<AssetKind, INewAssetService> { [kind] = service },
            saveMintOnlyAsset:      (_, path) => trace.WriteBlueprint(path),
            findCatalogued:         id => { trace.Find(); return catalogueResolves ? service.Minted : null; },
            refreshFromAssembly:    _ => trace.RefreshAssembly(),
            refreshJsonContributor: k => trace.RefreshJson(k),
            openDocument:           _ => trace.Open(),
            blueprintRootDir:       () => blueprintRoot);

        return (controller, service, trace);
    }

    // ══ ① the dedup itself ══════════════════════════════════════════════════

    /// <summary>
    /// ⭐⭐⭐ <b>THE RAIL THAT KEEPS THE DEDUP DONE: no host may re-declare a create-core.</b>
    /// 📐 It scans the two composition roots' SOURCE for the tell-tale sequence
    /// *(<c>CreateNew(</c> followed by a <c>FindByAssetId</c> in the same file)*. ⛔ A reference count
    /// cannot catch this — a re-derived copy calls the same primitives and references nothing new, which
    /// is exactly how the first duplicate survived review.
    ///
    /// <para>⚠ Source scanning is structurally necessary here and stated as such: the duplicate was a
    /// LOCAL FUNCTION and an inline lambda, so neither reflection nor the call graph can see it.</para>
    /// </summary>
    [Fact]
    public void NeitherCompositionRootDeclaresItsOwnCreateCore()
    {
        var repo = RepoRoot();
        var roots = new[]
        {
            Path.Combine(repo, "Hrot", "Subsystems", "Hrot.Editor", "EditorSubsystem.cs"),
            Path.Combine(repo, "Hrot", "Subsystems", "Hrot.CGF",    "CgfSubsystem.cs"),
        };

        foreach (var file in roots)
        {
            Assert.True(File.Exists(file), $"expected {file} to exist — the rail's target moved.");
            var text = File.ReadAllText(file);

            // ⭐ The signature of a create-core: it mints AND resolves the minted id itself.
            bool mints    = text.Contains(".CreateNew(", StringComparison.Ordinal);
            bool resolves = text.Contains("FindByAssetId", StringComparison.Ordinal);

            Assert.False(mints && resolves,
                $"{Path.GetFileName(file)} both calls CreateNew and resolves FindByAssetId — that is a "
              + "create-core. CE-049 collapsed the two copies into "
              + "Hrot.Editor.AiShared.Browser.AssetCreateController (ruling 9); route through it instead.");
        }
    }

    // ══ ② the four composition facts, in order ══════════════════════════════

    /// <summary>
    /// ⭐⭐⭐ <b>The JSON contributor is refreshed BEFORE the catalog lookup.</b>
    /// 🔴 <c>BUG-A6</c>: <c>RefreshFromAssembly</c> refreshes only the assembly contributors, so a
    /// just-written <c>.btree.json</c> is invisible until its own contributor refreshes. ⛔ Reversing these
    /// two yields a created file and a <c>null</c> id — with no exception.
    /// </summary>
    [Theory]
    [InlineData(AssetKind.BTree)]
    [InlineData(AssetKind.Hsm)]
    public void TheJsonContributorRefreshesBeforeTheCatalogIsAsked(AssetKind kind)
    {
        var (controller, _, trace) = Build(kind);

        var (id, status) = controller.Create(kind, recipe: null, name: "X", relPath: "");

        Assert.NotNull(id);
        Assert.Contains("[OK]", status, StringComparison.Ordinal);
        Assert.True(trace.CatalogueAfterJsonRefresh,
            "FindByAssetId ran before the JSON contributor refreshed — BUG-A6: the new file is not "
          + "discoverable yet, so the id would come back null.");

        // ⛔⛔ RELATIVE order only — and this rail's first TWO cuts both got that wrong, in a way worth
        //    recording because it is the exact disease the repo's rotating-red flake is:
        //      cut 1 asserted `["refresh:assembly", "refresh:json:X", "find", "open"]` and reddened
        //             alone, because the test host had not loaded `Hrot.AI.Behaviors`;
        //      cut 2 asserted `["refresh:json:X", "find", "open"]` — green ALONE, RED in the full suite,
        //             because a test running earlier had loaded that assembly and the guarded
        //             `refresh:assembly` step then DID fire.
        // ⇒ ⭐⭐⭐ an exact-list assertion here is ORDER-DEPENDENT ON THE WHOLE SUITE. That is a rail that
        //    lies, so it is gone: the conditional step is filtered out and only the invariant is pinned.
        //    ⭐ Whether the assembly refresh fires is asserted separately, and conditionally, by
        //    `TheAssemblyRefreshOnlyFiresWhenTheAiAssemblyIsLoaded`.
        Assert.Equal(
            new[] { $"refresh:json:{kind}", "find", "open" },
            trace.Steps.Where(s => s != "refresh:assembly").ToArray());
    }

    /// <summary>
    /// ⭐⭐ <b>Blueprint is mint-only: its file is written at the SUPPLIED source root, before any
    /// refresh.</b> 🔴 <c>BUG-A6</c> again — writing under the default *(bin/)* root puts the file where
    /// no contributor scans.
    /// </summary>
    [Fact]
    public void BlueprintIsWrittenAtTheHostsSourceRootBeforeAnyRefresh()
    {
        var root = Path.Combine(Path.GetTempPath(), "ce049-bp-root");
        var (controller, _, trace) = Build(AssetKind.Blueprint, blueprintRoot: root);

        controller.Create(AssetKind.Blueprint, recipe: null, name: "MyBp", relPath: "sub");

        var write = Assert.Single(trace.Steps.Where(s => s.StartsWith("write:", StringComparison.Ordinal)));
        Assert.Contains(root, write, StringComparison.Ordinal);
        Assert.Equal("write:", trace.Steps[0].Substring(0, 6));   // ⭐ FIRST, before the refreshes
    }

    /// <summary>
    /// ⭐⭐⭐ <b>An unresolvable id is reported as <c>null</c> WITH the remedy — never as success.</b>
    /// 🔒 <c>MA-004</c>: answering with the minted id before the catalog can resolve it hands the caller an
    /// id <c>GET /assets</c> cannot find. ⭐ And the message must name the fix — 📐 the CGF copy's text did
    /// *("pass <c>--asset-root</c> on a deployed node")* and the editor's did not; the survivor keeps the
    /// better one.
    /// </summary>
    [Fact]
    public void AnUnresolvableIdIsRefusedAndTheMessageNamesTheRemedy()
    {
        var (controller, _, _) = Build(AssetKind.BTree, catalogueResolves: false);

        var (id, status) = controller.Create(AssetKind.BTree, recipe: null, name: "Ghost", relPath: "");

        Assert.Null(id);
        Assert.Contains("not in the catalog", status, StringComparison.Ordinal);
        Assert.Contains("--asset-root", status, StringComparison.Ordinal);
    }

    /// <summary>
    /// ⭐⭐ <b>The document is NOT opened when the id could not be resolved.</b>
    /// ⚠ The half a one-sided rail misses: opening an asset the catalog does not have is how a host ends up
    /// with a document whose id nothing else can address.
    /// </summary>
    [Fact]
    public void NothingIsOpenedWhenTheIdCouldNotBeResolved()
    {
        var (controller, _, trace) = Build(AssetKind.Hsm, catalogueResolves: false);

        controller.Create(AssetKind.Hsm, recipe: null, name: "Ghost", relPath: "");

        Assert.DoesNotContain("open", trace.Steps);
    }

    // ══ ③ the string surface adds no create logic ═══════════════════════════

    /// <summary>
    /// ⭐⭐ <b><c>CreateByName</c> is a parse-and-resolve wrapper — it reaches the SAME body.</b>
    /// ⭐ This is what lets CGF's <c>POST /assets</c> and the editor's New-Asset dialog be one
    /// implementation instead of two.
    /// </summary>
    [Fact]
    public void TheStringSurfaceReachesTheSameBody()
    {
        var (controller, service, trace) = Build(AssetKind.BTree);

        var (id, status) = controller.CreateByName("btree", "FromMcp", "", recipeName: null);

        Assert.NotNull(id);
        Assert.Contains("[OK]", status, StringComparison.Ordinal);
        Assert.Equal("FromMcp", Assert.Single(service.Calls).Name);
        // ⭐ The SAME invariant sequence the typed surface produces — with the conditional assembly
        //   refresh filtered out, for the suite-order reason that rail records.
        Assert.Equal(
            new[] { "refresh:json:BTree", "find", "open" },
            trace.Steps.Where(s => s != "refresh:assembly").ToArray());
    }

    /// <summary>
    /// ⭐⭐ <b>The assembly refresh is CONDITIONAL on <c>Hrot.AI.Behaviors</c> being loaded — asserted so
    /// the guard cannot be "tidied" into an unconditional call.</b>
    ///
    /// <para>⚠ Both original copies carried <c>if (aiAsm != null)</c>. ⛔ Removing it would make the
    /// controller call a refresh delegate with a null assembly on any host that has not loaded the AI
    /// behaviours — which is every headless test host, and would be an <c>ArgumentNullException</c> deep
    /// inside a catalog builder rather than a clean no-op.</para>
    /// </summary>
    [Fact]
    public void TheAssemblyRefreshOnlyFiresWhenTheAiAssemblyIsLoaded()
    {
        bool aiLoaded = AppDomain.CurrentDomain.GetAssemblies()
            .Any(a => a.GetName().Name == "Hrot.AI.Behaviors");

        var (controller, _, trace) = Build(AssetKind.BTree);
        controller.Create(AssetKind.BTree, recipe: null, name: "X", relPath: "");

        Assert.Equal(aiLoaded, trace.Steps.Contains("refresh:assembly"));
    }

    /// <summary>⭐ An unparseable kind is refused with the usable kinds, ⛔ not silently defaulted.</summary>
    [Fact]
    public void AnUnknownKindTextIsRefused()
    {
        var (controller, _, _) = Build(AssetKind.BTree);

        var (id, status) = controller.CreateByName("Nonsense", "X", "", null);

        Assert.Null(id);
        Assert.Contains("is not an AssetKind", status, StringComparison.Ordinal);
    }

    /// <summary>
    /// ⭐⭐ <b>A kind this host composes no service for is refused with the composition explanation.</b>
    /// 🔒 The measured CGF case: <c>Scenario</c> has no service there, so
    /// <c>POST /assets {"kind":"Scenario"}</c> must explain rather than create something unopenable.
    /// </summary>
    [Fact]
    public void AnUncomposedKindIsRefusedWithTheCompositionReason()
    {
        var (controller, _, _) = Build(AssetKind.BTree);

        var (id, status) = controller.CreateByName("Scenario", "X", "", null);

        Assert.Null(id);
        Assert.Contains("composes no INewAssetService", status, StringComparison.Ordinal);
    }

    /// <summary>⭐ And <see cref="AssetCreateController.SupportedKinds"/> answers what the host CAN create.</summary>
    [Fact]
    public void SupportedKindsReportsWhatTheHostComposed()
    {
        var (controller, _, _) = Build(AssetKind.Hsm);

        Assert.Equal(new[] { AssetKind.Hsm }, controller.SupportedKinds.ToArray());
    }

    // ── helper ───────────────────────────────────────────────────────────────

    /// <summary>Walks up from the test binary to the repo root (the dir holding <c>docs/</c>).</summary>
    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !Directory.Exists(Path.Combine(dir.FullName, "docs")))
            dir = dir.Parent;
        Assert.NotNull(dir);
        return dir!.FullName;
    }
}
