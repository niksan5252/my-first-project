using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.TextCore.LowLevel;

/// The display fonts (Google Fonts, OFL / Apache - Assets/SharedAssets/Fonts/Display) as TextMesh Pro
/// font assets under Resources/Fonts, where DisplayFonts loads them: Bungee, Bungee Shade, Luckiest
/// Guy, Monoton, Russo One. Dynamic atlases with the printable ASCII baked in. Idempotent.
public static class DisplayFontsSetup
{
    private const string Src = "Assets/SharedAssets/Fonts/Display/";
    private const string Dst = "Assets/Resources/Fonts/";

    [MenuItem("Tunnel/Fonts/Build Display Fonts")]
    public static void Build()
    {
        if (!AssetDatabase.IsValidFolder("Assets/Resources/Fonts")) AssetDatabase.CreateFolder("Assets/Resources", "Fonts");
        foreach (var name in new[] { "Bungee-Regular", "BungeeShade-Regular", "LuckiestGuy-Regular", "Monoton-Regular", "RussoOne-Regular" })
        {
            string path = Dst + name + " SDF.asset";
            if (AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(path) != null) continue;
            var font = AssetDatabase.LoadAssetAtPath<Font>(Src + name + ".ttf");
            if (font == null) { Debug.LogError("[Fonts] missing " + name); continue; }
            var fa = TMP_FontAsset.CreateFontAsset(font, 90, 9, GlyphRenderMode.SDFAA, 1024, 1024, AtlasPopulationMode.Dynamic, true);
            fa.name = name + " SDF";
            AssetDatabase.CreateAsset(fa, path);
            fa.atlasTextures[0].name = name + " Atlas";
            AssetDatabase.AddObjectToAsset(fa.atlasTextures[0], fa);
            fa.material.name = name + " Material";
            AssetDatabase.AddObjectToAsset(fa.material, fa);
            string ascii = "";
            for (char c = ' '; c <= '~'; c++) ascii += c;
            fa.TryAddCharacters(ascii + "—·’⚠");
            EditorUtility.SetDirty(fa);
            Debug.Log("[Fonts] built " + path);
        }
        AssetDatabase.SaveAssets();
    }
}
