using System;
using Fdp.Core;
using Fdp.Presentation.WindowManager;
using Fdp.Toolkit.Blueprints;

namespace Hrot.Blueprints.Editor.EntityBlueprints;

/// <summary>
/// <see cref="ManagedWindow"/> wrapper that lazily creates an <see cref="EntityBlueprintsPanel"/>
/// on first render. The optional <paramref name="entityResolver"/> is called each frame before
/// refreshing reality so the panel tracks the editor's entity selection.
/// </summary>
public sealed class EntityBlueprintsManagedWindow : ManagedWindow
{
    private readonly Func<EntityBlueprintsPanel> _factory;
    private EntityBlueprintsPanel? _instance;

    public EntityBlueprintsManagedWindow(
        Func<EntityBlueprintsPanel> factory)
        // ⭐⭐⭐ CE-086 — THE ID IS SNAKE_CASE LIKE EVERY OTHER WINDOW ID.
        //    🔴 It used to be the DISPLAY TITLE, `"Entity Blueprints"` — space, capitals — and a window id
        //       is the LAYOUT KEY (`ManagedWindow.WindowInternalName` = `"{Title}###{Id}"`). ⇒ ⛔ anyone
        //       "fixing the capitalisation" of the visible title would silently reset saved layouts.
        //    ⭐ Surfaced by CE-076's golden growth, which made window ids visible to the baseline for the
        //       first time. 🔒 User ruling `2026-08-27`: *"Unify the internal window ids to snake, breaking
        //       layout is not an issue."*
        //    ⚠ The TITLE is unchanged — it is display text and it should read like display text.
        //    ⚠ The PANEL id (`PanelIds.EntityBlueprints` = "entity-blueprints") is deliberately NOT
        //      touched: panel ids are kebab across the whole diagnostics contract and the MCP /panels
        //      surface (`graph-signature`, `data-breakpoint-manager`, `watch`), so kebab is that
        //      namespace's convention, not an inconsistency. 📄 §5c.14.
        : base(id: "entity_blueprints", title: "Entity Blueprints",
            owningPerspective: "Blueprint", scope: WindowScope.PerspectiveBound)
    {
        _factory = factory ?? throw new ArgumentNullException(nameof(factory));
    }

    protected override void DrawClientArea()
    {
        _instance ??= _factory();
        Title = _instance.Title;
        _instance.DrawUI();
    }
}
