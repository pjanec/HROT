"""hrot_shotfx_assets.py — runs in the Unreal editor COMMANDLET (CE-4651, U-16): the two materials of UHrotShotRenderer.

    UnrealEditor-Cmd <project> -run=pythonscript -script=<this> -nullrhi      [HROT_SHOTFX_FLIPBOOK="cols,rows"]

/Game/Hrot/Fx/M_HrotFlash   additive, unlit: Lyra's muzzle-flash flipbook (T_MuzzleFlash_Directional_01_MSK, 4 × 2 frames along U × V),
                            the frame chosen by the instance's AGE (per-instance custom data 0, 0..1), the mask by its CARD (data 1:
                            0 = the front card, blue; 1 = a side card, green), faded out over the flash's life
/Game/Hrot/Fx/M_HrotTracer  additive, unlit: a hot core fading to the edges, along a short streak (data 0 = intensity)
Instanced (one draw call per material for every shot of the world): the renderer writes the custom data, no per-shot material state.
"""
import os
import unreal

MEL = unreal.MaterialEditingLibrary
AT = unreal.AssetToolsHelpers.get_asset_tools()
EAL = unreal.EditorAssetLibrary
COLS, ROWS = (int(v) for v in os.environ.get("HROT_SHOTFX_FLIPBOOK", "4,2").split(","))


def fresh(name):
    p = "/Game/Hrot/Fx/" + name
    if EAL.does_asset_exist(p): EAL.delete_asset(p)
    m = AT.create_asset(name, "/Game/Hrot/Fx", unreal.Material, unreal.MaterialFactoryNew())
    m.set_editor_property("shading_model", unreal.MaterialShadingModel.MSM_UNLIT)
    m.set_editor_property("blend_mode", unreal.BlendMode.BLEND_ADDITIVE)
    m.set_editor_property("two_sided", True)
    m.set_editor_property("used_with_instanced_static_meshes", True)
    return m


def builder(m):
    E = lambda cls, x, y: MEL.create_material_expression(m, cls, x, y)
    def c1(v, x, y):
        e = E(unreal.MaterialExpressionConstant, x, y); e.set_editor_property("r", v); return e
    def c3(rgb, x, y):
        e = E(unreal.MaterialExpressionConstant3Vector, x, y); e.set_editor_property("constant", unreal.LinearColor(*rgb, 1.0)); return e
    def link(a, b, pin, out=""): MEL.connect_material_expressions(a, out, b, pin)
    def op(cls, a, b, x, y, out_a="", out_b=""):
        # ⚠ measured: Power's inputs are "Base" / "Exp" (A / B left its base unconnected — the material fell back to the default)
        pa, pb = ("Base", "Exp") if cls is unreal.MaterialExpressionPower else ("A", "B")
        e = E(cls, x, y); link(a, e, pa, out_a); link(b, e, pb, out_b); return e
    def one(cls, a, x, y, out=""):
        e = E(cls, x, y); link(a, e, "", out); return e
    def data(i, x, y):
        e = E(unreal.MaterialExpressionPerInstanceCustomData, x, y); e.set_editor_property("data_index", i); return e
    return E, c1, c3, link, op, one, data


def flash():
    m = fresh("M_HrotFlash")
    E, c1, c3, link, op, one, data = builder(m)
    age, card = data(0, -1800, 0), data(1, -1800, 300)
    n = COLS * ROWS
    # frame = min(floor(age * n), n − 1); col = frame mod COLS; row = floor(frame / COLS); uv = (texcoord + (col, row)) / (COLS, ROWS)
    frame = op(unreal.MaterialExpressionMin, one(unreal.MaterialExpressionFloor, op(unreal.MaterialExpressionMultiply, age, c1(n, -1800, 100), -1600, 0), -1450, 0),
               c1(n - 1, -1450, 100), -1300, 0)
    col = op(unreal.MaterialExpressionFmod, frame, c1(COLS, -1300, 100), -1150, 0)
    row = one(unreal.MaterialExpressionFloor, op(unreal.MaterialExpressionDivide, frame, c1(COLS, -1150, 200), -1000, 200), -850, 200)
    cr = op(unreal.MaterialExpressionAppendVector, col, row, -700, 100)
    size = E(unreal.MaterialExpressionConstant2Vector, -550, 150)
    size.set_editor_property("r", float(COLS)); size.set_editor_property("g", float(ROWS))
    uv = op(unreal.MaterialExpressionDivide, op(unreal.MaterialExpressionAdd, E(unreal.MaterialExpressionTextureCoordinate, -850, -100), cr, -550, 0),
            size, -400, 0)
    t = E(unreal.MaterialExpressionTextureSample, -250, 0)
    t.set_editor_property("texture", unreal.load_asset("/Game/Effects/Textures/MuzzleFlash/T_MuzzleFlash_Directional_01_MSK"))
    link(uv, t, "UVs")
    mask = E(unreal.MaterialExpressionLinearInterpolate, 0, 0)
    # measured (the sheet rendered by channel): B = the round flash seen from the front, R / G = the flame tongues from the side,
    # A = solid white — so the front card reads B, a side card G
    link(t, mask, "A", "B"); link(t, mask, "B", "G"); link(card, mask, "Alpha")
    fade = op(unreal.MaterialExpressionPower, op(unreal.MaterialExpressionSubtract, c1(1.0, 0, 250), age, 150, 250), c1(1.5, 150, 350), 300, 250)
    glow = op(unreal.MaterialExpressionMultiply, op(unreal.MaterialExpressionMultiply, mask, fade, 300, 0), c1(60.0, 300, 150), 450, 0)
    em = op(unreal.MaterialExpressionMultiply, glow, c3((1.0, 0.55, 0.22), 450, 150), 600, 0)
    MEL.connect_material_property(em, "", unreal.MaterialProperty.MP_EMISSIVE_COLOR)
    return m


