"""hrot_dps_scene.py — CE-4651 (U-18b): the DPS WORLD proof, run in the full Unreal EDITOR (-ExecutePythonScript).

    HROT_DPS_SCENE_JOB = { "domain", "partition", "seconds", "ready", "out", "shots", "pictures": [{"name", "at", "look", "back", "up"}] }

A flat level played in Simulate; UHrotDpsWorld joins the station; the test's publisher (the stand-in for the gateway: same protocol
assembly, the gateway's send rule) starts when the ready file appears. Records, every 50 ms, each entity's SHOWN position (contract
frame) against the sim clock — the runner compares it with the publisher's truth — and renders the pictures at their times (real seconds
after ready). The kit table here is the TEST's choice of models, not a product mapping.
"""
import json, os, time, traceback
import unreal

JOB = json.load(open(os.environ["HROT_DPS_SCENE_JOB"], encoding="utf-8-sig"))
R = {"log": [], "shots": [], "track": [], "describe": {}}
S = {"handle": None, "phase": "level", "busy": False, "pending": None}
LES = lambda: unreal.get_editor_subsystem(unreal.LevelEditorSubsystem)
UES = lambda: unreal.get_editor_subsystem(unreal.UnrealEditorSubsystem)
EAS = lambda: unreal.get_editor_subsystem(unreal.EditorActorSubsystem)
T0 = time.time()
KITS = "/Game/Hrot/Vehicles/"


def log(m):
    m = f"{time.time() - T0:7.1f}s {m}"; unreal.log_warning(f"[HROT-DPS] {m}"); R["log"].append(m)


def finish(ok, why=""):
    R["ok"] = ok; R["why"] = why
    json.dump(R, open(JOB["out"], "w", encoding="utf-8"), indent=1)
    if S["handle"] is not None: unreal.unregister_slate_post_tick_callback(S["handle"]); S["handle"] = None
    unreal.SystemLibrary.quit_editor()


def spawn(cls, loc, rot=(0, 0, 0), label=None):
    a = EAS().spawn_actor_from_class(cls, unreal.Vector(*loc), unreal.Rotator(pitch=rot[0], yaw=rot[1], roll=rot[2]))
    if a and label: a.set_actor_label(label)
    return a


def level():
    if unreal.EditorAssetLibrary.does_asset_exist("/Game/Maps/DpsScene"): unreal.EditorAssetLibrary.delete_asset("/Game/Maps/DpsScene")
    if not LES().new_level("/Game/Maps/DpsScene"): raise RuntimeError("new_level failed")
    floor = spawn(unreal.StaticMeshActor, (0, 0, 0), label="Floor")
    floor.static_mesh_component.set_static_mesh(unreal.load_asset("/Engine/BasicShapes/Plane"))
    floor.set_actor_scale3d(unreal.Vector(600, 600, 1))
    sun = spawn(unreal.DirectionalLight, (0, 0, 1000), (-40, 60, 0), "Sun")
    sun.light_component.set_editor_property("atmosphere_sun_light", True); sun.light_component.set_editor_property("intensity", 8.0)
    spawn(unreal.SkyAtmosphere, (0, 0, 0), label="Sky")
    sky = spawn(unreal.SkyLight, (0, 0, 500), label="SkyLight"); sky.light_component.set_editor_property("real_time_capture", True)
    for c in ("t.IdleWhenNotForeground 0", "t.MaxFPS 0", "r.VSync 0", "r.VSyncEditor 0", "r.DontLimitOnBattery 1"):
        unreal.SystemLibrary.execute_console_command(None, c)
    LES().editor_set_viewport_realtime(True)
    LES().editor_play_simulate()
    S.update(phase="join", t=time.time() + 5)


