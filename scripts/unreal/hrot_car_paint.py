"""hrot_car_paint.py — CE-4651 (U-18i): a PAINTABLE body material for each City Sample car kit, run as an editor COMMANDLET.

    UnrealEditor-Cmd <project> -run=pythonscript -script=<this> -nullrhi      [HROT_CAR_PAINT_OUT = <result json>]

⚠ measured: the City Sample car paint (M_Veh_CarPaint) takes its body colour from a palette texture ("Paint Var Tex" / "Paint Var
Lookup", Mass Traffic's) while its static switch "Paint Variation" is on — setting "BaseColor" on a dynamic instance did nothing, and a
static switch cannot change at run time. ⇒ for every kit whose part has the paint slot (UHrotVehicleKit.PaintSlot): a material instance,
child of that slot's own material (its normal and grime textures kept), with "Paint Variation" and "Albedo Variation" OFF, saved as
/Game/Hrot/Vehicles/Paint/MI_HrotPaint_<kit> and set as the kit's PaintMaterial (AHrotVehicle.SetPaint makes its dynamic instance of it).
"""
import json, os, traceback
import unreal

KITS = ["DA_City_vehicle01_Van", "DA_City_vehicle02_Car", "DA_City_vehicle03_Car", "DA_City_vehicle04_Truck", "DA_City_vehicle05_Car",
        "DA_City_vehicle06_Car", "DA_City_vehicle10_Bus"]
OUT = os.environ.get("HROT_CAR_PAINT_OUT") or os.path.join(os.environ.get("TEMP", "."), "hrot_car_paint.json")
R = {"kits": {}}
EAL, MEL = unreal.EditorAssetLibrary, unreal.MaterialEditingLibrary
try:
    tools = unreal.AssetToolsHelpers.get_asset_tools()
    for n in KITS:
        kit = unreal.load_asset(f"/Game/Hrot/Vehicles/{n}")
        slot = str(kit.get_editor_property("paint_slot"))
        src = None
        for p in kit.get_editor_property("parts"):
            m = p.get_editor_property("mesh")
            if not m: continue
            for sm in m.get_editor_property("static_materials"):
                if str(sm.get_editor_property("material_slot_name")) == slot: src = sm.get_editor_property("material_interface"); break
            if src: break
        if not src: R["kits"][n] = "no paint slot"; continue
        path = f"/Game/Hrot/Vehicles/Paint/MI_HrotPaint_{n[3:]}"
        mi = unreal.load_asset(path) if EAL.does_asset_exist(path) else \
            tools.create_asset(path.rsplit("/", 1)[1], path.rsplit("/", 1)[0], unreal.MaterialInstanceConstant, unreal.MaterialInstanceConstantFactoryNew())
        MEL.set_material_instance_parent(mi, src)
        for sw in ("Paint Variation", "Albedo Variation"):
            MEL.set_material_instance_static_switch_parameter_value(mi, sw, False)
        MEL.update_material_instance(mi)
        EAL.save_loaded_asset(mi, False)
        kit.set_editor_property("paint_material", mi)
        EAL.save_loaded_asset(kit, False)
        R["kits"][n] = {"material": path, "parent": src.get_path_name()}
    R["ok"] = True
except Exception:
    R["ok"] = False; R["why"] = traceback.format_exc()
json.dump(R, open(OUT, "w", encoding="utf-8"), indent=1)
