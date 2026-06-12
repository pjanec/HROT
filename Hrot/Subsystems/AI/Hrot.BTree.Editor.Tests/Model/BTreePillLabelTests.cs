using System;
using System.Globalization;
using FluentAssertions;
using Fbt;
using Hrot.BTree.Editor.Model;
using Xunit;

namespace Hrot.BTree.Editor.Tests.Model;

/// <summary>
/// Headless tests verifying that <see cref="BTreePillAttachmentModel"/> produces
/// per-type glyphs and parameter-including labels for decorator pills.
/// </summary>
public sealed class BTreePillLabelTests
{
    // ── helpers ───────────────────────────────────────────────────────────────

    private static BTreePillAttachmentModel MakeAttachment(
        NodeType decoratorType,
        int? intParam = null,
        float? floatParam = null)
    {
        var pill = new BTreeEditorPill
        {
            VisualId          = Guid.NewGuid(),
            HostNodeVisualId  = Guid.NewGuid(),
            DecoratorType     = decoratorType,
            IntParam          = intParam,
            FloatParam        = floatParam,
            StackIndex        = 0,
        };
        return new BTreePillAttachmentModel(pill);
    }

    // ── Repeater ──────────────────────────────────────────────────────────────

    [Fact]
    public void Repeater_LabelIncludesCount()
    {
        var a = MakeAttachment(NodeType.Repeater, intParam: 3);

        a.Label.Should().NotBeNull();
        a.Label.Should().Contain("3", "Repeater label must include the IntParam count");
        a.Glyph.Should().NotBeNullOrEmpty("every decorator type must have a non-null glyph");
    }

    // ── Cooldown ──────────────────────────────────────────────────────────────

    [Fact]
    public void Cooldown_LabelIncludesDuration()
    {
        var a = MakeAttachment(NodeType.Cooldown, floatParam: 2f);

        a.Label.Should().NotBeNull();
        a.Label.Should().Contain("2", "Cooldown label must include the float value");
        a.Label.Should().Contain("s", "Cooldown label must include the 's' suffix");
        a.Glyph.Should().NotBeNullOrEmpty("every decorator type must have a non-null glyph");
    }

    [Fact]
    public void Cooldown_LabelIsInvariant()
    {
        var originalCulture = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("de-DE");

            var a = MakeAttachment(NodeType.Cooldown, floatParam: 2.5f);

            a.Label.Should().NotBeNull();
            a.Label.Should().Contain("2.5",
                "Cooldown label must use invariant culture (dot, not comma decimal)");
            a.Label.Should().NotContain("2,5",
                "Cooldown label must NOT use locale-specific decimal separator");
        }
        finally
        {
            CultureInfo.CurrentCulture = originalCulture;
        }
    }

    // ── Inverter ──────────────────────────────────────────────────────────────

    [Fact]
    public void Inverter_HasGlyphAndLabel()
    {
        var a = MakeAttachment(NodeType.Inverter);

        a.Glyph.Should().NotBeNullOrEmpty("Inverter must have a non-null glyph");
        a.Label.Should().NotBeNullOrEmpty("Inverter must have a non-null label");
    }

    // ── All decorator types have glyphs ───────────────────────────────────────

    [Theory]
    [InlineData(NodeType.Inverter)]
    [InlineData(NodeType.Repeater)]
    [InlineData(NodeType.Cooldown)]
    [InlineData(NodeType.ForceSuccess)]
    [InlineData(NodeType.ForceFailure)]
    [InlineData(NodeType.UntilSuccess)]
    [InlineData(NodeType.UntilFailure)]
    public void AllDecoratorTypes_HaveNonNullGlyph(NodeType decoratorType)
    {
        var a = MakeAttachment(decoratorType);

        a.Glyph.Should().NotBeNullOrEmpty(
            $"decorator type {decoratorType} must have a non-null glyph");
    }
}
