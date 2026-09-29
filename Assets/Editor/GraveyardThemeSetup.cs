using System.IO;
using UnityEditor;
using UnityEngine;

/// Builds Theme_HalloweenGraveyard - segment 1 of the Halloween playlist, "Graveyard under the
/// moon" - from code, like the other theme setups: the moonlit sky, the wrought-iron gate arches,
/// the environment prefab, the post-processing profile and the theme asset itself. Existing assets
/// are updated in place; nothing else is touched.
public static class GraveyardThemeSetup
{
    private const string ThemePath = "Assets/Themes/Theme_HalloweenGraveyard.asset";
    private const string VolumePath = "Assets/Settings/TunnelScene_PostProcessing_Graveyard.asset";
    private const string VolumeSource = "Assets/Settings/TunnelScene_PostProcessing_Phonk.asset";
    private const string RingPrefabPath = "Assets/Prefabs/Ring_GothicGate.prefab";
    private const string EnvPrefabPath = "Assets/Prefabs/Environment_Graveyard.prefab";
    private const string SkyPath = "Assets/Themes/Presets/GraveyardSky_Mat.mat";
    public const string Snap = "Assets/Halloween Pack-Cemetery Snap/Prefabs/";
    public const string Poly = "Assets/polyperfect/Poly Halloween/Prefabs/Halloween/";
    public const string Kenney = "Assets/SharedAssets/Kits3D/kenney_graveyard-kit_5.0/Models/";
    private const string Particles = "Assets/SharedAssets/VFX/kenney_particlePack/PNG/";
    private const string Masks = "Assets/SharedAssets/VFX/kenney_light-masks-1.0/Transparent/";

    // The palette. The cubes (the theme's two colours) are the two loud ones - ghost green and
    // pumpkin orange - and read against a world that is moonlit blue: the sky, the fog, the stone.
    public static readonly Color GhostGreen = new Color(0.42f, 1f, 0.22f);
    public static readonly Color Pumpkin = new Color(1f, 0.5f, 0.08f);
    public static readonly Color MoonBlue = new Color(0.55f, 0.72f, 1f);
    public static readonly Color NightFog = new Color(0.045f, 0.065f, 0.11f);
    public static readonly Color Soil = new Color(0.02f, 0.022f, 0.03f);

    [MenuItem("Tunnel/Themes/Build Halloween Graveyard Theme", priority = 71)]
    public static void Build()
    {
        if (!File.Exists(VolumePath)) AssetDatabase.CopyAsset(VolumeSource, VolumePath);

        var sky = LoadOrCreate(SkyPath, "Tunnel/GraveyardSky");
        sky.SetColor("_HorizonColor", NightFog * 1.4f);
        sky.SetColor("_TopColor", new Color(0.008f, 0.012f, 0.04f));
        sky.SetColor("_MoonColor", new Color(0.86f, 0.93f, 1f));
        sky.SetColor("_MoonHalo", MoonBlue * 0.6f);
        EditorUtility.SetDirty(sky);

        var ringGo = new GameObject("Ring_GothicGate");
        var ring = ringGo.AddComponent<RingArchGothicGate>();
        var rso = new SerializedObject(ring);
        rso.FindProperty("pillarModel").objectReferenceValue = AssetDatabase.LoadAssetAtPath<GameObject>(Kenney + "pillar-square.fbx");
        rso.FindProperty("flameMask").objectReferenceValue = AssetDatabase.LoadAssetAtPath<Texture2D>(Particles + "flame_01.png");
        rso.FindProperty("glowShader").objectReferenceValue = Shader.Find("Tunnel/ClubGlow");
        rso.ApplyModifiedPropertiesWithoutUndo();
        PrefabUtility.SaveAsPrefabAsset(ringGo, RingPrefabPath);
        Object.DestroyImmediate(ringGo);

        var env = BuildEnvironmentPrefab();

        var theme = AssetDatabase.LoadAssetAtPath<TunnelTheme>(ThemePath);
        bool created = theme == null;
        if (created) theme = ScriptableObject.CreateInstance<TunnelTheme>();
        theme.primaryColor = GhostGreen;
        theme.secondaryColor = Pumpkin;
        theme.accentEmissionIntensity = 1.25f;
        theme.ringArchPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(RingPrefabPath);
        theme.floorMaterialOverride = null;
        theme.floorColor = Soil;
        theme.floorMetallic = 0.2f;
        theme.floorSmoothness = 0.3f;
        theme.floorGridLineColor = MoonBlue * 0.35f;
        theme.floorGridGlintIntensity = 0.35f;
        theme.laneLineColor = GhostGreen * 0.8f;
        theme.laneLineEmissionIntensity = 0.5f;
        theme.fogColor = NightFog;
        theme.fogStartRatio = 0.12f;
        theme.fogEndRatio = 0.85f;
        // The moon: a cold, dim key light, just enough for the packs' textured stone to read.
        // Everything bright comes from lamps, candles, pumpkins and the gates' own light.
        theme.lightIntensity = 0.35f;
        theme.lightColor = MoonBlue;
        theme.skyboxMaterial = sky;
        theme.cameraBackgroundColor = NightFog;
        theme.environmentPrefab = env;
        theme.gatePrefab = null;
        theme.airParticles = null;
        theme.postProcessingProfile = AssetDatabase.LoadAssetAtPath<UnityEngine.Rendering.VolumeProfile>(VolumePath);
        theme.hudComboTextColor = GhostGreen;
        theme.hudBannerBorderColor = Pumpkin;
        if (created) AssetDatabase.CreateAsset(theme, ThemePath);
        EditorUtility.SetDirty(theme);
        AssetDatabase.SaveAssets();
        Debug.Log("[GraveyardTheme] built " + ThemePath);
    }

