In this chaet i aould like to discuss how to control/use the human character animation from an game AI code from my distributed HROT engine, how to send requests to and receive notification from the 'brain' node (where the AI code and btrees and blueprints run) to 'muscle' node where the   3d renderer and animation engine is running.  Please read project documents describing HROT architecture first to get the idea about my architecture. The 3d rendering/animation part is not implemented yet, it will be the Stride engine in the first iteration, later it will be changed to our proprietary 3d rendering/animation engine. We are using animations from unreal, using UE tools for authoring/editing them. So you can imagine the muscne node will be using enreal concept of animation control and execution. For technical questions about how our engine works today (how it handles network data transfer, requests and state updates etc.) we can ask our engine architect any time rather then guessing or making assumptions. Pls read briefly also the design documents on the blueprint subsystem runtime and the node editor UI used for visual authoring the btrees and blueprints. In the visual editing part for BTrees, I am (in different chat) desiging some easier way for the AI designer of adding "When" type nodes that can evaluate various conditions and check for events ans steer the execution off the btree. PLs read also the "Predicate-Infrastructure-Capabilities.md" to understand what infrastructure the When-nodes might be using once implemented. The main goal is to design how to deal with animations control/monitoring/responding to events like 'animation ended' from the AI 'brain' node. And taking inspiration from Unreal and other AAA engines to use the proven techniques and concepts and conventions.





I'll read the project documents to understand your HROT architecture, blueprint subsystem, node editor design, and predicate infrastructure before discussing animation control design.I have enough context. Let me also briefly check what's already known about the IG node's rendering role and event handling patterns, since the Stride/Unreal animation muscle node will be a close cousin.I have a comprehensive understanding now. Let me think through the design before responding.

The user wants to design how the Brain node (AI/BTrees/Blueprints) controls/monitors human character animation on a future "Muscle" node (Stride first, then proprietary, but Unreal-style animation concepts since they use UE animation assets and authoring tools).

Key things I know from the docs:

1. HROT already has a strong **Channel** pattern (Locomotion/Weapon/Interaction) — perfect inspiration; the new animation control should follow this pattern
2. CQRS Intent/Status pattern is the architectural backbone
3. DDS topics carry intents Brain→Muscle and statuses Muscle→Brain
4. The Blueprint subsystem has **ChannelCommand** and **WaitForChannel/WaitForEvent** nodes — animation control should integrate here
5. Capability gating (CanMove/CanShoot) via ActorCapabilityState — animation should have similar
6. EventScanner pattern + TraceBuffer scan via `TraceBufferScanPredicateDto` — relevant for "When animation ended" type predicates
7. Predicate infrastructure supports `TransientEventPredicateDto` for event-bus matchers — usable for When nodes
8. Async ring-buffer pattern (raycast/EQS) for round-tripping requests with RequestIds — relevant for animation montage tokens

Now let me think about Unreal's animation concepts to draw from:

- **AnimMontage** — discrete one-shot animations (reload, vault, attack swing) with sections, blend in/out, notifies
- **AnimNotify** / **AnimNotifyState** — events fired at specific frames (footstep, weapon-trail-on, hit-window-open/close, etc.)
- **Locomotion state machine / Blend spaces** — driven by speed/direction floats; ongoing not one-shot
- **Anim Layers / Slots** — upper body, full body, additive
- **Gameplay Cues / Gameplay Tags** — declarative way to talk about animation states
- **Movement Modes** (walking/running/crouching/prone) — selectable mode
- **Aim offsets / Lookat IK**
- **PlayMontage** completion delegates: OnCompleted, OnBlendOut, OnInterrupted, OnNotifyBegin, OnNotifyEnd

The clean mapping is:

- One-shot/montage-style → like a **channel command** with completion status (one of the most common AI animation operations)
- Continuous locomotion → already covered by LocomotionChannel — the Muscle Anim node will simply drive its blend-space from velocity/direction
- AnimNotify → engine events on the FdpEventBus (transient events, scanned via EventScannerCompiler) — perfect fit for "When" predicates
- Movement mode (walking/crouching/prone) → a stance parameter (could be a small channel or a stance descriptor)
- Aim/lookat targeting → continuously-published target descriptor

Let me also think about who owns what:

- Brain decides "play vault montage" / "switch to crouch" / "aim at target T"
- Muscle (Anim node) executes blends, IK, applies root motion → publishes back root delta for the kinematics Muscle, and publishes AnimNotify events for the Brain
- The interesting question: should the existing SimHost Muscle (ground kinematics) and the new Anim Muscle be the SAME node or DIFFERENT? For humanoid characters, root motion couples them. I should raise that.

Let me think about the design. I'll structure my response around:

1. **The shape of the problem** — what we're actually designing, what's settled
2. **Mapping Unreal animation concepts to HROT** — terminology table
3. **Topology question** — is Anim a new node role or co-located with Muscle?
4. **The Channel pattern applied to animation** — AnimationChannel (montage one-shots), StanceChannel (movement mode), LookAtChannel (aim/gaze)
5. **Continuous data flow** — locomotion params (speed/dir) for blend space; already implicit in NavigationStatus
6. **Notify events** — engine event catalog entries, naturally scanned by EventScannerCompiler, fit into Blueprint WaitForEvent + BTree When-nodes
7. **Root motion** — the tricky bit
8. **Specific Blueprint authoring** — what nodes the AI designer sees: PlayMontage, WaitForMontageEnd, SetStance, OnAnimNotify event graph, "When animation X ended" When-node via predicate infra
9. **Open questions** to confirm with the engine architect

