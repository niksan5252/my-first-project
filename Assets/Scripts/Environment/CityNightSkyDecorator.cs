using UnityEngine;
using UnityEngine.Rendering;

/// The cyberpunk theme's sky layer: a starfield dome and a single moon hanging behind the
/// skyline. Lives on a child of Environment_Cyberpunk and is driven by CityEnvironmentDecorator,
/// which hands over the theme's palette and builds it once - so the whole sky is still theme
/// content (TunnelTheme.environmentPrefab), not something the tunnel or the gameplay scripts
/// knows about.
///
/// Why geometry and not just the skybox texture: the Kenney night skybox is a flat gradient that
/// gives the city nothing to stand against, and a texture cannot be recoloured from the theme
/// palette or put a moon BEHIND a building. This is a backdrop, so it never scrolls: the camera
/// itself does not travel, only the street does, and a sky that slid past would read as the city
/// spinning.
///
/// The two shaders it uses are the ones already in the project and both ignore scene fog on
/// purpose (Custom/Starfield, Custom/PlanetSurfaceNeon): the tunnel's linear fog is fully opaque
/// at 144 units, so anything sky-like drawn with an ordinary Lit/Unlit material simply dissolves
/// into the fog colour - which is exactly the empty dark-blue background this layer exists to
/// fix. Depth still works normally, so the skyline occludes the moon.
public class CityNightSkyDecorator : MonoBehaviour
{
    [Header("Starfield")]
    [SerializeField] private int starCount = 900;
    [SerializeField] private float domeNearRadius = 260f;
    [SerializeField] private float domeFarRadius = 460f;
    // Stars start just above the rooftops rather than at the horizon: a star seen at eye level,
    // through the street's own haze, reads as a floating dot rather than as sky.
    [SerializeField, Range(0f, 0.8f)] private float horizonCutoff = 0.12f;
    [SerializeField, Range(0f, 2.5f)] private float starIntensity = 1.35f;
    // Angular, so a star at the back of the shell is not silently bigger on screen than an
    // identical one at the front.
    [SerializeField] private float starAngularSize = 0.0045f;
    // Power curve: most stars faint, a few bright. A flat range reads as even confetti.
    [SerializeField, Range(1f, 6f)] private float starBrightnessFalloff = 3.4f;
    // A city sky is not black and white: a share of the stars leans toward the theme's two
    // accents so the dome belongs to the same palette as the street below it.
    [SerializeField, Range(0f, 1f)] private float starTintStrength = 0.45f;
    [SerializeField, Range(0f, 1f)] private float starTwinkleAmount = 0.35f;
    [SerializeField, Range(0f, 8f)] private float starTwinkleSpeed = 1.3f;

    [Header("Moon (Planet Generator Kit surface shader)")]
    [SerializeField] private bool buildMoon = true;
    [SerializeField] private float moonRadius = 34f;
    // Deliberately low and off to one side: sitting partly behind the skyline row is what gives
    // the city depth. High in the middle of the frame it would read as a second sun.
    [SerializeField] private Vector3 moonPosition = new Vector3(-62f, 34f, 235f);
    [SerializeField, Range(0f, 2.5f)] private float moonIntensity = 0.55f;
    [SerializeField, Range(0.02f, 1f)] private float moonNoiseScale = 0.08f;
    [SerializeField, Range(0f, 1f)] private float moonLandThreshold = 0.52f;
    [SerializeField, Range(0.001f, 0.5f)] private float moonEdgeSmoothness = 0.12f;
    [SerializeField, Range(1f, 8f)] private float moonBlendSharpness = 3f;
    [SerializeField, Range(1f, 6f)] private float moonNoiseOctaves = 4f;
    // How far the moon's colours lean away from the palette toward a pale lunar grey. A moon in
    // pure accent pink stops being a moon.
    [SerializeField, Range(0f, 1f)] private float moonDesaturation = 0.65f;

