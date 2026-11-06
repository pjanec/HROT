<!--STATUS
state: LIVE — the protocol for HANDOFF_Coordinator_To_Ui_Editing_UX_Build and HANDOFF_Coordinator_To_Backend_Editing_UX_Build
updated: 2026-11-06
current-answer: the whole file
stale-below: nothing
known-rot: none
known-conflict: none
related-designs:
  - ../../../.claude/CLAUDE.md — the lane protocol, gate contract, fidelity (R-4146, R-4151), hot path (R-2084) this file applies
-->

# PROTOCOL — two lanes building in parallel, coordinating themselves

🔒 User, `2026-11-06`: *"write it in a way that those two lanes will merge each other as they go and write status or requests to the other
lanes as they go, without any external coordination"* · *"it can continue throughout the day as well … to keep the overnight worker busy"*.

**The lanes:** `ui` (branch `ui`) and `backend` (branch `backend`). Each has its own handoff with an ordered item list. Nobody is watching:
⛔ **never stop to ask the user; never wait idle.**

## 1. The loop — after EVERY item

```
① commit the item (only your own files, by path) and push your branch
② update YOUR status file (§3) and push it
③ git fetch origin ui backend coordinator
④ read the OTHER lane's status file and every NOTE addressed to you (§4)
⑤ if the other lane pushed: git merge origin/<other> (MERGE, never rebase), then `dotnet restore` the test projects you will run
   (the third stale-binary trap: a merge can add a package) — resolve conflicts keeping BOTH lanes' work
⑥ answer any REQUEST addressed to you (§4) — do it next if it is small, else queue it and say when
⑦ pick the next item that is NOT blocked (§2); a blocked one is skipped, never waited on
```

⭐ Merge `coordinator` too when it moved (a later coordinator commit is **FYI** — your scope is frozen at the dispatch sha; if it invalidates an
item, STOP that item and note it).

## 2. Dependencies between the lanes

Each handoff marks an item **⏳ needs `<lane>`·`<item>`**. Before starting such an item, check the other lane's status file: if that item is
**DONE** and merged into your branch, go; otherwise skip to the next item and come back after the next fetch. ⭐ If everything left is blocked,
take the other lane's REQUESTs, or the "always available" tail of your handoff (§ "When blocked").

## 3. The status file — one per lane, rewritten at each item

`docs/blueprints/batches/STATUS_Editing_UX_Build_<Ui|Backend>.md`:

```
<!--STATUS state: LIVE · updated: <time> · current-answer: the table -->
# STATUS — <lane>, editing-UX build
| item | state | sha | what the other lane can now use / notes |
|---|---|---|---|
| U-1 | DONE | abc1234 | TreeRow / SectionHeader / FormGrid in NodeEditor.Core (Widgets/) — use them |
| U-2 | IN PROGRESS | — | |
| U-5 | SKIPPED — blocked | — | needs backend B-1 |
| U-9 | STOPPED (R-106) | — | premise failed: <one line>, NOTE to coordinator |
```

## 4. Notes between lanes

`docs/blueprints/batches/NOTE_<From>_To_<To>_<Topic>.md` (From / To ∈ Ui, Backend, Coordinator), one short file each, never edited after
it is pushed (a reply is a new note):

| kind | first line | when |
|---|---|---|
| **REQUEST** | `REQUEST — <what you need>, for <your item>; I continue with <next item> meanwhile` | you need something in the other lane's files |
| **DONE** | `DONE — <what is now available>, at <sha>` | only when the status row is not enough (an API to call, a changed contract) |
| **ANSWER** | `ANSWER to <note> — <what you did>, at <sha>` | after acting on a REQUEST |
| **QUESTION** (to Coordinator only) | `QUESTION — <the decision>, lean <X>; item <N> skipped` | a real design decision appeared — ⛔ never decided by you; skip the item |

⛔ **A lane never edits the other lane's files to unblock itself**; it writes a REQUEST. ⭐ Exception: a merge conflict (resolve keeping both).

## 5. Rules both lanes keep

| rule | |
|---|---|
| **design first, as-built back** | build each item against its cited design section and diagrams; a deviation is folded into the design, prior state marked SUPERSEDED (CLAUDE.md obligation ⑤) |
| **fidelity, by yourself** (R-4146, R-4151) | a UI item renders its real frames under `xvfb-run -a` and compares them with the mockup itself, fixes and re-renders; a mockup governs only its named scope — nothing it omits is removed or restyled |
| **hot path** (R-2084) | 0 B per steady frame for anything drawn or run each frame — a rail with `GC.GetAllocatedBytesForCurrentThread()` |
| **new components** | unmanaged when recorded; managed = immutable records, replaced whole |
| **one implementation** (R-174) | before building, `search_graph` + grep for what exists — extend it |
| **ids** | your own block only (tracker header, `RULINGS.md` §0a); new tracker rows at the END of your area; ⛔ no rulings (you make no decisions) |
| **proof proportionate** | visual items: one key rail + the touched suites; sim / wire / save / cross-node items: the feature's suite (T-1) + the integration suite that guards the invariant |
| **no screenshots in git** | renders go to the scratchpad; the report names the rail |
| **builds** | build the TEST project, then `--no-build`; never the whole solution in the loop; slow runs in the background |

## 6. The gate report — per item, in your status file's notes or a REPORT at the end

Command · pass / fail / skip · delta vs the base sha · new rails red-proved (the mutation) · display rails run under Xvfb (a skip is not a pass)
· every red proved pre-existing at the base · the design section updated · ids allocated · `tracker-counts.py --check`.

---
_coordinator lane, 2026-11-06_
