using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Fdp.Core;
using Fdp.Toolkit.Orchestration;
using Fdp.Toolkit.Replication.Components;
using Fdp.Toolkit.Spatial;
using Hrot.Core.Network;
using HrotStrideApp;
using Xunit;

namespace HrotStrideApp.Tests;

/// <summary>
/// Headless unit/integration tests for <see cref="StrideInspectorViewModel"/>
/// and related view-model types (STR-P5-T2, BATCH-22).
///
/// <para>
/// All tests run without a GPU, a window, or any Raylib context.
/// They exercise the pure logic (mapping the FDP world → display rows/inspector fields)
/// by booting a headless <see cref="EditorStrideSubsystem"/>, spawning entities,
/// and asserting on the view-model output.
/// </para>
/// </summary>
public sealed class StrideInspectorViewModelTests : IDisposable
{
    private readonly EditorStrideSubsystem _sut;

    public StrideInspectorViewModelTests()
    {
        _sut = new EditorStrideSubsystem();
        _sut.Initialize();
    }

    public void Dispose() => _sut.Dispose();

    // ── B22-VM-1: null world returns empty list ──────────────────────────────

    /// <summary>
    /// <see cref="StrideInspectorViewModel.BuildEntityList"/> with a null world returns an
    /// empty list without throwing.
    /// </summary>
    [Fact]
    public void BuildEntityList_NullWorld_ReturnsEmpty()
    {
        var rows = StrideInspectorViewModel.BuildEntityList(null);
        Assert.Empty(rows);
    }

    // ── B22-VM-2: empty world returns empty list ─────────────────────────────

    /// <summary>
    /// <see cref="StrideInspectorViewModel.BuildEntityList"/> on a freshly-booted world
    /// (no entities) returns an empty list without throwing.
    /// </summary>
    [Fact]
    public void BuildEntityList_EmptyWorld_ReturnsEmpty()
    {
        var rows = StrideInspectorViewModel.BuildEntityList(_sut.World);
        Assert.Empty(rows);
    }

    // ── B22-VM-3: spawned entity appears in entity list ──────────────────────

    /// <summary>
    /// After spawning one entity via the Brain path and pumping three frames, exactly one row
    /// appears in the entity list with the correct TKB type and a non-null DisplayName.
    /// </summary>
    [Fact]
    public void BuildEntityList_AfterSpawn_ReturnsOneRow_WithCorrectTkbType()
    {
        // Spawn CivilianPedestrian (TkbType=1001).
        _sut.ScenarioSource.Enqueue(new EntityCreationRequest
        {
            RequestId          = Guid.NewGuid(),
            OwnerAppInstanceId = 0,
            TkbType            = 1001L,
            InitialComponents  = new List<object>
            {
                new SimTransform { Position = new System.Numerics.Vector3(10f, 20f, 0f) },
            },
        });

        _sut.Tick(1f / 60f);
        _sut.Tick(1f / 60f);
        _sut.Tick(1f / 60f);

        var rows = StrideInspectorViewModel.BuildEntityList(_sut.World);

        Assert.Single(rows);
        Assert.Equal(1001L, rows[0].TkbType);
        Assert.NotNull(rows[0].DisplayName);
        Assert.NotEmpty(rows[0].DisplayName);
        // Position should be the spawn position (X=10, Y=20).
        Assert.Equal(10f, rows[0].Position.X, precision: 1);
        Assert.Equal(20f, rows[0].Position.Y, precision: 1);
    }

    // ── B22-VM-4: display name contains TKB name for known types ────────────

    /// <summary>
    /// <see cref="StrideInspectorViewModel.BuildDisplayName"/> returns the human-readable
    /// TKB name for all known UrbanCombat types (1001–2003).
    /// </summary>
    [Theory]
    [InlineData(1001L, "CivilianPedestrian")]
    [InlineData(1002L, "CivilianCar")]
    [InlineData(2001L, "MilitaryAPC")]
    [InlineData(2002L, "InfantrySoldier")]
    [InlineData(2003L, "Insurgent")]
    public void BuildDisplayName_KnownTkbType_ContainsTkbName(long tkbType, string expectedName)
    {
        string displayName = StrideInspectorViewModel.BuildDisplayName(tkbType, networkId: 42L);

        Assert.Contains(expectedName, displayName);
        Assert.Contains("42", displayName); // NetworkId embedded
    }

    // ── B22-VM-5: display name fallback for unknown TKB type ────────────────

