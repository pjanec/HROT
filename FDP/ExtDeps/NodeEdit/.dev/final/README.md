# Blueprint Node Editor — Project Package

This directory contains everything needed to implement a generic, Unreal-style
node-graph editor library in C# (.NET 8) targeting Raylib-cs + ImGui.NET +
rlImGui-cs hosts. The editor is designed for a Blueprint visual-scripting
subsystem but is intentionally host-agnostic via interfaces.

## Audience

Implementation is delegated to an AI coding agent (Claude Sonnet 4.6 in VS Code).
This package is structured for that workflow.

## How to navigate

Start here:

1. **`instructions/00-START-HERE.md`** — Read this first. Explains the project
   structure, conventions, and points to the task list.
2. **`instructions/01-spec-brief.md`** — Condensed, normative specification.
   The single source of truth. All implementation work must conform.
3. **`instructions/02-task-list.md`** — Ordered list of implementation tasks.
   Each task references specific spec sections and kernel files.
4. **`instructions/03-NN-task-*.md`** — Individual task briefs. One file per task.
5. **`kernel/`** — Pre-written authoritative code: interfaces, primitives, and
   high-judgment implementations. Copy into the solution; do not modify without
   explicit approval.
6. **`specs/`** — Full design discussions and rationale. Reference material when
   the brief doesn't fully cover an edge case.

## What is built

A reusable C# library that renders a node-graph canvas via ImGui.NET, accepts
graph data from any host implementing a small set of interfaces, and provides
all the editor UX of a mature blueprint-style editor (search popup, picker,
mini-editors, comments, reroutes, undo/redo, find, bookmarks, hot-reload
indicators, etc.).

A demo application proves the editor works end-to-end with a "fake blueprint"
host so the editor can be developed and tested in isolation from the real
Blueprint subsystem.

## What is NOT built

- Anything Blueprint-specific (compiler, runtime, ECS integration). The editor
  is a library; the Blueprint subsystem will be a consumer later.
- Application shell (toolbars, menus, status bars). The editor exposes commands
  and indicators via an action API; the host renders the shell.
- Persistence of host data. The editor exposes commands; the host applies them
  to its data store.

## Conventions

- **Target framework:** `net8.0`
- **C# language version:** 12 (file-scoped namespaces, primary constructors,
  collection expressions, `record` types freely used)
- **Test framework:** xUnit
- **Code style:** Standard Microsoft C# conventions. Allow `var`. Allman braces.
  XML doc comments on public API surface. Inline `//` comments for non-obvious
  internal logic.
- **Namespace root:** `NodeEditor.*`
- **Solution layout** (each row a project):
  ```
  NodeEditor.Primitives        — Layer 1: IDs, geometry, zero deps
  NodeEditor.Core              — Layer 2: view-model, undo, interfaces
  NodeEditor.UI                — Layer 3: ImGui rendering, panels
  NodeEditor.Demo              — Layer 5: raylib-cs demo + fake host
  NodeEditor.Core.Tests        — xUnit
  NodeEditor.UI.Tests          — xUnit
  ```
- **Dependencies:**
  - `NodeEditor.Primitives`: none.
  - `NodeEditor.Core`: depends on `NodeEditor.Primitives` only.
  - `NodeEditor.UI`: depends on `NodeEditor.Core` + `ImGui.NET` (1.91.6.1 or
    compatible).
  - `NodeEditor.Demo`: depends on `NodeEditor.UI` + `Raylib-cs` + `rlImGui-cs`.

## Workflow for the agent

For each task:

1. Read the task brief file (`instructions/03-NN-task-*.md`).
2. Re-read the spec sections it references.
3. Implement.
4. Compile.
5. If tests are part of the task, run them.
6. Mark the task complete by writing a short status note at the bottom of the
   task brief.

If you (the agent) encounter ambiguity in the spec, do NOT invent. Instead:
- Pause work.
- Write a numbered question in `instructions/QUESTIONS.md`.
- Continue with another task that doesn't depend on the answer.