    /// The environment prefab: its decorator with every model and texture it builds from.
    private static GameObject BuildEnvironmentPrefab()
    {
        var go = new GameObject("Environment_Graveyard");
        var deco = go.AddComponent<GraveyardEnvironmentDecorator>();
        var so = new SerializedObject(deco);
        void Obj(string field, Object o) { var p = so.FindProperty(field); if (p != null) p.objectReferenceValue = o; else Debug.LogWarning("[GraveyardTheme] no field " + field); }
        void List(string field, string[] paths)
        {
            var p = so.FindProperty(field);
            p.arraySize = 0;
            foreach (var path in paths)
            {
                var o = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (o == null) { Debug.LogWarning("[GraveyardTheme] missing " + path); continue; }
                p.arraySize++;
                p.GetArrayElementAtIndex(p.arraySize - 1).objectReferenceValue = o;
            }
        }
        Obj("glowShader", Shader.Find("Tunnel/ClubGlow"));
        Obj("flareMask", AssetDatabase.LoadAssetAtPath<Texture2D>(Particles + "light_01.png"));
        Obj("flameTexture", AssetDatabase.LoadAssetAtPath<Texture2D>(Particles + "flame_01.png"));
        Obj("smokeTexture", AssetDatabase.LoadAssetAtPath<Texture2D>(Particles + "smoke_07.png"));
        Obj("coneMask", AssetDatabase.LoadAssetAtPath<Texture2D>(Masks + "cone_a_blur.png"));
        List("gravestones", new[]
        {
            Snap + "Gravestone_A.prefab", Snap + "Gravestone_B.prefab", Snap + "Gravestone_C.prefab", Snap + "Gravestone_D.prefab",
            Snap + "Gravestone_E.prefab", Snap + "Gravestone_F.prefab", Snap + "Gravestone_G.prefab", Snap + "Gravestone_H.prefab",
            Snap + "Gravestone S1.prefab", Snap + "Gravestone S2.prefab", Snap + "Gravestone S3.prefab", Snap + "Gravestone S4.prefab",
            Snap + "Gravestone_B_Destroyed.prefab", Snap + "Gravestone_E_Destroyed.prefab",
            Kenney + "gravestone-cross-large.fbx", Kenney + "gravestone-cross.fbx", Kenney + "gravestone-round.fbx",
            Kenney + "gravestone-decorative.fbx", Kenney + "gravestone-roof.fbx", Kenney + "cross.fbx", Kenney + "cross-wood.fbx",
            Poly + "Gravestone_Old_Classic_Cross.prefab", Poly + "Gravestone_Old_Cross.prefab", Poly + "Gravestone_Old_Tower.prefab",
            Poly + "Gravestone_Old_Classic_Broken.prefab",
        });
        List("crypts", new[] { Snap + "Large Crypt.prefab", Snap + "Small Crypt.prefab", Kenney + "crypt-large.fbx", Kenney + "crypt.fbx", Kenney + "pillar-obelisk.fbx" });
        List("trees", new[]
        {
            Snap + "Spooky Tree_ A.prefab", Snap + "Spooky Tree_ B.prefab", Snap + "Spooky Tree_ C.prefab", Snap + "Spooky Tree_ D.prefab",
            Snap + "Spooky Tree_ E.prefab", Snap + "Spooky Tree_ F.prefab", Poly + "Tree_Scary_Dead.prefab", Kenney + "pine-crooked.fbx",
        });
        List("fences", new[] { Snap + "Fence Wall A.prefab", Snap + "Fence Wall B.prefab", Snap + "Fence Wall C.prefab" });
        List("ironFences", new[] { Kenney + "iron-fence.fbx", Kenney + "iron-fence-damaged.fbx" });
        List("lamps", new[] { Snap + "Street lamp_A.prefab", Snap + "Street lamp_B.prefab", Poly + "Lantern_Street_Fixed_Halloween.prefab" });
        List("candles", new[]
        {
            Snap + "Candle_Group_A.prefab", Snap + "Candle_Group_B.prefab", Snap + "Candle_Group_C.prefab",
            Snap + "Head_Candle_A.prefab", Snap + "Head_Candle_B.prefab", Poly + "Candle_Skull_Lit.prefab",
        });
        List("pumpkins", new[] { Snap + "Pumpkin_A.prefab", Snap + "Pumpkin_C.prefab", Poly + "Pumpkin_Carved_Lit_A.prefab", Poly + "Pumpkin_Carved_Lit_B.prefab" });
        List("details", new[]
        {
            Snap + "Zombie_Hand.prefab", Snap + "Skeleton arm_up.prefab", Snap + "Wooden Coffin A.prefab", Snap + "Rock_A.prefab",
            Snap + "Rock_B.prefab", Snap + "Spooky_Mushroom_A.prefab", Snap + "Spooky_Mushroom_C.prefab", Snap + "Sarcophagus_A.prefab",
            Poly + "Mushroom_Magical_Cluster_A.prefab", Poly + "Scarecrow_A.prefab", Kenney + "urn-round.fbx",
        });
        so.ApplyModifiedPropertiesWithoutUndo();
        var prefab = PrefabUtility.SaveAsPrefabAsset(go, EnvPrefabPath);
        Object.DestroyImmediate(go);
        return prefab;
    }

    private static Material LoadOrCreate(string path, string shader)
    {
        var m = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (m == null)
        {
            m = new Material(Shader.Find(shader));
            AssetDatabase.CreateAsset(m, path);
        }
        m.shader = Shader.Find(shader);
        return m;
    }
}