    /// <summary>
    /// <see cref="StrideInspectorViewModel.BuildDisplayName"/> with an unknown TKB type falls
    /// back to a string that contains the network ID and the TKB type number.
    /// </summary>
    [Fact]
    public void BuildDisplayName_UnknownTkbType_FallsBackWithNetworkId()
    {
        string displayName = StrideInspectorViewModel.BuildDisplayName(9999L, networkId: 7L);

        // Must contain the network ID.
        Assert.Contains("7", displayName);
        // Must mention the TKB type in some form.
        Assert.Contains("9999", displayName);
    }

    // ── B22-VM-6: display name for zero TKB type uses "Entity #N" ───────────

    /// <summary>
    /// When TKB type is 0 (unset), <see cref="StrideInspectorViewModel.BuildDisplayName"/>
    /// returns an "Entity #N" fallback string.
    /// </summary>
    [Fact]
    public void BuildDisplayName_ZeroTkbType_Returns_EntityHashN()
    {
        string displayName = StrideInspectorViewModel.BuildDisplayName(0L, networkId: 5L);

        Assert.StartsWith("Entity #", displayName);
        Assert.Contains("5", displayName);
    }

    // ── B22-VM-7: inspector on null world returns "(no selection)" ───────────

    /// <summary>
    /// <see cref="StrideInspectorViewModel.BuildInspector"/> with a null world returns
    /// an inspector model whose Title is "(no selection)".
    /// </summary>
    [Fact]
    public void BuildInspector_NullWorld_ReturnsNoSelection()
    {
        var inspector = StrideInspectorViewModel.BuildInspector(null, Entity.Null);

        Assert.Equal("(no selection)", inspector.Title);
        Assert.Empty(inspector.Fields);
    }

    // ── B22-VM-8: inspector on dead entity returns "(no selection)" ──────────

    /// <summary>
    /// <see cref="StrideInspectorViewModel.BuildInspector"/> with a dead/null entity returns
    /// an inspector model whose Title is "(no selection)".
    /// </summary>
    [Fact]
    public void BuildInspector_DeadEntity_ReturnsNoSelection()
    {
        var inspector = StrideInspectorViewModel.BuildInspector(_sut.World, Entity.Null);

        Assert.Equal("(no selection)", inspector.Title);
    }

    // ── B22-VM-9: inspector on live entity contains SimTransform fields ───────

    /// <summary>
    /// After spawning an entity and pumping three frames, calling
    /// <see cref="StrideInspectorViewModel.BuildInspector"/> on that entity returns
    /// a model that contains at least one "SimTransform.Position" field with a non-empty value.
    /// This proves the inspector reads live ECS components.
    /// </summary>
    [Fact]
    public void BuildInspector_LiveEntity_ContainsSimTransformField()
    {
        _sut.ScenarioSource.Enqueue(new EntityCreationRequest
        {
            RequestId          = Guid.NewGuid(),
            OwnerAppInstanceId = 0,
            TkbType            = 2002L, // InfantrySoldier
            InitialComponents  = new List<object>
            {
                new SimTransform { Position = new System.Numerics.Vector3(5f, 3f, 0f) },
            },
        });

        _sut.Tick(1f / 60f);
        _sut.Tick(1f / 60f);
        _sut.Tick(1f / 60f);

        // Find the spawned entity.
        Entity? entity = _sut.World.Query()
            .With<SimTransform>()
            .Build()
            .FirstOrNull();

        Assert.True(entity.HasValue, "Entity must exist after spawn.");

        var inspector = StrideInspectorViewModel.BuildInspector(_sut.World, entity!.Value);

        // Title should be non-empty (entity name).
        Assert.NotEmpty(inspector.Title);
        Assert.NotEqual("(no selection)", inspector.Title);

        // At least one SimTransform.Position field.
        var posField = inspector.Fields.FirstOrDefault(f => f.Name == "SimTransform.Position");
        Assert.NotNull(posField);
        Assert.NotEmpty(posField!.Value);

        // Authority field present.
        var authField = inspector.Fields.FirstOrDefault(f => f.Name == "Authority(SimTransform)");
        Assert.NotNull(authField);
        // Entity is owned (localNodeId=0 → authority granted at birth).
        Assert.Equal("OWNED", authField!.Value);
    }

    // ── B22-VM-10: multiple spawns produce multiple rows ─────────────────────