def tracer():
    m = fresh("M_HrotTracer")
    E, c1, c3, link, op, one, data = builder(m)
    tc = E(unreal.MaterialExpressionTextureCoordinate, -1000, 0)
    u = one(unreal.MaterialExpressionComponentMask, tc, -850, -100); u.set_editor_property("r", True); u.set_editor_property("g", False)
    v = one(unreal.MaterialExpressionComponentMask, tc, -850, 100); v.set_editor_property("r", False); v.set_editor_property("g", True)
    # across: 1 at the centre line, 0 at the edges — sharpened; along: soft ends
    across = op(unreal.MaterialExpressionPower,
                op(unreal.MaterialExpressionSubtract, c1(1.0, -700, 200),
                   one(unreal.MaterialExpressionAbs, op(unreal.MaterialExpressionSubtract, op(unreal.MaterialExpressionMultiply, v, c1(2.0, -700, 300), -550, 150), c1(1.0, -550, 300), -400, 150), -250, 150),
                   -100, 150),
                c1(3.0, -100, 300), 50, 150)
    along = op(unreal.MaterialExpressionPower, one(unreal.MaterialExpressionSine, op(unreal.MaterialExpressionMultiply, u, c1(3.14159, -700, -200), -550, -100), -400, -100),
               c1(0.5, -400, 0), -250, -100)
    glow = op(unreal.MaterialExpressionMultiply, op(unreal.MaterialExpressionMultiply, across, along, 200, 0), data(0, 200, 150), 350, 0)
    em = op(unreal.MaterialExpressionMultiply, glow, c3((40.0, 28.0, 14.0), 350, 150), 500, 0)
    MEL.connect_material_property(em, "", unreal.MaterialProperty.MP_EMISSIVE_COLOR)
    return m


def scorch():
    """/Game/Hrot/Fx/M_HrotScorch — a deferred decal: soot, darkest at the centre, a soft ragged edge (the blast's mark on the ground).
    ⚠ Lyra's M_ExplosionDecal is made for Niagara's decal renderer (colour from particles): as a plain decal it painted a flat tan box."""
    m = fresh("M_HrotScorch")
    m.set_editor_property("material_domain", unreal.MaterialDomain.MD_DEFERRED_DECAL)
    m.set_editor_property("blend_mode", unreal.BlendMode.BLEND_TRANSLUCENT)
    m.set_editor_property("shading_model", unreal.MaterialShadingModel.MSM_DEFAULT_LIT)
    E, c1, c3, link, op, one, data = builder(m)
    tc = E(unreal.MaterialExpressionTextureCoordinate, -1000, 0)
    centred = op(unreal.MaterialExpressionMultiply, op(unreal.MaterialExpressionSubtract, tc, c1(0.5, -900, 100), -800, 0), c1(2.0, -800, 100), -650, 0)
    r = one(unreal.MaterialExpressionLength, centred, -500, 0)
    noise = E(unreal.MaterialExpressionNoise, -650, 250)
    ragged = op(unreal.MaterialExpressionAdd, r, op(unreal.MaterialExpressionMultiply, noise, c1(0.25, -500, 350), -350, 250), -200, 100)
    soot = op(unreal.MaterialExpressionPower, one(unreal.MaterialExpressionSaturate, op(unreal.MaterialExpressionSubtract, c1(1.0, -200, 250), ragged, -50, 150), 100, 150),
              c1(1.5, 100, 300), 250, 150)
    MEL.connect_material_property(op(unreal.MaterialExpressionMultiply, soot, c1(0.9, 250, 300), 400, 150), "", unreal.MaterialProperty.MP_OPACITY)
    MEL.connect_material_property(c3((0.015, 0.013, 0.011), 400, 0), "", unreal.MaterialProperty.MP_BASE_COLOR)
    MEL.connect_material_property(c1(0.95, 400, 400), "", unreal.MaterialProperty.MP_ROUGHNESS)
    return m


for mk in (flash, tracer, scorch):
    m = mk()
    MEL.recompile_material(m)
    stats = MEL.get_statistics(m) if hasattr(MEL, "get_statistics") else None
    if stats is not None: unreal.log_warning(f"[HROT-SHOTFX] {m.get_name()}: {stats.num_pixel_shader_instructions} pixel instructions")
    EAL.save_loaded_asset(m, False)
    unreal.log_warning(f"[HROT-SHOTFX] built {m.get_path_name()} (flipbook {COLS}x{ROWS})")