I should be careful not to make assumptions — call out where I'm guessing and what to confirm with the engine architect. Length: this is a meaty architectural discussion, so a thorough but not exhausting response. I'll use prose with some structure, modest formatting since this is a serious architectural discussion not a quick chat.

Let me draft this now.

------

# Animation Control from Brain to "Anim Muscle" — Design Notes

Good problem. Your existing architecture already gives us most of the vocabulary we need; the design challenge is mostly about *picking the right existing patterns* and identifying the one or two genuinely new pieces. I'll lay out what I think the design should look like, with the open questions for your engine architect called out as we go.

## 1. What we're actually designing

You have a Brain (CGF) running BTrees / HSMs / Blueprints, and you're adding a node whose job is "be the body" of a human character: skeletal mesh, animation state machine, blend spaces, montages, IK, root motion. First on Stride, later proprietary, but authored content comes from Unreal so the *vocabulary* the AI sees should be Unreal-shaped.

In your existing topology this is unambiguously a Muscle. It's not the IG (which is read-only ghost-and-render). It's actively executing physical commands and publishing results back. The cleanest framing: alongside `MuscleGround` (vehicle kinematics), add `MuscleCharacter` (humanoid animation + character movement). Whether `MuscleCharacter` co-locates with `MuscleGround` in one process or runs as its own node is a deployment question — the *contract* between Brain and Muscle is the same either way. **First open question for your architect**: for humanoid characters, does root-motion animation drive the kinematics, or does kinematics drive a passive animation blend? Unreal does both depending on game; the answer determines whether `MuscleCharacter` *replaces* the ground-kinematics path for humans or *augments* it. I'll assume root-motion-drives-kinematics below (the AAA default for humanoids) and flag where it matters.

## 2. Mapping Unreal animation concepts to your engine

Before designing topics and channels, let me line up the vocabulary so it's clear what we're keeping vs. renaming:

- **AnimMontage** (one-shot: reload, vault, melee swing, gesture) → maps cleanly to your **Channel command** pattern. Brain writes intent; Muscle runs it; Status returns Success/Failure/Running. This is the heart of animation control from AI.
- **Locomotion blend space** (continuous walk/run/strafe driven by speed/direction) → not a separate command. It's a *consequence* of the existing `LocomotionChannel` + `NavigationStatus`. The Muscle Character node reads its own kinematic state (speed/heading-relative-to-velocity/crouched flag) and picks the blend automatically. Brain doesn't author this per-frame.
- **Movement Mode / Stance** (walking, jogging, sprinting, crouching, prone) → a *modal* descriptor. Treat as a small channel-style intent that swaps the active state-machine layer. Slower-changing than montages, but same pattern: Brain writes intent, Muscle confirms via status.
- **Aim Offset / Look-At** (continuous targeting overlay) → a continuously-published descriptor. Brain says "look at target/point T"; Muscle blends the aim layer toward it every frame. No discrete completion event — it's a slow-changing setpoint, like `NavigationIntent`.
- **AnimNotify** / **AnimNotifyState begin-end** (footstep, weapon-trail-on, hit-window-open, montage-ended) → an **engine event** in your `FdpEventBus` sense. These are exactly the kind of transient one-frame occurrences `TransientEventPredicateDto` and `EventScannerCompiler` were designed for. They become first-class entries in your **Engine Event Catalog** so Blueprint authors can subscribe via event graphs and so the new "When" BTree nodes can match against them.
- **Anim Layers / Slots** (upper body vs. full body, additive) → a Muscle-side implementation detail, mostly. The Brain doesn't author slots; it specifies *what* (play this montage) and the catalog entry declares *which slot* on the Muscle side.
- **Root motion** (animation drives capsule movement) → flagged per montage in the catalog; the Muscle integrates it into the same `WorldPos` it already publishes, so the Brain doesn't need to know.

## 3. The three new channels (proposed)

Following the exact pattern of `LocomotionChannel` / `WeaponChannel` / `InteractionChannel`, I'd propose three new ECS channel components living on the entity, written by the Brain, executed by Muscle Character:

**`AnimationChannel`** — one-shot montages. Action IDs like `ActionIdPlayMontage` with params `{ MontageId, BlendInTime, BlendOutTime, PlayRate, StartSection, LoopCount }`. Status reports Running/Success/Failure as usual; Failure when the montage is blocked (e.g., capability missing) or interrupted by a higher-priority montage. This is what `PlayMontageAndWait` in Unreal becomes for your AI.

**`StanceChannel`** — movement mode. Action IDs like `ActionIdSetStance` with params `{ StanceId }` where StanceId enumerates Standing/Crouched/Prone/Vault/Climb. Modal: writing a new stance preempts the current one. Status reports Running while transitioning, Success when the blend finishes.

