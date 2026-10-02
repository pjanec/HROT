# Blueprint Canvas Parity — Task Tracker

Phases (impact order; deps: C → B → A → D → E → F → G → H → I). See [DESIGN.md](./DESIGN.md).

## Batch 01 — visible core (theme + movement + pins/wires)
- [x] **BCP-C** Demo theme on all 3 perspectives (`EngineEditorTheme` → demo scheme)
- [x] **BCP-B** In-place node movement (mutable Position, no rebuild-on-drag)
- [x] **BCP-A** Pin/wire hydration (canonical pin schema + link-GUID binding, projection-only)

## Batch 02 — editing richness
- [ ] **BCP-D** Mini-editors / inline pin default values (+ `NodeMetadata.PinDefaults`, ignore-null)
- [x] **BCP-E** Pickers wired (node-type/add-node, wire-drop, variable, type REAL; asset-grid + flags/enum placeholders — DEBT-BCP-003)
- [x] **BCP-F** FindBar + IEditorCommands wired into Render (all 3 perspectives)
- [x] Variable Get/Set typed value pin (My-Blueprint drag-create)

## Batch 03 — canvas metadata features
- [ ] **BCP-G** Comments + reroutes (editor-only `GraphMetadata`)
- [ ] **BCP-H** Containers (editor-only grouping, non-compiled)
- [ ] **BCP-I** Bookmarks + Details + My Blueprint parity audit

## Guardrails (every batch)
- [ ] Byte-stability test over all `.bp.json` fixtures (load→serialize identical)
- [ ] Compiler golden/snapshot suite unchanged (no drift)
- [ ] `dotnet build IOS-IG-SimHost.sln` 0/0; GizmoMap.Contracts stays 0.2.2; Hrot.IG/DDS untouched
