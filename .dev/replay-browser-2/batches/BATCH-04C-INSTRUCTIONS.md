# BATCH-04C Instructions — Corrective: Fix Fake Wiring Tests

**Batch**: BATCH-04C
**Status**: CHANGES REQUIRED (corrective for BATCH-04)
**Scope**: 5 targeted fixes — no new features, no refactoring beyond what is listed

---

## Context

See the review at `.dev/replay-browser-2/reviews/BATCH-04-REVIEW.md` for the full diagnosis.

BATCH-04's production code is correct. The problem is that three tests (FND-T15, FND-T16, FND-T18)
are fake — they do not exercise the subsystem's delegate wiring. A fourth test (entity-link click
in ComponentDiffPanel) is missing entirely.

This corrective addresses only those four deficiencies. **Do not touch any other existing code.**

---

## Codebase Exploration (required before writing a single line)

Before implementing, read:
1. `Hrot/Subsystems/Hrot.ReplayBrowser/ReplayBrowserSubsystem.cs` — understand `WireDelegates()` and `ExecuteCausalityJump` exactly.
2. `Hrot/Subsystems/Hrot.ReplayBrowser.Tests/ReplayBrowserSubsystemTests.cs` — understand what the fake tests currently do so you replace the right methods.
3. `FDP/Engine/Fdp.Presentation/ImGui/Panels/ReplayBrowser/ComponentDiffPanel.cs` — understand `OnEntityLinkClicked`, `CollectVisibleNodes`, and how entity-link detection works inside `DrawDiffNode`.
4. `FDP/Engine/Fdp.Presentation.Tests/ImGui/ReplayBrowser/ComponentDiff/ComponentDiffPanelTests.cs` — understand what tests already exist.
5. `FDP/Toolkits/Fdp.Toolkits/ReplayBrowser/History/EntitySelectionHistory.cs` — understand `PushSelection`, `OnSelectionChanged`, `CanGoBack`.
6. `FDP/Toolkits/Fdp.Toolkits/ReplayBrowser/History/PlaybackHistoryTracker.cs` — understand `PushFrame`, `OnSeekRequested`, `CanGoBack`.
7. `FDP/Toolkits/Fdp.Toolkits/ReplayBrowser/ReplayBrowserContext.cs` — understand `SeekToFrame`, `CurrentFrame`, `StepForward`.

---

## Fix 1: Add `WireDelegatesForTest` internal method to `ReplayBrowserSubsystem`

**File**: `Hrot/Subsystems/Hrot.ReplayBrowser/ReplayBrowserSubsystem.cs`

Add this internal method. It wires the same delegate chain as the private `WireDelegates()` but
accepts its dependencies as parameters instead of reading `this` fields. Then refactor the private
`WireDelegates()` to delegate to it.

```csharp
/// <summary>
/// Test seam: wires delegates using caller-supplied dependencies.
/// Returns the seek and select intents so tests can invoke them directly.
/// </summary>
internal (Action<int> seekIntent, Action<Entity> selectIntent) WireDelegatesForTest(
    EntitySelectionHistory entityHistory,
    PlaybackHistoryTracker playbackHistory,
    InspectorState inspectorState,
    ReplayBrowserContext context,
    ComponentDiffPanel diffPanel,
    EventBrowserPanel eventPanel)
{
    entityHistory.OnSelectionChanged += e => inspectorState.SelectedEntity = e;
    playbackHistory.OnSeekRequested  += f => context.SeekToFrame(f);

    Action<int>    seekIntent   = f => { playbackHistory.PushFrame(f); context.SeekToFrame(f); };
    Action<Entity> selectIntent = e => entityHistory.PushSelection(e);

    diffPanel.OnEntityLinkClicked  = selectIntent;
    eventPanel.OnEntityLinkClicked = selectIntent;

    return (seekIntent, selectIntent);
}
```

