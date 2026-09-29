using System.IO;
using UnityEditor;
using UnityEngine;

/// Builds Theme_HalloweenPumpkin - segment 2 of the Halloween playlist, "Pumpkin festival" - from
/// code, like GraveyardThemeSetup: a warm purple night under an orange harvest moon, wooden gates
/// hung with lanterns, and a harvest fair along the road. Existing assets are updated in place.
public static class PumpkinThemeSetup
{
    private const string ThemePath = "Assets/Themes/Theme_HalloweenPumpkin.asset";
    private const string VolumePath = "Assets/Settings/TunnelScene_PostProcessing_Pumpkin.asset";
    private const string VolumeSource = "Assets/Settings/TunnelScene_PostProcessing_Graveyard.asset";
    private const string RingPrefabPath = "Assets/Prefabs/Ring_PumpkinGate.prefab";
    private const string EnvPrefabPath = "Assets/Prefabs/Environment_Pumpkin.prefab";
    private const string SkyPath = "Assets/Themes/Presets/PumpkinSky_Mat.mat";
    private const string Snap = GraveyardThemeSetup.Snap, Poly = GraveyardThemeSetup.Poly, Kenney = GraveyardThemeSetup.Kenney;
    private const string Particles = "Assets/SharedAssets/VFX/kenney_particlePack/PNG/";

    // The world is pumpkin orange and purple, lit warm yellow; the cubes are the two colours that
    // stand out of it - a vivid violet and a lemon yellow.
    public static readonly Color Violet = new Color(0.72f, 0.3f, 1f);
    public static readonly Color Lemon = new Color(1f, 0.88f, 0.22f);
    public static readonly Color PumpkinOrange = new Color(1f, 0.48f, 0.08f);
    public static readonly Color NightPurple = new Color(0.075f, 0.035f, 0.11f);

    [MenuItem("Tunnel/Themes/Build Halloween Pumpkin Theme", priority = 72)]
    public static void Build()
    {
        if (!File.Exists(VolumePath)) AssetDatabase.CopyAsset(VolumeSource, VolumePath);

        var sky = AssetDatabase.LoadAssetAtPath<Material>(SkyPath);
        if (sky == null) { sky = new Material(Shader.Find("Tunnel/GraveyardSky")); AssetDatabase.CreateAsset(sky, SkyPath); }
        sky.shader = Shader.Find("Tunnel/GraveyardSky");
        sky.SetColor("_TopColor", new Color(0.03f, 0.008f, 0.06f));
        sky.SetColor("_HorizonColor", new Color(0.16f, 0.06f, 0.14f));
        sky.SetColor("_MoonColor", new Color(1f, 0.62f, 0.3f));
        sky.SetColor("_MoonHalo", new Color(0.9f, 0.35f, 0.12f));
        sky.SetFloat("_MoonSize", 0.13f);
        sky.SetVector("_MoonDir", new Vector4(-0.25f, 0.18f, 1f, 0f));
        sky.SetColor("_CloudColor", new Color(0.06f, 0.025f, 0.08f));
        EditorUtility.SetDirty(sky);

        var ringGo = new GameObject("Ring_PumpkinGate");
        var ring = ringGo.AddComponent<RingArchPumpkinGate>();
        var rso = new SerializedObject(ring);
        rso.FindProperty("pumpkinModel").objectReferenceValue = AssetDatabase.LoadAssetAtPath<GameObject>(Poly + "Pumpkin_Carved_Lit_A.prefab");
        rso.FindProperty("garlandModel").objectReferenceValue = AssetDatabase.LoadAssetAtPath<GameObject>(Poly + "Lanterns_On_String_5m.prefab");
        rso.FindProperty("glowMask").objectReferenceValue = AssetDatabase.LoadAssetAtPath<Texture2D>(Particles + "light_01.png");
        rso.FindProperty("glowShader").objectReferenceValue = Shader.Find("Tunnel/ClubGlow");
        rso.ApplyModifiedPropertiesWithoutUndo();
        PrefabUtility.SaveAsPrefabAsset(ringGo, RingPrefabPath);
        Object.DestroyImmediate(ringGo);

        var env = BuildEnvironmentPrefab();

        var theme = AssetDatabase.LoadAssetAtPath<TunnelTheme>(ThemePath);
        bool created = theme == null;
        if (created) theme = ScriptableObject.CreateInstance<TunnelTheme>();
        theme.primaryColor = Violet;
        theme.secondaryColor = Lemon;
        theme.accentEmissionIntensity = 1.25f;
        theme.ringArchPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(RingPrefabPath);
        theme.floorMaterialOverride = null;
        theme.floorColor = new Color(0.03f, 0.018f, 0.03f);
        theme.floorMetallic = 0.2f;
        theme.floorSmoothness = 0.35f;
        theme.floorGridLineColor = PumpkinOrange * 0.3f;
        theme.floorGridGlintIntensity = 0.35f;
        theme.laneLineColor = PumpkinOrange;
        theme.laneLineEmissionIntensity = 0.55f;
        theme.fogColor = NightPurple;
        theme.fogStartRatio = 0.12f;
        theme.fogEndRatio = 0.85f;
        theme.lightIntensity = 0.3f;
        theme.lightColor = new Color(1f, 0.7f, 0.5f);
        theme.skyboxMaterial = sky;
        theme.cameraBackgroundColor = NightPurple;
        theme.environmentPrefab = env;
        theme.gatePrefab = null;
        theme.airParticles = null;
        theme.postProcessingProfile = AssetDatabase.LoadAssetAtPath<UnityEngine.Rendering.VolumeProfile>(VolumePath);
        theme.hudComboTextColor = Lemon;
        theme.hudBannerBorderColor = Violet;
        if (created) AssetDatabase.CreateAsset(theme, ThemePath);
        EditorUtility.SetDirty(theme);
        AssetDatabase.SaveAssets();
        Debug.Log("[PumpkinTheme] built " + ThemePath);
    }