def join():
    gw = UES().get_game_world()
    if gw is None: raise RuntimeError("Simulate did not start")
    w = unreal.HrotDpsWorld.for_world(gw)
    if w is None: raise RuntimeError("no UHrotDpsWorld in the game world")
    L = unreal.load_asset
    w.set_editor_property("kits_by_type", {2001: L(KITS + "DA_ArmoredTruck")})                       # by the contract's type id
    w.set_editor_property("kits_by_dis", {"1.1.222": L(KITS + "DA_BTR60"), "1.1.225.1": L(KITS + "DA_Sherman")})   # by DIS prefix
    w.set_editor_property("kits_by_domain", {"Air": L(KITS + "DA_Air_Apache")})                     # by domain
    w.set_editor_property("person_mesh", L("/Game/Characters/Heroes/Mannequin/Meshes/SKM_Manny"))
    w.set_editor_property("person_clip", L("/Game/Characters/Heroes/Mannequin/Animations/Locomotion/Rifle/MF_Rifle_Walk_Fwd"))
    if not w.start(int(JOB["domain"]), JOB["partition"]): raise RuntimeError("DPS start failed")
    S.update(w=w, phase="run", t0=time.time(), next_rec=0.0, pics=list(JOB.get("pictures", [])))
    open(JOB["ready"], "w").write("ready")
    log("joined; ready")


def camera(look, back, up):
    cam = unreal.Vector(look[0] + back * 0.45, look[1] + back, up)   # from the south-east (Unreal +Y = south)
    UES().set_level_viewport_camera_info(cam, unreal.MathLibrary.find_look_at_rotation(cam, unreal.Vector(*look)))


def run():
    w, t = S["w"], time.time() - S["t0"]
    if t >= S["next_rec"]:
        S["next_rec"] += 0.05
        now = w.placed_at()   # the time the shown poses belong to (a stalled frame keeps its old time: not recorded twice)
        fresh = now != S.get("last_placed"); S["last_placed"] = now
        for i in (w.entity_ids() if fresh else []):
            p = w.shown_position(i); a = w.actor_of(i)
            loc = a.get_actor_location() if a else None
            R["track"].append([round(now, 4), i, round(p.x, 4), round(p.y, 4), round(p.z, 4),
                               [round(loc.x, 1), round(loc.y, 1), round(loc.z, 1), round(a.get_actor_rotation().yaw, 2)] if a else None])
    if S["pics"] and t >= S["pics"][0]["at"]:
        pic = S["pics"].pop(0)
        if pic.get("top"):   # straight down, screen up = east (+X), screen right = south (+Y)
            UES().set_level_viewport_camera_info(unreal.Vector(pic["look"][0], pic["look"][1], pic.get("up", 5000)), unreal.Rotator(pitch=-90, yaw=0, roll=0))
        elif "look" in pic: camera(pic["look"], pic.get("back", 6000), pic.get("up", 3500))
        S["pending_pic"] = pic["name"]; S["pic_at"] = time.time() + 0.3   # the camera settles one frame
    if S.get("pending_pic") and time.time() >= S["pic_at"]:
        name = S.pop("pending_pic")
        path = os.path.join(JOB["shots"], f"dps-{name}.png").replace("\\", "/")
        if os.path.exists(path): os.remove(path)
        unreal.AutomationLibrary.take_high_res_screenshot(1600, 900, path, None)
        R["shots"].append(path)
        R["describe"][name] = {str(i): w.describe(i) for i in w.entity_ids()}
        log(f"picture {name} at sim {w.sim_now():.2f}")
    if t >= float(JOB["seconds"]):
        R["final"] = {str(i): w.describe(i) for i in w.entity_ids()}
        R["spawned"], R["removed"] = w.spawned_total(), w.removed_total()
        LES().editor_request_end_play(); S.update(phase="end", t=time.time() + 3)


def tick(dt):
    if S["busy"]: return
    S["busy"] = True
    try:
        p = S["phase"]
        if p == "level": level()
        elif p == "join" and time.time() >= S["t"]: join()
        elif p == "run": run()
        elif p == "end" and time.time() >= S["t"]: finish(True)
    except Exception:
        finish(False, traceback.format_exc())
    finally:
        S["busy"] = False


S["handle"] = unreal.register_slate_post_tick_callback(tick)
unreal.EditorPythonScripting.set_keep_python_script_alive(True)
