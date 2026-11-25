<!--STATUS
state: LIVE — a NOTE (protocol §5): a finding in terrain's code, with the measurements · updated: 2026-11-25 · current-answer: the whole file
related-designs:
  - ../../designs/engineering/DESIGN_Bridge_Laying.md — §11 (BL-S1/S2 as built, terrain), §12f (BL-S3/S4 as built, backend)
-->
# NOTE — `backend` → `terrain`: in the EDITOR a vehicle's path never uses the floating deck (TKB 8826) — CE-4696

| | |
|---|---|
| what blocks | CE-4693's `bl-floating-bridge` (BL-S4): the floating engineer lays its 72 m span and it opens, but the column never leaves its start |
| the probe | one scenario on basic-desert, every deck PLACED in the file (no laying involved): a 72 m and a 14 m FLOATING deck (8826, 0.6 m high, 4.5 m wide), a 14 m ASSAULT deck (8825, 1.2 m, 4 m) |
| ✅ | men march across the 72 m floating deck (editor) · an M1 crosses the 14 m assault deck (editor) |
| ⛔ editor (the regression's live load; threaded AND serial) | an M1 ordered across the 14 m floating deck is routed 80 m east ONTO THE ASSAULT DECK and ends `FailedBlocked` there — `SimTransform` (160.05, 300.86, 1.2), `NavigationStatus.Result = FailedBlocked` |
| ✅ `--mode all` cluster | the same order: `Arrived` at (79.9, 340.4), over the floating deck |
| ⇒ | the 8826 deck is in the cluster's vehicle navmesh and not in the editor's. The difference from the assault deck: 0.6 m high with a 2.4 m ramp (`RampLengthFor(0.6)`), 4.5 m wide |
| ⛔ not measured | which bake input differs between the two hosts |
| the ask | find and fix it in the deck's vehicle bake; `bl-floating-bridge` is the rail (`scripts/scenario-regression.sh bl-floating-bridge`) |
