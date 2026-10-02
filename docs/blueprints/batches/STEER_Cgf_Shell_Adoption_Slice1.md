<!--STATUS
state: LIVE
build-state: STEER — amends the SCOPE of HANDOFF_Cgf_Shell_Adoption_Slice1.md (running batch, started at
  3f78c905d). Relayed by the USER to the running session (rule 1c). Not an edit of the handoff in place.
updated: 2026-08-25
known-conflict: this SUPERSEDES the handoff's "read/diagnostics only" framing (§1 NOT-row "asset editing";
  §5 item ③ "leave write cells absent"). Everything else in the handoff stands UNCHANGED.
-->
# STEER — **do NOT artificially refuse editing; take the windows WHOLESALE** *(user, `2026-08-25`)*

> 🔒 **User, verbatim intent:** *"the editor never disallowed asset editing, so it is easier to take
> wholesale WITH editing than to refuse one artificially."* ⭐⭐ **Correct, and it is the charter principle**
> *(editor is not special; share wholesale)*. Artificially gating the shared windows' edit affordances on
> CGF is EXTRA work AND against the goal. ⇒ **Register the windows as they are; let their native editing
> come along.**

## ⭐ WHAT CHANGES vs the handoff

| the handoff said | ⭐ the steer says |
|---|---|
| §1 NOT: *asset editing / hot-reload writes* · §5-③ *leave write cells absent* | ⛔ **Do NOT add any code to disable, hide, or gate the windows' editing.** Register them wholesale. **Whatever the same window does in the editor, it does on CGF.** |

## ⭐⭐ BUT — two edit PATHS behind those windows differ; treat them differently *(honesty, not gating)*

⭐ **① Asset / graph authoring** *(add/rename nodes, change params → save the `.bp.json`/asset file → hot
reload)* — **THIS is the editor editing. Take it wholesale.** Two honest facts to MEASURE and REPORT, ⛔ not
to gate away:
- ⭐ an edit only **takes effect on the running brain** if the **reload pipeline** is active on CGF
  *(`QuickReloadService` / the file-watcher → ALC reload; hot-reload classification lives in
  `AI_Editor_Shared_Infrastructure.md` §17 — Cosmetic/Soft/Hard)*. 📐 **Measure:** is it constructed on CGF?
  If it is a cheap construct, **wire it** so edits hot-apply. If not, **report** it — editing still saves the
  file, it just does not live-apply yet.
- ⚠ on a **DEPLOYED** node the asset-write root resolves to `null` *(ruling 67)* ⇒ a save has nowhere to go.
  In a **dev / source-tree** run it works. ⇒ **do not gate in dev**; on a deployed node either apply
  ruling 67's config-into-`AssetRoots` fix *(if cheap)* or **report** the deployed-node gap. ⛔ Never a
  silent save-to-nowhere.

🔴 **② Live variable-VALUE editing** *(the watch/Details value edit → staged write to the shared
`Blackboard1024`)* — ⛔⛔ **DO NOT enable this on CGF in this slice.** 📌 It carries **`R-52`**: a
whole-component write that **clobbers a tick of BTree/HSM state** *(a live-corruption bug that exists in the
editor too, needs `SetComponentFieldRaw`)*, and it is the **variable-model lane's** frozen territory. ⇒ ⭐ if
a window exposes this path, **coordinate with the variable-model lane** before enabling it on a live node —
this is the ONE place a gate is honest, and the reason is corruption, not policy.

## ⭐⭐ MANIFEST HONESTY — the deliverable, per `AQ54` D3/D4
⭐ **Flip each capability cell to what ACTUALLY works, measured** — present where editing genuinely functions
*(dev asset-graph authoring)*, **absent with the stated reason** where a real blocker applies
*(deployed-node asset root; the `R-52` value-write path)*. ⛔⛔ **Never report an edit endpoint present if it
silently no-ops or corrupts.** ⭐ That is exactly what the three-way conformance verdict + the manifest are
for: *"editing works here, absent-with-reason there"* is the honest, assertable state — not a two-way
pass/fail.

## ⭐ UNCHANGED — everything else in the handoff stands *(rule 1c)*
- The shell construct *(§2 ①)*, window registration *(②)*, `perspectiveMap` *(④)*, the test method
  *(capture editor goldens → conformance `SAME` by `PanelKind`)*, the gates, and the *"do NOT modify
  AiShared internals — coordinate"* rule are all **unchanged**.
- ⭐ You still MAY extend the harness/MCP as required, and ⛔ still must not fake a pass.
- ⚠ **This is a scope CLARIFICATION, not a correction of your work** — nothing built so far is wrong; this
  removes an artificial constraint the handoff imposed.

⇒ ⭐⭐⭐ **Net:** register the windows wholesale · let asset-graph authoring work where it naturally works ·
wire the reload pipeline if cheap · keep the live variable-value write OFF pending its lane · and make the
capability manifest tell the exact truth.
