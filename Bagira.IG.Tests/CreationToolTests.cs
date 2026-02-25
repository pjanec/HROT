using System.Numerics;
using Bagira.IG.Components;
using Bagira.IG.Tools;
using Fdp.Kernel;
using FDP.Toolkit.NetworkSpawning.Events;
using Raylib_cs;

namespace Bagira.IG.Tests;

/// <summary>
/// Unit tests for <see cref="CreationTool"/> (IG.3.3).
///
/// Validates that the tool publishes a correctly-formed
/// <see cref="SpawnEntityCommand"/> to the <see cref="FdpEventBus"/> when the
/// operator left-clicks the map canvas, and that right-click cancels without
/// publishing.
///
/// No Raylib window context is required — <see cref="MeasureTool.HandleClick"/>
/// operates purely on in-memory state; the <c>_canvas?.PopTool()</c> call is
/// null-safe when <c>OnEnter</c> has not been called.
/// </summary>
public class CreationToolTests
{
    // ── Test constants (§CODE-STANDARDS §1) ───────────────────────────────────

    private const long   TestTkbType   = 202L;
    private const float  ClickX        = 1234.5f;
    private const float  ClickY        = 5678.9f;

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static FdpEventBus CreateBus() => new FdpEventBus();

    // ── Left-click publishes command ──────────────────────────────────────────

    /// <summary>
    /// A left-click must publish exactly one <see cref="SpawnEntityCommand"/> with the
    /// correct world coordinates embedded in <see cref="SpawnEntityCommand.InitialComponents"/>.
    /// </summary>
    [Fact]
    public void HandleClick_LeftClick_PublishesSpawnEntityCommand()
    {
        var bus   = CreateBus();
        var tool  = new CreationTool(bus, tkbType: TestTkbType);

        SpawnEntityCommand? captured = null;
        tool.OnCommandPublished += cmd => captured = cmd;

        tool.HandleClick(new Vector2(ClickX, ClickY), MouseButton.Left);

        Assert.NotNull(captured);
    }

    /// <summary>
    /// The published command must contain the TKB type supplied at construction.
    /// </summary>
    [Fact]
    public void HandleClick_LeftClick_CommandHasCorrectTkbType()
    {
        var bus  = CreateBus();
        var tool = new CreationTool(bus, tkbType: TestTkbType);

        SpawnEntityCommand? captured = null;
        tool.OnCommandPublished += cmd => captured = cmd;

        tool.HandleClick(new Vector2(ClickX, ClickY), MouseButton.Left);

        Assert.Equal(TestTkbType, captured!.Value.TkbType);
    }

    /// <summary>
    /// The <see cref="SpawnEntityCommand.OwnerNodeId"/> must equal
    /// <see cref="IgNetworkConstants.LocalNodeId"/> so the SimHost assigns
    /// ownership to the IG application.
    /// </summary>
    [Fact]
    public void HandleClick_LeftClick_CommandHasLocalNodeId()
    {
        var bus  = CreateBus();
        var tool = new CreationTool(bus, tkbType: TestTkbType);

        SpawnEntityCommand? captured = null;
        tool.OnCommandPublished += cmd => captured = cmd;

        tool.HandleClick(new Vector2(ClickX, ClickY), MouseButton.Left);

        Assert.Equal(IgNetworkConstants.LocalNodeId, captured!.Value.OwnerNodeId);
    }

    /// <summary>
    /// The <see cref="SpawnEntityCommand.NetworkId"/> must be zero so the SimHost
    /// allocates a fresh network identity for the new entity.
    /// </summary>
    [Fact]
    public void HandleClick_LeftClick_CommandNetworkIdIsZero()
    {
        var bus  = CreateBus();
        var tool = new CreationTool(bus, tkbType: TestTkbType);

        SpawnEntityCommand? captured = null;
        tool.OnCommandPublished += cmd => captured = cmd;

        tool.HandleClick(new Vector2(ClickX, ClickY), MouseButton.Left);

        Assert.Equal(0L, captured!.Value.NetworkId);
    }

