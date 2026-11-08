using System;
using System.Linq;
using Fdp.Toolkit.Input;
using Xunit;

namespace Fdp.Presentation.Tests.ImGui.Input;

/// <summary>
/// ⭐⭐ B-10 K7 (docs/UX/DESIGN_Input_And_Commands.md §6b, R-4163) — the mouse gestures are rows of the SAME keymap, in the same scope chain as the keys: a row covers a MASK of
/// targets, names the modifiers it needs (the row naming the most wins), belongs to a scope (+ the hat's name), and the keymap refuses a gesture bound twice where both apply.
/// </summary>
public sealed class KeymapGestureTests
{
    private const GestureButton L = GestureButton.Left, R = GestureButton.Right;
    private static GestureEntry Row(GestureButton b, GestureTarget t, string id, GestureModifiers m = GestureModifiers.None, InputScope scope = InputScope.Map, string? q = null, GestureKind k = GestureKind.Click)
        => new(new MapGesture(b, k, m, t), scope, q, ActionRef.Command(id));
    private static MapGesture Hit(GestureButton b, GestureTarget t, GestureModifiers m = GestureModifiers.None, GestureKind k = GestureKind.Click) => new(b, k, m, t);

    /// <summary>🔴 Red-proof (compiles): matching on equality instead of the mask (<c>== </c> for <c>&amp; != 0</c>) ⇒ a row for AnyEntity answers no single entity ⇒ red.</summary>
    [Fact]
    public void ARowCoversAMaskOfTargets_TheHitIsOneOfThem()
    {
        var map = new Keymap();
        map.DeclareGesture(Row(L, GestureTarget.AnyEntity, "sel"));
        map.DeclareGesture(Row(L, GestureTarget.Ground, "clear"));
        Assert.True(map.TryFindGesture(InputScope.Map, null, Hit(L, GestureTarget.Hostile), out var a)); Assert.Equal("sel", a.Action.CommandId);
        Assert.True(map.TryFindGesture(InputScope.Map, null, Hit(L, GestureTarget.Selected), out a)); Assert.Equal("sel", a.Action.CommandId);
        Assert.True(map.TryFindGesture(InputScope.Map, null, Hit(L, GestureTarget.Ground), out a)); Assert.Equal("clear", a.Action.CommandId);
        Assert.False(map.TryFindGesture(InputScope.Map, null, Hit(R, GestureTarget.Ground), out _), "another button");
        Assert.False(map.TryFindGesture(InputScope.Map, null, Hit(L, GestureTarget.Ground, k: GestureKind.Drag), out _), "a drag is not a click");
    }

    /// <summary>🔴 Red-proof (compiles): the best-match tie-break dropped (first declared wins) ⇒ Shift+click resolves to the plain click's row ⇒ red.</summary>
    [Fact]
    public void TheRowNamingTheMostModifiersWins_AnExtraHeldModifierStillMatchesAPlainRow()
    {
        var map = new Keymap();
        map.DeclareGesture(Row(L, GestureTarget.Any, "select"));
        map.DeclareGesture(Row(L, GestureTarget.Any, "add", GestureModifiers.Shift));
        Assert.Equal("select", Find(map, Hit(L, GestureTarget.Entity)));
        Assert.Equal("add", Find(map, Hit(L, GestureTarget.Entity, GestureModifiers.Shift)));
        Assert.Equal("add", Find(map, Hit(L, GestureTarget.Entity, GestureModifiers.Shift | GestureModifiers.Ctrl)));
        Assert.Equal("select", Find(map, Hit(L, GestureTarget.Entity, GestureModifiers.Alt)));   // a modifier no row names does not unbind the plain row

        static string? Find(Keymap m, MapGesture g) => m.TryFindGesture(InputScope.Map, null, g, out var e) ? e.Action.CommandId : null;
    }

    [Fact]
    public void AHatScopeIsAQualifier_TheSameGestureInAnotherScopeIsNotAConflict_SameScopeOverlapIs()
    {
        var map = new Keymap();
        Assert.True(map.DeclareGesture(Row(R, GestureTarget.Ground, "menu")));
        Assert.True(map.DeclareGesture(Row(R, GestureTarget.Ground, "move", scope: InputScope.Hat, q: "Command")));
        Assert.False(map.DeclareGesture(Row(R, GestureTarget.Ground, "menu")), "idempotent");
        Assert.Empty(map.ValidateGestures());
        Assert.False(map.TryFindGesture(InputScope.Hat, "Edit", Hit(R, GestureTarget.Ground), out _), "another hat's scope has no row");

        map.DeclareGesture(Row(R, GestureTarget.Ground | GestureTarget.Entity, "other"));   // overlaps 'menu' on Ground, different action
        var conflict = Assert.Single(map.ValidateGestures());
        Assert.Contains("menu", conflict.ToString());
        Assert.Contains("other", conflict.ToString());
    }

    /// <summary>⛔ Hot path (R-2084) — a lookup allocates nothing.</summary>
    [Fact]
    public void ALookup_AllocatesNothing()
    {
        var map = new Keymap();
        map.DeclareGesture(Row(L, GestureTarget.Any, "select"));
        map.DeclareGesture(Row(L, GestureTarget.Any, "add", GestureModifiers.Shift));
        var g = Hit(L, GestureTarget.Entity, GestureModifiers.Shift);
        for (int i = 0; i < 3; i++) map.TryFindGesture(InputScope.Map, null, g, out _);
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 1000; i++) map.TryFindGesture(InputScope.Map, null, g, out _);
        Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
    }
}
