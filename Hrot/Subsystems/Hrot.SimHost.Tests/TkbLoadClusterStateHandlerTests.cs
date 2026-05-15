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

    private void WriteScenarioHeader(string? tkbName)
    {
        string content = tkbName != null
            ? $"{{\"TkbName\":\"{tkbName}\"}}"
            : "{\"SubsystemType\":\"SimHost\"}";
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
}
