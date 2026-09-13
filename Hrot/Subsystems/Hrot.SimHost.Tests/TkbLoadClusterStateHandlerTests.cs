using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Fdp.Core.Orchestration;
using Fdp.Toolkit.Orchestration;
using Fdp.Toolkit.Tkb;
using Hrot.SimHost.Orchestration.Handlers;
using Xunit;

namespace Hrot.SimHost.Tests;

public class TkbLoadClusterStateHandlerTests : IDisposable
{
    private readonly string _stagingRoot;
    private readonly string _tkbDir;

    public TkbLoadClusterStateHandlerTests()
    {
        _stagingRoot = Path.Combine(Path.GetTempPath(), "TkbHandlerTest_" + Guid.NewGuid().ToString("N")[..8]);
        _tkbDir = Path.Combine(_stagingRoot, "TKB");
        Directory.CreateDirectory(_tkbDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_stagingRoot))
            Directory.Delete(_stagingRoot, recursive: true);
    }

    private void WriteScenarioHeader(string? tkbName, bool phase2Format = false)
    {
        string content;
        if (phase2Format && tkbName != null)
        {
            // Phase 2 format: $meta first, then TkbName at root level.
            content = $"{{\"$meta\":{{\"docType\":\"Hrot.Scenario\",\"schemaVersion\":1}},\"TkbName\":\"{tkbName}\"}}";
        }
        else if (tkbName != null)
        {
            // Legacy format.
            content = $"{{\"TkbName\":\"{tkbName}\"}}";
        }
        else
        {
            content = "{\"SubsystemType\":\"SimHost\"}";
        }
        File.WriteAllText(Path.Combine(_tkbDir, "ScenarioHeader.json"), content, new UTF8Encoding(false));
    }

    /// <summary>
    /// Creates a minimal ZIP at <paramref name="path"/> containing one dummy entity file.
    /// Unknown type => TkbDeserializer logs warning and skips.
    /// </summary>
    private static void CreateMinimalTkbZip(string path, string tkbName)
    {
        using var archive = ZipFile.Open(path, ZipArchiveMode.Create);
        var entry = archive.CreateEntry($"{tkbName}/entity.json");
        using var sw = new StreamWriter(entry.Open(), new UTF8Encoding(false));
        // Unknown type => TkbDeserializer logs warning and skips.
        sw.Write("{\"$guid\":9001,\"Name\":\"TestEntity\"}");
    }

    private static ExecuteNodeOpIntent MakeIntent(NodeOpType op = NodeOpType.PrepareLive) =>
        new ExecuteNodeOpIntent { Operation = op, TransactionId = Guid.NewGuid() };

    [Fact]
    public void CanHandle_ReturnsTrue_ForPrepareLive()
    {
        var db = new TkbDatabase();
        var h  = new TkbLoadClusterStateHandler(db, _stagingRoot);
        Assert.True(h.CanHandle(NodeOpType.PrepareLive));
    }

    [Fact]
    public void CanHandle_ReturnsTrue_ForPrepareEdit()
    {
        var db = new TkbDatabase();
        var h  = new TkbLoadClusterStateHandler(db, _stagingRoot);
        Assert.True(h.CanHandle(NodeOpType.PrepareEdit));
    }

    [Fact]
    public void CanHandle_ReturnsFalse_ForOtherOps()
    {
        var db = new TkbDatabase();
        var h  = new TkbLoadClusterStateHandler(db, _stagingRoot);
        Assert.False(h.CanHandle(NodeOpType.FinalizeLive));
    }

    [Fact]
    public async Task CacheHit_SameTkbAndTimestamp_DoesNotClearDb()
    {
        var db   = new TkbDatabase();
        var h    = new TkbLoadClusterStateHandler(db, _stagingRoot);
        var zipPath = Path.Combine(_tkbDir, "Alpha.zip");

        WriteScenarioHeader("Alpha");
        CreateMinimalTkbZip(zipPath, "Alpha");

        // First call — loads TKB (or at minimum does not throw)
        await h.PrepareAsync(MakeIntent(), CancellationToken.None);
        int countAfterFirst = db.GetAll().Count();

        // Second call — should be a cache hit, db unchanged (Clear not called)
        await h.PrepareAsync(MakeIntent(), CancellationToken.None);
        int countAfterSecond = db.GetAll().Count();

        Assert.Equal(countAfterFirst, countAfterSecond);
    }

    [Fact]
    public async Task CacheMiss_NameChange_ClearsCalled()
    {
        var db = new TkbDatabase();
        var h  = new TkbLoadClusterStateHandler(db, _stagingRoot);

        CreateMinimalTkbZip(Path.Combine(_tkbDir, "Alpha.zip"), "Alpha");
        CreateMinimalTkbZip(Path.Combine(_tkbDir, "Beta.zip"), "Beta");

        WriteScenarioHeader("Alpha");
        await h.PrepareAsync(MakeIntent(), CancellationToken.None);

        WriteScenarioHeader("Beta");
        await h.PrepareAsync(MakeIntent(), CancellationToken.None);

        Assert.Equal("Beta", db.ActiveTkbName);
    }

    [Fact]
    public async Task AfterSuccessfulLoad_ActiveTkbNameIsSet()
    {
        var db  = new TkbDatabase();
        var h   = new TkbLoadClusterStateHandler(db, _stagingRoot);
        var zip = Path.Combine(_tkbDir, "TestTkb.zip");

        WriteScenarioHeader("TestTkb");
        CreateMinimalTkbZip(zip, "TestTkb");

        await h.PrepareAsync(MakeIntent(), CancellationToken.None);

        Assert.Equal("TestTkb", db.ActiveTkbName);
    }

    [Fact]
    public async Task Fallback_NullTkbName_EmptyDb_RegistersNedCatalog()
    {
        var db = new TkbDatabase();
        var h  = new TkbLoadClusterStateHandler(db, _stagingRoot);

        // No ScenarioHeader.json -- triggers fallback path
        await h.PrepareAsync(MakeIntent(), CancellationToken.None);

        Assert.True(db.GetAll().Any());
    }

    [Fact]
    public async Task Fallback_NullTkbName_PopulatedDb_DoesNotOverwrite()
    {
        var db = new TkbDatabase();
        Hrot.Map.Definitions.Tkb.NedTkbCatalog.RegisterAll(db);
        int countBefore = db.GetAll().Count();

        var h = new TkbLoadClusterStateHandler(db, _stagingRoot);

        // Write header without TkbName
        WriteScenarioHeader(tkbName: null);
        await h.PrepareAsync(MakeIntent(), CancellationToken.None);

        int countAfter = db.GetAll().Count();
        Assert.Equal(countBefore, countAfter);
    }

    [Fact]
    public async Task MissingZip_ThrowsFileNotFoundException()
    {
        var db = new TkbDatabase();
        var h  = new TkbLoadClusterStateHandler(db, _stagingRoot);

        WriteScenarioHeader("MissingFile");
        // Do NOT create the ZIP.

        await Assert.ThrowsAsync<FileNotFoundException>(
            () => h.PrepareAsync(MakeIntent(), CancellationToken.None));
    }

    /// <summary>
    /// JM-P2-005-T01: Phase 2 scenario header ($meta first, then TkbName) is parsed
    /// correctly by the forward-only Utf8JsonReader scanner.
    /// The ExtractTkbNameFromLocalScenario private method skips $meta (not "TkbName")
    /// and continues until it finds the TkbName property.
    /// </summary>
    [Fact]
    public async Task ExtractTkbName_Phase2Format_ReturnsCorrectName()
    {
        var db  = new TkbDatabase();
        var h   = new TkbLoadClusterStateHandler(db, _stagingRoot);
        var zip = Path.Combine(_tkbDir, "TestTkb.zip");

        WriteScenarioHeader("TestTkb", phase2Format: true);
        CreateMinimalTkbZip(zip, "TestTkb");

        await h.PrepareAsync(MakeIntent(), CancellationToken.None);

        Assert.Equal("TestTkb", db.ActiveTkbName);
    }

    // ── CE-259az — the file path applies HROT's component conventions ─────────────────────────────

    /// <summary>
    /// ⭐⭐⭐ <b><c>CE-259az</c> — a template loaded from a TKB FILE must still declare
    /// <c>SimTransform</c> birth-critical.</b>
    ///
    /// <para>📐 <c>TkbDeserializer</c> builds a template purely from descriptor keys, so the list comes
    /// back EMPTY — while every programmatic catalogue declares it on every template. ⛔ Without the
    /// convention, a named-TKB deployment produces entities whose CREATOR does not own its own position:
    /// <c>CarKinematicsSystem.cs:73</c> filters <c>.WithOwned&lt;SimTransform&gt;()</c> so it never moves
    /// on the node that made it, and <c>GeoSpatialIngressTranslator.cs:90</c> then treats it as remote and
    /// overwrites the position from the wire.</para>
    ///
    /// <para>⚠ The zip's entity carries an unknown descriptor key, so this also pins the important half:
    /// the convention is applied to EVERY loaded template, ⛔ not only to ones whose descriptors the
    /// parser recognised.</para>
    /// </summary>
    [Fact]
    public async Task AFileLoadedTemplate_DeclaresSimTransformBirthCritical()
    {
        var db  = new TkbDatabase();
        var h   = new TkbLoadClusterStateHandler(db, _stagingRoot);
        var zip = Path.Combine(_tkbDir, "Conv.zip");

        WriteScenarioHeader("Conv");
        CreateMinimalTkbZip(zip, "Conv");

        await h.PrepareAsync(MakeIntent(), CancellationToken.None);

        var template = Assert.Single(db.GetAll());
        Assert.Contains(
            Fdp.Core.ComponentType<Fdp.Core.SimTransform>.ID,
            template.BirthCriticalComponents);
    }

    /// <summary>
    /// ⭐⭐ <b>The fallback path keeps its own seeding</b> — the convention must not be the only thing
    /// holding the invariant up. ⚠ With no <c>TkbName</c> the handler loads <c>NedTkbCatalog</c>, which
    /// declares birth-criticality itself at <c>BdcTkbBuilder.cs:44</c>; this rail fails if either half is
    /// removed.
    /// </summary>
    [Fact]
    public async Task TheProgrammaticFallback_AlsoDeclaresIt()
    {
        var db = new TkbDatabase();
        var h  = new TkbLoadClusterStateHandler(db, _stagingRoot);

        WriteScenarioHeader(null);

        await h.PrepareAsync(MakeIntent(), CancellationToken.None);

        Assert.NotEmpty(db.GetAll());
        Assert.All(db.GetAll(), t => Assert.Contains(
            Fdp.Core.ComponentType<Fdp.Core.SimTransform>.ID,
            t.BirthCriticalComponents));
    }

    /// <summary>
    /// ⛔⛔ <b><c>CE-265</c> — mandatory components are DELIBERATELY left empty on the file path, and this
    /// rail exists so that stays a DECISION rather than drift.</b>
    ///
    /// <para>📐 There is no derivable rule: the programmatic catalogues already DISAGREE for
    /// identically-shaped templates — <c>NedTkbBuilder.DefineVehicle</c> declares
    /// <c>EntityInfo</c>+<c>SimTransform</c> hard-mandatory, while <c>UrbanCombatTkbCatalog</c>'s five
    /// templates carry the same descriptors and declare none. 🔴 And guessing is the worse failure:
    /// <c>MandatoryComponents</c> is the PROMOTION GATE, so a wrong hard requirement means a ghost that
    /// never promotes, forever.</para>
    ///
    /// <para>⚠ The status quo is not harmless either — an empty list promotes on frame 1 and the P3
    /// promote-leg role claim fires ONCE. That is <c>CE-265</c>, and it needs a design answer, not a
    /// convention.</para>
    /// </summary>
    [Fact]
    public async Task AFileLoadedTemplate_GetsNoInventedMandatoryComponents()
    {
        var db  = new TkbDatabase();
        var h   = new TkbLoadClusterStateHandler(db, _stagingRoot);
        var zip = Path.Combine(_tkbDir, "Conv2.zip");

        WriteScenarioHeader("Conv2");
        CreateMinimalTkbZip(zip, "Conv2");

        await h.PrepareAsync(MakeIntent(), CancellationToken.None);

        var template = Assert.Single(db.GetAll());
        Assert.Empty(template.MandatoryComponents);
    }
}