Then refactor the existing private `WireDelegates()` so its body calls
`WireDelegatesForTest(_entityHistory, _playbackHistory, _inspectorState!, _context, _diffPanel!, _eventPanel!)`.
Assign the returned `seekIntent` and `selectIntent` to local variables exactly as before.
The private method must remain; it is called from `Initialize`.

**Important**: If `InspectorState` does not have a public `SelectedEntity` property visible from
the test project, use whatever property the production `WireDelegates()` actually assigns — look at
the existing `WireDelegates()` code to find the exact assignment.

---

## Fix 2: Rewrite FND-T15 in `ReplayBrowserSubsystemTests.cs`

**File**: `Hrot/Subsystems/Hrot.ReplayBrowser.Tests/ReplayBrowserSubsystemTests.cs`

Replace the existing `WireDelegates_SelectIntent_PushesExactlyOneSelection` test with:

```
[Fact]
public void WireDelegates_SelectIntent_PushesSelectionAndUpdatesInspectorState()
{
    var entityHistory  = new EntitySelectionHistory();
    var playbackHistory = new PlaybackHistoryTracker();
    var inspectorState = new InspectorState();
    var context        = new ReplayBrowserContext();
    var diffPanel      = new ComponentDiffPanel();
    var eventPanel     = new EventBrowserPanel(context.HistoryService);

    var (_, selectIntent) = _subsystem.WireDelegatesForTest(
        entityHistory, playbackHistory, inspectorState, context, diffPanel, eventPanel);

    int changeCount = 0;
    entityHistory.OnSelectionChanged += _ => changeCount++;

    var targetEntity = new Entity(7, 2);
    selectIntent(targetEntity);

    // Exactly one history entry pushed
    Assert.Equal(1, changeCount);
    // InspectorState updated via OnSelectionChanged chain
    Assert.Equal(targetEntity, inspectorState.SelectedEntity);

    // Second push with the same entity: EntitySelectionHistory may suppress
    // duplicates; regardless, no extra fire from the wiring itself
    selectIntent(targetEntity);
    // CanGoBack is true because at least 2 pushes were made (or 1 if dup-suppressed)
    // — either way the intent ran without throwing
}
```

Adapt to the actual API of `InspectorState` (whatever `SelectedEntity` property is called in
production). The critical assertions are: `changeCount >= 1` after first call, and
`inspectorState.SelectedEntity == targetEntity`.

---

## Fix 3: Rewrite FND-T16 in `ReplayBrowserSubsystemTests.cs`

Replace the existing `ExecuteCausalityJump_EnqueuesCorrectSequence` test with one that verifies
the actual sequence. Use the subsystem's history tracker fields through `WireDelegatesForTest`
to observe events.

Strategy:
- Create a fresh `ReplayBrowserContext`. In headless mode, `CurrentFrame` starts at some value (read it first as `preFrame`).
- Create `EntitySelectionHistory` and `PlaybackHistoryTracker` spies.
- Call `_subsystem.WireDelegatesForTest(...)` to wire them up.
- Subscribe to `playbackHistory.OnSeekRequested` to record the sequence of pushed frame values.
- Subscribe to `entityHistory.OnSelectionChanged` to record the selected entity.
- Call `_subsystem.ExecuteCausalityJump(target)`.
- Verify exactly two `PushFrame` calls were made (pre-frame and post-frame values recorded).
- Verify the `entityHistory.OnSelectionChanged` fired with `target`.
- Verify ordering: since `PlaybackHistoryTracker` fires `OnSeekRequested` only on GoBack (not on
  Push), use `CanGoBack` state to verify pushes happened. After 2 pushes, `CanGoBack == true`.
  After 1 push, `CanGoBack == false`. After 0 pushes, `CanGoBack == false`.

Since `StepForward` and `PushFrame` are called in a specific order, and we can observe
`_playbackHistory.CanGoBack` state:

```
Before call: CanGoBack == false (0 pushes)
After ExecuteCausalityJump: CanGoBack == true (2 pushes happened)
entitySelectionChangeCount == 1 (target was selected)
```

