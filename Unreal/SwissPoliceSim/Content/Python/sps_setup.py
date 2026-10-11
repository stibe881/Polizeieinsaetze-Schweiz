"""
Swiss Police Simulator - one-click material setup for the Unreal client.

Run inside the Unreal Editor (Output Log > Python, or  Tools > Execute Python Script):
    py "<project>/Content/Python/sps_setup.py"

What it does
  1. imports the CC0 PBR texture sets (ambientCG) from  <repo>/Assets/SwissPoliceSim/Resources/PBR  into /Game/SPS/Textures
  2. builds the master materials  M_Sps_VertexColor (flat, vertex coloured)  and  M_Sps_Pbr (world-space triplanar PBR, tinted by vertex colour)  and  M_Sps_Terrain
  3. creates one material instance MI_<Slot> per slot the C++ client asks for (see SpsMaterials.cpp)

NOT TESTED against a real editor (none was available when this was written). Every step is wrapped so a failure is reported and the rest continues;
if the PBR master cannot be built the client still renders with M_Sps_VertexColor. Vertex colours are authored in sRGB, hence the pow(2.2) in the masters.
"""
import os
import traceback
import unreal

PROJECT_DIR = unreal.SystemLibrary.get_project_directory()
PBR_DIR = os.path.normpath(os.path.join(PROJECT_DIR, "..", "..", "Assets", "SwissPoliceSim", "Resources", "PBR"))
TEX_PATH = "/Game/SPS/Textures"
MAT_PATH = "/Game/SPS/Materials"

mel = unreal.MaterialEditingLibrary
tools = unreal.AssetToolsHelpers.get_asset_tools()


def log(msg):
    unreal.log("[SPS] " + str(msg))


def warn(msg):
    unreal.log_warning("[SPS] " + str(msg))


# ---------------------------------------------------------------- textures
def import_textures():
    if not os.path.isdir(PBR_DIR):
        warn("PBR folder not found: %s" % PBR_DIR)
        return
    tasks = []
    for f in sorted(os.listdir(PBR_DIR)):
        if not f.lower().endswith(".jpg"):
            continue
        t = unreal.AssetImportTask()
        t.set_editor_property("filename", os.path.join(PBR_DIR, f))
        t.set_editor_property("destination_path", TEX_PATH)
        t.set_editor_property("replace_existing", True)
        t.set_editor_property("automated", True)
        t.set_editor_property("save", True)
        tasks.append(t)
    tools.import_asset_tasks(tasks)
    # texture settings by suffix
    for f in sorted(os.listdir(PBR_DIR)):
        if not f.lower().endswith(".jpg"):
            continue
        name = os.path.splitext(f)[0]
        tex = unreal.EditorAssetLibrary.load_asset("%s/%s" % (TEX_PATH, name))
        if not tex:
            continue
        try:
            if name.endswith("_Normal"):
                tex.set_editor_property("compression_settings", unreal.TextureCompressionSettings.TC_NORMALMAP)
                tex.set_editor_property("srgb", False)
                tex.set_editor_property("flip_green_channel", True)   # ambientCG ships OpenGL normals (green up); Unreal expects DirectX
            elif name.endswith("_Rough") or name.endswith("_AO"):
                tex.set_editor_property("compression_settings", unreal.TextureCompressionSettings.TC_MASKS)
                tex.set_editor_property("srgb", False)
            else:
                tex.set_editor_property("srgb", True)
            unreal.EditorAssetLibrary.save_loaded_asset(tex)
        except Exception as ex:   # noqa
            warn("texture settings for %s: %s" % (name, ex))


# ---------------------------------------------------------------- material graph helpers
def new_material(name):
    path = "%s/%s" % (MAT_PATH, name)
    if unreal.EditorAssetLibrary.does_asset_exist(path):
        unreal.EditorAssetLibrary.delete_asset(path)
    return tools.create_asset(name, MAT_PATH, unreal.Material, unreal.MaterialFactoryNew())


def node(mat, cls, x, y, **props):
    e = mel.create_material_expression(mat, cls, x, y)
    for k, v in props.items():
        e.set_editor_property(k, v)
    return e


def link(a, b, b_in, a_out=""):
    """Connect a -> b.<b_in>, trying the common pin spellings."""
    candidates = [b_in] if isinstance(b_in, str) else list(b_in)
    for cand in candidates:
        try:
            if mel.connect_material_expressions(a, a_out, b, cand):
                return True
        except Exception:   # noqa
            pass
    warn("could not connect %s -> %s (%s)" % (a.get_class().get_name(), b.get_class().get_name(), candidates))
    return False


def scalar_param(mat, name, default, x, y):
    return node(mat, unreal.MaterialExpressionScalarParameter, x, y, parameter_name=name, default_value=default)