    [Header("Moon halo")]
    [SerializeField] private bool buildHalo = true;
    [SerializeField] private Texture2D haloMask;          // Kenney light mask, soft circle
    [SerializeField] private float haloSizeRatio = 3.4f;  // multiples of the moon's radius
    [SerializeField, Range(0f, 2f)] private float haloStrength = 0.5f;

    [Header("Determinism")]
    [SerializeField] private int seed = 71620;

    private Color primaryColor = new Color(1f, 0.05f, 0.45f);
    private Color secondaryColor = new Color(0f, 0.85f, 1f);

    private Material starMaterial, moonMaterial, haloMaterial;
    private Mesh starMesh, haloMesh;
    private Texture2D starSprite;
    private bool built;

    /// Called by CityEnvironmentDecorator once, with the theme's own two accent colours.
    public void Build(Color primary, Color secondary)
    {
        if (built) return;
        built = true;

        primaryColor = primary;
        secondaryColor = secondary;

        var rng = new System.Random(seed);
        BuildStarfield(rng);
        if (buildMoon) BuildMoon();
    }

    // --- starfield ---

    /// One combined mesh of camera-facing quads, each carrying its own brightness, tint and
    /// twinkle phase in vertex data, so the whole dome stays a single draw call.
    private void BuildStarfield(System.Random rng)
    {
        int quadCount = Mathf.Max(0, starCount);
        if (quadCount == 0) return;

        var vertices = new Vector3[quadCount * 4];
        var uvs = new Vector2[quadCount * 4];
        var phases = new Vector2[quadCount * 4];
        var colors = new Color[quadCount * 4];
        var triangles = new int[quadCount * 6];

        float near = Mathf.Min(domeNearRadius, domeFarRadius);
        float far = Mathf.Max(domeNearRadius, domeFarRadius);

        for (int i = 0; i < quadCount; i++)
        {
            // Only the half of the sphere in front of the camera is ever seen - the camera looks
            // down +Z and never turns - so the whole star budget goes there instead of behind.
            float azimuth = (float)(rng.NextDouble() * Mathf.PI * 1.4 - Mathf.PI * 0.2);
            float height = horizonCutoff + (float)rng.NextDouble() * (1f - horizonCutoff);
            float ring = Mathf.Sqrt(Mathf.Max(0f, 1f - height * height));
            var dir = new Vector3(Mathf.Cos(azimuth) * ring, height, Mathf.Sin(azimuth) * ring).normalized;

            float radius = Mathf.Lerp(near, far, (float)rng.NextDouble());
            Vector3 center = dir * radius;

            float magnitude = Mathf.Pow((float)rng.NextDouble(), starBrightnessFalloff);
            float depth = Mathf.InverseLerp(far, near, radius);
            float brightness = Mathf.Lerp(0.16f, 1f, magnitude) * Mathf.Lerp(0.5f, 1f, depth);
            float size = radius * starAngularSize * Mathf.Lerp(0.7f, 2.8f, magnitude);

            Color accent = rng.NextDouble() < 0.5 ? primaryColor : secondaryColor;
            Color tint = Color.Lerp(Color.white, accent, (float)rng.NextDouble() * starTintStrength);

            Vector3 normal = -dir;
            Vector3 tangent = Vector3.Cross(normal, Vector3.up);
            if (tangent.sqrMagnitude < 0.001f) tangent = Vector3.Cross(normal, Vector3.right);
            tangent.Normalize();
            Vector3 right = tangent * size;
            Vector3 up = Vector3.Cross(normal, tangent) * size;

            int vi = i * 4;
            vertices[vi + 0] = center - right - up;
            vertices[vi + 1] = center + right - up;
            vertices[vi + 2] = center + right + up;
            vertices[vi + 3] = center - right + up;

            uvs[vi + 0] = new Vector2(0f, 0f);
            uvs[vi + 1] = new Vector2(1f, 0f);
            uvs[vi + 2] = new Vector2(1f, 1f);
            uvs[vi + 3] = new Vector2(0f, 1f);

            var phase = new Vector2((float)rng.NextDouble(), 0.6f + (float)rng.NextDouble());
            phases[vi + 0] = phase; phases[vi + 1] = phase; phases[vi + 2] = phase; phases[vi + 3] = phase;

            var vertexColor = new Color(tint.r, tint.g, tint.b, brightness);
            colors[vi + 0] = vertexColor; colors[vi + 1] = vertexColor;
            colors[vi + 2] = vertexColor; colors[vi + 3] = vertexColor;

            // Single winding: the shader culls nothing, and drawing each quad twice under
            // additive blending would just double every star's brightness.
            int ti = i * 6;
            triangles[ti + 0] = vi + 0; triangles[ti + 1] = vi + 2; triangles[ti + 2] = vi + 1;
            triangles[ti + 3] = vi + 0; triangles[ti + 4] = vi + 3; triangles[ti + 5] = vi + 2;
        }

        starMesh = new Mesh { name = "CityStarfield" };
        starMesh.indexFormat = vertices.Length > 65000 ? IndexFormat.UInt32 : IndexFormat.UInt16;
        starMesh.vertices = vertices;
        starMesh.uv = uvs;
        starMesh.uv2 = phases;
        starMesh.colors = colors;
        starMesh.triangles = triangles;
        starMesh.RecalculateBounds();

        starSprite = BuildStarSprite(64);
        starMaterial = new Material(Shader.Find("Custom/Starfield"));
        starMaterial.SetTexture("_BaseMap", starSprite);
        starMaterial.SetFloat("_Intensity", starIntensity);
        starMaterial.SetFloat("_TwinkleAmount", starTwinkleAmount);
        starMaterial.SetFloat("_TwinkleSpeed", starTwinkleSpeed);

        var go = new GameObject("Starfield");
        go.transform.SetParent(transform, false);
        go.AddComponent<MeshFilter>().sharedMesh = starMesh;
        var mr = go.AddComponent<MeshRenderer>();
        mr.sharedMaterial = starMaterial;
        mr.shadowCastingMode = ShadowCastingMode.Off;
        mr.receiveShadows = false;
    }