To verify ordering more precisely: record a call log using an `int` sequence counter that
increments on each observable event. Subscribe to `OnSelectionChanged` and check that the
selection fires *after* the second PushFrame (proved by CanGoBack being true at selection time).

```csharp
[Fact]
public void ExecuteCausalityJump_PushesPreAndPostFrameThenSelectsTarget()
{
    var entityHistory   = new EntitySelectionHistory();
    var playbackHistory = new PlaybackHistoryTracker();
    var inspectorState  = new InspectorState();
    var context         = new ReplayBrowserContext();
    var diffPanel       = new ComponentDiffPanel();
    var eventPanel      = new EventBrowserPanel(context.HistoryService);

    _subsystem.WireDelegatesForTest(
        entityHistory, playbackHistory, inspectorState, context, diffPanel, eventPanel);

    int selectionFireCount = 0;
    bool playbackHadTwoPushesBefore = false;
    entityHistory.OnSelectionChanged += _ =>
    {
        selectionFireCount++;
        // At selection time, both PushFrame calls must already have happened
        playbackHadTwoPushesBefore = playbackHistory.CanGoBack;
    };

    var target = new Entity(5, 1);
    _subsystem.ExecuteCausalityJump(target);

    // Two frames were pushed (pre + post)
    Assert.True(playbackHistory.CanGoBack, "Two PushFrame calls must produce CanGoBack==true");
    // Selection fired exactly once
    Assert.Equal(1, selectionFireCount);
    // Both PushFrame calls happened BEFORE the selection
    Assert.True(playbackHadTwoPushesBefore, "PushFrame calls must precede PushSelection");
    // InspectorState updated
    Assert.Equal(target, inspectorState.SelectedEntity);
}
```

---

## Fix 4: Rewrite FND-T18 in `ReplayBrowserSubsystemTests.cs`

Replace the existing `PlaybackHistoryTracker_PushAndGoBack_WorksCorrectly` test:

```csharp
[Fact]
public void WireDelegates_SeekIntent_PushesFrameAndSeeksContext()
{
    var entityHistory   = new EntitySelectionHistory();
    var playbackHistory = new PlaybackHistoryTracker();
    var inspectorState  = new InspectorState();
    var context         = new ReplayBrowserContext();
    var diffPanel       = new ComponentDiffPanel();
    var eventPanel      = new EventBrowserPanel(context.HistoryService);

    var (seekIntent, _) = _subsystem.WireDelegatesForTest(
        entityHistory, playbackHistory, inspectorState, context, diffPanel, eventPanel);

    // Track ordering
    var callLog = new System.Collections.Generic.List<string>();
    playbackHistory.OnSeekRequested += _ => callLog.Add("seek-from-history");

    // seekIntent must call PushFrame (which makes CanGoBack true after the second push)
    // and SeekToFrame on the context.
    // We can only observe PushFrame side-effect via CanGoBack and OnSeekRequested.
    seekIntent(5);
    seekIntent(10);

    // After two seeks, CanGoBack must be true (two frames pushed)
    Assert.True(playbackHistory.CanGoBack, "seekIntent must call PushFrame so two calls produce CanGoBack");

    // GoBack fires OnSeekRequested with the previous frame
    int seekTarget = -1;
    playbackHistory.OnSeekRequested += f => seekTarget = f;
    playbackHistory.GoBack();
    Assert.Equal(5, seekTarget);
}
```

Note: `context.SeekToFrame` may be a no-op in headless mode if no recording is loaded.
The test focuses on verifying `PushFrame` happens (observable via `CanGoBack`) and
`GoBack` fires `OnSeekRequested` (observable via the event). This is sufficient to
confirm `seekIntent` is wired correctly.

---

## Fix 5: Add entity-link click test to `ComponentDiffPanelTests.cs`