    /// <summary>
    /// Spawning N entities produces exactly N rows in the entity list.
    /// Each row has a non-empty DisplayName (no nulls or empty strings).
    /// </summary>
    [Fact]
    public void BuildEntityList_MultipleSpawns_ReturnsMatchingRowCount()
    {
        for (int i = 0; i < 3; i++)
        {
            _sut.ScenarioSource.Enqueue(new EntityCreationRequest
            {
                RequestId          = Guid.NewGuid(),
                OwnerAppInstanceId = 0,
                TkbType            = 2002L,
                InitialComponents  = new List<object>
                {
                    new SimTransform { Position = new System.Numerics.Vector3(i * 2f, 0f, 0f) },
                },
            });
        }

        _sut.Tick(1f / 60f);
        _sut.Tick(1f / 60f);
        _sut.Tick(1f / 60f);

        var rows = StrideInspectorViewModel.BuildEntityList(_sut.World);

        Assert.Equal(3, rows.Count);
        foreach (var row in rows)
        {
            Assert.NotEmpty(row.DisplayName);
            Assert.NotEqual(Entity.Null, row.Entity);
        }
    }

    // ── B22-VM-11: QuaternionToEulerDeg identity quaternion returns zero ──────

    /// <summary>
    /// <see cref="StrideInspectorViewModel.QuaternionToEulerDeg"/> of the identity quaternion
    /// returns approximately (0, 0, 0).  Verifies the Euler extraction formula is not
    /// producing garbage on the degenerate case.
    /// </summary>
    [Fact]
    public void QuaternionToEulerDeg_Identity_ReturnsZeroAngles()
    {
        var euler = StrideInspectorViewModel.QuaternionToEulerDeg(System.Numerics.Quaternion.Identity);

        Assert.Equal(0f, euler.X, precision: 3); // pitch
        Assert.Equal(0f, euler.Y, precision: 3); // yaw
        Assert.Equal(0f, euler.Z, precision: 3); // roll
    }

    // ── B22-VM-12: QuaternionToEulerDeg pure yaw gives nonzero yaw only ──────

    /// <summary>
    /// A 90° yaw quaternion (rotation around Y in FDP space) should produce
    /// yaw ≈ 90° and pitch ≈ roll ≈ 0° from
    /// <see cref="StrideInspectorViewModel.QuaternionToEulerDeg"/>.
    /// </summary>
    [Fact]
    public void QuaternionToEulerDeg_90DegYaw_GivesNonzeroYaw_ZeroPitchRoll()
    {
        // Quaternion for 90° rotation around Y axis.
        var q = System.Numerics.Quaternion.CreateFromAxisAngle(System.Numerics.Vector3.UnitY, (float)(Math.PI / 2.0));
        var euler = StrideInspectorViewModel.QuaternionToEulerDeg(q);

        // Yaw should be approximately 90°; pitch and roll approximately 0°.
        Assert.Equal(0f, euler.X, precision: 2); // pitch ≈ 0°
        Assert.Equal(90f, euler.Y, precision: 2); // yaw  ≈ 90°
        Assert.Equal(0f, euler.Z, precision: 2); // roll ≈ 0°
    }

    // ── B22-CFG-1: StrideInspectorWindowConfig with ForceEnabled=false ───────

    /// <summary>
    /// When <see cref="StrideInspectorWindowConfig.ForceEnabled"/> is set to <c>false</c>,
    /// <see cref="StrideInspectorWindowConfig.IsEnabled"/> returns <c>false</c> regardless
    /// of the environment variable.
    /// </summary>
    [Fact]
    public void InspectorWindowConfig_ForceEnabled_False_DisablesWindow()
    {
        var saved = StrideInspectorWindowConfig.ForceEnabled;
        try
        {
            StrideInspectorWindowConfig.ForceEnabled = false;
            Assert.False(StrideInspectorWindowConfig.IsEnabled);
        }
        finally
        {
            StrideInspectorWindowConfig.ForceEnabled = saved;
        }
    }

    // ── B22-CFG-2: StrideInspectorWindowConfig with ForceEnabled=true ────────

    /// <summary>
    /// When <see cref="StrideInspectorWindowConfig.ForceEnabled"/> is set to <c>true</c>,
    /// <see cref="StrideInspectorWindowConfig.IsEnabled"/> returns <c>true</c>.
    /// </summary>
    [Fact]
    public void InspectorWindowConfig_ForceEnabled_True_EnablesWindow()
    {
        var saved = StrideInspectorWindowConfig.ForceEnabled;
        try
        {
            StrideInspectorWindowConfig.ForceEnabled = true;
            Assert.True(StrideInspectorWindowConfig.IsEnabled);
        }
        finally
        {
            StrideInspectorWindowConfig.ForceEnabled = saved;
        }
    }
}
