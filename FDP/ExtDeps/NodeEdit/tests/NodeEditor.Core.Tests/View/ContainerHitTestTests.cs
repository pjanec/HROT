using FluentAssertions;
using NodeEditor.Core.View;
using NodeEditor.Primitives;
using System;
using Xunit;

namespace NodeEditor.Core.Tests.View;

/// <summary>Smoke tests for HoverKind.Container, ContainerHoverZone, and HoverInfo.ContainerZone.</summary>
public sealed class ContainerHitTestTests
{
    [Fact]
    public void HoverKind_HasContainerValue()
    {
        var names = Enum.GetNames<HoverKind>();
        names.Should().Contain("Container");
    }

    [Fact]
    public void ContainerHoverZone_HasExpectedValues()
    {
        var names = Enum.GetNames<ContainerHoverZone>();
        names.Should().Contain("None");
        names.Should().Contain("Header");
        names.Should().Contain("CollapseArrow");
        names.Should().Contain("Interior");
    }

    [Fact]
    public void HoverInfo_ContainerHeader_StoresNodeIdAndZone()
    {
        var nodeId = IdGenerator.NewNodeId();
        var info = new HoverInfo
        {
            Kind          = HoverKind.Container,
            Node          = nodeId,
            ContainerZone = ContainerHoverZone.Header,
        };

        info.Kind.Should().Be(HoverKind.Container);
        info.Node.Should().Be(nodeId);
        info.ContainerZone.Should().Be(ContainerHoverZone.Header);
    }

    [Fact]
    public void HoverInfo_CollapseArrow_StoresCorrectZone()
    {
        var nodeId = IdGenerator.NewNodeId();
        var info = new HoverInfo
        {
            Kind          = HoverKind.Container,
            Node          = nodeId,
            ContainerZone = ContainerHoverZone.CollapseArrow,
        };

        info.ContainerZone.Should().Be(ContainerHoverZone.CollapseArrow);
    }

    [Fact]
    public void HoverInfo_ContainerInterior_StoresCorrectZone()
    {
        var nodeId = IdGenerator.NewNodeId();
        var info = new HoverInfo
        {
            Kind          = HoverKind.Container,
            Node          = nodeId,
            ContainerZone = ContainerHoverZone.Interior,
        };

        info.ContainerZone.Should().Be(ContainerHoverZone.Interior);
    }

    [Fact]
    public void HoverInfo_None_HasNoContainerZone()
    {
        var info = HoverInfo.None;

        info.Kind.Should().Be(HoverKind.None);
        info.ContainerZone.Should().Be(ContainerHoverZone.None);
    }

    [Fact]
    public void HoverInfo_ContainerZoneDefault_IsNone()
    {
        // Default-constructed HoverInfo should have ContainerZone.None
        var info = new HoverInfo { Kind = HoverKind.Node, Node = IdGenerator.NewNodeId() };
        info.ContainerZone.Should().Be(ContainerHoverZone.None);
    }
}