def vector_param(mat, name, color, x, y):
    return node(mat, unreal.MaterialExpressionVectorParameter, x, y, parameter_name=name, default_value=color)


def tex_param(mat, name, x, y, sampler=None):
    e = node(mat, unreal.MaterialExpressionTextureSampleParameter2D, x, y, parameter_name=name)
    if sampler is not None:
        e.set_editor_property("sampler_type", sampler)
    return e


def vertex_color_linear(mat, x, y):
    vc = node(mat, unreal.MaterialExpressionVertexColor, x, y)
    p = node(mat, unreal.MaterialExpressionPower, x + 200, y)
    two2 = node(mat, unreal.MaterialExpressionConstant, x, y + 150, r=2.2)
    link(vc, p, "Base")
    link(two2, p, "Exponent")
    return p


def build_vertex_color_master():
    m = new_material("M_Sps_VertexColor")
    base = vertex_color_linear(m, -900, 0)
    tint = vector_param(m, "Tint", unreal.LinearColor(1, 1, 1, 1), -900, 300)
    mul = node(m, unreal.MaterialExpressionMultiply, -500, 0)
    link(base, mul, "A")
    link(tint, mul, "B")
    mel.connect_material_property(mul, "", unreal.MaterialProperty.MP_BASE_COLOR)
    rough = scalar_param(m, "Roughness", 0.75, -500, 250)
    mel.connect_material_property(rough, "", unreal.MaterialProperty.MP_ROUGHNESS)
    metal = scalar_param(m, "Metallic", 0.0, -500, 350)
    mel.connect_material_property(metal, "", unreal.MaterialProperty.MP_METALLIC)
    # emissive: vertex colour * Glow (lamps, signal lenses)
    glow = scalar_param(m, "Glow", 0.0, -900, 500)
    em = node(m, unreal.MaterialExpressionMultiply, -500, 500)
    link(base, em, "A")
    link(glow, em, "B")
    mel.connect_material_property(em, "", unreal.MaterialProperty.MP_EMISSIVE_COLOR)
    mel.recompile_material(m)
    unreal.EditorAssetLibrary.save_loaded_asset(m)
    return m


def build_pbr_master():
    """World-space triplanar PBR: albedo/normal/roughness/AO, tinted by the (linear) vertex colour. Parameters: Color, Normal, Rough, AO textures, Tiling (repeats per metre)."""
    m = new_material("M_Sps_Pbr")
    wp = node(m, unreal.MaterialExpressionWorldPosition, -1800, 0)
    tiling = scalar_param(m, "Tiling", 0.25, -1800, 250)       # repeats per metre; world units are cm
    cm_to_m = node(m, unreal.MaterialExpressionConstant, -1800, 350, r=0.01)
    scale = node(m, unreal.MaterialExpressionMultiply, -1600, 300)
    link(tiling, scale, "A")
    link(cm_to_m, scale, "B")

    def planar(label, mask_rgb, y):
        mask = node(m, unreal.MaterialExpressionComponentMask, -1500, y, r=mask_rgb[0], g=mask_rgb[1], b=mask_rgb[2], a=False)
        link(wp, mask, ["", "Input"])
        uv = node(m, unreal.MaterialExpressionMultiply, -1300, y)
        link(mask, uv, "A")
        link(scale, uv, "B")
        return uv

    uv_xy = planar("XY", (True, True, False), -300)     # Z-facing planes (floors, roofs)
    uv_xz = planar("XZ", (True, False, True), 0)        # Y-facing
    uv_yz = planar("YZ", (False, True, True), 300)      # X-facing

    nrm = node(m, unreal.MaterialExpressionVertexNormalWS, -1500, 650)
    absn = node(m, unreal.MaterialExpressionAbs, -1300, 650)
    link(nrm, absn, ["", "Input"])
    powr = node(m, unreal.MaterialExpressionPower, -1100, 650)
    four = node(m, unreal.MaterialExpressionConstant, -1300, 780, r=4.0)
    link(absn, powr, "Base")
    link(four, powr, "Exponent")
    wx = node(m, unreal.MaterialExpressionComponentMask, -900, 560, r=True, g=False, b=False, a=False)
    wy = node(m, unreal.MaterialExpressionComponentMask, -900, 660, r=False, g=True, b=False, a=False)
    wz = node(m, unreal.MaterialExpressionComponentMask, -900, 760, r=False, g=False, b=True, a=False)
    for w in (wx, wy, wz):
        link(powr, w, ["", "Input"])
    wsum = node(m, unreal.MaterialExpressionAdd, -700, 600)
    wsum2 = node(m, unreal.MaterialExpressionAdd, -520, 640)
    link(wx, wsum, "A")
    link(wy, wsum, "B")
    link(wsum, wsum2, "A")
    link(wz, wsum2, "B")

    def sample_blend(param, y0, sampler):
        outs = []
        for i, uv in enumerate((uv_yz, uv_xz, uv_xy)):          # X-facing, Y-facing, Z-facing
            t = tex_param(m, param, -300, y0 + i * 130, sampler)
            link(uv, t, ["UVs", "Coordinates"])
            outs.append(t)
        weights = (wx, wy, wz)
        acc = None
        for t, w in zip(outs, weights):
            mul = node(m, unreal.MaterialExpressionMultiply, 0, y0)
            link(t, mul, "A")
            link(w, mul, "B")
            if acc is None:
                acc = mul
            else:
                add = node(m, unreal.MaterialExpressionAdd, 150, y0)
                link(acc, add, "A")
                link(mul, add, "B")
                acc = add
        div = node(m, unreal.MaterialExpressionDivide, 300, y0)
        link(acc, div, "A")
        link(wsum2, div, "B")
        return div

    color = sample_blend("Color", -400, unreal.MaterialSamplerType.SAMPLERTYPE_COLOR)
    rough = sample_blend("Rough", 100, unreal.MaterialSamplerType.SAMPLERTYPE_LINEAR_COLOR)
    ao = sample_blend("AO", 600, unreal.MaterialSamplerType.SAMPLERTYPE_LINEAR_COLOR)
    normal = sample_blend("Normal", 1100, unreal.MaterialSamplerType.SAMPLERTYPE_NORMAL)

    vc = vertex_color_linear(m, 300, -700)
    boost = scalar_param(m, "Boost", 1.0, 300, -500)
    tint = node(m, unreal.MaterialExpressionMultiply, 600, -600)
    link(vc, tint, "A")
    link(boost, tint, "B")
    albedo = node(m, unreal.MaterialExpressionMultiply, 800, -300)
    link(color, albedo, "A")
    link(tint, albedo, "B")
    mel.connect_material_property(albedo, "", unreal.MaterialProperty.MP_BASE_COLOR)
    mel.connect_material_property(normal, "", unreal.MaterialProperty.MP_NORMAL)
    mel.connect_material_property(ao, "", unreal.MaterialProperty.MP_AMBIENT_OCCLUSION)
    mel.connect_material_property(rough, "", unreal.MaterialProperty.MP_ROUGHNESS)
    mel.recompile_material(m)
    unreal.EditorAssetLibrary.save_loaded_asset(m)
    return m


