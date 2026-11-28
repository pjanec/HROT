"""hrot_dps_render7.py - CE-4651 (U-18q): DPS ROUND 7 §2, the RENDER CLIENT, end to end over real DDS against the plugin's FAKE STATION.
Run in the full editor started as a render client:  -HrotDpsRender -HrotDpsRenderRoot=<dir> -deterministicaudio  (PLAY, a private domain).

    HROT_DPS_R7R_JOB = { "domain", "partition", "out", "frames", "shots": [frame indices with a rifle shot], "silence": [title-card frames],
                         "hold": a frame whose world sample is sent 0.6 s AFTER its step and request (the D-B4 barrier must hold it) }

Lockstep as HROT's runner drives it: per frame the step's world samples (the unit's EntityState, a Fire on a shot frame), then ClockStep
(AckRequired, Samples = that count), then FrameRequest; the next frame only after its FrameDone. The runner script checks the PNGs and
the soundtrack (the onsets of the shots, the silence) afterwards.
"""
import json, os, time, traceback
import unreal

JOB = json.load(open(os.environ["HROT_DPS_R7R_JOB"], encoding="utf-8-sig"))
R = {"log": [], "checks": [], "frames": []}
S = {"handle": None, "phase": "level", "busy": False}
LES = lambda: unreal.get_editor_subsystem(unreal.LevelEditorSubsystem)
UES = lambda: unreal.get_editor_subsystem(unreal.UnrealEditorSubsystem)
EAS = lambda: unreal.get_editor_subsystem(unreal.EditorActorSubsystem)
T0 = time.time()
FPS, SIM0, UNIT = 30, 10.0, 77
CAM = unreal.Vector(-18, -22, 6)   # m, ENU: south-west of the unit, looking north-east at it


def log(m):
    m = f"{time.time() - T0:7.1f}s {m}"; unreal.log_warning(f"[HROT-R7R] {m}"); R["log"].append(m)


def check(name, ok, detail):
    R["checks"].append({"name": name, "ok": bool(ok), "detail": detail}); log(f"{'PASS' if ok else 'FAIL'} {name}: {detail}")


def finish(ok, why=""):
    R["ok"] = ok and all(c["ok"] for c in R["checks"]); R["why"] = why
    json.dump(R, open(JOB["out"], "w", encoding="utf-8"), indent=1)
    if S["handle"] is not None: unreal.unregister_slate_post_tick_callback(S["handle"]); S["handle"] = None
    unreal.SystemLibrary.quit_editor()


def level():
    # ONE scratch level: the runner deletes its file before Unreal starts (new_level SAVES a level, and could not replace one deleted in the same session)
    if not LES().new_level("/Game/Maps/DpsRender7"): raise RuntimeError("new_level failed")
    floor = EAS().spawn_actor_from_class(unreal.StaticMeshActor, unreal.Vector(0, 0, 0))
    floor.static_mesh_component.set_static_mesh(unreal.load_asset("/Engine/BasicShapes/Plane")); floor.set_actor_scale3d(unreal.Vector(4000, 4000, 1))
    sun = EAS().spawn_actor_from_class(unreal.DirectionalLight, unreal.Vector(0, 0, 1000), unreal.Rotator(pitch=-40, yaw=60, roll=0))
    sun.light_component.set_editor_property("atmosphere_sun_light", True); sun.light_component.set_editor_property("intensity", 8.0)
    EAS().spawn_actor_from_class(unreal.SkyAtmosphere, unreal.Vector(0, 0, 0))
    sky = EAS().spawn_actor_from_class(unreal.SkyLight, unreal.Vector(0, 0, 500)); sky.light_component.set_editor_property("real_time_capture", True)
    for c in ("t.IdleWhenNotForeground 0", "r.VSync 0"): unreal.SystemLibrary.execute_console_command(None, c)
    unreal.EditorLevelLibrary.get_editor_world().get_world_settings().set_editor_property("default_game_mode", unreal.HrotDpsGameMode.static_class())
    LES().editor_set_viewport_realtime(True)
    LES().editor_request_begin_play()
    S.update(phase="join", t=time.time() + 8)