**File**: `FDP/Engine/Fdp.Presentation.Tests/ImGui/ReplayBrowser/ComponentDiff/ComponentDiffPanelTests.cs`

First, look at `ComponentDiffPanel.cs` to see if there is already a way to simulate or invoke
`OnEntityLinkClicked` from a `DiffValue` node. The entity-link click logic lives inside
`DrawDiffNode` (ImGui-bound). Extract or expose a static helper:

**In `ComponentDiffPanel.cs` — add an internal static helper:**

```csharp
/// <summary>
/// Test seam: fires <paramref name="callback"/> if <paramref name="node"/> is a
/// DiffValue whose NewValue parses as an entity handle.
/// </summary>
internal static bool TryFireEntityLink(DiffValue node, Action<Entity> callback)
{
    if (!ImGuiEntityLink.TryParse(node.NewValue, out Entity entity))
        return false;
    callback(entity);
    return true;
}
```

Then add a test:

```csharp
[Fact]
public void TryFireEntityLink_EntityHandleNewValue_FiresCallbackWithParsedEntity()
{
    var leaf = new DiffValue("Target", "[10, v2]", "[11, v3]", JsonValueKind.String, isModified: true);

    Entity captured = default;
    bool fired = ComponentDiffPanel.TryFireEntityLink(leaf, e => captured = e);

    Assert.True(fired);
    Assert.Equal(new Entity(11, 3), captured);  // NewValue = "[11, v3]"
}

[Fact]
public void TryFireEntityLink_PlainStringValue_DoesNotFireCallback()
{
    var leaf = new DiffValue("Name", "Alice", "Bob", JsonValueKind.String, isModified: true);

    bool fired = ComponentDiffPanel.TryFireEntityLink(leaf, _ => throw new InvalidOperationException("Must not fire"));

    Assert.False(fired);
}
```

If `DiffValue` does not have a public `NewValue` property, look at the actual fields on `DiffValue`
in `FDP/Toolkits/Fdp.Toolkits/ReplayBrowser/Diff/DiffNode.cs` and adapt the test accordingly.
Use whatever property/field holds the new (right-hand side) value.

---

## Definition of Done

- [ ] `WireDelegatesForTest` method exists and compiles on `ReplayBrowserSubsystem`.
- [ ] Private `WireDelegates()` delegates to it (same behavior, no regression in production).
- [ ] `WireDelegates_SelectIntent_PushesSelectionAndUpdatesInspectorState` passes — verifies wiring, not direct history calls.
- [ ] `ExecuteCausalityJump_PushesPreAndPostFrameThenSelectsTarget` passes — verifies sequence with real observables.
- [ ] `WireDelegates_SeekIntent_PushesFrameAndSeeksContext` passes — verifies seekIntent wiring.
- [ ] `TryFireEntityLink_EntityHandleNewValue_FiresCallbackWithParsedEntity` passes.
- [ ] `TryFireEntityLink_PlainStringValue_DoesNotFireCallback` passes.
- [ ] Total new tests in BATCH-04C: exactly 6 (replacing 3 fake tests + adding 2 entity-link tests +1 net new for FND-T15 expansion)
  - Actually: 3 rewrites + 2 new = effectively 5 meaningful test methods total.
- [ ] `dotnet build` — 0 errors in changed projects.
- [ ] `dotnet test` on changed projects — all pass.
- [ ] Write report to `.dev/replay-browser-2/reports/BATCH-04C-REPORT.md`.

---

## Constraints

- Do NOT add new features, new window classes, or new panels.
- Do NOT rewrite any tests that already pass and are genuine (FND-T09, T10, T11, T12, T13, T14, T17, CollectVisibleNodes tests).
- Minimize diff: change only the lines listed above.
- If `InspectorState.SelectedEntity` is not the correct property name, look it up from the existing `WireDelegates()` body and adapt.
- Keep all existing comments on `ReplayBrowserSubsystem.cs` — move them with any refactored code.
