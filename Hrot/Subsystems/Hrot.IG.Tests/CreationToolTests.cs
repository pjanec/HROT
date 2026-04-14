using System;
using System.Collections.Generic;
using System.Numerics;
using Hrot.IG.Components;
using Hrot.ScenarioEditor.Tools;
using Fdp.Toolkit.NetworkSpawning.Events;
using Raylib_cs;

namespace Hrot.IG.Tests;

/// <summary>
/// Unit tests for <see cref="CreationTool"/> (D001 refactor).
///
/// Validates that the tool invokes the <c>onEntityCreated</c> delegate with a
/// correctly-formed <see cref="SpawnEntityCommand"/> on left-click and that
/// right-click cancels without firing the delegate.
///
/// No Raylib window context is required â€” <see cref="CreationTool.HandleClick"/>
/// is pure in-memory state; <c>_canvas?.PopTool()</c> is null-safe when
/// <c>OnEnter</c> has not been called.
/// </summary>
public class CreationToolTests
{
    // â”€â”€ Test constants â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

    private const long  TestTkbType = 202L;
    private const float ClickX      = 1234.5f;
    private const float ClickY      = 5678.9f;

    // â”€â”€ Helpers â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

    /// <summary>
    /// Creates a capture list and a <see cref="CreationTool"/> that appends each
    /// <see cref="SpawnEntityCommand"/> to it.
    /// </summary>
    private static (List<SpawnEntityCommand> captured, CreationTool tool)
        CreateTool(long tkbType = TestTkbType, string? initialPropertiesJson = null)
    {
        var captured = new List<SpawnEntityCommand>();
        var tool     = new CreationTool(
            cmd => captured.Add(cmd),
            tkbType:               tkbType,
            initialPropertiesJson: initialPropertiesJson);
        return (captured, tool);
    }

    // â”€â”€ Left-click publishes exactly one command â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

    /// <summary>
    /// A left-click must invoke the delegate exactly once.
    /// </summary>
    [Fact]
    public void HandleClick_LeftClick_WritesExactlyOneCommand()
    {
        var (captured, tool) = CreateTool();
        tool.HandleClick(new Vector2(ClickX, ClickY), MouseButton.Left);
        Assert.Single(captured);
    }

    /// <summary>
    /// The published command must have a non-empty <see cref="SpawnEntityCommand.RequestId"/>
    /// so responses can be correlated by the SimHost.
    /// </summary>
    [Fact]
    public void HandleClick_LeftClick_CommandHasNonEmptyRequestId()
    {
        var (captured, tool) = CreateTool();
        tool.HandleClick(new Vector2(ClickX, ClickY), MouseButton.Left);
        Assert.NotEqual(Guid.Empty, captured[0].RequestId);
    }

    /// <summary>
    /// The published command must carry the TKB type supplied at construction.
    /// </summary>
    [Fact]
    public void HandleClick_LeftClick_CommandHasCorrectTkbType()
    {
        var (captured, tool) = CreateTool();
        tool.HandleClick(new Vector2(ClickX, ClickY), MouseButton.Left);
        Assert.Equal(TestTkbType, captured[0].TkbType);
    }

    /// <summary>
    /// <see cref="SpawnEntityCommand.InitialTransform"/> must be set with the
    /// canvas click position (X = East, Y = North) so the ACL egress translator
    /// can geo-convert it to the DDS wire format.
    /// </summary>
    [Fact]
    public void HandleClick_LeftClick_CommandHasInitialTransformMatchingClickPosition()
    {
        var (captured, tool) = CreateTool();
        tool.HandleClick(new Vector2(ClickX, ClickY), MouseButton.Left);
        Assert.True(captured[0].InitialTransform.HasValue);
        Assert.Equal(ClickX, captured[0].InitialTransform!.Value.Position.X, precision: 2);
        Assert.Equal(ClickY, captured[0].InitialTransform!.Value.Position.Y, precision: 2);
    }

    /// <summary>
    /// The <see cref="CreationTool.OnCommandPublished"/> event must fire once with
    /// the same command that was passed to the delegate.
    /// </summary>
    [Fact]
    public void HandleClick_LeftClick_RaisesOnCommandPublishedWithSamePayload()
    {
        var (captured, tool) = CreateTool();
        SpawnEntityCommand? observed = null;
        tool.OnCommandPublished += cmd => observed = cmd;

        tool.HandleClick(new Vector2(ClickX, ClickY), MouseButton.Left);

        Assert.NotNull(observed);
        Assert.Equal(captured[0].RequestId, observed!.Value.RequestId);
    }

    // â”€â”€ Right-click does NOT publish â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

    /// <summary>
    /// A right-click must not invoke the delegate â€” it cancels placement.
    /// </summary>
    [Fact]
    public void HandleClick_RightClick_DoesNotPublish()
    {
        var (captured, tool) = CreateTool();
        tool.HandleClick(new Vector2(ClickX, ClickY), MouseButton.Right);
        Assert.Empty(captured);
    }

    // â”€â”€ Default TKB type fallback â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

