# CE-4651 (U-18z) - the gizmo-line material: /Game/Hrot/Fx/Labels/M_HrotGizmoLine (beside M_HrotCard, under /Game/Hrot/Fx which the client always cooks).
#   UnrealEditor-Cmd <project> -ExecutePythonScript=scripts/unreal/hrot_gizmo_line_material.py -unattended -nullrhi
# A world gizmo line is a camera-facing ribbon quad (HrotDpsGizmos.cpp, a ProceduralMeshComponent): vertex colour = the gizmo colour (linear),
# UV0.x = the line's width in pixels, UV0.y = -1..+1 across the ribbon. The ribbon is (px / 2 + 1 px) wide on each side, so the solid core ends
# at |v| = px / (px + 2) and the last pixel is a soft edge: opacity = alpha * (1 - smoothstep(core, 1, |v|)) - an analytic 1 px anti-aliased edge.
#   Unlit, translucent, after DOF (as M_HrotCard), two-sided, no fog, depth-tested (the default), responsive AA when the engine has it.
import os, stat
import unreal

AT = unreal.AssetToolsHelpers.get_asset_tools()
MEL = unreal.MaterialEditingLibrary
EAL = unreal.EditorAssetLibrary
# variants (compared in the scene, UHrotDpsWorld.GizmoLineMaterial): "" = after DOF + responsive AA (before TSR upscale), "NoRaa" = without responsive AA,
# "Post" = translucency AFTER MOTION BLUR, i.e. after TSR, at display resolution (the temporal upscaler cannot smear it)
VARIANTS = [("M_HrotGizmoLine", unreal.MaterialTranslucencyPass.MTP_AFTER_DOF, True),
            ("M_HrotGizmoLineNoRaa", unreal.MaterialTranslucencyPass.MTP_AFTER_DOF, False),
            ("M_HrotGizmoLinePost", unreal.MaterialTranslucencyPass.MTP_AFTER_MOTION_BLUR, True)]


def prop(o, name, value):
    try:
        o.set_editor_property(name, value); return True
    except Exception as e:
        unreal.log_warning(f"[HROT-GIZ] {name}: {e}"); return False


def make(name, passe, raa):
    PATH = "/Game/Hrot/Fx/Labels/" + name
    if EAL.does_asset_exist(PATH):
        f = os.path.join(unreal.Paths.project_content_dir(), "Hrot/Fx/Labels", name + ".uasset")
        if os.path.exists(f): os.chmod(f, stat.S_IWRITE | stat.S_IREAD)   # (a committed .uasset is read-only here: the delete would fail)
        EAL.delete_asset(PATH)
    m = AT.create_asset(name, "/Game/Hrot/Fx/Labels", unreal.Material, unreal.MaterialFactoryNew())
    prop(m, "blend_mode", unreal.BlendMode.BLEND_TRANSLUCENT)
    prop(m, "shading_model", unreal.MaterialShadingModel.MSM_UNLIT)
    prop(m, "two_sided", True)
    prop(m, "translucency_pass", passe)
    prop(m, "use_translucency_vertex_fog", False)
    prop(m, "compute_fog_per_pixel", False)
    prop(m, "enable_responsive_aa", raa)
    E = lambda cls, x, y: MEL.create_material_expression(m, cls, x, y)
    col = E(unreal.MaterialExpressionVertexColor, -900, 0)
    eye = E(unreal.MaterialExpressionEyeAdaptationInverse, -700, -200)
    emi = E(unreal.MaterialExpressionMultiply, -450, -100)
    ok_rgb = MEL.connect_material_expressions(col, "", emi, "A"); unreal.log_warning(f"[HROT-GIZ] vertex colour connected: {ok_rgb}")      # (a linear colour: no exposure drift)
    MEL.connect_material_expressions(eye, "", emi, "B")
    MEL.connect_material_property(emi, "", unreal.MaterialProperty.MP_EMISSIVE_COLOR)

    uv = E(unreal.MaterialExpressionTextureCoordinate, -1100, 300)
    px = E(unreal.MaterialExpressionComponentMask, -900, 250); prop(px, "r", True); prop(px, "g", False); prop(px, "b", False); prop(px, "a", False)
    vv = E(unreal.MaterialExpressionComponentMask, -900, 400); prop(vv, "r", False); prop(vv, "g", True); prop(vv, "b", False); prop(vv, "a", False)
    MEL.connect_material_expressions(uv, "", px, "")
    MEL.connect_material_expressions(uv, "", vv, "")
    absv = E(unreal.MaterialExpressionAbs, -700, 400); MEL.connect_material_expressions(vv, "", absv, "")
    two = E(unreal.MaterialExpressionConstant, -900, 150); prop(two, "r", 2.0)
    den = E(unreal.MaterialExpressionAdd, -700, 200); MEL.connect_material_expressions(px, "", den, "A"); MEL.connect_material_expressions(two, "", den, "B")
    core = E(unreal.MaterialExpressionDivide, -500, 250); MEL.connect_material_expressions(px, "", core, "A"); MEL.connect_material_expressions(den, "", core, "B")
    one = E(unreal.MaterialExpressionConstant, -500, 450); prop(one, "r", 1.0)
    ss = E(unreal.MaterialExpressionSmoothStep, -300, 350)
    MEL.connect_material_expressions(core, "", ss, "Min"); MEL.connect_material_expressions(one, "", ss, "Max"); MEL.connect_material_expressions(absv, "", ss, "Value")
    inv = E(unreal.MaterialExpressionOneMinus, -150, 350); MEL.connect_material_expressions(ss, "", inv, "")
    op = E(unreal.MaterialExpressionMultiply, 0, 250)
    MEL.connect_material_expressions(col, "A", op, "A"); MEL.connect_material_expressions(inv, "", op, "B")
    MEL.connect_material_property(op, "", unreal.MaterialProperty.MP_OPACITY)
    MEL.recompile_material(m)
    EAL.save_asset(PATH)
    unreal.log_warning(f"[HROT-GIZ] {PATH} written")


for v in VARIANTS:
    make(*v)
