using System.IO;
using UnityEditor;
using UnityEngine;

/// Builds Theme_HalloweenHaunted - segment 3 of the Halloween playlist, "Haunted house" - from
/// code, like the other two Halloween themes: the gallery of a gothic mansion in violet and ghostly
/// green, cold lightning behind its windows, stained-glass doorways over the road.
public static class HauntedThemeSetup
{
    private const string ThemePath = "Assets/Themes/Theme_HalloweenHaunted.asset";
    private const string VolumePath = "Assets/Settings/TunnelScene_PostProcessing_Haunted.asset";
    private const string VolumeSource = "Assets/Settings/TunnelScene_PostProcessing_Graveyard.asset";
    private const string RingPrefabPath = "Assets/Prefabs/Ring_StainedGlass.prefab";
    private const string EnvPrefabPath = "Assets/Prefabs/Environment_Haunted.prefab";
    private const string SkyPath = "Assets/Themes/Presets/HauntedSky_Mat.mat";
    private const string Snap = GraveyardThemeSetup.Snap, Poly = GraveyardThemeSetup.Poly, Kenney = GraveyardThemeSetup.Kenney;
    private const string Particles = "Assets/SharedAssets/VFX/kenney_particlePack/PNG/";

    // Violet and ghost green are the house's own light; the cubes are the two that cut through it,
    // an electric cyan and a hot pink - neither the graveyard's green/orange nor the fair's
    // violet/lemon.
    public static readonly Color Cyan = new Color(0.2f, 0.95f, 1f);
    public static readonly Color HotPink = new Color(1f, 0.25f, 0.65f);
    public static readonly Color Violet = new Color(0.55f, 0.2f, 1f);
    public static readonly Color GhostGreen = new Color(0.35f, 1f, 0.45f);
    public static readonly Color Gloom = new Color(0.05f, 0.022f, 0.08f);

    [MenuItem("Tunnel/Themes/Build Halloween Haunted Theme", priority = 73)]
    public static void Build()
    {
        if (!File.Exists(VolumePath)) AssetDatabase.CopyAsset(VolumeSource, VolumePath);

        var sky = AssetDatabase.LoadAssetAtPath<Material>(SkyPath);
        if (sky == null) { sky = new Material(Shader.Find("Tunnel/GraveyardSky")); AssetDatabase.CreateAsset(sky, SkyPath); }
        sky.shader = Shader.Find("Tunnel/GraveyardSky");
        sky.SetColor("_TopColor", new Color(0.012f, 0.004f, 0.03f));
        sky.SetColor("_HorizonColor", new Color(0.08f, 0.03f, 0.12f));
        // A small, veiled moon behind storm clouds - the lightning is the light here.
        sky.SetColor("_MoonColor", new Color(0.45f, 0.4f, 0.6f));
        sky.SetColor("_MoonHalo", new Color(0.3f, 0.15f, 0.5f));
        sky.SetFloat("_MoonSize", 0.07f);
        sky.SetVector("_MoonDir", new Vector4(0.1f, 0.35f, 1f, 0f));
        sky.SetColor("_CloudColor", new Color(0.05f, 0.02f, 0.08f));
        sky.SetFloat("_Stars", 0.4f);
        sky.SetColor("_FlashColor", new Color(0.7f, 0.75f, 1f));
        EditorUtility.SetDirty(sky);

        var ringGo = new GameObject("Ring_StainedGlass");
        ringGo.AddComponent<RingArchStainedGlass>();
        PrefabUtility.SaveAsPrefabAsset(ringGo, RingPrefabPath);
        Object.DestroyImmediate(ringGo);

        var env = BuildEnvironmentPrefab();

        var theme = AssetDatabase.LoadAssetAtPath<TunnelTheme>(ThemePath);
        bool created = theme == null;
        if (created) theme = ScriptableObject.CreateInstance<TunnelTheme>();
        theme.primaryColor = Cyan;
        theme.secondaryColor = HotPink;
        theme.accentEmissionIntensity = 1.3f;
        theme.ringArchPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(RingPrefabPath);
        theme.floorMaterialOverride = null;
        theme.floorColor = new Color(0.03f, 0.02f, 0.035f);
        theme.floorMetallic = 0.3f;
        theme.floorSmoothness = 0.6f;
        theme.floorGridLineColor = Violet * 0.35f;
        theme.floorGridGlintIntensity = 0.4f;
        theme.laneLineColor = GhostGreen;
        theme.laneLineEmissionIntensity = 0.5f;
        theme.fogColor = Gloom;
        theme.fogStartRatio = 0.1f;
        theme.fogEndRatio = 0.8f;
        theme.lightIntensity = 0.25f;
        theme.lightColor = new Color(0.6f, 0.55f, 1f);
        theme.skyboxMaterial = sky;
        theme.cameraBackgroundColor = Gloom;
        theme.environmentPrefab = env;
        theme.gatePrefab = null;
        theme.airParticles = null;
        theme.postProcessingProfile = AssetDatabase.LoadAssetAtPath<UnityEngine.Rendering.VolumeProfile>(VolumePath);
        theme.hudComboTextColor = Cyan;
        theme.hudBannerBorderColor = HotPink;
        if (created) AssetDatabase.CreateAsset(theme, ThemePath);
        EditorUtility.SetDirty(theme);
        AssetDatabase.SaveAssets();
        Debug.Log("[HauntedTheme] built " + ThemePath);
    }

