using System;
using System.IO;
using System.Linq;
using FluentAssertions;
using Fhsm.Compiler;
using Fhsm.Kernel.Data;
using Hrot.AiEditor.Persistence.Hsm;
using Hrot.Hsm.Editor.Catalog;
using Hrot.Hsm.Editor.Model;
using Hrot.Hsm.Editor.Persistence;
using Xunit;

namespace Hrot.Hsm.Editor.Tests.Catalog;

/// <summary>
/// PU-301: Tests for <see cref="HsmJsonAssetContributor"/>.
/// Exercises header-lazy discovery, lazy LoadFull, malformed-skip, IsEditorOwned,
/// SourceFilePath.  Symmetric to BTreeJsonAssetContributorTests.
/// </summary>
public sealed class HsmJsonAssetContributorTests : IDisposable
{
    private readonly string _tempDir;

    public HsmJsonAssetContributorTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "HsmJsonContrib_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, recursive: true); } catch { }
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static HsmAssetDto MakeDto(string name = "TestMachine")
    {
        var assetId    = Guid.NewGuid();
        var stableId   = Guid.NewGuid();
        var transVisId = Guid.NewGuid();

        var dto = new HsmAssetDto
        {
            AssetId  = assetId,
            Name     = name,
            TargetNamespace = "Test.Machines",
        };

        // Add one state
        dto.States.Add(new StateNodeDto
        {
            StableId  = stableId,
            Name      = "Idle",
            IsInitial = true,
            X = 100f,
            Y = 150f,
        });

        // Add one event
        dto.Events.Add(new EventDefinitionDto
        {
            Name    = "OnAttack",
            EventId = 1,
        });

        return dto;
    }

    private string WriteJson(HsmAssetDto dto, string? fileName = null)
    {
        var json = HsmJsonServices.Serialize(dto);
        var path = Path.Combine(_tempDir, fileName ?? (dto.Name + ".hsm.json"));
        File.WriteAllText(path, json);
        return path;
    }

    // ── PU-301 SC1: Discover reads header ────────────────────────────────────

    [Fact]
    public void Discover_ValidFile_HeaderContainsAssetIdAndName()
    {
        var dto  = MakeDto("Guard");
        WriteJson(dto);

        var contrib = new HsmJsonAssetContributor();
        contrib.Refresh(rootDirectory: _tempDir);

        var assets = contrib.Enumerate();
        assets.Should().HaveCount(1);
        assets[0].AssetId.Should().Be(dto.AssetId);
        assets[0].Name.Should().Be("Guard");
    }

    // ── PU-301 SC2: LoadFull → model with correct topology + ownership ────────

    [Fact]
    public void LoadFull_ValidFile_ModelHasCorrectTopologyAndOwnership()
    {
        var dto  = MakeDto("Guard");
        var path = WriteJson(dto);

        var contrib = new HsmJsonAssetContributor();
        contrib.Refresh(rootDirectory: _tempDir);

        var assets = contrib.Enumerate();
        assets.Should().HaveCount(1);
        var asset = (HsmAsset)assets[0];

        asset.AllStates.Should().HaveCount(1, "one state was written to the DTO");
        asset.AllEvents.Should().HaveCount(1, "one event was written to the DTO");
        asset.IsEditorOwned.Should().BeTrue("JSON-loaded assets are always editor-owned");
        asset.SourceFilePath.Should().Be(path, "SourceFilePath must point at the .hsm.json file");
        asset.IsDirty.Should().BeFalse("load must not mark the asset dirty");
    }

    // ── PU-301 SC3: malformed file skipped; sibling still discovered ──────────

    [Fact]
    public void Discover_MalformedFile_IsSkipped_SiblingStillDiscovered()
    {
        var validDto = MakeDto("ValidMachine");
        WriteJson(validDto, "valid.hsm.json");
        File.WriteAllText(Path.Combine(_tempDir, "malformed.hsm.json"), "NOT { VALID");

        var contrib = new HsmJsonAssetContributor();
        var ex = Record.Exception(() => contrib.Refresh(rootDirectory: _tempDir));
        ex.Should().BeNull("malformed files must be silently skipped");

        contrib.Enumerate().Should().HaveCount(1);
        contrib.Enumerate()[0].Name.Should().Be("ValidMachine");
    }

    // ── PU-301 SC4: IsDirty remains false after load ──────────────────────────

    [Fact]
    public void LoadFull_DoesNotMarkDirty()
    {
        var dto  = MakeDto("NoDirty");
        WriteJson(dto);

        var contrib = new HsmJsonAssetContributor();
        contrib.Refresh(rootDirectory: _tempDir);

        contrib.Enumerate()[0].IsDirty.Should().BeFalse(
            "load must not call MarkDirty (PU-602 constraint)");
    }
}
