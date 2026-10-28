<!--STATUS
state: LIVE — ui → backend: CE-1431, one hosting rule for every behaviour tree, whatever surface authored it
updated: 2026-10-28
current-answer: the whole note
stale-below: nothing
related-designs:
  - ../DESIGN_Behavior_Self_Registration.md — §10d / §10d-i: what the C# registration path declares (CE-1410, CE-1412, CE-1430)
  - ../RULINGS.md — R-1042: shipped behaviour trees are C# (`BTreeBuilder`), not hand-written `.btree.json`
-->
# NOTE — ui → backend: one hosting rule for every behaviour tree (CE-1431)

**Dispatched at** the commit that adds this file (`ui`). Your scope; ui hands it over — the user, `2026-10-28`: *"write a note to backend
to do it as this is not a ui stuff so much"*.

## The user's ruling, verbatim

> *"These two should not behave differently, it is still the same btree having the same parameters and variables, just coming from
> different authoring surface. The only thing I think might be different is that for csharp hardcoded one the editor might not be able to
> edit the blackboard variables and parameters DTOs as they do not exist as json asset."*

⭐ Approved lean (same exchange: *"look good"*). Suggest recording it as a `R-3xxx` ruling from your block.

## The defect, measured

The registry uses **"has a variables manifest"** as a stand-in for **"its parameters / variables split is declared"**:

| rule | file | with a manifest | without one |
|---|---|---|---|
| what a host binds to start the child | `BehaviorRegistry.TryGetHostedInputType` (`BehaviorRegistry.cs:806`) | the declared params DTO (`JsonParamsDtoType`); none ⇒ *"takes nothing"* | the whole block (`BlackboardLayoutType`) |
| how many Input bytes the child has | `RootParamsAccess.InputBytes` (`RootParamsAccess.cs:319`) | the params DTO / the block's `In` / the manifest extent | the whole block |
| the curated-resolver overlay check | `BehaviorRegistry.cs:745–749` | uses `InputBytes` | uses `InputBytes` |

The JSON (generated) path always declares both a params DTO and a manifest; the C# (curated) path declares the params DTO only when a
`[Behavior(Params = …)]` says so (~76 files do), and a manifest only for the 15 trees converted under CE-1410
(`[BTreeDefinition(PublishesVariables = true)]`, CE-1412). ⇒ the SAME tree hosts differently depending on its authoring surface, and the
other ~77 curated trees publish no manifest, so their live Variables in the editor / Brain View are empty.

## What to build

1. **Enumerate** the curated C# trees that declare no params contract (no `[Behavior(Params)]`, no resolver) — that list is the blast
   radius. ⚠ Nobody has listed it yet.
2. **Declare** the missing params contract on each (what a host passes), so every tree states the same two facts the JSON path states:
   its parameters and its variables.
3. **Key the rules on the declaration**, not on "has a manifest": `TryGetHostedInputType`, `InputBytes`, the overlay check.
4. **Publish the variables manifest for every curated tree** (`SharedNodeBinder.PublishedVariablesOf` / `VariablesOf`, CE-1412) and
   **delete the `PublishesVariables` opt-in** flag.
5. **Rail:** for every registered tree, its hosted input type and Input byte count are the SAME before and after (record before, compare
   after) — any tree whose answer changes is named and argued. Plus: every curated tree publishes a manifest.

## What stays ui's (already built)

- The editor shows a C# tree's variables **read-only** with live values (CE-1430) and their defaults (CE-1433). Once step 4 lands, every
  C# tree gets that automatically — the projector reads the same manifest (`BehaviorTreeAssetProjector`, `BTreeAssetContributor`).

_ui lane, 2026-10-28_
