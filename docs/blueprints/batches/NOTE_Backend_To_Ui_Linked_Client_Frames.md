<!--STATUS
state: LIVE
updated: 2026-11-21
current-answer: the whole file — the contract the gateway maps (CE-11018)
related-designs:
  - ../../designs/IG/DESIGN_Linked_Client_Director.md §2b, §2c — the design these signatures realise
  - HANDOFF_Ui_To_Backend_Linked_Client.md — the dispatch (frozen at 710b16f47); §3 asked for this note first
  - SimNetGateway docs/design/DD-022-Dps-Director-Rendering.md §3e, §3h — the wire the gateway maps these onto (FYI)
-->

# NOTE — backend → ui (and the gateway): the linked-client film contract, as built (CE-11018)

**Where:** `Hrot/Engine/Hrot.Core/Infrastructure/LinkedClientFilm.cs` (the types) and `LinkedClients.cs` (the new `ILinkedClient` members), namespace
`Hrot.Common.Infrastructure` — below both the director (`Hrot.Presentation`) and the gateway (`Hrot.Core` only). ⛔ Nothing names what the client is.

## 1. `ILinkedClient` — the film members (added; the world members unchanged)

```csharp
bool CanFilm => false;                                  // read at a job's BeginFilm only; a job never changes source part way
Task BeginFilm(FilmJob job);                            // completes when warmed up; FAULTS with the reason it cannot take the job
Task Frame(FilmFrame frame);                            // in Index order, none skipped; completes when done; FAULTS with the reason it failed
Task<FilmTrack?> EndFilm(FilmJob job, FilmJobOutcome outcome);   // once per BeginFilm (failed / stopped too); the client's track or null
```
⭐ **Default interface members** — a client that only presents the world (CE-11026) compiles unchanged and films nothing (`CanFilm` false).
⚠ Named `BeginFilm` / `EndFilm` (the design's diagram says `Begin` / `End`): beside `BeginWorld` a bare `Begin` reads as a world call. ⭐ **A failure is a
faulted task with the reason as its message** (DD-022 `FrameDone.failed` / `RenderJobResult.failed` ⇒ fault the task) — the director fails the job with it.

## 2. The types

```csharp
enum FilmJobKind : byte { Reel = 1, Still = 2 }
enum FilmFrames  : byte { Auto = 0, Map = 1 }            // the job's "frames: auto | map"
enum FilmSound   : byte { Hrot = 0, Client = 1 }         // the job's "sound: hrot | client" (the mux is CE-11021)
record FilmJob(Guid JobId, FilmJobKind Kind, string What, int Fps, int Width, int Height, int Supersample, string Dir,
               FilmSound Sound = FilmSound.Hrot, FilmFrames Frames = FilmFrames.Auto);

readonly record struct FilmCamera(Vector3 Position, Vector3 Forward, Vector3 Up, float FovDeg);
enum FilmFrameKind : byte { Scene = 0, Repeat = 1, Silence = 2 }
enum FilmSubtitlePosition : byte { Bottom = 0, Top = 1, Middle = 2 }
record FilmSubtitle(string Text, FilmSubtitlePosition Position, float Alpha);
enum FilmGraphicKind : byte { Ring = 0, Arrow = 1, Route = 2, Area = 3, Label = 4 }
record FilmGraphic(FilmGraphicKind Kind, IReadOnlyList<Vector3> Points, float Radius, string? Text, uint Colour, float Alpha);
record FilmFrame(Guid JobId, int Index, FilmFrameKind Kind, double SimTime, double FilmRate, long Step, FilmCamera Camera,
                 int Width, int Height, int Supersample, string Path, FilmSubtitle? Subtitle, IReadOnlyList<FilmGraphic> Graphics, float Fade,
                 IReadOnlyList<string> LayersOn, IReadOnlyList<string> LayersOff);

enum FilmJobEnd : byte { Ok = 0, Failed = 1, Stopped = 2 }
record FilmJobOutcome(FilmJobEnd End, string? Reason = null);
record FilmTrack(string Path, long Samples, int SampleRate, int Channels);
```

## 3. Field by field — against DD-022's `FrameRequest` list (handoff §3)

| `FrameRequest` asks | `FilmFrame` | meaning |
|---|---|---|
| job id · frame index | `JobId` · `Index` | the film frame number 0, 1, 2 … — ⭐ EVERY frame of the movie, in order |
| scene or silence | `Kind` | `Scene` render · `Silence` under a title card (the card's picture is HROT's — Q113 D-4; the client writes no PNG, only keeps its track) · ⭐ `Repeat` (added): slow motion past one frame per step repeats the last scene — HROT copies the PNG, the client only advances its track |
| step | `Step` | the director's steps in this reel item so far; the world is HELD between frames — frame N is taken after step N |
| sim time | `SimTime` | the world's sim time the frame shows (s) |
| film rate | `FilmRate` | sim seconds per film second now (the shot's speed); the job's `Fps` is the film's frames per second |
| position · orientation · vertical FOV | `Camera.Position` · `Camera.Forward` + `Camera.Up` · `Camera.FovDeg` | ⚠ orientation as a forward + up basis (unit vectors, engine space X east / Y north / Z up), not a quaternion — no axis convention to agree. It is the pose the node's own map would have rendered (its film layer placed it) |
| width · height · supersample | `Width` · `Height` · `Supersample` | render `Supersample`× larger, scale down |
| file | `Path` | the PNG the client writes (a scene frame only) |
| subtitle (text, position, alpha) | `Subtitle` | null = none |
| overlay graphics (world points) | `Graphics` | resolved this frame: world points, radius, text, colour `0xRRGGBB` (opacity is the separate `Alpha`; measured: the director sends `FilmGraphics.DefaultColour = 0xFFD23C`), alpha |
| layers on / off | `LayersOn` · `LayersOff` | the take's layer override (names) |
| — | ⭐ `Fade` (added) | the dip to black over the picture this frame (0…1) |
| per-frame result | `Frame`'s task | completed = written · faulted = failed + the reason |
| per-job result: the track and its samples | `EndFilm`'s `FilmTrack` | `Samples` per channel — CE-11021 checks them against the frames |

## 4. When a job goes to the client

`frames: auto` (the default) and the node's `LinkedClients.Current` with `CanFilm` true at the job's start ⇒ the client; otherwise HROT's map, as today.
`frames: map` ⇒ always the map. ⛔ A take showing the 2-D map while its frames go to the client fails the job with the reason (render it with `frames: map`).

## ⭐ Addendum `2026-11-21` — CE-11024: an impact's effect entity (`EffectCause`, Hrot.Core, id 72, recorded)

Two fields APPENDED (the earlier ones keep their offsets):

```csharp
public byte   Impact;    // Fdp.Toolkit.Combat.ImpactKind + 1 — 1 Entry, 2 Exit, 3 Stop; ⭐ 0 = not a round's impact (a detonation)
public Entity Shooter;   // who fired it (Null = unknown) — from RoundImpactNotification.Shooter or DetonationNotification.Shooter
```

⚠ Map `Impact` as `ImpactKind = Impact - 1` only when `Impact > 0`. A plain copy of `ImpactKind` would have made every detonation read "entered"
(Entry is 0). Both fields read the same live and after a replay seek (`EffectStateTests.CE11024_*`).
