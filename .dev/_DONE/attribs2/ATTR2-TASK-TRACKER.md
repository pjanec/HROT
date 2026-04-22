# ATTR2 Task Tracker

**Reference:** See [ATTR2-TASK-DETAIL.md](./ATTR2-TASK-DETAIL.md) for detailed task descriptions  
**Design:** See [ATTR2-DESIGN.md](./ATTR2-DESIGN.md)

---

## Phase 1: Binary Contract & Schema Foundation

**Goal:** Establish the new DDS wire types and extend existing messages with binary attribute list
fields — zero runtime behaviour changes.

- [x] **ATTR2-P1T1** `AttributeValueUnion` and `AttributeRecord` DDS Types  [details](./ATTR2-TASK-DETAIL.md#attr2-p1t1--attributevalueunion-and-attributerecord-dds-types)
- [x] **ATTR2-P1T2** `AttributeId` Schema Constants  [details](./ATTR2-TASK-DETAIL.md#attr2-p1t2--attributeid-schema-constants)
- [x] **ATTR2-P1T3** Update Wire Messages  [details](./ATTR2-TASK-DETAIL.md#attr2-p1t3--update-wire-messages-createentityrequest-updateentityattributerequest)

---

## Phase 2: Edge Compiler

**Goal:** Build the JSON → AttributeRecord compiler that runs on the IG/IOS client side, converting
any JSON shape to a zero-allocation binary record stream.

- [x] **ATTR2-P2T1** `JsonToRecordCompiler` and `JsonToRecordCompilerBuilder`  [details](./ATTR2-TASK-DETAIL.md#attr2-p2t1--jsontorecordcompiler-and-jsontorecordcompilerbuilder)
- [x] **ATTR2-P2T2** `EdgeCompilerFactory` (Domain Schema Registration)  [details](./ATTR2-TASK-DETAIL.md#attr2-p2t2--edgecompilerfactory-domain-schema-registration)

---

## Phase 3: Binary Interpreter Core

**Goal:** Build the generic dispatch engine that routes AttributeRecord streams to ECS components
via installer-registered handlers, scratchpads, and flush phases.

- [x] **ATTR2-P3T1** `IBinaryAttributeInstaller`, `BinaryPatchContext`, `BinaryInterpreterBuilder`, `BinaryInterpreter`  [details](./ATTR2-TASK-DETAIL.md#attr2-p3t1--ibinaryattributeinstaller-binarypatchcontext-binaryinterpreterbuilder-binaryinterpreter)

---

## Phase 4: Domain Installers

**Goal:** Wire SimHost-specific ECS component handlers (name, affiliation, geo-position) into the
generic interpreter.

- [x] **ATTR2-P4T1** `EntityDataAttributeInstaller`  [details](./ATTR2-TASK-DETAIL.md#attr2-p4t1--entitydataattributeinstaller)
- [x] **ATTR2-P4T2** `SimTransformAttributeInstaller`  [details](./ATTR2-TASK-DETAIL.md#attr2-p4t2--simtransformattributeinstaller)
- [x] **ATTR2-P4T3** `BinaryInterpreterFactory` (SimHost Wiring)  [details](./ATTR2-TASK-DETAIL.md#attr2-p4t3--binaryinterpreterfactory-simhost-wiring)

---

## Phase 5: System Integration

**Goal:** Make `CreateEntityRequestSystem` and `UpdateEntityAttributeRequestSystem` accept and
apply binary attribute records as the primary path, with JSON as the backward-compatible fallback.

- [x] **ATTR2-P5T1** `CreateEntityRequestSystem` Binary Branch  [details](./ATTR2-TASK-DETAIL.md#attr2-p5t1--createentityrequestsystem-binary-branch)
- [x] **ATTR2-P5T2** `UpdateEntityAttributeRequestSystem` Binary Branch  [details](./ATTR2-TASK-DETAIL.md#attr2-p5t2--updateentityattributerequestsystem-binary-branch)

---

## Phase 6: Client-Side Integration

**Goal:** `CreationTool` converts `_initialPropertiesJson` to binary records before sending the
`CreateEntityRequest`, completing the full end-to-end binary pipeline.

- [x] **ATTR2-P6T1** `CreationTool` EdgeCompiler Injection  [details](./ATTR2-TASK-DETAIL.md#attr2-p6t1--creationtool-edgecompiler-injection)
