# BS-1 Task Tracker — Brain / Muscle Node Separation

**Reference:** See [BS-1-TASK-DETAIL.md](./BS-1-TASK-DETAIL.md) for detailed task descriptions.  
**Design:** [BS-1-DESIGN.md](./BS-1-DESIGN.md)

---

## Batches

- **BS-1-BATCH-01** (10–11h): BS1-T001..BS1-T004 → `.dev-workstream/batches/BS-1-BATCH-01-INSTRUCTIONS.md`
- **BS-1-BATCH-02** (10–12h): BS1-T005..BS1-T007 (+ TD-1..TD-3) → `.dev-workstream/batches/BS-1-BATCH-02-INSTRUCTIONS.md`
- **BS-1-BATCH-03** (10–12h): TD-4..TD-5 + BS1-T008..BS1-T010 → `.dev-workstream/batches/BS-1-BATCH-03-INSTRUCTIONS.md`
- **BS-1-BATCH-04** (10–12h): TD-6..TD-8 + BS1-T011..BS1-T015 → `.dev-workstream/batches/BS-1-BATCH-04-INSTRUCTIONS.md`
- **BS-1-BATCH-05** (10-12h): BS1-T016..BS1-T020 → `.dev-workstream/batches/BS-1-BATCH-05-INSTRUCTIONS.md`
- **BS-1-BATCH-06** (10-12h): TD-10..TD-12 + BS1-T021..BS1-T022 → `.dev-workstream/batches/BS-1-BATCH-06-INSTRUCTIONS.md`

---

## Phase 1: Event & Contract Foundations

**Goal:** Define the ECS event structs and DDS message types that the entire pipeline depends on;
add the `HasAuthority` guard to `DamageSystem`.

- [✅] **BS1-T001** Define WeaponFire pipeline ECS event structs [details](./BS-1-TASK-DETAIL.md#bs1-t001--define-weaponfire-pipeline-ecs-event-structs)
- [✅] **BS1-T002** Define Detonation/Damage pipeline ECS event structs [details](./BS-1-TASK-DETAIL.md#bs1-t002--define-detonation--damage-pipeline-ecs-event-structs)
- [✅] **BS1-T003** Add HasAuthority guard to DamageSystem [details](./BS-1-TASK-DETAIL.md#bs1-t003--add-hasauthority-guard-to-damagesystem)

---

## Phase 2: Weapon Fire CQRS Pipeline

**Goal:** Replace local `FireRequestEvent` firing loop with a network-transparent Brain→Muscle
CQRS chain; IG receives muzzle-flash notification via DDS.

- [✅] **BS1-T004** Refactor AimAndFireExecutor to publish WeaponFireIntent [details](./BS-1-TASK-DETAIL.md#bs1-t004--refactor-aimandfire-executor-to-publish-weaponfireintent)
- [✅] **BS1-T005** Create WeaponFireIntentEgressTranslator [details](./BS-1-TASK-DETAIL.md#bs1-t005--create-weaponfireintentegress-translator)
- [✅] **BS1-T006** Create WeaponFireRequestIngressTranslator [details](./BS-1-TASK-DETAIL.md#bs1-t006--create-weaponfirerequest-ingress-translator)
- [✅] **BS1-T007** Refactor FireProcessingSystem to consume WeaponFireIntent + emit notification [details](./BS-1-TASK-DETAIL.md#bs1-t007--refactor-fireprocessingsystem-to-consume-weaponfireintent-and-emit-weaponfirenotification)
- [✅] **BS1-T008** Create WeaponFireNotificationEgressTranslator [details](./BS-1-TASK-DETAIL.md#bs1-t008--create-weaponfirenotificationegress-translator)
- [✅] **BS1-T009** Create WeaponFireIngressTranslator for IG [details](./BS-1-TASK-DETAIL.md#bs1-t009--create-weaponfire-ingress-translator-for-ig)

---

## Phase 3: Detonation & Damage Assessment Pipeline

**Goal:** Build the detonation→damage→health-update CQRS chain; publish EntityDamage to the IG.

- [✅] **BS1-T010** Refactor HitResolutionSystem to emit DetonationNotification [details](./BS-1-TASK-DETAIL.md#bs1-t010--refactor-hitresolutionsystem-to-emit-detonationnotification)
- [✅] **BS1-T011** Create MunitionDetonationEgressTranslator [details](./BS-1-TASK-DETAIL.md#bs1-t011--create-munitiondetonationegress-translator)
- [✅] **BS1-T012** Create DamageAssessmentModule [details](./BS-1-TASK-DETAIL.md#bs1-t012--create-damageassessmentmodule)
- [✅] **BS1-T013** Create DamageAssessedEgressTranslator [details](./BS-1-TASK-DETAIL.md#bs1-t013--create-damageassessedegress-translator)
- [✅] **BS1-T014** Create EntityHitDamageIngressTranslator + HealthApplicationSystem [details](./BS-1-TASK-DETAIL.md#bs1-t014--create-entityhitdamage-ingress-translator-and-healthapplicationsystem)
- [✅] **BS1-T015** Create EntityDamageEgressTranslator [details](./BS-1-TASK-DETAIL.md#bs1-t015--create-entitydamageegress-translator)

---

## Phase 4: Node Role Reconfiguration

**Goal:** Enforce correct module-per-role assignments and wire all new translators into
`SimHostApp`.

- [✅] **BS1-T016** Update NodeBootstrapper role assignments [details](./BS-1-TASK-DETAIL.md#bs1-t016--update-nodebootstrapper-role-assignments)
- [✅] **BS1-T017** Register new translators in SimHostApp [details](./BS-1-TASK-DETAIL.md#bs1-t017--register-new-translators-in-simhostapp)

---

## Phase 5: Navigation CQRS Compliance

**Goal:** Remove all direct `NavState` mutations from Brain-tier executors; fix the
`ReachedDestination` mission trigger.

- [✅] **BS1-T018** Refactor FleeExecutor to use NavigationIntent [details](./BS-1-TASK-DETAIL.md#bs1-t018--refactor-fleeexecutor-to-use-navigationintent)
- [✅] **BS1-T019** Refactor FollowRoadGraphExecutor to use NavigationIntent [details](./BS-1-TASK-DETAIL.md#bs1-t019--refactor-followroadgraphexecutor-to-use-navigationintent)
- [✅] **BS1-T020** Refactor FollowRouteExecutor to use NavigationIntent [details](./BS-1-TASK-DETAIL.md#bs1-t020--refactor-followrouteexecutor-to-use-navigationintent)
- [✅] **BS1-T021** Remove NavState poll from Action_Wander [details](./BS-1-TASK-DETAIL.md#bs1-t021--remove-navstate-poll-from-action_wander)
- [✅] **BS1-T022** Fix MissionDirectorSystem.ReachedDestination + UI generator [details](./BS-1-TASK-DETAIL.md#bs1-t022--fix-missiondirectorsystemreacheddestination--ui-generator)
