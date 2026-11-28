"""hrot_ig_start_level.py - CE-4651 (U-18s): the packaged IG client's START level, /Game/Maps/IgStart (run in the editor: -ExecutePythonScript).

The client opens it before the station names a terrain: a ground plane, a sun, a sky, the IG game mode (AHrotDpsGameMode: the camera pawn,
the HUD, the join from -HrotDpsDomain / -HrotDpsPartition). A terrain then arrives as its own cooked package (step 2) and replaces it.
Re-running replaces the level.
"""
import unreal

LES = unreal.get_editor_subsystem(unreal.LevelEditorSubsystem)
EAS = unreal.get_editor_subsystem(unreal.EditorActorSubsystem)
PATH = "/Game/Maps/IgStart"

if unreal.EditorAssetLibrary.does_asset_exist(PATH):
    unreal.EditorAssetLibrary.delete_asset(PATH)
if not LES.new_level(PATH):
    raise RuntimeError("new_level failed: " + PATH)
floor = EAS.spawn_actor_from_class(unreal.StaticMeshActor, unreal.Vector(0, 0, 0))
floor.set_actor_label("Ground")
floor.static_mesh_component.set_static_mesh(unreal.load_asset("/Engine/BasicShapes/Plane"))
floor.set_actor_scale3d(unreal.Vector(4000, 4000, 1))   # 4 km
sun = EAS.spawn_actor_from_class(unreal.DirectionalLight, unreal.Vector(0, 0, 1000), unreal.Rotator(pitch=-40, yaw=60, roll=0))
sun.set_actor_label("Sun")
sun.light_component.set_editor_property("atmosphere_sun_light", True)
sun.light_component.set_editor_property("intensity", 8.0)
EAS.spawn_actor_from_class(unreal.SkyAtmosphere, unreal.Vector(0, 0, 0)).set_actor_label("Sky")
sky = EAS.spawn_actor_from_class(unreal.SkyLight, unreal.Vector(0, 0, 500))
sky.set_actor_label("SkyLight")
sky.light_component.set_editor_property("real_time_capture", True)
EAS.spawn_actor_from_class(unreal.ExponentialHeightFog, unreal.Vector(0, 0, 0)).set_actor_label("Fog")
unreal.EditorLevelLibrary.get_editor_world().get_world_settings().set_editor_property("default_game_mode", unreal.HrotDpsGameMode.static_class())
if not LES.save_current_level():
    raise RuntimeError("save failed: " + PATH)
unreal.log_warning("[HROT-IG] /Game/Maps/IgStart written")
unreal.SystemLibrary.quit_editor()