# ---------------------------------------------------------------- instances
# slot -> (parent, texture set or None, tiling per metre, boost, scalar overrides)
SLOTS = {
    "Terrain": ("M_Sps_Pbr", "Grass004", 0.25, 1.3, {}),
    "Road0": ("M_Sps_Pbr", "Asphalt031", 0.25, 1.4, {}),
    "Road1": ("M_Sps_Pbr", "Asphalt004", 0.25, 1.5, {}),
    "Road2": ("M_Sps_Pbr", "Asphalt004", 0.25, 1.4, {}),
    "Sidewalk": ("M_Sps_Pbr", "PavingStones070", 0.5, 1.3, {}),
    "MarkWhite": ("M_Sps_VertexColor", None, 0, 1, {"Roughness": 0.55}),
    "MarkYellow": ("M_Sps_VertexColor", None, 0, 1, {"Roughness": 0.55}),
    "Facade0": ("M_Sps_VertexColor", None, 0, 1, {"Roughness": 0.9}),
    "Facade1": ("M_Sps_VertexColor", None, 0, 1, {"Roughness": 0.9}),
    "Facade2": ("M_Sps_VertexColor", None, 0, 1, {"Roughness": 0.9}),
    "RoofTiles": ("M_Sps_Pbr", "RoofingTiles014A", 0.5, 1.5, {}),
    "RoofFlat": ("M_Sps_Pbr", "Gravel022", 0.5, 1.4, {}),
    "Plaster": ("M_Sps_Pbr", "Plaster003", 0.4, 1.7, {}),
    "Windows": ("M_Sps_VertexColor", None, 0, 1, {"Roughness": 0.05, "Metallic": 0.1}),
    "Trim": ("M_Sps_Pbr", "Concrete025", 0.5, 1.6, {}),
    "Shutters": ("M_Sps_VertexColor", None, 0, 1, {"Roughness": 0.6}),
    "Railing": ("M_Sps_VertexColor", None, 0, 1, {"Roughness": 0.4, "Metallic": 0.7}),
    "Furniture": ("M_Sps_Pbr", "Metal032", 0.5, 1.2, {}),
    "SignalLens": ("M_Sps_VertexColor", None, 0, 1, {"Roughness": 0.2, "Glow": 3.0}),
    "SignFace": ("M_Sps_VertexColor", None, 0, 1, {"Roughness": 0.4}),
    "Ballast": ("M_Sps_Pbr", "Gravel022", 0.5, 1.2, {}),
    "Rails": ("M_Sps_VertexColor", None, 0, 1, {"Roughness": 0.4, "Metallic": 0.8}),
    "Sleepers": ("M_Sps_Pbr", "Concrete025", 0.5, 1.4, {}),
    "Masts": ("M_Sps_VertexColor", None, 0, 1, {"Roughness": 0.5, "Metallic": 0.6}),
    "Water": ("M_Sps_VertexColor", None, 0, 1, {"Roughness": 0.05, "Metallic": 0.0}),
    "Trunk": ("M_Sps_VertexColor", None, 0, 1, {"Roughness": 0.9}),
    "Foliage": ("M_Sps_VertexColor", None, 0, 1, {"Roughness": 0.7}),
    "HumanBody": ("M_Sps_VertexColor", None, 0, 1, {"Roughness": 0.85}),
    "HumanLeather": ("M_Sps_VertexColor", None, 0, 1, {"Roughness": 0.45}),
    "HumanPatch": ("M_Sps_VertexColor", None, 0, 1, {"Roughness": 0.7}),
    "CarPaint": ("M_Sps_VertexColor", None, 0, 1, {"Roughness": 0.18, "Metallic": 0.35}),
    "CarGlass": ("M_Sps_VertexColor", None, 0, 1, {"Roughness": 0.03}),
    "CarTrim": ("M_Sps_VertexColor", None, 0, 1, {"Roughness": 0.5}),
    "LampFront": ("M_Sps_VertexColor", None, 0, 1, {"Roughness": 0.1, "Glow": 0.0}),
    "LampRear": ("M_Sps_VertexColor", None, 0, 1, {"Roughness": 0.1, "Glow": 0.4}),
    "Indicator": ("M_Sps_VertexColor", None, 0, 1, {"Roughness": 0.1, "Glow": 0.0}),
    "LampBlue": ("M_Sps_VertexColor", None, 0, 1, {"Roughness": 0.1, "Glow": 0.0}),
    "Plate": ("M_Sps_VertexColor", None, 0, 1, {"Roughness": 0.4}),
    "CarDecal": ("M_Sps_VertexColor", None, 0, 1, {"Roughness": 0.4}),
    "CarInterior": ("M_Sps_VertexColor", None, 0, 1, {"Roughness": 0.8}),
    "CarChrome": ("M_Sps_VertexColor", None, 0, 1, {"Roughness": 0.1, "Metallic": 1.0}),
    "Tyre": ("M_Sps_VertexColor", None, 0, 1, {"Roughness": 0.9}),
    "Rim": ("M_Sps_VertexColor", None, 0, 1, {"Roughness": 0.25, "Metallic": 1.0}),
    "Prop": ("M_Sps_VertexColor", None, 0, 1, {"Roughness": 0.8}),
}