    private static GameObject BuildEnvironmentPrefab()
    {
        var go = new GameObject("Environment_Haunted");
        var deco = go.AddComponent<HauntedEnvironmentDecorator>();
        var so = new SerializedObject(deco);
        void Obj(string field, Object o) { var p = so.FindProperty(field); if (p != null) p.objectReferenceValue = o; else Debug.LogWarning("[HauntedTheme] no field " + field); }
        void Col(string field, Color c) { var p = so.FindProperty(field); if (p != null) p.colorValue = c; else Debug.LogWarning("[HauntedTheme] no field " + field); }
        void List(string field, string[] paths)
        {
            var p = so.FindProperty(field);
            p.arraySize = 0;
            foreach (var path in paths)
            {
                var o = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (o == null) { Debug.LogWarning("[HauntedTheme] missing " + path); continue; }
                p.arraySize++;
                p.GetArrayElementAtIndex(p.arraySize - 1).objectReferenceValue = o;
            }
        }
        Obj("glowShader", Shader.Find("Tunnel/ClubGlow"));
        Obj("flareMask", AssetDatabase.LoadAssetAtPath<Texture2D>(Particles + "light_01.png"));
        Obj("flameTexture", AssetDatabase.LoadAssetAtPath<Texture2D>(Particles + "flame_01.png"));
        Obj("smokeTexture", AssetDatabase.LoadAssetAtPath<Texture2D>(Particles + "smoke_07.png"));
        Obj("candelabrum", AssetDatabase.LoadAssetAtPath<GameObject>(Kenney + "candle-multiple.fbx"));
        Obj("pedestal", AssetDatabase.LoadAssetAtPath<GameObject>(Kenney + "column-large.fbx"));
        Obj("ghostModel", AssetDatabase.LoadAssetAtPath<GameObject>(Kenney + "character-ghost.fbx"));
        List("books", new[] { Poly + "Book_Evil.prefab", Poly + "Book_Magical_Eye.prefab", Poly + "Book_Evil_Open.prefab" });
        List("candles", new[] { Poly + "Candle_Stick_B_Lit.prefab", Poly + "Candle_Skull_Lit.prefab", Snap + "Candle_Group_C.prefab" });
        List("coffins", new[] { Poly + "Coffin_Old.prefab", Snap + "Wooden Coffin B.prefab", Snap + "Sarcophagus_B.prefab", Kenney + "coffin.fbx" });
        List("cobwebs", new[] { Poly + "Spiderweb_A.prefab", Poly + "Spiderweb_B.prefab", Poly + "Spiderweb_C.prefab" });
        List("lamps", new[] { Poly + "Lantern_Street_Fixed_Halloween.prefab" });
        List("pumpkins", new[] { Snap + "Pumpkin_A.prefab" });
        List("details", new[] { Snap + "Skeleton.prefab" });
        Col("moonBlue", Violet);
        Col("flameOrange", new Color(1f, 0.55f, 0.2f));
        Col("poisonGreen", GhostGreen);
        Col("stoneTint", new Color(0.55f, 0.5f, 0.65f));
        // The phonk segment: flames and fog kick harder, the lights stronger.
        var li = so.FindProperty("lampLightIntensity"); if (li != null) li.floatValue = 6f;
        so.ApplyModifiedPropertiesWithoutUndo();
        var prefab = PrefabUtility.SaveAsPrefabAsset(go, EnvPrefabPath);
        Object.DestroyImmediate(go);
        return prefab;
    }
}