    /// <summary>
    /// Passing <c>tkbType = 0</c> falls back to
    /// <see cref="CreationToolConstants.DefaultTkbType"/>.
    /// </summary>
    [Fact]
    public void Ctor_TkbTypeZero_UsesDefaultTkbType()
    {
        var (captured, tool) = CreateTool(tkbType: 0);
        tool.HandleClick(Vector2.Zero, MouseButton.Left);
        Assert.Equal(CreationToolConstants.DefaultTkbType, captured[0].TkbType);
    }

    // â”€â”€ nameResolver â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

    private static (List<SpawnEntityCommand> captured, CreationTool tool)
        CreateToolWithResolver(Func<string> nameResolver, long tkbType = TestTkbType)
    {
        var captured = new List<SpawnEntityCommand>();
        var tool     = new CreationTool(
            cmd => captured.Add(cmd),
            tkbType:      tkbType,
            nameResolver: nameResolver);
        return (captured, tool);
    }

    /// <summary>
    /// A <c>nameResolver</c> delegate is retained for future wiring.
    /// The request still fires and <c>InitialAttributesJson</c> is null
    /// because no <c>initialPropertiesJson</c> was supplied.
    /// </summary>
    [Fact]
    public void HandleClick_LeftClick_NameResolver_DoesNotThrowAndInitialAttributesJsonIsNull()
    {
        var (captured, tool) = CreateToolWithResolver(() => "Generated-5");

        tool.HandleClick(new Vector2(ClickX, ClickY), MouseButton.Left);

        Assert.Single(captured);
        Assert.Null(captured[0].InitialAttributesJson);
    }

    /// <summary>
    /// Two successive clicks produce two commands, both with null
    /// <c>InitialAttributesJson</c> when no initial properties were given.
    /// </summary>
    [Fact]
    public void HandleClick_LeftClick_NameResolver_TwoClicksProduceTwoCommands()
    {
        int callIndex = 0;
        var (captured, tool) = CreateToolWithResolver(() => "G-" + ++callIndex);

        tool.HandleClick(new Vector2(ClickX, ClickY), MouseButton.Left);
        tool.HandleClick(new Vector2(ClickX, ClickY), MouseButton.Left);

        Assert.Equal(2, captured.Count);
        Assert.Null(captured[0].InitialAttributesJson);
        Assert.Null(captured[1].InitialAttributesJson);
    }

    // â”€â”€ InitialAttributesJson forwarding â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

    /// <summary>
    /// Without a name resolver, <c>InitialAttributesJson</c> carries the raw
    /// <c>initialPropertiesJson</c> string verbatim (dumb-pipe forwarding).
    /// </summary>
    [Fact]
    public void HandleClick_LeftClick_NullNameResolver_InitialAttributesJsonForwardedVerbatim()
    {
        const string json = "{\"name\":\"MyUnit\"}";
        var (captured, tool) = CreateTool(initialPropertiesJson: json);

        tool.HandleClick(new Vector2(ClickX, ClickY), MouseButton.Left);

        Assert.Equal(json, captured[0].InitialAttributesJson);
    }

    /// <summary>
    /// When <c>initialPropertiesJson</c> is supplied at construction, the tool
    /// forwards it verbatim as <see cref="SpawnEntityCommand.InitialAttributesJson"/>.
    /// </summary>
    [Fact]
    public void CreationTool_SetsInitialAttributesJson_FromInitialPropertiesJson()
    {
        const string json = "{\"name\":\"Alpha\",\"affiliation\":\"FORCE_FRIENDLY\"}";
        var (captured, tool) = CreateTool(initialPropertiesJson: json);

        tool.HandleClick(new Vector2(ClickX, ClickY), MouseButton.Left);

        Assert.Equal(json, captured[0].InitialAttributesJson);
    }

    /// <summary>
    /// When no <c>initialPropertiesJson</c> is supplied,
    /// <c>InitialAttributesJson</c> must be <c>null</c>.
    /// </summary>
    [Fact]
    public void CreationTool_InitialAttributesJson_IsNull_WhenNoPropertiesJson()
    {
        var (captured, tool) = CreateTool();

        tool.HandleClick(new Vector2(ClickX, ClickY), MouseButton.Left);

        Assert.Null(captured[0].InitialAttributesJson);
    }

    // â”€â”€ Ghost colour â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

    /// <summary>
    /// <c>ParseAffiliationFromJson</c> must still be called during construction
    /// so the ghost entity colour is correct. Verified via reflection on
    /// the private <c>_affiliationForDisplay</c> field.
    /// </summary>
    [Fact]
    public void CreationTool_GhostColor_StillReflectsAffiliation()
    {
        const string json = "{\"affiliation\":\"FORCE_FRIENDLY\"}";
        var captured = new List<SpawnEntityCommand>();
        var tool = new CreationTool(cmd => captured.Add(cmd), initialPropertiesJson: json);

        var field = typeof(CreationTool).GetField(
            "_affiliationForDisplay",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        Assert.NotNull(field);
        var affiliation = (ForceId)field!.GetValue(tool)!;
        Assert.Equal(ForceId.Friend, affiliation);
    }
}
