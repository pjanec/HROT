<!--STATUS
state: SUPERSEDED — by NOTE_Coordinator_To_Backend_NED_Wire_Cutover_Built.md (AQ107 approved and built on coordinator)
updated: 2026-10-27
current-answer: the whole note
stale-below: nothing
related-designs:
  - ../Architect_Question_107_NED_Wire_Frame_And_Names.md — the design (leans, order, acceptance)
  - ../../reference/NED_DDS_Protocol.md — the living reference of the wire (new today)
-->
# NOTE — coordinator → backend: the NED wire clean-up (AQ107) and one bug (CE-4068)

**Dispatched at** the commit that adds this file (`coordinator`). FYI — 🔒 the user: nothing is to be implemented yet.

| what | for you |
|---|---|
| 📄 [NED_DDS_Protocol.md](../../reference/NED_DDS_Protocol.md) — the wire described: concepts, rules, all 128 types by full C# name; `scripts/ned-wire-inventory.py --check` keeps it current | ⭐ run `--check` whenever you add, remove or rename a `[DdsTopic]` type, and update the doc in the same commit |
| 📐 [AQ107](../Architect_Question_107_NED_Wire_Frame_And_Names.md) — ✅ ruled (R-4092): positions on the wire = local ENU metres at a published origin; topic names derived from types; `WorldPos` kept as a quaternion pose. Leans awaiting the user: `GeoReference` published by the master, ids all `long`, BDC the same, SI everywhere, one cutover | ⏳ you own NED, so the build will be yours; §5 is the order. Push back on any lean now, before it is approved |
| 📚 CE-3000 ⑤ — CycloneDDS.NET's default topic name is documented but not applied by the runtime reader/writer | the library fix comes first; ⚠ do NOT remove topic-name strings before the package bump (30+ readers/writers would open `null` topics) |
| ⛔ **CE-4068** — `WorldPos` loses the rotation of a nose pointing near straight up/down (heading forced to 0, roll kept — `SimTransformBridgeSystem.cs:26-30`) | a live defect today; fix it only if AQ107's cutover is far off (one consistent decomposition; the row has the rail) |

_coordinator lane, 2026-10-27_
