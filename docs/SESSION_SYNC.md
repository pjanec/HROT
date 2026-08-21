# Cross-session sync — branch registry and the merge protocol

> **Neutral ground.** Owned by no single programme. Both long-running sessions link here rather than
> each keeping their own copy, so the protocol cannot drift between them.
>
> **Read this when:** you are about to start work, about to push, or you have just been told "merge the
> updates from the other session".

## The sessions

| # | Programme | Entry doc | Branch | Runs on |
|---|---|---|---|---|
| 1 | **Scenario-authoring UX** (outer loop — the editor shell) | [UX/UX_RESUME.md](UX/UX_RESUME.md) | `claude/ux-session-resume-i2le7f` ⚠ **renamed 2026-08-10** — fast-forwarded from `claude/reset-working-branch-qd1qpv` (`ab2c91a`), same history. Merge from **this** one; the old branch is stale but not force-moved | Linux cloud (coordinator) + Windows implementers |
| 2 | **AI Debug API + MCP port** (infrastructure) | [mcp-port/MCP_PORT_RESUME.md](mcp-port/MCP_PORT_RESUME.md) | ⚠ **TBD — the user will supply it. Record it here in your first commit.** | ✅ Linux cloud — **no Windows needed** |
| 3 | **Blueprint gaps & QoL** (inner loop — graph canvases) | [blueprints/Blueprint_Gaps_Programme_RESUME.md](blueprints/Blueprint_Gaps_Programme_RESUME.md) | `claude/blueprint-authoring-status-6sr5ld` | Windows, **active in parallel** |