def join():
    gw = UES().get_game_world()
    if gw is None: raise RuntimeError("PIE did not start")
    w = unreal.HrotDpsWorld.for_world(gw)
    w.set_editor_property("kits_by_domain", {"Ground": unreal.load_asset("/Game/Hrot/Vehicles/DA_ArmoredTruck")})
    if not w.start(int(JOB["domain"]), JOB["partition"]): raise RuntimeError("DPS start failed")
    sub = unreal.get_engine_subsystem(unreal.HrotDpsSubsystem)
    rc = unreal.HrotDpsRender.for_world(gw)
    unreal.HrotShotRenderer.for_world(gw).set_editor_property("log_sounds", True)
    if JOB.get("audioDiag"): unreal.SystemLibrary.execute_console_command(gw, f"hrot.render.AudioDiag {JOB['audioDiag']}")   # every sound as it starts (when, how far, how late)
    S.update(w=w, sub=sub, rc=rc, gw=gw, phase="setup", t=time.time() + 1.0)
    log(f"joined; render client {rc.is_enabled()} root {rc.get_render_root()}")


def unit_pos(sim): return unreal.Vector(2.0 * (sim - SIM0), 0, 0)   # 2 m/s east


def setup():
    sub = S["sub"]
    sub.fake_station_start(0.0)
    sub.fake_station_session(1, SIM0)
    sub.fake_station_entity(UNIT, SIM0, unit_pos(SIM0), unreal.Vector(2, 0, 0), 0.0)
    sub.fake_station_clock(True, SIM0, 1.0, 1, 0)   # paused: the director steps it
    S.update(phase="client", t=time.time() + 3.0)


def client():
    rc = S["sub"].fake_station_render_client()
    if not rc and time.time() < S["t"] + 10: return   # announced every 2 s on a VOLATILE topic: wait for the next one (a loading engine hitches)
    check("RenderClient announced (PNG, WAV PCM16 with the non-real-time mixer)", "ready 1" in rc and "formats 1" in rc and "audio 1" in rc, rc)
    # first: a job beyond the limits is refused before frame 0, with the limit named
    S["sub"].fake_station_render_job("big", 1, FPS, 8000, 4000, 1, "big", False, 0)
    S.update(phase="big", t=time.time() + 1.5)


def big():
    res = S["sub"].fake_station_job_result()
    check("a job beyond the limits fails before frame 0 with the limit named", res.startswith("big Failed") and "8000x4000" in res, res)
    S["sub"].fake_station_render_job("big", 5, FPS, 8000, 4000, 1, "big", False, 0)   # cancelled by the station
    S["sub"].fake_station_render_job("r1", 1, FPS, 640, 360, 1, "r1", True, 48000)
    S.update(phase="ready", t=time.time() + 60, i=0)


def ready():
    rc = S["sub"].fake_station_render_client()
    if "readyJob 'r1'" in rc:
        check("ReadyJobId after the job's warm-up", True, rc); S.update(phase="frame", t0=time.time()); return
    if time.time() > S["t"]: raise RuntimeError("no ReadyJobId within 60 s: " + rc + " | " + S["rc"].describe())


def send_frame(i):
    sub = S["sub"]; sim = SIM0 + (i + 1) / FPS; n = 0
    if i != JOB["hold"]: sub.fake_station_entity(UNIT, sim, unit_pos(sim), unreal.Vector(2, 0, 0), 0.0); n += 2   # Entity + EntityState: 2 INSTANCES
    else: n += 2
    if i == JOB.get("twice"): sub.fake_station_entity(UNIT, sim, unit_pos(sim), unreal.Vector(2, 0, 0), 0.0)        # a second write of both: still 2 instances (the gateway's rule)
    if i in JOB["shots"]:   # from the unit, toward the camera's side: heard ~29 m away
        sub.fake_station_fire(sim, unit_pos(sim) + unreal.Vector(0, 0, 1.5), unreal.Vector(-500, -600, 0)); n += 1
    sub.fake_station_render_step(i + 1, sim, 1, n)
    kind = 2 if i in JOB["silence"] else 1
    sub.fake_station_frame("r1", i, kind, i + 1, sim, CAM, 50.0, -8.0, 45.0, 640, 360, f"r1/f{i:04d}.png", "DPS round 7 - the render client",
                           unit_pos(sim), 4.0 if kind == 1 else 0.0)
    S.update(sent=time.time(), sim=sim)


