<!--STATUS
state: LIVE
build-state: DISPATCH
updated: 2026-08-23
current-answer: dispatch pointer for the TIME lane — now that U-obs-1 (PanelSnapshot) is merged, build Group T
  (GET /panels* over the snapshot) and the cheap slice-③ reuse endpoints (MX2 hot-attach, MX3 entity-state).
  Cross-host conformance is the NEXT batch (it needs the read-API on CGF/SimHost — a bigger piece).
known-conflict: none.
-->
# HANDOFF — TIME lane · **Group T (panel read) + MX2/MX3 reuse endpoints**

> 📌 **Dispatched at `aba159c8d`** *(the joined commit — the UI lane's panel sweep + your HN-121 are both
> merged; solution builds 0 errors)*. ⭐ Branch **fresh from the coordinator branch** *(rule 7)*; **rule 1b:
> started-marker FIRST.** ⛔ **Scope FROZEN at this sha.** ⭐ ids **`MX-`**, tracker **Area J**. ⭐ **The wall is
> gone** — `PanelSnapshot` now exists.

## 0. ⭐ THE UNBLOCK — `PanelSnapshot` is live and has 53 panels feeding it

📄 The UI lane shipped the contract in **`Fdp.Diagnostics.Contracts.Panels`** *(design:
[`DESIGN_UI_Observability_Snapshot.md`](../../DESIGN_UI_Observability_Snapshot.md), read its AS-BUILT)*:
```csharp
static class PanelSnapshot {
    bool CaptureEnabled { get; set; }          // OFF by default — you turn it on so panels register
    void Register(IPanelViewModel vm);          // panels call this each frame when enabled
    IPanelViewModel? TryGet(string panelId);
    JsonObject DumpAll();                        // { panelId: model, ... }
    IReadOnlyCollection<string> RegisteredPanels;   // instrumented (declared)
    IReadOnlyCollection<string> CapturedPanels;     // actually dumped this frame
}
interface IPanelViewModel { string PanelId; string PanelKind; JsonNode Dump(); }
```
⭐⭐ **`PanelKind` is the conformance key** — the same panel on editor vs CGF carries the same `PanelKind`,
addressed per-host by `PanelId`. ⚠ **The snapshot currently has NO consumer** *(the design says so)* — **Group T
is that consumer.**

## 1. ⭐⭐ THE TASKS

| # | task | design | gate |
|---|---|---|---|
| **MX9 (Group T)** | **`GET /panels`** *(list — `RegisteredPanels` + `CapturedPanels`, so "not instrumented" ≠ "empty")* · **`GET /panels/{id}`** *(`TryGet(id)?.Dump()`)* · **`GET /panels/_gizmo`** *(`DebugPrimitiveBuffer.GetFrame()`)*. ⭐ Read-only over `PanelSnapshot`/the buffer | `MCP_Integration.md` §"Group T" | +MX6 smoke: `GET /panels/{id}` returns a known panel's model, a field asserted |
| **MX9-cap** | ⭐⭐ **enable capture** — set `PanelSnapshot.CaptureEnabled` true while the debug API is active *(the MCP/harness wants dumps; production stays off)*. ⛔ **The one wiring touch in `EditorSubsystem`/`DebugApi`** — the UI lane deliberately left the flag for the consumer to own | design §"Perf" | with the API up, `CapturedPanels` is non-empty after a frame |
| **MX2 (Group Q)** | **`POST /entities/{id}/attach-blueprint`** / **`detach-blueprint`** — wrap `AttachInstanceBlueprintEvent`/`AssignBehaviorEvent` *(the runtime mechanism exists; just expose it)* | `MCP_Integration.md` §"Group Q" | +smoke: attach a blueprint, the entity runs it |
| **MX3 (Group R)** | **`GET /entities/{id}/state`** — the well-known fields parsed out *(position, velocity, grounded, current behavior)* | `MCP_Integration.md` §"Group R" | +smoke: `state.position.x` reads without digging component JSON |
| **MX5/MX6** | Node wrappers + `SKILL.md` for the new tools; the smoke cases above | §"Sequencing ⤫" | tools callable; smokes green headless |

⛔ **NOT this batch:** **MX4b** *(mission editing)* — it is gated on resolving the `IMissionEditorService`
namespace ambiguity *(`MX-002`; three same-named interfaces)*, which is a design call, not a build one.

## 2. ⛔ LANE

⭐ **Your surface:** `Hrot.Editor/DebugApi/*`, the capture-flag wiring in `EditorSubsystem`, `tools/ai-debug-mcp/`,
the `Hrot.SystemTests` harness. ⛔ **Do NOT touch** the UI lane's panels / `PanelSnapshot` contract / the
variable model / `Hrot.Editor.AiShared`, the engine beyond what an endpoint needs, the parked Stride tree.
⚠ A cross-lane edit is STOP-and-report *(`R-128`)*. ⛔ **Do not modify `IPanelViewModel`/`PanelSnapshot`** — if a
panel's dump is wrong or missing a field, that is a UI-lane finding — **report it, don't fix it here.**

## 3. ⭐ THE NEXT BATCH (not now) — cross-host conformance

⭐ Once Group T lands, the headline de-risk piece is **cross-host conformance** *(umbrella
[`DESIGN_Headless_Testability.md`](../../DESIGN_Headless_Testability.md) §Conformance, steps 6–7)*: a
**debug-API READ subset on CGF + SimHost** *(today it is editor-only)*, then a suite that boots each host on one
curated scenario and **diffs the `PanelSnapshot` models by `PanelKind`.** ⛔ **Not this batch** — it needs a
design pass on the read-subset wiring; I will detail it when Group T is in.

## 4. GATES

⭐ Standing contract *(rule 8)*: one row per gate · verbatim command · pass/fail/skip · delta vs base
`aba159c8d` · `--no-build` column · every RED confirmed pre-existing · goldens as a diff shape ·
`tracker-counts.py --check` · `rulings-check.py` · `design-digest.py --check` · the **`MX-` ids you allocated** ·
`R-106` verdicts. ⭐⭐ **Row 8 — the harness smoke suite** *(now including the Group T panel-read case)*, headless
under Xvfb, green. ⭐ Rule 4/7: re-sync + pull the coordinator branch around the batch. ⭐ Rule 1b: started-marker
before code.
