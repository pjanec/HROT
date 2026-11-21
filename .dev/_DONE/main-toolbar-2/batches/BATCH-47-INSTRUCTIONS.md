# BATCH-47 — BUG-A13 (P1): unify toolbar icon chrome (vector + bitmap share ONE hover/toggle indicator)

**Model:** pro · **Repo root:** `D:\Work\IOS-IG-SimHost-FDP` · branch `blueprint-integ-1`.
Do NOT use codebase-memory tooling. Read `.dev/.guides/DEV-GUIDE.md` then this file.

## Problem
Toolbar icons render through **two unsynchronized code paths**, so hover/toggle look different:
- **Vector** (time-control): `FDP/Engine/Fdp.Presentation/ImGui/Icons/TransportIconRenderer.cs` `DrawButton`
  draws its OWN hover fill (white@0.12) + white@0.55 frame.
- **Bitmap** (New Asset, perspective-switch, Open/Save): `FDP/Engine/Fdp.Presentation/ImGui/Icons/IconWidgets.cs`
  `ToggleIcon`/`IconButton` overloads — after the last change these draw a filled hover and NO frame.
Result: only the time-control icons show a frame; the bitmap ones don't → inconsistent. Also the frame's top edge
is invisible (it sits flush against the menu-bar boundary).

## Goal — ONE shared chrome, used by BOTH renderers
The hover + toggle indicator must be **identical** for every icon; the vector-vs-bitmap difference must be ONLY the
glyph draw. Agreed scheme (decided with the user — do NOT change it):
- **Hover (enabled): a white frame, INSET by 1px** so all four edges (incl. the top) are visible even at a window
  boundary. ~1.5px thick, white @ 0.9. No hover fill.
- **Toggled (enabled): a blue/accent FILLED background** (`ImGuiCol.HeaderActive` @ 0.85), drawn BEHIND the glyph.
- **Toggled + hovered:** both compose (blue fill + white inset frame on top). Naturally falls out of the order below.

## Part A — shared chrome helper
Add a small static helper in `Fdp.Presentation.Icons` (new file `IconButtonChrome.cs`, or static methods on
`IconWidgets` — your call, but keep it the single source of truth):
```csharp
public static void DrawToggleFill(ImDrawListPtr dl, Vector2 pos, Vector2 size, bool toggled)
{
    if (!toggled) return;
    var c = ImGui.GetStyle().Colors[(int)ImGuiCol.HeaderActive]; c.W = 0.85f;
    dl.AddRectFilled(pos, pos + size, ImGui.GetColorU32(c), 2f);
}
public static void DrawHoverFrame(ImDrawListPtr dl, Vector2 pos, Vector2 size, bool hovered)
{
    if (!hovered) return;
    var a = pos + new Vector2(1f, 1f);
    var b = pos + size - new Vector2(1f, 1f);   // inset so the top/left edges are visible
    dl.AddRect(a, b, ImGui.GetColorU32(new Vector4(1f, 1f, 1f, 0.9f)), 2f, ImDrawFlags.None, 1.5f);
}
```
Draw order at every call site: **DrawToggleFill → (glyph image/shape) → DrawHoverFrame** (frame on top).

## Part B — IconWidgets uses the shared chrome
In the **IconHandle** `ToggleIcon` overload (~L289): remove the current hover-fill block and the toggled-fill block
(and any leftover white `AddRect`); replace with `DrawToggleFill(dl, screenPos, size, enabled && isToggled)` BEFORE
`AddImage`, and `DrawHoverFrame(dl, screenPos, size, enabled && isHovered)` AFTER `AddImage`. Keep the disabled-dim
image logic and the 1px pressed glyph shift unchanged. Do the SAME (use the shared helper) in the other interactive
overloads for consistency: the `IconAtlas` `ToggleIcon` (~L68), `AlternatingFaceToggleIcon` (~L112), and
`DropdownFaceIcon` (~L152) — replace each one's bespoke white `AddRect` hover border (and the IconAtlas gray toggle
fill) with `DrawHoverFrame` (+ `DrawToggleFill` where a toggle exists). Update XML docs to "shared hover frame /
toggle fill".

## Part C — TransportIconRenderer uses the shared chrome
In `DrawButton` (~L52-67): remove the bespoke hover fill (white@0.12) AND the white@0.55 frame; instead call
`DrawHoverFrame(dl, pos, new Vector2(size,size), hovered && enabled)` AFTER `DrawShape`. (Transport buttons have no
persistent toggle, so no `DrawToggleFill` here.) Keep the `DrawShape` glyph + the pressed 1px shift unchanged. The
`hovered`-brightening of the glyph (alpha 1.0 on hover) may stay.

## Tests
- `Fdp.Presentation.Tests` and `Hrot.Presentation.Tests` (`TransportIconsTests`, icon/toolbar tests): build + run.
  If a test asserts the OLD hover fill/border colors or the old transport frame, UPDATE those assertions to the new
  shared chrome (intentional, user-approved scheme — note each updated assertion in the report). Do NOT weaken
  unrelated assertions or delete tests.
- Build `Fdp.Presentation` + any test projects above with 0 warnings.

## Definition of done
- Both renderers call the single shared chrome; every toolbar icon shows the SAME hover (inset white frame, visible
  top edge) and the SAME toggle (blue fill). New Asset + perspective-switch + time-control all match.
- Build 0 warnings; named suites `Failed: 0`.
- Write `.dev/_DONE/main-toolbar-2/reports/BATCH-47-REPORT.md`: the shared helper, the call-site changes per renderer,
  tests, any updated assertions, summary. (Visuals runtime-verified by the lead.)

If something cannot be done as specified, STOP and report why rather than stubbing.