    private static GameObject BuildEnvironmentPrefab()
    {
        var go = new GameObject("Environment_Pumpkin");
        var deco = go.AddComponent<PumpkinEnvironmentDecorator>();
        var so = new SerializedObject(deco);
        void Obj(string field, Object o) { var p = so.FindProperty(field); if (p != null) p.objectReferenceValue = o; else Debug.LogWarning("[PumpkinTheme] no field " + field); }
        void Col(string field, Color c) { var p = so.FindProperty(field); if (p != null) p.colorValue = c; else Debug.LogWarning("[PumpkinTheme] no field " + field); }
        void List(string field, string[] paths)
        {
            var p = so.FindProperty(field);
            p.arraySize = 0;
            foreach (var path in paths)
            {
                var o = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (o == null) { Debug.LogWarning("[PumpkinTheme] missing " + path); continue; }
                p.arraySize++;
                p.GetArrayElementAtIndex(p.arraySize - 1).objectReferenceValue = o;
            }
        }
        Obj("glowShader", Shader.Find("Tunnel/ClubGlow"));
        Obj("flareMask", AssetDatabase.LoadAssetAtPath<Texture2D>(Particles + "light_01.png"));
        Obj("flameTexture", AssetDatabase.LoadAssetAtPath<Texture2D>(Particles + "flame_01.png"));
        Obj("smokeTexture", AssetDatabase.LoadAssetAtPath<Texture2D>(Particles + "smoke_07.png"));
        Obj("sparkTexture", AssetDatabase.LoadAssetAtPath<Texture2D>(Particles + "spark_05.png"));
        Obj("fireBasket", AssetDatabase.LoadAssetAtPath<GameObject>(Kenney + "fire-basket.fbx"));
        Obj("cauldron", AssetDatabase.LoadAssetAtPath<GameObject>(Poly + "Cauldron_Potion_Full.prefab"));
        List("pumpkins", new[] { Snap + "Pumpkin_A.prefab", Snap + "Pumpkin_C.prefab", Snap + "Pumpkin_E.prefab", Poly + "Pumpkin_A.prefab", Kenney + "pumpkin.fbx", Kenney + "pumpkin-tall.fbx" });
        List("litPumpkins", new[] { Poly + "Pumpkin_Carved_Lit_A.prefab", Poly + "Pumpkin_Carved_Lit_B.prefab", Poly + "Pumpkin_Carved_Lit_C.prefab", Kenney + "pumpkin-carved.fbx", Kenney + "pumpkin-tall-carved.fbx" });
        List("hay", new[] { Kenney + "hay-bale.fbx", Kenney + "hay-bale-bundled.fbx" });
        List("scarecrows", new[] { Poly + "Scarecrow_A.prefab", Poly + "Scarecrow_B.prefab" });
        List("garlands", new[] { Poly + "Lanterns_On_String_5m.prefab" });
        List("bunting", new[] { Poly + "String_Of_Flags_5m.prefab" });
        List("lamps", new[] { Poly + "Lantern_Street_Movable_Halloween.prefab", Poly + "Lantern_Street_Fixed_Halloween.prefab" });
        List("candles", new[] { Snap + "Candle_Group_A.prefab", Snap + "Candle_Group_B.prefab", Poly + "Candle_Skull_Lit.prefab" });
        List("details", new[] { Snap + "Witch Hat.prefab", Snap + "Broom.prefab", Poly + "Stump_Possesed.prefab" });
        Col("moonBlue", new Color(0.75f, 0.45f, 1f));
        Col("flameOrange", PumpkinOrange);
        Col("poisonGreen", new Color(0.45f, 1f, 0.3f));
        Col("stoneTint", new Color(0.8f, 0.66f, 0.62f));
        so.ApplyModifiedPropertiesWithoutUndo();
        var prefab = PrefabUtility.SaveAsPrefabAsset(go, EnvPrefabPath);
        Object.DestroyImmediate(go);
        return prefab;
    }
}
