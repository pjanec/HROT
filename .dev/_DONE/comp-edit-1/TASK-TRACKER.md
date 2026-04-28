# Task Tracker — Component Editor

**Reference:** See [TASK-DETAIL.md](./TASK-DETAIL.md) for detailed task descriptions.

---

## Phase 1: StructEdit Core Extensions

**Goal:** Extend the StructEdit library to generate a complete EditDocument tree including array
element nodes, and surface domain-specific attributes through metadata.

- [x] **TASK-CE01** NestedMemberBinding [details](./TASK-DETAIL.md#task-ce01-nestedmemberbinding)
- [x] **TASK-CE02** EditNodeMetadata.CustomAttributes [details](./TASK-DETAIL.md#task-ce02-editnodemetadatacustomattributes)
- [x] **TASK-CE03** Array Element Node Generation [details](./TASK-DETAIL.md#task-ce03-array-element-node-generation)

---

## Phase 2: Picker Infrastructure

**Goal:** Define the attribute markers and picker-context interface that decouple the component
editor from the application's map and entity-selection services.

- [x] **TASK-CE04** Picker Attributes [details](./TASK-DETAIL.md#task-ce04-picker-attributes)
- [x] **TASK-CE05** IComponentPickerContext [details](./TASK-DETAIL.md#task-ce05-icomponentpickercontext)

---

## Phase 3: Component Edit Window

**Goal:** Implement the floating editor window and its ImGui renderer inside Fdp.Presentation.

- [x] **TASK-CE06** Add StructEdit Project References [details](./TASK-DETAIL.md#task-ce06-add-structedit-project-references)
- [x] **TASK-CE07** ComponentEditDrawer [details](./TASK-DETAIL.md#task-ce07-componenteditdrawer)
- [x] **TASK-CE08** ComponentEditWindow [details](./TASK-DETAIL.md#task-ce08-componenteditwindow)

---

## Phase 4: Wiring

**Goal:** Connect the double-click trigger in ComponentReflector to the new editor window,
and expose the Reflector on both inspector panels so host subsystems can inject dependencies.

- [x] **TASK-CE09** ComponentReflector Double-Click Integration [details](./TASK-DETAIL.md#task-ce09-componentreflector-double-click-integration)
- [x] **TASK-CE10** Host Wiring (EntityInspectorPanel + EntityWatchPanel) [details](./TASK-DETAIL.md#task-ce10-host-wiring-entityinspectorpanel--entitywatchpanel)