def frame():
    i = S["i"]; sub = S["sub"]
    if i >= JOB["frames"]:
        sub.fake_station_render_job("r1", 3, FPS, 640, 360, 1, "r1", True, 48000)   # Done
        S.update(phase="result", t=time.time() + 20); return
    if "sent" not in S: send_frame(i); return
    done = sub.fake_station_frames_done()
    if i == JOB["hold"] and not S.get("held"):
        if time.time() - S["sent"] >= 0.6:
            check("D-B4: a step whose world sample has not arrived is NOT presented", done == i, f"frames done {done} after 0.6 s (want {i}) | {S['rc'].describe()}")
            sim = S["sim"]; sub.fake_station_entity(UNIT, sim, unit_pos(sim), unreal.Vector(2, 0, 0), 0.0); S["held"] = True
        return
    if done >= i + 1:
        R["frames"].append(sub.fake_station_frame_done()); S["i"] = i + 1; S.pop("sent"); return
    if time.time() - S["sent"] > 30: raise RuntimeError(f"frame {i}: no FrameDone in 30 s | {S['rc'].describe()}")


def result():
    res = S["sub"].fake_station_job_result()
    if res.startswith("r1"):
        el = time.time() - S["t0"]
        check("RenderJobResult after Done: the track written", "Written" in res and "r1/r1.wav" in res, res)
        check("every frame acknowledged in lockstep (FrameDone + StepAck)", S["sub"].fake_station_frames_done() == JOB["frames"] and S["sub"].fake_station_step_acks() == JOB["frames"],
              f"{S['sub'].fake_station_frames_done()} frames done, {S['sub'].fake_station_step_acks()} step acks, {JOB['frames']} frames in {el:.1f} s | last {S['sub'].fake_station_frame_done()}")
        d = S["rc"].describe()
        check("the barrier counted each step exactly (instances per state topic, samples per event; a doubled write counted once)", " over 0 " in d and "timeouts 0" in d, d)
        R["result"] = res; R["describe"] = d; R["root"] = S["rc"].get_render_root()
        sr = unreal.HrotShotRenderer.for_world(S["gw"]); R["events"] = dict(S["w"].event_counts()); R["soundsPlayed"] = sr.get_editor_property("sounds_played")
        R["soundscape"] = unreal.HrotSoundscape.for_world(S["gw"]).describe(); log(f"events {R['events']} sounds played {R['soundsPlayed']} | {R['soundscape']}")
        LES().editor_request_end_play(); S.update(phase="end", t=time.time() + 3); return
    if time.time() > S["t"]: raise RuntimeError("no RenderJobResult in 20 s | " + S["rc"].describe())


def tick(dt):
    if S["busy"]: return
    S["busy"] = True
    try:
        p = S["phase"]
        if p == "level": level()
        elif p == "join" and time.time() >= S["t"]: join()
        elif p == "setup" and time.time() >= S["t"]: setup()
        elif p == "client" and time.time() >= S["t"] - 2.5: client()
        elif p == "big" and time.time() >= S["t"]: big()
        elif p == "ready": ready()
        elif p == "frame": frame()
        elif p == "result": result()
        elif p == "end" and time.time() >= S["t"]: finish(True)
    except Exception:
        finish(False, traceback.format_exc())
    finally:
        S["busy"] = False


S["handle"] = unreal.register_slate_post_tick_callback(tick)
unreal.EditorPythonScripting.set_keep_python_script_alive(True)
