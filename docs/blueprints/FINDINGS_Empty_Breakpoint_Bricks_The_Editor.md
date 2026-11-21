<!--STATUS
state: LIVE
updated: 2026-08-22
current-answer: the whole file — the crash, the fix that shipped, and two questions left OPEN.
note: found by the user's Windows visual-check session. Numbers are NOT allocated here (rule 3:
  the coordinator allocates no ids); the implementation session should number the two open
  questions when it creates the rows.
-->

# FINDINGS — an empty "New Breakpoint" **bricked the editor**, permanently

> 🔴 **Severity: the editor died on EVERY launch, with no route back through the UI.** Not a crash
> you retry — the poisoned state was reloaded on each start, so the only recovery was deleting a
> gitignored file by hand.

## 1. What happened

Clicking **Add** in the Breakpoints panel and not choosing a component type left an empty
breakpoint. It was saved to `.debug/bpsession.json` on exit and reloaded on the next launch, where
it killed the process a few seconds after the window appeared:

```
System.ArgumentNullException: Value cannot be null. (Parameter 'key')
  at Fdp.Core.ComponentTypeRegistry.GetId(Type)                    ComponentType.cs:322
  at Hrot.Diagnostics.Breakpoints.DataBreakpointSystem.ExecuteCore DataBreakpointSystem.cs:76
  → ModuleHostKernel.Update → EditorSubsystem.Update → Program.Main
```

The persisted state, verbatim:

```json
"DataBreakpoints": [{
  "Condition": { "$type": "PropertyMatch", "ComponentType": null,
                 "PropertyPath": "", "Operator": 0, "Predicate": null },
  "DisplayName": "New Breakpoint", "Enabled": true
}]
```

⚠ **It was first blamed on the `claude/stride-port` branch** — it appeared right after switching
there. That was a coincidence of timing: `DataBreakpointSystem.cs` is byte-identical across branches
and the trigger is **untracked local state that follows you across checkouts**. The same file bricks
the editor on any branch.

## 2. The chain — every link verified in source

| # | site | what it did |
|---|---|---|
| 1 | `PredicateBuilderState.cs:98` · `DataBreakpointManagerPanel.cs:54` | "Add" builds a bare `new PropertyMatchDto()`; `ComponentType` is declared `null!` |
| 2 | `EditorSubsystem.SaveDebugSession` | persists it, null and all |
| 3 | `TypeNameJsonConverter.Read` *(SearchPredicateDto.cs:17)* | **returns null** for an unresolvable type name — the second route to null |
| 4 | `PredicateCompiler.CollectMandatoryComponents` | added `ComponentType` **with no null check** |
| 5 | `DataBreakpointSystem.cs:76` → `ComponentTypeRegistry.GetId` | `TryGetValue(null)` **threw** |

## 3. ✅ What shipped

| guard | change |
|---|---|
| `ComponentTypeRegistry.GetId` | signature → `Type?`, returns `-1` for null. It always *documented* "-1 if not registered" — the lookup advertised as total was in fact partial |
| `PredicateCompiler.AddIfResolvable` | keeps nulls out of `MandatoryComponents` — the direct cause |

⭐ **Both are enforced by the nullable type system.** Reverting either does not fail a test — it
**fails to compile** (`CS8604`). The regression is impossible, not merely detected.

**Tests:** `Fdp.Toolkits.Tests/ReplayBrowser/Search/IncompletePredicateTests.cs` (7) — both routes to
null, nesting two deep, and the full collector → `GetId` chain. **Proven end to end:** the editor was
relaunched with the exact poisoned file and started clean.

### ⛔ A third guard was tried and is WRONG — do not re-add it

Skipping the mount for `ComponentType is null` in `DataBreakpointManager` looks like the natural
third guard. It broke
`WatchPersistenceTests.Watches_Restore_FailsGracefullyOnDriftedSchema`: skipping the compile also
skips the throw that `LoadWatches` catches to mark the entry **broken**, so a watch whose component
no longer resolves would return silently unmounted instead of visibly broken. That convention —
*"present but marked broken, not silently discarded"* — is deliberate and test-locked. A comment at
the site now says so.

## 4. ⭐ OPEN — two questions the fix deliberately did NOT answer

### Q-A — should an INCOMPLETE predicate be persisted at all?

The crash is now harmless, but the cause is untouched: an empty "New Breakpoint" is still created,
still saved, and still reloaded as a meaningless row that survives restarts.

| option | |
|---|---|
| **(a)** don't persist an incomplete predicate | simplest; ⚠ silently discards a row the designer created and may be mid-way through filling in |
| **(b)** persist it, mark it **broken** on load | matches the drifted-schema convention above — one rule for "cannot be evaluated", not two |
| **(c)** don't let it be created | the panel's "Add" would require a component type up front; ⚠ changes an authoring gesture |

⭐ **Lean: (b)** — it reuses a convention that already exists and is already tested, and §3 shows what
happens when the two "cannot be evaluated" paths disagree. ⛔ Not built: the choice is a UX ruling.

### Q-B — should a poisoned debug session be able to brick startup at all?

`.debug/bpsession.json` is restored **before** the editor is usable, so anything unparseable or
unmountable in it takes the process down before the designer can reach the UI that would fix it.
⚠ `RestoreDebugSession` already try/catches the *load*; what killed us ran a few frames **later**.

⇒ **Question: should restore be fail-soft as a policy** — a bad session is renamed aside, the editor
starts empty, and the designer is told? Every future defect in a restored breakpoint has the same
"unreachable UI" shape, so this is worth a ruling once rather than a guard per field.

## 5. Unrelated defect found while classifying — worth its own row

`Fdp.Toolkits.Tests`'s gizmo-registry tests are **order-dependent** and share the static
`ComponentTypeRegistry`. One of the family fails per full run and which one **varies by scheduling**
— observed as `SC_GZ022_2_Register_UnregisteredType_Throws` and, on a clean tree with all of the
above stashed, `SC_GZ004_2_Register_UnregisteredComponent_Throws`. ⭐ Each passes in isolation.
⚠ **Pre-existing** — reproduced with every change here stashed — but it makes the suite unreliable as
a gate, which matters more than the individual reds.
