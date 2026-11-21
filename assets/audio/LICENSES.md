# Sound files — licence table

Owning design: [`docs/designs/audio/DESIGN_FPS_Audio.md`](../../../../../docs/designs/audio/DESIGN_FPS_Audio.md) (AU-8, CE-1120).

Every sound type plays a **placeholder generated in code** (`SoundSynthesizer`) until its TKB `SoundDto.Files` names a file in this
folder. A file put here is copied beside the executable (`assets/audio/`) and replaces the placeholder of every sound type that
names it. To hear the placeholders: `HROT_EXPORT_SOUNDS=<folder> dotnet test Hrot/Engine/Hrot.Presentation.Tests --filter CE1120_Export`.

| rule | |
|---|---|
| ✅ allowed | **CC0** (Freesound filtered to CC0, Kenney, OpenGameArt CC0) |
| ⚠ allowed only with the attribution column filled | CC-BY |
| ⛔ not in the repo | Sonniss GDC bundles, BBC Sound Effects, anything "free for personal use", anything without a stated licence |
| format | OGG (or WAV), mono, any sample rate raylib reads |

| file | sound type(s) | source (URL) | licence | author / attribution |
|---|---|---|---|---|
| *(none yet — every sound is a generated placeholder)* | | | | |
