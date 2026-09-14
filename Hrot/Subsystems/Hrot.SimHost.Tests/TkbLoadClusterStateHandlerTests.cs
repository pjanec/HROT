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
    /// <para>⭐⭐ <b>Closed by DERIVATION, not by an app-layer convention</b> (<c>2026-09-13</c>): the set is
    /// a read-only view of <c>[BirthCritical]</c> on the component type, so a file-loaded template carries
    /// it BY CONSTRUCTION. ⚠ The zip's entity carries an unknown descriptor key, which pins the important
    /// half: this holds for EVERY loaded template, ⛔ not only ones whose descriptors the parser
    /// recognised. 📄 <c>docs/designs/tkb-1/DESIGN.md</c> §6.6a.</para>
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
    /// ⭐⭐ <b>The programmatic fallback gets the SAME answer</b> — which is the half an app-layer
    /// convention in this handler could never have reached. ⚠ With no <c>TkbName</c> the handler loads
    /// <c>NedTkbCatalog</c>, whose templates no longer declare birth-criticality at all; they report it
    /// because the derivation is a property of the COMPONENT.
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
    /// ⭐⭐ <b>The LOADER invents no mandatory components — and after <c>CE-265</c> that is a STRONGER
    /// statement, not a gap.</b> 📄 <c>docs/designs/tkb-1/DESIGN.md</c> §6.6b.
    ///
    /// <para>✅✅ <b>UPDATED <c>2026-09-13</c>.</b> An earlier version of this header said the emptiness was
    /// an unavoidable hole because <i>"there is no derivable rule — the catalogues DISAGREE"</i>, and that
    /// <c>CE-265</c> <i>"needs a design answer"</i>. 🔒 The user ruled that disagreement was DRIFT, not
    /// policy, and the derivation shipped: <c>MandatoryComponentResolver</c> computes the gate PER HOST
    /// from <c>[PerInstanceValue] ∩ produced ∩ ingressible ∩ registered</c>. ⇒ a file-loaded template is
    /// now gated exactly like a programmatic one.</para>
    ///
    /// <para>⛔⛔ <b>So why must this list still be EMPTY?</b> Because three of the derivation's four inputs
    /// are HOST-LOCAL while a <c>TkbTemplate</c> is a SHARED record. ⇒ the answer has no home on the
    /// template at all, and a loader that wrote one here would be asserting one node's answer on every
    /// node. ⭐ The remaining purpose of the field is the AUTHORING ESCAPE HATCH for components no
    /// translator produces — ⛔ which a file, knowing nothing of translators, can never identify.</para>
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
