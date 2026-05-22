# 00 — START HERE

You are implementing a node-graph editor library in C# .NET 8.

you are a dev lead. follow .github\agents\dev-lead-agent.md . 
Your goal is to manage the implementation of tasks from
TASK-TRACKER.md 

In the batches, prefer referencing task details and design over duplicating the existing
instructions into the batch.

Once deleoper sub-agent finished and provides the batch report, you must perform a thorough
review, focused mainly on the test quality - especially if they are aligned with the architecture
(not a fake tests or overly simplified test not exercising all necessary part of the code)
and testing thoroughly the features. Issues found should be projected to the next batch for fixing.

After each batch review and commiting don't forget to continue with creating next batch
 and delegating the work to subagent, do not stop until all tasks are done!

Do NOT use explorer sub-agent, always delegate the batches to Claude Sonnet 4.6 sub-agent.


- A spec brief section (always in `01-spec-brief.md`)
- A detailed spec file in `../specs/`
- A kernel file in `../kernel/`

**Track your progress in `TASK-TRACKER.md`** — tick the checkbox for each
task as you complete it. The tracker is organized by phase and links to
every task brief.

## Ground rules

1. **The spec brief (`01-spec-brief.md`) is the authoritative quick reference.**
   For UX questions and behavioral details, consult it first.

2. **The full specs in `../specs/` are the deeper reference** when the brief is
   too terse. They are organized by topic; the brief references the relevant
   sections by filename.

3. **Kernel files in `../kernel/` are authoritative code.** Copy them verbatim
   into the solution. Do not modify their public surface. If you find a bug in
   a kernel file, write a note in `QUESTIONS.md` and continue working around it.

4. **Inline code blocks inside task briefs are authoritative.** If a brief shows
   a class skeleton, the names and signatures are committed; the implementation
   body is yours unless explicitly given.

5. **No surprises.** Do not invent features the spec doesn't describe. Do not
   "improve" the spec. If something seems missing or contradictory, log a
   question; do not guess.

6. **One project per task by default.** Each task targets one .csproj. Do not
   make changes to other projects unless the task says so.

7. **Compile after every task.** If the solution doesn't build, the task is not
   complete. Fix or log a blocker.

8. **Write tests when the task asks for them.** Don't write tests when it
   doesn't — keep scope tight.

9. **Comment styles:**
   - XML doc comments (`///`) on every public type and public member.
   - Inline `//` on non-obvious implementation details.
   - No noise comments (`// Increment i` over `i++` is forbidden).

10. **Style:**
    - File-scoped namespaces.
    - One public type per file.
    - File name matches the public type name exactly.
    - `var` is preferred when the type is obvious from the RHS.
    - Allman braces.
    - Spaces, not tabs, 4-space indent.

## Solution layout (target)

When complete, the solution looks like:

```
src/
  NodeEditor.Primitives/
    NodeEditor.Primitives.csproj
    NodeId.cs
    PinId.cs
    ...
  NodeEditor.Core/
    NodeEditor.Core.csproj
    Interfaces/
      IGraphModel.cs
      INodeModel.cs
      IPinModel.cs
      ILinkModel.cs
      ...
    ViewModel/
      GraphView.cs
      SelectionState.cs
      ...
    Commands/
      GraphCommand.cs
      UndoStack.cs
      ...
    Search/
      FuzzyMatcher.cs
      ...
    Spatial/
      SpatialIndex.cs
  NodeEditor.UI/
    NodeEditor.UI.csproj
    Canvas/
      CanvasRenderer.cs
      ...
    Panels/
      MyBlueprintPanel.cs
      DetailsPanel.cs
      ...
    Picker/
      PickerWindow.cs
      ...
    Editors/
      BoolEditor.cs
      FloatEditor.cs
      ...
  NodeEditor.Demo/
    NodeEditor.Demo.csproj
    Program.cs
    FakeBlueprint/
      ...
    Scenarios/
      ...
tests/
  NodeEditor.Core.Tests/
    NodeEditor.Core.Tests.csproj
    ...
  NodeEditor.UI.Tests/
    NodeEditor.UI.Tests.csproj
    ...
```

The exact root folder is up to your VS Code workspace. Adapt `src/` and
`tests/` to fit existing solution layout if needed.

## How to read a task brief

Each task brief has these sections:

- **Goal** — one-sentence description.
- **Project** — which .csproj the work lives in.
- **References** — spec sections + kernel files to read first.
- **Deliverables** — files to create or modify, with one-line descriptions.
- **Implementation** — guidance, often including inline code skeletons.
- **Acceptance** — what "done" means.
- **Estimated size** — rough LOC count, helps you gauge if you're off track.

## Order of work

Do tasks in the order listed in `02-task-list.md`. Later tasks depend on
earlier ones. Don't skip ahead.

## Handling blockers

If a task is blocked by a question or by missing information:

1. Append the question to `instructions/QUESTIONS.md` with a header line:
   ```
   ## Q-NNN (task T-XX): <one-line summary>
   ```
2. Continue with the next task that isn't blocked, if any.
3. When unblocked, return to the blocked task.

## Demo app is part of the deliverables

The demo app (`NodeEditor.Demo`) is not optional. It is the editor's test
harness and the deliverable that proves the editor works end-to-end without a
real Blueprint subsystem. Building it is task T-19 onward.

## Final acceptance for the project

- All tasks marked complete.
- Solution builds with no warnings (target: `<TreatWarningsAsErrors>true</TreatWarningsAsErrors>`).
- All xUnit tests pass.
- Demo app runs and exercises every spec'd feature via at least one scenario.

Begin with `02-task-list.md`.