**`LookAtChannel`** — aim/gaze targeting. Action IDs `ActionIdLookAtPoint` / `ActionIdLookAtEntity` / `ActionIdReleaseLook`, with params `{ Vector3 Point | Entity Target, BlendInTime, Priority }`. Continuous, not one-shot — but the channel pattern still works: Status stays Running while the look is held; Brain releases it explicitly.

These reuse all your existing infrastructure: `ActorCapabilityState` gating (no `CanAnim` flag → dispatcher forces Failure), Dispatcher/Executor lifecycle with `ActionInstanceId`/`DispatchedInstanceId`, network egress via `XxxChannelEgressTranslator`, ingress on the Muscle side feeding into the actual animation runtime. The Blueprint `ChannelCommandCatalog` extends with the new (channel, action) pairs and the `ChannelCommandNode` works unchanged. The Wait Primitive Catalog extends with `WaitForChannel:AnimationChannel` etc. So a Blueprint that says "play reload montage and wait for it" is literally a `ChannelCommand(Animation/PlayMontage)` → `WaitForChannel(AnimationChannel)` pair, indistinguishable from the existing `MoveToAndFire` example.

## 4. AnimNotify as engine events

This is where your Predicate Infrastructure earns its keep. AnimNotify is per-frame, transient, broadcast — exactly the `FdpEventBus` event shape. The Muscle Character node publishes `AnimNotifyEvent { Entity Target, NotifyId, MontageId, FloatPayload, MarkerName }` whenever the animation runtime crosses a notify, plus more specific typed events for high-value notifies (`MontageEndedEvent`, `FootstepEvent`, `WeaponTrailEvent`, `HitWindowOpenEvent`/`Closed`).

Cross-node propagation uses the same translator pattern you use for `WeaponFire` / `EntityDamage`: a `NetworkEventTranslator` egress on Muscle, ingress on Brain that republishes onto the Brain's `FdpEventBus`. Because these are typed events, they slot directly into:

- **Blueprint event graphs** — register each one in the `EngineEventCatalog` and `WaitForEventNode` becomes the equivalent of Unreal's `OnMontageBlendOut` delegate. The Blueprint compiler will emit `view.ReadEvents<MontageEndedEvent>()` poll loops the same way it does for `HitEvent` today.
- **BTree "When" nodes** via your existing predicate infrastructure — a `TransientEventPredicateDto` over `MontageEndedEvent` filtered by `Target == self` is exactly the shape `EventScannerCompiler` already handles. No new compiler work needed.

One thing worth being deliberate about: keep the *generic* `AnimNotifyEvent` (string-named markers from arbitrary content) as the catch-all for animator-authored markers that don't deserve a typed event. Have the high-value ones be typed. The split mirrors how Unreal does it — generic `AnimNotify` plus specific delegates for the well-known ones.

## 5. What the AI designer actually sees in the editor

This is the practical payoff. From the BTree / Blueprint author's seat:

For one-shot animations, they drop a `ChannelCommand(Animation/PlayMontage)` node with the montage picked from a catalog dropdown (montages are content-authored in UE and exported into the engine's montage registry — that registry feeds the editor combo). Wired to `WaitForChannel(AnimationChannel)` it gives them PlayMontageAndWait semantics. The Success/Failure branches let them handle interruption cleanly.

For stance changes, same shape: `ChannelCommand(Stance/SetStance)` with a stance enum dropdown.

For look-at, they have a `ChannelCommand(LookAt/LookAtEntity)` to acquire the gaze and a paired `ChannelCommand(LookAt/ReleaseLook)` later — modeled like Unreal's gameplay-tag-loose-add/remove pattern.

For *reacting* to animations — your "When" node design — the predicate infra gives the author a fluent picker:

- "When this entity's `AnimationChannel.Status` transitions to Success" → `PropertyMatchDto` on `AnimationChannel.Status` (with the two-tick edge detection living in the When-node's last-tick cache, exactly as §7.1 of the predicate doc spells out).
- "When a `FootstepEvent` for self is published" → `TransientEventPredicateDto(FootstepEvent, TargetField=Target)`.
- "When a `MontageEndedEvent` for montage X is published" → `TransientEventPredicateDto(MontageEndedEvent)` with a property match on `MontageId`.

The When-node UI doesn't need to know any of this is animation-specific — it's just another predicate over another event type. The work is in the catalogs, not the When-node itself.

## 6. The Brain-Muscle round trip for a montage, concretely

Drawing on the `MoveTo` example from the architecture doc:

1. BTree action node writes `ActionIdPlayMontage` into `AnimationChannel.ActiveAction`, fills `Params` with `{ MontageId=Reload, BlendIn=0.1, ... }`, increments `ActionInstanceId`.
2. `AnimationChannelEgressTranslator` on the Brain notices the change (SmartEgress dirty-flag pattern — montages are low-frequency events, definitely not 60Hz polling material), publishes an `AnimationIntent` DDS sample.
3. `AnimationIntentIngressTranslator` on Muscle Character receives it, writes the local ghost's `AnimationChannel`.
4. `AnimationDispatcherSystem` sees the `ActionInstanceId` mismatch, calls `OnExit` on any outgoing montage, `OnEnter` on the new one — which in turn calls into the Stride/Unreal animation runtime to enqueue the montage onto the correct slot.
5. As the montage plays, the animation runtime fires AnimNotifies → Muscle publishes `AnimNotifyEvent` / typed notify events onto its bus → egress translator → DDS → Brain bus → BTree/Blueprint consumes.
6. When the montage's blend-out completes, the Muscle's `AnimationExecutionSystem` writes `Status = Success` (or Failure if interrupted) into `AnimationChannel`, and `AnimationChannelStatusEgressTranslator` ships it back. Brain's `WaitForChannel` lowering sees Success, BTree node returns Success, control flow continues.

The key Unreal convention worth keeping: **a montage Status Success means "blended out cleanly," not "the last frame played."** Authors who write `PlayMontage → WaitForChannel` and then immediately fire another montage should not see a visual pop. That's the Muscle's responsibility to honor via blend-out time.

## 7. Continuous locomotion data — what *doesn't* need new mechanism

Worth being explicit about what stays the same. The character's walk/run blend is driven by `WorldPos` velocity and the orientation of the velocity vector relative to facing — both already in the entity's authoritative state owned by Muscle. The Muscle Character node reads its own `SimTransform` and feeds its blend-space inputs locally. The Brain never sends "play walk animation at 3.2 m/s" — it sends "MoveTo X at 3.2 m/s" via the existing `LocomotionChannel`, and the Muscle figures out the rest. This is the right division: the Brain doesn't know what a blend space is.

This also clarifies the topology question from §1: if root motion drives kinematics, you want `MuscleCharacter` to *be* the kinematics owner for humanoids — same way `MuscleGround` is for vehicles. The split-authority pattern handles per-entity-class differences naturally; a humanoid entity's `WorldPos` descriptor is owned by a `MuscleCharacter`-flagged node, a vehicle's by a `MuscleGround`-flagged one.

## 8. Things to confirm with your engine architect

A handful of things I'd want pinned down before going further:

1. **Root motion vs. kinematic capsule** for humanoids — drives the `MuscleCharacter` vs. `MuscleGround` topology decision.
2. **Whether animation Status replication is high-frequency or low**. I'd default to low (SmartEgress), since `Status` only flips on completion. But if you want per-frame *progress* feedback (e.g., a "montage is 47% complete" gauge for designers), that's a different story and probably argues for a separate `AnimationProgress` ghost descriptor.
3. **Notify-event QoS**. Critical notifies (hit-window-open, deals-damage-now) should be Reliable; cosmetic ones (footstep) can be BestEffort. Same split you already use for `EntityDamage` vs. `WorldPos`.
4. **Montage registry / asset-ID story** — montages authored in UE need stable IDs that both Brain (writing the intent) and Muscle (executing it) agree on. Likely lives in TKB or alongside.
5. **Whether the Muscle Character node hosts a `Perception` role too**. For humanoid AI it's natural — gaze targets need LOS — but you may want it separate.

Want me to drill into any of these — sketch the actual ECS structs / catalog entries / DDS topic schemas for the new channels, or work through the When-node interaction with `MontageEndedEvent` in more concrete detail?





We have not yet implemented the root motion at all. So far kinematics drive animation blend (speed of motion switche walk-run animation, direction of movement select strafe etc.) Animation state machines are hardcoded. But I need to be prepared also for the other way (animation drives the kinematics) which will come later.

are those 3 suggested channels a truly independent channels? I.e. no combining them into one?

update in design ideas: "when" node is not on a btree. Is it a node in an instance (entity script like) blueprint able to run other blueprint nodes if condition is fullfilled. You can briefly check "When_Reactivity_Iteration_Design_v2_2.md")

Idea: Is there a need for sending a request for chaining multiple animations, to ensure their seamless continuation?

replies

1. replied above
2. low, at least for now
3. agreed
4. sure, every separately controllable animation needs an id; each entity (based on ots rendering type) has a set of animations; could be expressed in TKB
5. muscle = motion, perception, weapon control, all physics...





Good — I have what I need on the When-node concept. It's an Instance-Blueprint authoring node with four modes (Value Changed, Event Fired, Condition Met, EQS Result). Now let me address your replies properly.

## On the three channels: are they truly independent?

Yes — they're independent because each one carries a **different shape of intent** with different lifetimes and different arbitration semantics. Let me lay out why splitting matters and where I'd push back on collapsing them.

**`AnimationChannel`** is for *one-shot, discrete, completable* commands. PlayMontage. Reload. Vault. Melee swing. They have a natural "running → success/failure" lifecycle that matches the existing Channel `Status` shape exactly. Authors will write `ChannelCommand(Animation/PlayMontage)` → `WaitForChannel(AnimationChannel)` and expect Success to mean "blended out cleanly." Preemption is binary: writing a new montage cancels the previous one (with crossfade).

**`StanceChannel`** is for *modal* state. Standing/Crouched/Prone/Vault/Climb. Two crucial differences from a montage:

- It's *sticky* — once set, it stays until changed. There's no natural "completion." A `WaitForChannel(StanceChannel)` waits for the *transition* blend to finish, not for the stance to end.
- It affects *other* commands. A montage authored for "standing reload" must not play while crouched, or it'll play the wrong asset variant. Stance is read by the dispatcher of `AnimationChannel` to pick the right montage variant, the same way `ActorCapabilityState.CanMove` is read by the Locomotion dispatcher today.

**`LookAtChannel`** is *continuous and concurrent*. Crucially, it must run *simultaneously* with whatever montage is playing — aim-offset is an additive layer in Unreal precisely because a character can reload while looking at a threat. If you fold this into `AnimationChannel`, you have to invent sub-channels inside the channel just to represent "the montage slot is busy but the aim-offset slot isn't." That's a regression to a uglier version of what slot decomposition already buys you.

The deepest reason they should stay split is **arbitration locality**. Your existing architecture says each channel's dispatcher owns capability gating, preemption, and `ActionInstanceId` lifecycle for its own action family. If you fold aim into montage into stance, the single dispatcher has to know about three orthogonal arbitration policies — montage vs. montage is "newer wins," stance vs. stance is "newest wins with blend," look-at vs. look-at is "priority-weighted blend." Three policies in one dispatcher is a recipe for bugs the moment anyone wants a fourth thing.

That said — **two cases worth considering for consolidation**:

1. If `StanceChannel` ends up being just one byte ("current stance enum") with no real action semantics, you might not want a full Channel at all. It could just be an ECS *descriptor* (like `ActorCapabilityState`) that the Brain writes and the Muscle reads, no dispatcher needed. The `Status` lifecycle for stance transitions is the only thing that argues for full-channel treatment. If you don't care about authors *waiting* on the transition, drop it to a plain descriptor.
2. If your initial humanoid set never needs aim-offset (no shooters yet, just locomotion + montages), defer `LookAtChannel` to whenever you actually have weapon-aiming characters. Channels are cheap to add later; over-designing them upfront is the trap.

So my recommendation: keep `AnimationChannel` as a full channel from day one. Treat `StanceChannel` as a full channel if designers will `WaitForChannel(StanceChannel)` on transitions, otherwise demote it to a descriptor. Defer `LookAtChannel` until you have an entity class that needs it.

## On root motion (your note: kinematics-drives-animation today, root-motion-may-come-later)

Good — that confirms the cleanest split for now. The Muscle Character node will be a *consumer* of the kinematic state the existing motion Muscle already produces. The animation blend space reads velocity from `SimTransform`, the state machine is locally hardcoded. No new authority transfer needed.

The future root-motion case is interesting because it inverts a piece of the contract: the animation runtime would become *authoritative* over per-frame position delta for the character, and the kinematics layer would integrate that delta instead of computing one. Two ways to handle this when it lands, both clean:

- **Per-entity-class authority split.** Vehicles keep `WorldPos`-owned-by-motion-Muscle. Humanoids switch `WorldPos`-owned-by-Muscle-Character. Your existing `DescriptorOwnershipMap` and `BrainMuscleOwnershipStrategy` already do per-entity ownership routing, so this is a matter of TKB metadata: humanoid templates pre-genesis-route `WorldPos` to whichever node carries the `MuscleCharacter` role flag. The existing kinematics integrator on the motion Muscle naturally skips entities it doesn't own.
- **Per-montage authority handoff.** A montage flagged `UsesRootMotion=true` causes the Muscle Character to take temporary authority over the entity's position descriptors for the montage's duration, then release. This is more complicated and probably overkill — the per-entity-class split is what AAA engines actually do in practice. Mentioned for completeness only.

You don't need to design for this now. You just want to make sure nothing in today's design *prevents* it. The Channel pattern itself is neutral on this — it doesn't care which node owns kinematics.

## On the "When" node (corrected understanding)

Reading the doc — yes, this is much cleaner than my BTree-side framing was. The When-node lives in Instance Blueprints, has four modes, and bridges the polling-runtime-vs-event-authoring gap by compiling event-feel syntax to your existing polling machinery. The four modes are exactly what an AI designer authoring character behavior needs for animation:

- **Event Fired** mode — matches the AnimNotify story directly. The designer drops a `WhenNode` in mode "Event Fired", picks `MontageEndedEvent` from the Engine Event Catalog dropdown, optionally adds a payload filter for `MontageId`. Underneath, the compiler emits a `view.ReadEvents<MontageEndedEvent>()` poll loop in the Tick graph and routes to the When's downstream exec on first match. This is structurally identical to how an Instance Blueprint subscribes to `HitEvent` via an event graph today — except the When-node lets the designer express it inline in any graph instead of needing a separate event graph. For animation, every typed notify event (`MontageEndedEvent`, `FootstepEvent`, `HitWindowOpenEvent`, generic `AnimNotifyEvent`) becomes an entry in the Engine Event Catalog and is then automatically authorable as a When-node trigger. Zero When-node-specific work for the animation feature — just register the events.
- **Value Changed** mode — this is the underused-but-powerful one for animation. "When `AnimationChannel.Status` changes" or "When `StanceChannel.ActiveAction` changes." Compiles to a previous-value snapshot + per-tick comparison. Designers can react to the channel completing without authoring the explicit `WaitForChannel` Boolean dance. Especially useful for *secondary* reactions: a `WaitForChannel` is great when the *primary* exec flow is "play a montage and continue after," but a `When(AnimationChannel.Status, ValueChanged → Failure)` is great when you want a *background* reaction ("any time a montage I'm running gets interrupted, log it and re-evaluate cover").
- **Condition Met** mode — uses the full predicate infrastructure. Allows compound conditions like "When `(AnimationChannel.ActiveAction == Reload AND TimeRemaining < 0.5s)` becomes true." This is where the predicate-DTO toolbox earns its keep for animation, especially mid-animation transitions (cancel-into-roll, etc.).
- **EQS Result** mode — not animation-related; you'll mostly use Event Fired and Value Changed for animation.

So the alignment is good: as long as we register the animation events in the Engine Event Catalog and the animation channel components in the editor's component picker, the When-node carries the reactive animation authoring story without needing animation-specific extensions.

One concrete callout: the typed events I proposed for animation should each have a `Target` field (the Entity the notify is for), and the catalog entry should set `TargetFieldName = "Target"` so the When-node automatically filters to Self. Otherwise a designer's When would fire on every entity's footsteps, which is comically wrong.

## On chaining animations for seamless continuation

Yes — this is real and worth a first-class mechanism. Two distinct flavors though, worth keeping separate in your head:

**Flavor 1: pre-authored sequence with sections.** Unreal's `AnimMontage` has named *sections*, and `MontageJumpToSection` can be called from a notify or from gameplay code to jump to the next section at a specific frame *without breaking the blend*. This is how reload-with-shell-by-shell-loading works: one montage with sections {Open, LoadShell1, LoadShell2, LoadShell3, Close}, and the AI decides at each section-end whether to continue or break out (e.g., to abort reload because a threat appeared). The seamlessness here is the *content author's* job inside one montage asset; the AI only sees "continue to next section" / "exit at next section break."

**Flavor 2: cross-montage chaining at the AI's discretion.** "Play vault, then play land-roll, then return to locomotion." Three different montages, but the author wants the boundary blends to be tight. The naive way — `WaitForChannel` for montage A, then issue montage B — guarantees a frame or two of locomotion poking through between them, because the wait sees Success only *after* blend-out. That's the visual pop.

For Flavor 2, the proven pattern is **queued / chained intents** at the channel level. Two flavors of API to consider:

- **Explicit `ActionIdPlayMontageQueue`** with params taking a list of montage IDs + per-transition blend times. The Muscle's `AnimationDispatcherSystem` knows the next montage in advance and starts blending into it *while* the previous one is still playing. The whole queue is one channel command with one final Success — the BTree/Blueprint just sees one PlayMontage that takes longer.
- **Continuation hint on PlayMontage itself.** Add a `NextMontageId` (optional) and `BlendIntoNextTime` to `PlayMontageParams`. When set, the Muscle knows to pre-blend. When the first finishes, it does *not* mark Success — it transitions internally and continues running until the chain ends. The AI sees one intent with one Success at the end of the chain.

I'd lean toward the queue API because chains can extend at the Brain side ("if still under threat, append a flee montage to the queue while the current one is still playing"). The continuation-hint approach only handles strict pairs at the time the first is issued. Queue + an `ActionIdEnqueueMontage` follow-up command for late additions is the most flexible — though "most flexible" isn't always "best to ship first." For the first iteration, the continuation-hint approach is enough for 90% of use cases.

For Flavor 1, no Brain-side mechanism is needed — section authoring is a content-side concern, and the Muscle's animation runtime handles section-jumping internally based on notifies in the montage data. The AI just sees a `MontageEndedEvent` when the whole thing finishes. If you do want the AI to influence section transitions ("at the next section break, exit the reload"), expose that as a small action on the channel: `ActionIdAdvanceMontageToSection` with a section-name param, treated as a soft request the runtime honors at the next section boundary. This is a power-user feature; defer until someone asks.

## Summary of revisions to the design

Given your replies, the design as I'd update it:

- **`AnimationChannel`**: full channel, one-shot montage commands, completion semantics. Add a `NextMontageId` / `BlendIntoNextTime` continuation hint to `PlayMontageParams` in v1 to handle the chaining-for-seamlessness case without a separate queue mechanism. Revisit a proper queue mechanism if/when designers ask.
- **`StanceChannel`**: keep as a full channel if `WaitForChannel(StanceChannel)` on transition completion is useful to authors (probably yes — "set to crouch then peek" is a real pattern). Otherwise demote to a descriptor.
- **`LookAtChannel`**: defer to when an entity class actually needs aim-offset.
- **AnimNotify events**: register in the Engine Event Catalog with proper `TargetFieldName` so When-nodes auto-filter to Self. Typed events for the well-known notifies, plus a generic `AnimNotifyEvent { Entity Target, MarkerName, FloatPayload }` catch-all for animator-authored markers.
- **When-node integration**: free of charge — animation events show up in When-node's Event Fired catalog dropdown automatically once they're registered. Value Changed mode covers `AnimationChannel.Status` and `StanceChannel.ActiveAction` reactions without any animation-specific compiler work.
- **TKB**: humanoid templates carry the set of animation IDs (montages, stances) that entity class supports. The Muscle Character node consults TKB at spawn to know which assets to preload. The editor's montage-picker dropdown filters by the selected entity class.
- **Root motion**: not designed for, not designed *against*. When it lands, per-entity-class authority routing handles the kinematic ownership flip, no channel changes needed.

Want me to go further on any of these — work out the actual ECS struct shapes for `AnimationChannel` + `PlayMontageParams` with the continuation hint, sketch the Engine Event Catalog entries for the notify family, or walk through a concrete designer-facing flow ("AI patrols, sees threat, plays alert-bark montage then aims-and-fires") end to end?







i need all the separate channels including aim at, no reduction flavor 2 - queue API, explicit

yes please sketch the ECS structs and events (just roughly), then prepare an explanation of what we are doing (small mini design) to be reviewed by the architect and hw might correct us a bit, suggest concrete ECS components and events etc, all in a way that fits the engine





Got it — three full channels including LookAt, explicit queue API for chaining. Let me sketch the structs first, then build the architect-review document.

## Rough ECS struct sketches

These are illustrative shapes following your existing channel conventions (fixed-size `Params`/`State` byte arrays, action IDs, `ActionInstanceId`/`DispatchedInstanceId` lifecycle). Field sizes and exact layout will need the engine architect's eye for cache-line / SoA-friendliness.

### `AnimationChannel`

```csharp
public unsafe struct AnimationChannel
{
    public int ActiveAction;                // ActionIdPlayMontage, ActionIdStopMontage,
                                            // ActionIdPlayMontageQueue, ActionIdEnqueueMontage,
                                            // ActionIdClearMontageQueue
    public NodeStatus Status;               // Running | Success | Failure
    public uint BehaviorInstanceId;
    public uint ActionInstanceId;
    public uint DispatchedInstanceId;

    public fixed byte Params[64];           // per-action params blob
    public fixed byte State[32];            // executor working state (current section, etc.)
}

// Params variants (one of these is unsafe-cast into the Params blob):

public struct PlayMontageParams
{
    public int MontageId;                   // stable ID from TKB animation set
    public float BlendInTime;
    public float BlendOutTime;
    public float PlayRate;
    public byte  StartSectionIndex;         // 0 = from start
    public byte  LoopCount;                 // 0 = single play, 255 = infinite-until-stopped
    public byte  Priority;                  // for preemption arbitration
    public byte  Flags;                     // bit 0: UsesRootMotion (future)
                                            // bit 1: InterruptCurrent (else queue-if-busy)
}

public struct StopMontageParams
{
    public float BlendOutTime;
    public byte  Reason;                    // 0 = normal, 1 = forced
}

public struct PlayMontageQueueParams
{
    // Inline queue of up to N entries in the Params blob.
    // Each entry is a compact MontageQueueEntry; Count tells how many are live.
    public byte Count;                      // 1..MaxQueueLen
    public byte Reserved0, Reserved1, Reserved2;
    // followed by Count * sizeof(MontageQueueEntry) entries (fits in Params blob)
}

public struct MontageQueueEntry
{
    public int   MontageId;
    public float BlendIntoTime;             // crossfade time INTO this entry from the previous
    public float PlayRate;
    public byte  StartSectionIndex;
    public byte  Flags;
}

public struct EnqueueMontageParams
{
    // Single entry to append to currently-running queue.
    // Fails (Status=Failure) if no queue is active.
    public MontageQueueEntry Entry;
}
```

### `StanceChannel`

```csharp
public unsafe struct StanceChannel
{
    public int ActiveAction;                // ActionIdSetStance
    public NodeStatus Status;               // Running while blending; Success on transition done
    public uint BehaviorInstanceId;
    public uint ActionInstanceId;
    public uint DispatchedInstanceId;

    public fixed byte Params[16];
    public fixed byte State[8];             // current stance, target stance, blend progress
}

public struct SetStanceParams
{
    public byte  TargetStance;              // StanceId enum: Standing | Crouched | Prone | etc.
    public float BlendTime;
    public byte  Reserved0, Reserved1, Reserved2;
}
```

### `LookAtChannel`

```csharp
public unsafe struct LookAtChannel
{
    public int ActiveAction;                // ActionIdLookAtPoint, ActionIdLookAtEntity, ActionIdReleaseLook
    public NodeStatus Status;               // Running while looking; Success on release; Failure if unable
    public uint BehaviorInstanceId;
    public uint ActionInstanceId;
    public uint DispatchedInstanceId;

    public fixed byte Params[32];
    public fixed byte State[16];            // current aim direction, blend progress
}

public struct LookAtPointParams
{
    public Vector3 WorldPoint;
    public float   BlendInTime;
    public byte    Priority;
    public byte    Reserved0, Reserved1, Reserved2;
}

public struct LookAtEntityParams
{
    public Entity TargetEntity;             // resolved via NetworkEntityMap on Muscle side
    public Vector3 LocalOffset;             // offset on target (head-bone-ish)
    public float   BlendInTime;
    public byte    Priority;
    public byte    Reserved0, Reserved1, Reserved2;
}

public struct ReleaseLookParams
{
    public float BlendOutTime;
}
```

### Animation events (Muscle → Brain)

```csharp
// Generic catch-all for animator-authored notify markers
public struct AnimNotifyEvent
{
    public Entity Target;
    public int    MontageId;
    public uint   MarkerHash;               // stable hash of marker name; or use an enum/registry
    public float  PayloadFloat;
}

// Typed events for high-value notifies. Each one has Target as first field
// so the EngineEventCatalog can set TargetFieldName="Target" for Self-filtering.

public struct MontageStartedEvent
{
    public Entity Target;
    public int    MontageId;
    public uint   ActionInstanceId;         // correlates back to the issuing command
}

public struct MontageEndedEvent
{
    public Entity Target;
    public int    MontageId;
    public uint   ActionInstanceId;
    public byte   EndReason;                // 0=NaturalEnd, 1=Interrupted, 2=BlendedOutByNext, 3=Failed
}

public struct MontageSectionAdvancedEvent
{
    public Entity Target;
    public int    MontageId;
    public byte   FromSectionIndex;
    public byte   ToSectionIndex;
}

public struct FootstepEvent
{
    public Entity Target;
    public Vector3 WorldPosition;
    public byte    FootIndex;               // 0=left, 1=right
    public byte    SurfaceTypeHint;         // resolved by Muscle physics surface
}

public struct HitWindowOpenedEvent
{
    public Entity Target;
    public int    MontageId;
    public byte   WindowId;                 // melee-attack hit-window id
}

public struct HitWindowClosedEvent
{
    public Entity Target;
    public int    MontageId;
    public byte   WindowId;
}

public struct StanceChangedEvent           // fired by StanceChannel executor on transition complete
{
    public Entity Target;
    public byte   PreviousStance;
    public byte   NewStance;
}
```

### DDS topic shape (one per channel intent, one per status, plus events)

Mirrors the existing `NavigationIntent` / `NavigationStatus` split. SmartEgress for low-frequency channel changes; notify events go through `INetworkEventTranslator` like `WeaponFire` does.

```
Intents (Brain → Muscle, Reliable, TransientLocal):
  AnimationIntent     -- carries AnimationChannel state (ActiveAction, Params, ActionInstanceId)
  StanceIntent        -- carries StanceChannel state
  LookAtIntent        -- carries LookAtChannel state

Statuses (Muscle → Brain, Reliable, TransientLocal):
  AnimationStatus     -- Status + ActionInstanceId echo
  StanceStatus
  LookAtStatus

Events (Muscle → Brain, mostly Reliable / Volatile; FootstepEvent could be BestEffort):
  MontageStartedEvent
  MontageEndedEvent
  MontageSectionAdvancedEvent
  AnimNotifyEvent
  FootstepEvent
  HitWindowOpenedEvent
  HitWindowClosedEvent
  StanceChangedEvent
```

------

Now the architect-review mini-design. I'll write it in your project's prose style and save it as a document you can hand to the architect.The mini design is ready for your architect.

A few notes on the struct sketches above and the doc:

The **`AnimationChannel.Params` budget for the queue** is the load-bearing open question. If the architect's convention is a fixed channel size across the board (matching `LocomotionChannel`'s `Params` footprint), the queue depth has to fit inside it — at ~16 bytes per `MontageQueueEntry`, that's 4 entries in 64 bytes. If 4 is too few, `ActionIdPlayMontageQueue` could carry the queue *by reference* — point at a side-buffer (a per-entity `MontageQueue1024` component, allocated only when needed) rather than inlining. That avoids ballooning the channel `Params` but adds an allocation story. The architect will probably have a clean answer here based on what they've done for `MissionPlan` (which has a similar "variable-length list owned by one entity" shape).

The **`ActionInstanceId` semantics for enqueue** is the other genuinely tricky bit. The channel pattern as documented uses `ActionInstanceId` to detect "new command arrived" vs. "still running same command." Enqueue *modifies* the running command. Two options:

- Treat enqueue as a sub-event that bypasses the `ActionInstanceId` bump, with a separate `QueueDirtyCounter` field the dispatcher watches.
- Treat the queue as a separate ECS component (`AnimationMontageQueue`) and the channel command just refers to it; enqueue mutates the queue component directly without touching the channel.

The second is cleaner but more components. Worth raising as a real question rather than guessing.

The doc deliberately avoids prescribing on a few things (root-motion specifics, exact event QoS table, capability bit numbering) — these are the kinds of details where the architect's eye matters more than ours. The shape and substance of the proposal — three channels, queue API, notify-event catalog registration buying the When-node integration for free — is where we want their substantive review.

Want me to also draft a one-page diagram showing the end-to-end flow (Brain `ChannelCommand` → DDS → Muscle dispatcher → animation runtime → notify event → DDS → Brain `WhenNode`) for the same review packet? It might help the architect see the data-flow story at a glance before reading the prose.