⚠ **Session 3 does not know this file exists.** Nobody has added a pointer from its RESUME, because that
edits a doc it owns — see [SHARED_SURFACES.md](UX/SHARED_SURFACES.md#open-does-the-other-side-read-this).
Treat session 3 as *unreachable*: do not rely on it reading anything here.

**Sessions 1 and 2 do know about each other** and are expected to exchange updates in both directions.

## The one collision that actually matters

```
Hrot/Subsystems/Hrot.Editor/EditorSubsystem.cs     (~4.2k lines, the editor composition root)
```

Both sessions need to add to it:

| Session | What it adds there |
|---|---|
| **MCP port** | `_debugApiHost` field + `DebugApiHost`/`DebugApiService` construction — ~10 lines |
| **UX** | the new shell's registration/composition, eventually |

Session 3 also wires the blueprint windows there. **This file is the single point where all three
programmes meet.**

> ### <a id="sequencing-rule"></a>Sequencing rule
>
> **The MCP port lands its `EditorSubsystem.cs` wiring FIRST**, before the UX programme's shell work
> starts touching that file. Reason: the port's wiring is small, known, and already designed; doing it
> after the shell work means resolving it against a moving target and wiring it twice.
>
> Until the port has landed, the UX session treats `EditorSubsystem.cs` as **read-only**.

## Merge protocol

**Direction:** both ways, but never blind. Neither branch is "the trunk" for the other.

### Before you start work in a session

```bash
git fetch origin
git log --oneline -10 origin/<the-other-session-branch>     # what changed over there?
```

If the other branch has moved, **merge it in before doing anything else** — resolving one merge is
cheaper than resolving a merge plus your own half-finished work:

```bash
git merge origin/<the-other-session-branch>
```

These two branches **share history** (both descend from `main`), so this is an ordinary merge. ⚠ This is
**not** true of `feat/ai-debug-api`, which has no common ancestor with anything — see
[MCP_PORT_PLAN.md](UX/MCP_PORT_PLAN.md#the-blocking-fact-unrelated-histories).

### After you push

Tell the user, in your final message, that the other session should pull. Claude cannot notify it.

### When you hit a conflict

| Conflict in | Resolution |
|---|---|
| `EditorSubsystem.cs` | **Keep both additions.** Neither side is replacing the other — this is two features being wired into one composition root. If the merge looks like a choice, you have misread it |
| Each programme's own `docs/` folder | Should never conflict. If it does, someone edited the other programme's docs — revert that part |
| [UX/SHARED_SURFACES.md](UX/SHARED_SURFACES.md) · this file | **Union the rows.** Both sessions append; nobody rewrites the other's entries |
| A shared panel's internals | **Stop.** That change should have been proposed in [SHARED_SURFACES.md](UX/SHARED_SURFACES.md#proposed-changes-awaiting-consultation) first. Resolve by keeping the version that went through consultation |

### Never

- ⛔ `git merge --allow-unrelated-histories` — the only branch that would need it is `feat/ai-debug-api`,
  and doing so would try to reconcile two disjoint project histories (1187 files, 133k deletions).
- ⛔ Force-push either session branch. The other session may have merged from it already.
- ⛔ Rebase a branch the other session has merged from.
- ⛔ Edit the other programme's `docs/` folder, its trackers, or its RESUME. Add a row to a shared file
  instead.

## Facts both sessions need

Established 2026-08-06; **re-derive before relying on any of them** — the sibling blueprint audit was
wrong ten times.

| Fact | Evidence |
|---|---|
| The editor is **networkless by design and in code** — it discards the injected network factory | `EditorSubsystem.cs:180` hardcodes `OfflineNetworkFactory`; `:557` takes `INetworkFactory _` and ignores it |
| ⚡ **Sharpened 2026-08-10 (UX session):** it consumes **no** `INetworkFactory` — `_networkFactory` is declared and **never read**, so `:180` is a *dead field*, not a used offline default. ⇒ the editor preset can omit network composition entirely | `EditorSubsystem.cs:180` is the sole occurrence in the file; `:165` is `sealed` and not `partial`, so the file is the whole class |
| ⚠ …but **"networkless" ≠ "no sockets"**: session 2's port gives the editor a loopback `HttpListener` (`DebugApiHost`). It means no DDS / no cluster transport | `MCP_PORT_PLAN.md`; `feat/ai-debug-api` tip `d7b2a6e1` |
| ⇒ the DDS participant the host builds for the editor is **created and thrown away** | `Program.cs:194` inside `ScanForSubsystems`, which runs for *every* discovered subsystem before filtering |
| 🔴 **The shell silently drops a restored `BTree`/`HSM`/`Blueprint` perspective** — it validates the persisted id against *subsystem names*, which those are not. Only `"Editor"` survives, coincidentally. ⚠ code-derived, unconfirmed | `WindowManager.cs:368-382`/`:388-411` → `LocalWindowController.cs:83-84`; `EditorSubsystem.cs:172`. Session 3 touches these perspectives daily — see [Q25-F-ii](UX/Architect_Question_25_Scenario_Authoring_Golden_Path.md#f-ii-perspective-restore) |
| 🔴 **`Hrot.UI.Common` is dead but its namespace is live.** In **no `.csproj` and no `.sln`** — it never builds — yet the panels that *do* compile, in `Hrot.Presentation/Panels/`, declare `namespace Hrot.UI.Common.Panels`. The copies have **drifted**. ⚠ **Relevant to all three sessions: check which project a file belongs to before editing any shared panel** | verified 2026-08-10: `grep` for the `.csproj` across all projects and for the name across all `.sln` both return empty |
| ⭐ **The seam law** — every UI surface exposing a contribution seam is shared successfully across modes; every surface without one has been forked. No counter-example in five scans | [UX/UX_Current_UI_Architecture.md](UX/UX_Current_UI_Architecture.md) |
| ~26 production context-menu sites exist; the claim *"no right-click affordances"* was **false**, holding only for the 27-line `EditorOrbatPanel` | as above; the graph canvases session 3 works on have the richest such system in the repo |
| The **construction kit must survive** — distributed `--mode` variants keep working | user constraint, 2026-08-06 |
| 🔒 **`ClusterRunner` must stay fully operational** — session 3 works against it | user constraint, 2026-08-06 |
| The editor's UI is produced by a **generic cluster-node window aggregator**, not a designed shell | `LocalWindowController.OpenLocalWindow()`, ~60 lines; default perspective = `_subsystems.Skip(1).FirstOrDefault()?.Name` |
| `feat/ai-debug-api` has **no common ancestor** with `main` or either session branch | `git merge-base` returns empty both ways |
| 🗺 The **2D symbolic map is Raylib across the whole OS window, behind ImGui**, visible only through the `PassthruCentralNode` central node; it stays Raylib for speed | `Program.cs:347-349`; user constraint 2026-08-06. Its *visible* rect ≠ the window — see [UXR-18](UX/UX_Requirements.md#uxr-18) |
| 🔴 The editor **never sets `MapCamera.Offset`**, so centre-on-entity should land at the window's top-left | no `Camera.Offset` assignment in `Hrot.Editor`; `MapCamera.cs:62`. ⚠ code-derived, unconfirmed |

## Shared work habits

Both sessions inherit these from the blueprint programme, where each was earned the hard way. Full
statement: [UX/UX_Programme_Briefing.md §5](UX/UX_Programme_Briefing.md#5-work-habits--non-negotiable).

1. **Verify before you build.** Never build against a doc's claim — including these docs. Fix the doc in
   the same commit if it was wrong.
2. **Assert the effect, never the report.** `BlueprintCommandSink.Apply`'s `default:` arm returns
   *success* for commands it does not handle; that shape has silently killed four shipped features.
3. **Revert to watch it go red.** Required. A test written for a fix once passed against the bug.
4. **Grep the production construction sites.** An optional ctor dependency defaulting to an inert value
   has silently disabled a feature three times.
5. **Delegate to Sonnet** for mirror-a-pattern slices, mechanical edits and broad searches; stay hands-on
   for novel design and the final diff review. **Re-run the gates yourself** — never accept a subagent's
   "all green" unseen.
6. **Architect gate** for non-trivial capability: an `Architect_Question_NN` doc, A/B/C/D
   decision-shaped, with Claude's lean. **Claude cannot reach the architect; the user relays.** UX
   numbering is at **Q25**; take the next free number and say which you used.
7. **Docs short**, terse tables, hand-authored SVG for non-trivial diagrams, deep-link everything.
8. **Plain chat prose for questions** — never the multiple-choice widget.
9. **Report faithfully.** Red gate → say so with the output. Skipped step → say so.

## Change log for this file

| Date | Change |
|---|---|
| 2026-08-06 | Created when the MCP port was split into its own session. Branch for session 2 is **TBD**. Session 2 confirmed as **Linux-capable, no Windows needed**; added the map/viewport facts |
| 2026-08-10 | Session 1 published the [UI architecture assessment](UX/UX_Current_UI_Architecture.md) — the seam law, the dead-`Hrot.UI.Common` trap (**affects all three sessions**), and the refutation of the "no right-click affordances" claim |
| 2026-08-10 | Session 1 ran the pre-seam check. Sharpened the networkless fact (dead field, not a used default), added the "networkless ≠ no sockets" rider, and added the 🔴 perspective-restore defect — **relevant to session 3**, which lives in the `Blueprint` perspective |
