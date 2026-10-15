<!--STATUS
state: LIVE — DISPATCHED at the commit that adds this file (2026-10-15); scope frozen there.
updated: 2026-10-15
current-answer: the whole file.
stale-below: nothing.
known-rot: none.
known-conflict: none.
related-designs:
  - docs/DESIGN_Dead_Reckoning.md — §9 P3 (pitch and roll on the wire) owns the replicated transform; step 1 writes here
  - docs/blueprints/batches/STEER_Backend_P3_Every_Ingress_Restores_Attitude.md — the steer whose only open row (7) this carries forward
  - docs/designs/air-attack/DESIGN_Air_Operations.md — the attitude the aircraft models write
-->

# HANDOFF — backend: a heading-only attribute patch keeps the body's pitch and roll

**For:** the backend lane (`backend`). **From:** the ui lane (`ui`). ⭐ Dispatched at the commit that adds this file; scope frozen there. A FRAME:
step 1 is yours (Dead Reckoning §9, diagram first). Ids from your `CE-3xxx` block; the ui row tracking this is **CE-1110**.

## Why

🔒 **User, `2026-10-15`:** *"Ingress shouldnt restore just heading."* Your `b6ad14c83` fixed all six descriptor ingress sites
(`EulerDegToRotation`) — ✅ thank you; it overtook the [steer](STEER_Backend_P3_Every_Ingress_Restores_Attitude.md) before it arrived. ⚠ The
steer's **row 7** is still open: the ATTRIBUTE path rebuilds the whole rotation from a new heading alone, so a heading patch on a banked or
pitched body **levels it**:

| site | what it does |
|---|---|
| `Fdp.Toolkits/Replication/Attributes/SimTransformHeadingInstaller.cs:60` | `st.Rotation = HeadingDegToRotation(headingDeg)` |
| `Fdp.Toolkits/Replication/Attributes/AttributeCompilerFactory.cs:109` | the JSON `"Heading"` route, the same |

## The item

| | |
|---|---|
| **lean** | ⭐ rebuild with the NEW heading and the body's CURRENT pitch and roll: `RotationToPitchRollDeg(st.Rotation, out p, out r)` then `EulerDegToRotation(heading, p, r)` — both helpers exist; ⛔ no new wire field, no second conversion |
| **fences** | a debug "set heading" (`DebugApiService.cs:2262`) and the IG test hook (`IgApplication.cs:2217`) are heading-only by intent — leave them, or give the hook optional pitch / roll |
| **acceptance** | ★ a heading patch on a body at pitch 10° / roll −20° gives the new heading with pitch and roll unchanged (± 0.1°), on both the binary and the JSON route; ★ a level body is unchanged (the existing heading rails stay green) |
