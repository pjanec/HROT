# Feature design — perspective restore

> **Design for [UXI-06](UX_Issues.md#uxi-06) · drafted 2026-08-10.**
> **Status: ✅ designed — one policy question flagged for the user.**

## 0. Prior art — ✅ checked before designing ([rule 6](UX_Issues.md#rules))

| Exists? | What | Bearing |
|:--:|---|---|
| ⭐ | **`WindowManager.GetPerspectives()`** (`:178-186`) — distinct `OwningPerspective` over `PerspectiveBound` windows. Its own doc comment calls it *"the testable seam for perspective enumeration"* | 🔴 **The seam for this exact fix exists, is documented as such, feeds the perspective menu — and the restore path does not call it.** [The seam law](UX_Seam_Inventory.md), fifth instance |
| ✅ | `RegisterPerspectiveLabel(id, label)` (`:228`) — display name decoupled from id | ⇒ ids are stable; labels are cosmetic |
| ✅ | Persistence round-trip: `SaveSettings` writes `CurrentPerspective` into `fdp_windows.json` (`:368-382`), `LoadSettings` returns it (`:388-411`) | **the storage layer is fine** — nothing to build |
| ❌ | Any validation inside `SwitchPerspective` (`:164-171`) | accepts **any** string; an unknown value hides every `PerspectiveBound` window |

## The defect: the predicate tests the wrong set — in **both** directions

```csharp
// LocalWindowController.cs:81-84
var first = _subsystems.Skip(1).FirstOrDefault();
string defaultPersp = first?.Name ?? "Default";
bool valid = !string.IsNullOrEmpty(persisted) && _subsystems.Any(s => s.Name == persisted);
wm.SwitchPerspective(valid ? persisted! : defaultPersp);
```

It compares a **perspective** against the set of **`ISubsystem.Name`s**. Those two sets overlap by
coincidence — for the cluster roles a subsystem's name happens to equal its perspective — and diverge
everywhere else.

| Direction | Case | Effect |
|---|---|---|
| **False negative** | `BTree` · `HSM` · `Blueprint` — the Editor's document perspectives. No subsystem bears these names | **silently discarded**; you always return to `Editor` |
| **False positive** ⚠ | `Orchestrator`, `PerspectiveCoordinator` — real subsystem names, **not** perspectives | would **pass**, then `SwitchPerspective` hides **every** `PerspectiveBound` window. Latent: nothing sets them today |
| **Fragile default** | `_subsystems.Skip(1)` | positional — it skips the always-injected `PerspectiveCoordinator` (`Program.cs:211`). Reorder composition and the default silently changes |

### The ten real perspectives, and which survive a restart

| | Perspectives | Restored? |
|---|---|:--:|
| Cluster roles | `IG` `SimHost` `ExCon` `CGF` `StrideMock` `ReplayBrowser` | ✅ — by coincidence, names match |
| Editor shell | `Editor` | ✅ |
| **Editor documents** | **`BTree` `HSM` `Blueprint`** | ❌ |

> ### ⚠ Correction — `"Scenario"` is **not** a perspective
>
> It is a **display label** over the `Editor` perspective:
> `windowManager.RegisterPerspectiveLabel("Editor", "Scenario")` (`EditorSubsystem.cs:3449`). The id is
> always `Editor`. ⇒ this programme has repeatedly written *"Scenario/BTree/HSM/Blueprint"* as four
> Editor perspectives; **there are three**, plus a relabelled shell.
> [Corrections 17](UX_Tasks_Detail.md#corrections).

## ⭐ The finding that makes this a policy question, not just a bug

**The Editor's document perspectives are not a user preference — they are a function of the open
document.** Activating a document switches perspective automatically:

```
AiDocumentManager.Activate(doc)            AiDocumentManager.cs:152-168
  → _perspectiveSwitchCallback(doc.Kind.ToPerspectiveName())
      AssetKind.BTree → "BTree" · Hsm → "HSM" · Blueprint → "Blueprint"     AssetKindExtensions.cs:28-34
  → WindowManagerPerspectiveSwitcher → wm.SwitchPerspective(...)
```

**And documents are not persisted.** `AiDocumentManager` has no save/load/restore of any kind; only
`AssetBrowserPanel.LastOpenedByKind` (`:196-205`) remembers a *path per kind*, for the browser's
selection — it does not reopen anything.

> ### ⇒ Restoring `BTree` today would land the user in the BTree workspace **with no document open**
>
> So the current fallback is *defensible* — but it is **accidental**: it happens because of a vocabulary
> mismatch, not because anyone reasoned about empty workspaces. And it is **silent**.

## The design

### Part 1 — fix the predicate. Not optional, and independent of the policy below

```csharp
var known = wm.GetPerspectives();                       // the documented seam, already populated:
                                                        // RegisterWindows(wm) ran ~30 lines earlier
string defaultPersp = known.Contains(preferred) ? preferred : known.FirstOrDefault() ?? "Default";
bool valid = !string.IsNullOrEmpty(persisted) && known.Contains(persisted);
wm.SwitchPerspective(valid ? persisted! : defaultPersp);
```

✅ Closes **both** error directions and drops the positional `Skip(1)`. **This is the whole correctness
fix**, and it is a handful of lines in one file.

⚠ **`SwitchPerspective` should also refuse an unknown id** (log + no-op) rather than silently hiding every
`PerspectiveBound` window. Defence in depth — the caller above is the real fix.

### Part 2 — 🔷 **policy question for the user: what should restoring `BTree` do?**

| | Option | Behaviour | Cost |
|---|---|---|---|
| **A** | **Restore it** | you return to the BTree workspace, **empty until you open a tree** | none beyond Part 1 |
| **B** | **Fall back deliberately** — treat document perspectives as non-restorable without their document | today's behaviour, but *intentional*, documented, and no longer coupled to subsystem names | a small explicit set |
| **C** | **Restore the document, let it drive the perspective** | the model the code already implies — `Activate` switches perspective, so the document is the real state | ⚠ needs document-session persistence, which **does not exist** |

> **Claude's lean: A**, and note C as the eventual right answer.
>
> A matches the mental model this programme is built on — *remember where I was* — and an empty workspace
> costs one click to fill, whereas silent discard is exactly the class of behaviour
> [UXR-86](UX_Requirements.md#uxr-86) and the wider programme exist to remove. B preserves a behaviour
> whose only justification is an accident. **C is strictly better than both** and is a different feature
> (session restore); A does not block it and is the natural half-step.

## Migration

| Step | Change | Gate |
|--:|---|---|
| 1 | Predicate + default from `GetPerspectives()`; drop `Skip(1)` | restart in each cluster-role mode → same perspective as before (they already worked) |
| 2 | `SwitchPerspective` rejects unknown ids with a log line | a deliberately corrupted `fdp_windows.json` no longer blanks the UI |
| 3 | *(pending the policy answer)* A: nothing further · B: an explicit non-restorable set | restart from a BTree perspective → lands per the chosen policy |

**Test seam:** `GetPerspectives()` is already called *"the testable seam"* — assert `{IG, SimHost, ExCon,
CGF, StrideMock, ReplayBrowser, Editor, BTree, HSM, Blueprint}` for `--mode all` + editor, and assert the
restore predicate against that list rather than against subsystem names.

## 🔒 Out of scope

| | Why |
|---|---|
| Document/session restore | that is option C — a separate feature with no existing persistence |
| The `perspectiveMap` → `SwitchMapOwner` table (`Program.cs:244-251`, 5 entries) | **correct as-is.** `Editor`/`BTree`/`HSM`/`Blueprint`/`ReplayBrowser` own no network map, and unmapped names are a deliberate no-op |
| Renaming `Editor`→`Scenario` | the label seam already handles it; renaming the **id** would invalidate every persisted setting |

## Risk — and the dependency [UXI-05](UX_Feature_Menu_Follows_Focus.md) now has here

⚠ **UXI-05 binds menu items to perspective ids.** If restore lands on the wrong perspective, those items
are hidden — so a restore bug becomes a *menu* bug. And UXI-05's items may name a perspective that
`GetPerspectives()` does not know, because the set is **emergent from windows**: a perspective with menu
items but no windows would be unreachable and unswitchable.

🔒 **Consequence: do UXI-06 before UXI-05**, and if UXI-05 ever needs a perspective with no windows, the
set must become **declared** rather than emergent — which is exactly the prerequisite
[Q26-D](Architect_Question_26_Entity_Action_Model.md#q26-d--is-perspective-the-right-profile-key) flagged.
Today no such perspective exists, so `GetPerspectives()` is sufficient.