def make_instances():
    for slot, (parent, texset, tiling, boost, scalars) in SLOTS.items():
        try:
            name = "MI_" + slot
            path = "%s/%s" % (MAT_PATH, name)
            if unreal.EditorAssetLibrary.does_asset_exist(path):
                unreal.EditorAssetLibrary.delete_asset(path)
            mi = tools.create_asset(name, MAT_PATH, unreal.MaterialInstanceConstant, unreal.MaterialInstanceConstantFactoryNew())
            mi.set_editor_property("parent", unreal.EditorAssetLibrary.load_asset("%s/%s" % (MAT_PATH, parent)))
            if texset:
                for param, suffix in (("Color", "Color"), ("Normal", "Normal"), ("Rough", "Rough"), ("AO", "AO")):
                    tex = unreal.EditorAssetLibrary.load_asset("%s/%s_%s" % (TEX_PATH, texset, suffix))
                    if tex:
                        mel.set_material_instance_texture_parameter_value(mi, param, tex)
                mel.set_material_instance_scalar_parameter_value(mi, "Tiling", tiling)
                mel.set_material_instance_scalar_parameter_value(mi, "Boost", boost)
            for k, v in scalars.items():
                mel.set_material_instance_scalar_parameter_value(mi, k, v)
            unreal.EditorAssetLibrary.save_loaded_asset(mi)
        except Exception:   # noqa
            warn("instance %s failed:\n%s" % (slot, traceback.format_exc()))


def run():
    steps = (("import textures", import_textures), ("vertex-colour master", build_vertex_color_master), ("PBR master", build_pbr_master), ("instances", make_instances))
    for label, fn in steps:
        try:
            log("%s ..." % label)
            fn()
        except Exception:   # noqa
            warn("%s failed:\n%s" % (label, traceback.format_exc()))
    log("done. Check /Game/SPS/Materials; if M_Sps_Pbr is broken, the client falls back to M_Sps_VertexColor automatically.")


run()
