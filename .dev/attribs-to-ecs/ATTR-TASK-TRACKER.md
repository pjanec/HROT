# Task Tracker: Attributes-to-ECS — Zero-Allocation JSON Entity Patching

**Reference:** See [ATTR-TASK-DETAIL.md](./ATTR-TASK-DETAIL.md) for detailed task descriptions and
success conditions.  
**Design:** See [ATTR-DESIGN.md](./ATTR-DESIGN.md) for the full architectural context.

---

## Phase 1: DDS API Migration

**Goal:** Replace the fixed-enum `List<EntityAttributePayload> InitialAttributes` field in
`CreateEntityRequest` with `string InitialAttributesJson`, and migrate `UpdateEntityAttributeRequest`
from `AttributeId`+`Payload` to `string AttributePatchJson`. Both messages then share a single
`JsonAttributeCompiler` routing table. Remove `EntityAttribute` enum and `EntityAttributePayload`
union entirely.

- [x] **ATTR-S1T1** Replace `InitialAttributes` with `InitialAttributesJson` in `CreateEntityRequest` — [details](./ATTR-TASK-DETAIL.md#attr-s1t1--replace-initialattributes-with-initialattributesjson-in-createentityrequest)
- [x] **ATTR-S1T2** Replace `AttributeId`+`Payload` with `AttributePatchJson` in `UpdateEntityAttributeRequest`; remove `EntityAttribute` enum and `EntityAttributePayload` union — [details](./ATTR-TASK-DETAIL.md#attr-s1t2--replace-attributeidpayload-in-updateentityattributerequest-with-attributepatchjson)

---

## Phase 2: IG Pipe Simplification

**Goal:** `CreationTool` stops parsing `initialPropertiesJson` into an `EntityInfo` descriptor
and becomes a dumb pipe that forwards the raw JSON into `InitialAttributesJson`.

- [x] **ATTR-S2T1** `CreationTool`: forward JSON verbatim, remove `dtEntityInfo` descriptor — [details](./ATTR-TASK-DETAIL.md#attr-s2t1--creationtool-forward-json-verbatim-remove-dtentityinfo-descriptor)

---

## Phase 3: Zero-Allocation Compiler Core

**Goal:** Create the `JsonAttributeCompiler` class with `Utf8JsonReader` token streaming and
a `stackalloc`-based state machine that tracks depth, hash, and array indices on the thread stack.

- [x] **ATTR-S3T1** Create `JsonAttributeCompiler` with `Utf8JsonReader` streaming — [details](./ATTR-TASK-DETAIL.md#attr-s3t1--create-jsonattributecompiler-with-utf8jsonreader-streaming)
- [x] **ATTR-S3T2** Implement FNV-1a incremental path hashing with wildcard array indices — [details](./ATTR-TASK-DETAIL.md#attr-s3t2--fnv-1a-incremental-path-hashing)

---

## Phase 4: Pre-Compiled Delegate Registry

**Goal:** Define the dual-mode delegate types and `IEntityPatchContext` interface (including
`FlushDirtyMarks()`); create `AttributeCompilerBuilder` with `descriptorOrdinal` parameter on every
`Register*Path` call; implement `ListPatchContext` and `EcsPatchContext` with ordinal-dedup flush.

- [x] **ATTR-S4T1** Define delegate types and `IEntityPatchContext` interface — [details](./ATTR-TASK-DETAIL.md#attr-s4t1--define-delegate-types-and-ientitypatchcontext)
- [x] **ATTR-S4T2** Create `AttributeCompilerBuilder` — [details](./ATTR-TASK-DETAIL.md#attr-s4t2--create-attributecompilerbuilder)
- [x] **ATTR-S4T3** Create `ListPatchContext` and `EcsPatchContext` — [details](./ATTR-TASK-DETAIL.md#attr-s4t3--create-listpatchcontext-and-ecspatchcontext)

---

## Phase 5: Registration and Integration

**Goal:** Register all current ECS property paths (Name, Affiliation, GeoPosition); wire the
`JsonAttributeCompiler` into `CreateEntityRequestSystem` and `UpdateEntityAttributeRequestSystem`.

- [x] **ATTR-S5T1** Register component paths in SimHost startup — [details](./ATTR-TASK-DETAIL.md#attr-s5t1--register-component-paths-in-simhost-startup)
- [x] **ATTR-S5T2** Update `CreateEntityRequestSystem` to use `JsonAttributeCompiler` — [details](./ATTR-TASK-DETAIL.md#attr-s5t2--update-createentityrequestsystem-to-use-jsonattributecompiler)
- [x] **ATTR-S5T3** `UpdateEntityAttributeRequestSystem`: full JSON pipeline, `EcsPatchContext`, `FlushDirtyMarks` — [details](./ATTR-TASK-DETAIL.md#attr-s5t3--updateentityattributerequestsystem-full-json-pipeline-integration)
- [x] **ATTR-S5T4** Register descriptor ordinals on all compiler paths; `EcsPatchContext.FlushDirtyMarks` dedup — [details](./ATTR-TASK-DETAIL.md#attr-s5t4--register-descriptor-ordinals-in-simhost-compiler-startup)

---

## Phase 6: Unified Descriptor Routing (Advanced)

**Goal:** `DescriptorMapper` reuses the same pre-compiled delegates as the JSON compiler,
eliminating duplicate field-mapping logic for `dtEntityInfo` and `dtGeoSpatial`.

- [x] **ATTR-S6T1** `DescriptorMapper` `dtEntityInfo` case uses routing delegates — [details](./ATTR-TASK-DETAIL.md#attr-s6t1--descriptormapper-dtentityinfo-uses-routing-delegates)
- [x] **ATTR-S6T2** `DescriptorMapper` `dtGeoSpatial` case uses routing delegates — [details](./ATTR-TASK-DETAIL.md#attr-s6t2--descriptormapper-dtgeospatial-uses-routing-delegates)