    /// <summary>
    /// The <see cref="SpawnEntityCommand.RequestId"/> must be a non-empty
    /// <see cref="Guid"/> so the command can be correlated with a SimHost response.
    /// </summary>
    [Fact]
    public void HandleClick_LeftClick_CommandHasNonEmptyRequestId()
    {
        var bus  = CreateBus();
        var tool = new CreationTool(bus, tkbType: TestTkbType);

        SpawnEntityCommand? captured = null;
        tool.OnCommandPublished += cmd => captured = cmd;

        tool.HandleClick(new Vector2(ClickX, ClickY), MouseButton.Left);

        Assert.NotEqual(System.Guid.Empty, captured!.Value.RequestId);
    }

    /// <summary>
    /// <see cref="SpawnEntityCommand.InitialComponents"/> must contain exactly one
    /// <see cref="SimTransform"/> so the spawning system knows where to place the entity.
    /// </summary>
    [Fact]
    public void HandleClick_LeftClick_InitialComponentsContainsSimTransform()
    {
        var bus  = CreateBus();
        var tool = new CreationTool(bus, tkbType: TestTkbType);

        SpawnEntityCommand? captured = null;
        tool.OnCommandPublished += cmd => captured = cmd;

        tool.HandleClick(new Vector2(ClickX, ClickY), MouseButton.Left);

        Assert.NotNull(captured!.Value.InitialComponents);
        var transform = captured.Value.InitialComponents.OfType<SimTransform>().SingleOrDefault();
        Assert.IsType<SimTransform>(transform);
    }

    /// <summary>
    /// The <see cref="SimTransform"/> in the command must have the exact X and Y
    /// coordinates of the click point, mapping the screen-to-world conversion correctly.
    /// </summary>
    [Fact]
    public void HandleClick_LeftClick_SimTransformMatchesClickCoordinates()
    {
        var bus  = CreateBus();
        var tool = new CreationTool(bus, tkbType: TestTkbType);

        SpawnEntityCommand? captured = null;
        tool.OnCommandPublished += cmd => captured = cmd;

        tool.HandleClick(new Vector2(ClickX, ClickY), MouseButton.Left);

        var transform = captured!.Value.InitialComponents.OfType<SimTransform>().Single();
        Assert.Equal(ClickX, transform.Position.X, precision: 3);
        Assert.Equal(ClickY, transform.Position.Y, precision: 3);
        Assert.Equal(0f,     transform.Position.Z, precision: 3);
    }

    // ── Right-click cancels ───────────────────────────────────────────────────

    /// <summary>
    /// A right-click must not publish any command (it cancels the placement).
    /// </summary>
    [Fact]
    public void HandleClick_RightClick_DoesNotPublishCommand()
    {
        var bus  = CreateBus();
        var tool = new CreationTool(bus, tkbType: TestTkbType);

        bool commandPublished = false;
        tool.OnCommandPublished += _ => commandPublished = true;

        tool.HandleClick(new Vector2(ClickX, ClickY), MouseButton.Right);

        Assert.False(commandPublished);
    }

    // ── Default TKB type fallback ─────────────────────────────────────────────

    /// <summary>
    /// Passing <c>tkbType = 0</c> falls back to
    /// <see cref="CreationToolConstants.DefaultTkbType"/>.
    /// </summary>
    [Fact]
    public void Ctor_TkbTypeZero_UsesDefaultTkbType()
    {
        var bus  = CreateBus();
        var tool = new CreationTool(bus, tkbType: 0);

        SpawnEntityCommand? captured = null;
        tool.OnCommandPublished += cmd => captured = cmd;

        tool.HandleClick(Vector2.Zero, MouseButton.Left);

        Assert.Equal(CreationToolConstants.DefaultTkbType, captured!.Value.TkbType);
    }
}
