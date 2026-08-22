<!--STATUS
state: LIVE
build-state: BUILT
updated: 2026-08-22
current-answer: this whole file — how to run the GUI editor on a headless Linux host under Xvfb, and the
  measured status of the "null IconAtlas handle" blocker.
stale-below: nothing.
known-rot: none.
-->
# Running the editor on a headless Linux host (Xvfb)

The full GUI editor (`--mode editor`) runs on a headless Linux machine under **Xvfb**. The `--headless`
flag is a *different* thing — it runs the cluster with **no UI at all**; this doc is about running the real
windowed editor without a physical display.

## The command

```bash
# from the built output dir (…/Hrot.ClusterRunner/bin/Debug/net8.0)
xvfb-run -a -s "-screen 0 1600x1000x24" dotnet Hrot.ClusterRunner.dll --mode editor
```

On a host with **no GPU / no GL driver under Xvfb**, force Mesa's software rasteriser:

```bash
LIBGL_ALWAYS_SOFTWARE=1 GALLIUM_DRIVER=llvmpipe \
  xvfb-run -a -s "-screen 0 1600x1000x24" dotnet Hrot.ClusterRunner.dll --mode editor
```

## Measured status of the "null IconAtlas handle" blocker — 2026-08-22

On a standard **Xvfb + Mesa** environment the editor starts cleanly: GLFW/X11 initialises, the icon-atlas
texture uploads (`TEXTURE: [ID 4] loaded successfully (512x512)`), fonts bake, and it reaches
`Perspective changed: Default -> Editor` and runs the render loop. The reported `null IconAtlas handle`
crash **did not reproduce** here on either the default GL path or the forced-software path.

That failure mode is specific to an Xvfb with **no working GL driver**, where `LoadTextureFromImage`
returns texture id `0`. `RaylibPresentationShell.LoadIconAtlas` now guards that case: it logs a clear
warning naming the software-GL launch line and returns a usable zero-handle atlas (raylib's id 0 is the
built-in 1×1 white texture, so icons draw blank rather than crashing) with the decoded image's dimensions
so the UV math has a valid divisor. The normal path (id != 0) is unchanged.

⇒ **If the editor still crashes at icon-atlas load on a specific host, capture the stack trace** — it is
not reproducible on an Xvfb+Mesa box, so the fix depends on what that host's GL stack actually returns.

## Notes

- **Clean exit vs. `timeout`.** Killing the process mid-frame (e.g. via `timeout`/SIGTERM) can print
  `free(): corrupted unsorted chunks` during the forced teardown — a shutdown artifact of being killed
  inside a render frame, not a startup fault. Quit through the app for a clean exit.
- The presentation frame rails already run under Xvfb on Linux (`R-124`); this is the same environment,
  extended to the full editor.
