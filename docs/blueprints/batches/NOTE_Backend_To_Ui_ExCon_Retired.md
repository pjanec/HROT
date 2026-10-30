<!--STATUS
state: LIVE
updated: 2026-10-30
current-answer: whole file
related-designs:
  - ../Architect_Question_109_Retire_ExCon.md — §6 the slices and their as-builts (X1–X5)
-->
# NOTE backend → ui — ExCon is retired (R-3067), and what that changes for you

| slice | commit | what you might notice |
|---|---|---|
| X1 tests off ExCon | `fc65d1434` | `HrotRunnerHarness` rejects an `excon` node (naming R-3067). The mission-commit rail now drives CGF's shared `IMissionEditorService`, which a headless CGF now has (`b2cb97ad1`) |
| X2 the node | `7126a8a24` | `Hrot.ExCon` and its tests are gone; `--mode excon` / `ios` are invalid; `all` is 5 nodes. ⭐ the Cluster Control window id is `cluster_control` (was `excon_cluster_control`) in the default layout and goldens |
| X3 the IG's remote map | `49cdc382f` | `MiniExConPanel` (`ig_mini_excon`), `MapCommandController` and `ContextMenuSystem` are gone. ⚠ The IG's area / route / pick / placement tools went with them: they had no entry except a remote command, because `ActivateEditorToolEvent` is not wired on the IG. If the Player should author anything itself, that is a new design on the shared `ToolController` |
| X4 world-less mirrors | `08243c8cc` | the editor's never-written DER, `EditorOrbatPanel` / `EditorOrbatWindow` and `EntityPropertyInspector` are gone. `DESIGN_Cluster_View.md` V2 is superseded (the observer-only translator is gone) |
| X5 wire inspector | `862d3c10f`, `2e14700ef` | ⭐ a new **Wire Entity Inspector** (`{prefix}wire_entities`) in `DiagnosticsWindowsBundle` on every host. It shows each entity's raw descriptors as received for the 57 entity-keyed wire types, and is opt-in (no readers while closed). DER ids are now `long` (`IDerRepo`, `DerEntityInspectorPanel`). `ManagedWindow` gained `OnRenderWhileClosed()` (default no-op) |

Retired wire topics (names reserved, never reuse): `MapCommandRequest`, `ContextActionsUpdate`, `ContextMenuRequest`,
`MapInteractionConfig`, `MapCommandAck`, `ContextActionInvoked`, `MapClickEvent`, `SelectionChangedEvent`, `MapConfigStatus`,
`IGCapabilitiesAnnounce` (the `Hrot_NED_Descriptors_*` one; the gizmo-namespace one stays) and `DragEvent`. They are listed in the protocol
doc's "Retired topics" row.
⚠ The `ui-baseline-*` window goldens were hand-edited for the new ids. A real capture in the system suite (T3) will confirm them.
