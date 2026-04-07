# Task Tracker — Module Init

**Reference:** See [TASK-DETAIL.md](./TASK-DETAIL.md) for detailed task descriptions.

---

## Stage 1: Push Down Architecturally Coupled Systems

**Goal:** Remove application-layer dependencies from systems and packs that `NedReplicationModule` needs, so it can be relocated to `Hrot.Network`.

- [ ] **MODINIT-S100** Create Hrot.Network assembly [details](./TASK-DETAIL.md#modinit-s100--create-hrotnetwork-assembly)
- [ ] **MODINIT-S107** Move navigation translators to Hrot.Map.Common [details](./TASK-DETAIL.md#modinit-s107--move-navigation-translators-to-hrotmapcommon)
- [ ] **MODINIT-S101** Move DeadReckoningSyncSystem to Hrot.Common [details](./TASK-DETAIL.md#modinit-s101--move-deadreckoningsyncsystem-to-hrotcommon)
- [ ] **MODINIT-S102** Move SharedTranslatorPack to Hrot.Map.Common [details](./TASK-DETAIL.md#modinit-s102--move-sharedtranslatorpack-to-hrotmapcommon)
- [ ] **MODINIT-S103** Move KinematicTranslatorPack to Hrot.Map.Common [details](./TASK-DETAIL.md#modinit-s103--move-kinematictranslatorpack-to-hrotmapcommon)
- [ ] **MODINIT-S104** Move CognitiveTranslatorPack to Hrot.Network [details](./TASK-DETAIL.md#modinit-s104--move-cognitivetranslatorpack-to-hrotnetwork)
- [ ] **MODINIT-S106** Validate Stage 1 layer boundaries [details](./TASK-DETAIL.md#modinit-s106--validate-stage-1-layer-boundaries)

---

## Stage 2: Relocate NedReplicationModule

**Goal:** Move `NedReplicationModule` from `Hrot.ClusterRunner.Replication` to `Hrot.Map.Common.Replication`, establishing it as shared ACL infrastructure.

- [ ] **MODINIT-S201** Move NedReplicationModule to Hrot.Network [details](./TASK-DETAIL.md#modinit-s201--move-nedreplicationmodule-to-hrotmapcommon)
- [ ] **MODINIT-S202** Wire NedReplicationModule into HrotNodeContext (INedReplicationModule interface + extension class) [details](./TASK-DETAIL.md#modinit-s202--wire-nedreplicationmodule-into-hrotnodecontext-mandatory)

---

## Stage 3: Eradicate Legacy Boilerplate

**Goal:** Replace ~300 lines of manual translator wiring in `SimHostApp` and `IgApplication` with `NedReplicationModule`, fulfilling the DRY promise. Resolve the P2 debt comment.

- [ ] **MODINIT-S301** Refactor SimHostApp + NodeBootstrapper namespace update [details](./TASK-DETAIL.md#modinit-s301--refactor-simhostapp-to-use-nedreplicationmodule)
- [ ] **MODINIT-S302** Refactor IgApplication to use NedReplicationModule [details](./TASK-DETAIL.md#modinit-s302--refactor-igapplication-to-use-nedreplicationmodule)

---

## Stage 4: Decouple CGF and Prove Isolation

**Goal:** Update CGF to use the module from its new home; verify no application project references `Hrot.ClusterRunner`; prove standalone executable readiness.

- [ ] **MODINIT-S401** Update CgfSubsystem to reference Hrot.Map.Common [details](./TASK-DETAIL.md#modinit-s401--update-cgfsubsystem-to-reference-hrotcommon)
- [ ] **MODINIT-S402** Sever upward project references and prove isolation [details](./TASK-DETAIL.md#modinit-s402--sever-upward-project-references)