    /// A tight bright core over a much wider, much fainter halo. One falloff alone gives either a
    /// hard dot with visible quad edges or a soft blob; the two terms together read as a point of
    /// light at this size.
    private static Texture2D BuildStarSprite(int size)
    {
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, true)
        {
            name = "CityStarSprite",
            wrapMode = TextureWrapMode.Clamp,
            filterMode = FilterMode.Bilinear,
        };

        var pixels = new Color32[size * size];
        float half = size * 0.5f;
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float dx = (x + 0.5f - half) / half;
                float dy = (y + 0.5f - half) / half;
                float r = Mathf.Sqrt(dx * dx + dy * dy);
                float core = Mathf.Pow(Mathf.Clamp01(1f - r / 0.22f), 2.6f);
                float halo = Mathf.Pow(Mathf.Clamp01(1f - r), 3.5f) * 0.35f;
                byte a = (byte)(Mathf.Clamp01(core + halo) * 255f);
                pixels[y * size + x] = new Color32(255, 255, 255, a);
            }
        }
        tex.SetPixels32(pixels);
        tex.Apply();
        return tex;
    }

    // --- moon ---

    private void BuildMoon()
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        go.name = "Moon";
        Destroy(go.GetComponent<Collider>());
        go.transform.SetParent(transform, false);
        go.transform.localPosition = moonPosition;
        go.transform.localScale = Vector3.one * moonRadius * 2f;
        go.transform.localRotation = Quaternion.Euler(0f, 145f, 12f);

        moonMaterial = CreateMoonMaterial();
        var mr = go.GetComponent<MeshRenderer>();
        mr.sharedMaterial = moonMaterial;
        mr.shadowCastingMode = ShadowCastingMode.Off;
        mr.receiveShadows = false;

        if (buildHalo && haloMask != null) BuildMoonHalo();
    }

    /// The kit's triplanar noise picks between two colours; here they are the theme's accents
    /// pulled most of the way to grey, so the moon carries the palette without becoming a second
    /// neon sign. Falls back to a plain emissive sphere if the kit shader has been removed.
    private Material CreateMoonMaterial()
    {
        Color seaColor = Color.Lerp(secondaryColor, new Color(0.62f, 0.66f, 0.78f), moonDesaturation);
        Color landColor = Color.Lerp(primaryColor, new Color(0.85f, 0.83f, 0.8f), moonDesaturation);

        var shader = Shader.Find("Custom/PlanetSurfaceNeon");
        if (shader == null)
        {
            var fallback = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
            fallback.SetColor("_BaseColor", landColor * moonIntensity);
            return fallback;
        }

        var mat = new Material(shader);
        mat.SetColor("_ColorOcean", seaColor);
        mat.SetColor("_ColorLand", landColor);
        mat.SetFloat("_Intensity", moonIntensity);
        mat.SetFloat("_Scale", moonNoiseScale);
        mat.SetFloat("_LandThreshold", moonLandThreshold);
        mat.SetFloat("_EdgeSmoothness", moonEdgeSmoothness);
        mat.SetFloat("_BlendSharpness", moonBlendSharpness);
        mat.SetFloat("_Octaves", moonNoiseOctaves);
        return mat;
    }

    /// A soft glow card sitting at the moon's own centre. Centre depth, not in front: the sphere
    /// is opaque and writes depth, so the half of the card behind it fails the depth test and
    /// only the glow that spills past the rim survives - which is what a halo is.
    ///
    /// It uses the starfield shader rather than an additive Unlit material for the same reason
    /// the stars do: an Unlit card 235 units out is past the fog's end distance and would be
    /// fogged away to nothing.
    private void BuildMoonHalo()
    {
        haloMaterial = new Material(Shader.Find("Custom/Starfield"));
        haloMaterial.SetTexture("_BaseMap", haloMask);
        haloMaterial.SetFloat("_Intensity", haloStrength);
        haloMaterial.SetFloat("_TwinkleAmount", 0f);
        haloMaterial.SetFloat("_TwinkleSpeed", 0f);

        // Built by hand rather than with PrimitiveType.Quad: the starfield shader reads vertex
        // colour as its tint and brightness, and Unity's built-in quad carries no colour stream.
        float half = moonRadius * haloSizeRatio;
        Color tint = Color.Lerp(secondaryColor, Color.white, 0.55f);
        haloMesh = new Mesh { name = "MoonHalo" };
        haloMesh.vertices = new[]
        {
            new Vector3(-half, -half, 0f), new Vector3(half, -half, 0f),
            new Vector3(half, half, 0f), new Vector3(-half, half, 0f),
        };
        haloMesh.uv = new[] { new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(1f, 1f), new Vector2(0f, 1f) };
        haloMesh.uv2 = new[] { Vector2.zero, Vector2.zero, Vector2.zero, Vector2.zero };
        haloMesh.colors = new[] { tint, tint, tint, tint };
        haloMesh.triangles = new[] { 0, 2, 1, 0, 3, 2 };
        haloMesh.RecalculateBounds();

        var go = new GameObject("MoonHalo");
        go.transform.SetParent(transform, false);
        go.transform.localPosition = moonPosition;
        go.AddComponent<MeshFilter>().sharedMesh = haloMesh;
        var mr = go.AddComponent<MeshRenderer>();
        mr.sharedMaterial = haloMaterial;
        mr.shadowCastingMode = ShadowCastingMode.Off;
        mr.receiveShadows = false;
    }

    // Everything here was made with new Material/new Mesh/new Texture2D at runtime, so a theme
    // swap destroying this object has to take the whole set with it.
    private void OnDestroy()
    {
        if (starMaterial != null) Destroy(starMaterial);
        if (moonMaterial != null) Destroy(moonMaterial);
        if (haloMaterial != null) Destroy(haloMaterial);
        if (starMesh != null) Destroy(starMesh);
        if (haloMesh != null) Destroy(haloMesh);
        if (starSprite != null) Destroy(starSprite);
    }
}
