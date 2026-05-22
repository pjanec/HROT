# D.1 — Custom Event UX

## What a custom event is

A named entry-point graph with declared parameters that can be invoked from elsewhere in the same asset (or by external callers, depending on dispatch).

In the Blueprint subsystem's terminology: a `CustomEventDecl` from your asset model.

## D.1.1 Creation flow

User clicks `+` next to "Custom Events" in the **My Blueprint** panel, or invokes `editor.create-custom-event`.

A small modal form opens (using picker chrome from `C_generic_picker.md` C.11):

```
┌────────────────────────────────────────────┐
│ Create Custom Event                     ✕  │
├────────────────────────────────────────────┤
│ Name:    [ OnEnemyKilled                ]  │
│                                            │
│ Category: [ Combat ▾ ]                     │
│                                            │
│ Description:                               │
│ [                                       ]  │
│                                            │
│ Parameters (0):                            │
│   [ + Add parameter ]                      │
│                                            │
│ Replicated: ☐                              │
│ Reliable:   ☐ (grey unless replicated)     │
├────────────────────────────────────────────┤
│         [ Cancel ]    [ Create Event ]     │
└────────────────────────────────────────────┘
```

**Name validation**: same rules as variables — unique within scope, valid identifier, red border + inline error if invalid.

### After creation

- New entry appears in My Blueprint panel under "Custom Events".
- New graph tab opens automatically with the event's entry node placed at canvas center.
- Focus moves to the new graph.

## D.1.2 The entry node

The Custom Event entry node looks like a regular event node (red header, no input exec pin, one output exec pin):

```
┌─────────────────────────────────┐
│ ⚡ OnEnemyKilled                 │
├─────────────────────────────────┤
│                          ▶ Then │
│  EnemyId: int            ◯      │
│  Killer:  Entity         ◯      │
└─────────────────────────────────┘
```

The entry node is **special**:

- Cannot be deleted from inside the graph (would orphan the graph). Right-click → Delete is greyed.
- Parameters are edited via the **Details panel** when the entry node is selected.
- Renaming the event renames it everywhere it's called (host's command sink applies a project-wide rename batch).

## D.1.3 Editing parameters

When the entry node is selected, the Details panel shows:

```
Custom Event: OnEnemyKilled
────────────────────────────
Name:        [ OnEnemyKilled            ]
Category:    [ Combat ▾ ]
Description: [                          ]

Parameters:
┌──────────────────────────────────────┐
│ ≡ EnemyId    : int     [default: 0]× │
│ ≡ Killer     : Entity  [default: -] ×│
│ + Add parameter                      │
└──────────────────────────────────────┘
```

Each parameter row has:

- `≡` grip for reordering (drag to reorder; reorders pins on entry node and all call sites).
- Name field (inline rename).
- Type via type picker (clicking the type opens the type picker).
- Default value editor inline (same mini-editor system, see `B_mini_editors.md`).
- `×` to delete.

When parameters are added/removed/reordered: **entry node and all call sites update automatically**. Existing wires connected to renamed/moved parameters stay connected via parameter Guid (not by name or order). Existing wires on *deleted* parameters are broken; the editor shows a notification:

```
Parameter 'X' removed. 3 connections broken in 2 graphs.
```

## D.1.4 Calling a custom event

In My Blueprint panel: drag the custom event onto the canvas. A "Call OnEnemyKilled" node is placed:

```
┌─────────────────────────────────┐
│ ⚡ Call OnEnemyKilled            │   ← purple header (custom event call)
├─────────────────────────────────┤
│ ▶ In           ▶ Out            │
│ ◯ EnemyId: int                  │
│ ◯ Killer:  Entity               │
└─────────────────────────────────┘
```

Right-click the call node → "Go to Definition" opens the event's graph tab.

## D.1.5 Renaming

Three paths, all running the same underlying rename command:

- Inline rename on the entry node header (double-click).
- Via Details panel.
- Via My Blueprint right-click → Rename.

**Side effect**: all call sites update. Editor verifies no callsites are broken; if a rename would conflict (name already taken), the rename is rejected with an inline error and a toast.

## D.1.6 Deletion

Right-click custom event in My Blueprint → Delete.

- If no call sites: silently deletes the event + its graph.
- If call sites exist: shows confirmation `Delete OnEnemyKilled? 3 call sites will be broken.` User confirms; call sites become red error nodes `Unknown custom event: OnEnemyKilled`. User can manually fix or undo.

## D.1.7 Network settings (V2)

The Replicated / Reliable checkboxes are V2 features and depend on host support for network replication. The validator must enforce:

- Reliable can only be checked if Replicated is checked.
- The replication mode (RunOnServer, RunOnClient, Multicast) is declared in Details panel when Replicated is on.

Host integration: these settings map to your asset's `CustomEventDecl` flags.
