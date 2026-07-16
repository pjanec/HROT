# Behavior Variables — Designer Quickstart

> **Who this is for:** anyone authoring BTree / blueprint behaviors who is *not* thinking about
> bytes, slots, or hashing — you just want your node to remember something, or share it. It answers
> one question: **"What kind of memory does my node need, and how do I set it up?"**
> **Related (deeper, for engineers):** `Blueprint_SharedState_GetShared_Design.md`,
> `BTree_AiActionParameterBinding_Detailed_Design.md`. You should not need those to use the system.

---

## Start here: describe what you want, in plain words

Every piece of data a node uses falls into one of **four kinds**. You don't pick "Role" and "Scope"
directly — you decide *what the data is for*, and that tells you the settings. Follow the tree:

```mermaid
flowchart TD
    Start([What does this node need<br/>to remember or read?]) --> Q1{Is it a setting you fix<br/>BEFORE the behavior runs,<br/>that never changes during play?}

    Q1 -->|"Yes — it's a knob<br/>(a threshold, a speed…)"| PARAM["<b>① Parameter</b><br/>set once, then read-only"]
    Q1 -->|"No — it changes<br/>while the behavior runs"| Q2{Who needs to SEE<br/>the changes?}

    Q2 -->|"Only this one node"| PRIV["<b>② Private scratch</b><br/>this node only"]
    Q2 -->|"Several nodes in<br/>THIS behavior tree"| BEH["<b>③ Shared in behavior</b>"]
    Q2 -->|"The whole entity,<br/>or another entity / the squad"| ENT["<b>④ Shared across entity / squad</b>"]

    PARAM --> PARAMHOW["Add a variable → <b>Role = Input</b>,<br/>bind it as the node's parameters.<br/>Edit its default values in the Inspector."]
    PRIV --> PRIVHOW["<b>Role = State, Scope = Node</b><br/>— this is the default.<br/>Usually nothing to change."]
    BEH --> BEHHOW["<b>Role = State, Scope = Behavior.</b><br/>Point every node's memory at the<br/><i>same</i> variable to share it."]
    ENT --> ENTHOW["<b>Role = State, Scope = Entity.</b><br/>To read ANOTHER entity's copy,<br/>use a <b>Get Shared</b> node + its entity."]

    classDef kind fill:#dbeafe,stroke:#1f6feb,stroke-width:2px,color:#0a2540;
    classDef how fill:#dcfce7,stroke:#2da44e,color:#0a2f16;
    class PARAM,PRIV,BEH,ENT kind;
    class PARAMHOW,PRIVHOW,BEHHOW,ENTHOW how;
```

---

## The four kinds, at a glance

| # | You want… | Think of it as | Set | Example |
|---|-----------|----------------|-----|---------|
| **①** | a value you **configure once** and never change while playing | a **knob / setting** | Role **Input** | "Fire when enemies ≥ **3**" · "Move at **4 m/s**" |
| **②** | scratch memory **only this node** keeps between ticks | this node's **notepad** | Role **State**, Scope **Node** *(default)* | a local retry counter · a cooldown timer for this node |
| **③** | memory **several nodes in one behavior** read and write together | a **shared whiteboard** for this behavior | Role **State**, Scope **Behavior** | two nodes advancing the same cursor / plan |
| **④** | memory the **whole entity** keeps (survives behavior switches), or that **other entities read** | the **squad's shared board** | Role **State**, Scope **Entity** | a squad rally count members read from the commander |

**Rule of thumb:** start at ① and only move down when you actually need sharing. Most nodes never
leave ① or ②. Sharing (③ ④) is powerful but means "someone else can change this too" — reach for it
deliberately.

---

## Single value or a bundle?

A variable can hold **one value** (a number, a position, an on/off flag) or a **bundle of related
values** (a small *struct* — e.g. `{ RallyCount, LastRallyTime }`). Pick a bundle when several values
belong together and travel together. In the **Add variable** dropdown you'll see the basic types
(`int`, `float`, `Vector3`, …) *and* any bundle types the project defines — pick whichever matches
what you're storing.

---

## How each kind looks in the editor

- **① Parameter** — Add a variable, set its **Role** to *Input*, and bind it as the node's
  parameters. Its starting values are editable in the Inspector's **Static Parameters** panel
  (they're applied once when the behavior is assigned, then left alone).
- **② Private scratch** — When you drop a composed node it already gets a private memory variable
  (Role *State*, Scope *Node*). You usually don't touch it.
- **③ Shared in behavior** — Change that memory variable's **Scope** to *Behavior* (the dropdown in
  the Blackboard Variables panel), then point the other nodes' memory at the **same** variable. Now
  they share one copy.
- **④ Shared across entity / squad** — Set **Scope** to *Entity*. The copy now lives on the entity
  and survives switching behaviors. For one entity to read *another's* copy (squad members reading
  the commander), use a **Get Shared** node and hand it the target entity.

---

## One honest caveat (we're working on it)

There are currently **two doors to the same room** for kind ④: you can either set a node's memory
**Scope = Entity**, *or* drop explicit **Get Shared / Set Shared** nodes that name a shared slot.
They use the same underlying storage. For now:

- Use **Scope = Entity** when the sharing is *within* one entity (across its behaviors).
- Use **Get Shared / Set Shared** when a blueprint needs to **explicitly read/write a named shared
  slot**, especially **another entity's** (e.g. a squad member reading the commander's plan).

Unifying these into a single, obvious concept is a known UX goal — if this trips you up, that's the
system's fault, not yours.

---

## Cheat sheet

```
Is it a fixed setting?  ───────────────► ① Parameter        (Role Input)
Changes at runtime, only this node?  ──► ② Private scratch  (State · Node)     ← default
Changes, shared in this behavior?  ────► ③ Shared in behavior (State · Behavior)
Changes, shared across entity/squad? ──► ④ Shared entity/squad (State · Entity + Get/Set Shared)
```

*Start at ①. Move down only when you truly need someone else to see the data.*
