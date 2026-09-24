using UnityEngine;
using UnityEngine.Rendering;

/// The sci-fi background set-dressing for the neon tunnel — planet, starfield, nebula wash,
/// drifting ships — extracted verbatim out of NeonTunnelGenerator so it can be swapped for a
/// different theme's environment prefab without touching the tunnel's own (stable) geometry,
/// scrolling or hit-detection code. Instantiated by TunnelThemeApplier as the active theme's
/// environmentPrefab; ApplyPalette (if the theme wants a recolor) is called before Initialize.
public class SciFiEnvironmentDecorator : MonoBehaviour, IThemePalette, IEnvironmentDecorator
{
    private const float MaxEmissionIntensity = 2.5f; // hard ceiling so neon glow can never blow out to white

    // The only two accent hues in the whole palette (#FF00CC / #00E5FF), same as the tunnel's
    // own copies — kept as this decorator's own fields (not a live reference back to
    // NeonTunnelGenerator) so a future theme can recolor this environment independently.
    [SerializeField] private Color magentaColor = new Color(1f, 0f, 0.8f);
    [SerializeField] private Color cyanColor = new Color(0f, 0.898f, 1f);

    [Header("Planet")]
    [SerializeField] private Color planetColor = new Color(1f, 0f, 0.8f); // muted via planetEmissionIntensity, not a third hue
    [SerializeField] private float planetRadius = 9f;
    [SerializeField] private Vector3 planetOffset = new Vector3(-26f, 10f, 46f);
    [SerializeField, Range(0f, MaxEmissionIntensity)] private float planetEmissionIntensity = 0.9f;
    // Planet Generator Kit (Assets/PlanetGeneratorKit/) surface shader — a triplanar
    // continent-noise mask blended between the two accent colors and output as capped emission,
    // same as every other neon material here (the scene has zero Lights, so a diffuse shader
    // would render pure black regardless of its color).
    [SerializeField, Range(0.02f, 1f)] private float planetNoiseScale = 0.15f;
    [SerializeField, Range(0f, 1f)] private float planetLandThreshold = 0.5f;
    [SerializeField, Range(0.001f, 0.5f)] private float planetEdgeSmoothness = 0.05f;
    [SerializeField, Range(1f, 8f)] private float planetBlendSharpness = 4f;
    [SerializeField, Range(1f, 6f)] private float planetNoiseOctaves = 4f;

    [Header("Starfield")]
    // Real starfields are mostly faint specks with a scattered few bright ones, at a spread of
    // distances — the previous flat grid of same-size, same-color quads read as confetti. Each
    // star now gets its own brightness, size, tint and depth, all baked into one mesh's vertex
    // data so the whole sky stays a single draw call.
    [SerializeField] private int starCount = 1400;
    [SerializeField] private float starFieldNearRadius = 45f;
    [SerializeField] private float starFieldFarRadius = 110f;
    [SerializeField] private Color starColor = new Color(0f, 0.898f, 1f); // cyan, dimmed by distance/fog rather than a near-white 3rd hue
    [SerializeField, Range(0f, MaxEmissionIntensity)] private float starEmissionIntensity = 1.1f;
    // Angular size of a star's quad, before its own brightness scales it. The visible dot is much
    // smaller than the quad: most of the sprite is transparent falloff.
    [SerializeField] private float starAngularSize = 0.008f;
    // How strongly brightness is skewed toward the faint end. 1 = uniform; higher = the handful
    // of bright stars gets rarer, which is what stops the field looking like even confetti.
    [SerializeField, Range(1f, 6f)] private float starBrightnessFalloff = 3.2f;
    // How far a star's tint can stray from white toward one of the two accent hues. Stars are
    // mostly pale — white here is neutral, not a third palette color.
    [SerializeField, Range(0f, 1f)] private float starTintStrength = 0.55f;
    [SerializeField, Range(0f, 1f)] private float starTwinkleAmount = 0.3f;
    [SerializeField, Range(0f, 8f)] private float starTwinkleSpeed = 1.5f;

    [Header("Ships")]
    [SerializeField] private int shipCount = 4;
    [SerializeField] private float shipDriftSpeed = 0.5f;

    [Header("Nebula Wash")]
    // Small, dim, opaque color-wash spheres far behind the tunnel — distant background hints,
    // not foreground shapes. Deliberately kept BELOW bloomThreshold so they never bloom-bleed
    // into a big glowing blob; they should read as quiet color, not compete with the rings.
    [SerializeField] private Color nebulaColorA = new Color(1f, 0f, 0.8f); // magenta, muted via nebulaEmissionIntensity
    [SerializeField] private Color nebulaColorB = new Color(0f, 0.898f, 1f); // cyan, muted via nebulaEmissionIntensity
    [SerializeField] private float nebulaRadius = 10f;
    [SerializeField, Range(0f, MaxEmissionIntensity)] private float nebulaEmissionIntensity = 0.35f;

    private Transform[] shipTransforms;
    private Vector3[] shipBasePos;
    private float[] shipPhase;
    private float tunnelLength;
    private bool built;

    private void OnValidate()
    {
        planetEmissionIntensity = Mathf.Clamp(planetEmissionIntensity, 0f, MaxEmissionIntensity);
        starEmissionIntensity = Mathf.Clamp(starEmissionIntensity, 0f, MaxEmissionIntensity);
        nebulaEmissionIntensity = Mathf.Clamp(nebulaEmissionIntensity, 0f, MaxEmissionIntensity);
    }

    // Recolors every element that currently reads magentaColor/cyanColor (planet, nebula,
    // starfield) from the theme's two-color palette. Must be called BEFORE Initialize(), which
    // is when those fields actually get baked into materials/mesh colors.
    public void ApplyPalette(Color primary, Color secondary, float emissionIntensity)
    {
        magentaColor = primary;
        cyanColor = secondary;
        starColor = secondary;
        nebulaColorA = primary;
        nebulaColorB = secondary;
    }

    // Called once by TunnelThemeApplier right after Instantiate(). tunnelLength is only needed
    // for BuildShips' Z placement range; everything else is self-contained.
    public void Initialize(float tunnelLength, float scrollSpeed)
    {
        if (built) return;
        built = true;

        this.tunnelLength = tunnelLength;

        BuildStarfield();
        BuildNebulaWash();
        BuildPlanet();
        BuildShips();
    }

    private void Update()
    {
        if (!built) return;
        UpdateShips();
    }

    private void UpdateShips()
    {
        if (shipTransforms == null) return;
        for (int i = 0; i < shipTransforms.Length; i++)
        {
            Vector3 p = shipBasePos[i];
            p.x += Mathf.Sin(Time.time * shipDriftSpeed + shipPhase[i]) * 3f;
            p.y += Mathf.Sin(Time.time * shipDriftSpeed * 0.6f + shipPhase[i] * 1.3f) * 0.6f;
            shipTransforms[i].localPosition = p;
        }
    }

    private void BuildPlanet()
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        go.name = "Planet";
        Destroy(go.GetComponent<Collider>());
        go.transform.SetParent(transform, false);
        go.transform.localPosition = planetOffset;
        go.transform.localScale = Vector3.one * planetRadius * 2f;

        var mr = go.GetComponent<MeshRenderer>();
        mr.sharedMaterial = CreatePlanetSurfaceMaterial(cyanColor, magentaColor, planetEmissionIntensity);
        mr.shadowCastingMode = ShadowCastingMode.Off;
        mr.receiveShadows = false;
    }

    // The kit's noise mask picks between an ocean and a land color — always one of the two
    // accent hues, never a third color, so callers (the main Planet and the two NebulaWash
    // blobs) just pass which accent plays which role. Falls back to a flat emissive sphere
    // material if the kit shader isn't present, so a missing/deleted asset can't hard-break
    // the scene.
    private Material CreatePlanetSurfaceMaterial(Color oceanColor, Color landColor, float intensity)
    {
        var shader = Shader.Find("Custom/PlanetSurfaceNeon");
        if (shader == null) return CreateNeonMaterial(landColor, intensity);

        var mat = new Material(shader);
        mat.SetColor("_ColorOcean", oceanColor);
        mat.SetColor("_ColorLand", landColor);
        mat.SetFloat("_Intensity", intensity);
        mat.SetFloat("_Scale", planetNoiseScale);
        mat.SetFloat("_LandThreshold", planetLandThreshold);
        mat.SetFloat("_EdgeSmoothness", planetEdgeSmoothness);
        mat.SetFloat("_BlendSharpness", planetBlendSharpness);
        mat.SetFloat("_Octaves", planetNoiseOctaves);
        return mat;
    }

    // Two oversized, dim spheres well behind the tunnel mouth, using the same Planet Generator
    // Kit surface shader as the main Planet so all three read as one consistent planet look
    // instead of two flat-color circles next to a textured one. Their land/ocean colors are
    // swapped between the two so they stay visually distinct from each other; bloom scatter
    // still softens their silhouettes into a diffuse wash against the tunnel mouth.
    private void BuildNebulaWash()
    {
        BuildNebulaBlob(cyanColor, nebulaColorA, new Vector3(-18f, 22f, 70f));
        BuildNebulaBlob(magentaColor, nebulaColorB, new Vector3(24f, 14f, 85f));
    }

    private void BuildNebulaBlob(Color oceanColor, Color landColor, Vector3 offset)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        go.name = "NebulaWash";
        Destroy(go.GetComponent<Collider>());
        go.transform.SetParent(transform, false);
        go.transform.localPosition = offset;
        go.transform.localScale = Vector3.one * nebulaRadius * 2f;

        var mr = go.GetComponent<MeshRenderer>();
        mr.sharedMaterial = CreatePlanetSurfaceMaterial(oceanColor, landColor, nebulaEmissionIntensity);
        mr.shadowCastingMode = ShadowCastingMode.Off;
        mr.receiveShadows = false;
    }

    // A single combined mesh (one draw call) of small double-sided quads scattered over the
    // upper sky dome, well outside the tunnel's own footprint so it never distracts from it.
    private void BuildStarfield()
    {
        int quadCount = Mathf.Max(0, starCount);
        if (quadCount == 0) return;

        var vertices = new Vector3[quadCount * 4];
        var uvs = new Vector2[quadCount * 4];
        var phases = new Vector2[quadCount * 4];
        var colors = new Color[quadCount * 4];
        var triangles = new int[quadCount * 6];

        float near = Mathf.Min(starFieldNearRadius, starFieldFarRadius);
        float far = Mathf.Max(starFieldNearRadius, starFieldFarRadius);

        for (int i = 0; i < quadCount; i++)
        {
            Vector3 dir = Random.onUnitSphere;
            if (dir.y < 0.1f) dir.y = 0.1f + Mathf.Abs(dir.y) * 0.5f;
            dir.Normalize();

            float radius = Random.Range(near, far);
            Vector3 center = dir * radius;

            // Power curve, not a flat range: this is what makes most stars faint and only a few
            // bright, which is the difference between a sky and a scatter of identical dots.
            float magnitude = Mathf.Pow(Random.value, starBrightnessFalloff);

            // Depth fade — the far half of the shell reads dimmer, so the sky has layers rather
            // than sitting on one surface. Scene fog can't do this job here (see Starfield.shader).
            float depth = Mathf.InverseLerp(far, near, radius);
            float brightness = Mathf.Lerp(0.18f, 1f, magnitude) * Mathf.Lerp(0.55f, 1f, depth);

            // Quad size is angular, so a star at the back of the shell is not silently larger
            // on screen than an identical one at the front. Brighter stars read bigger.
            float size = radius * starAngularSize * Mathf.Lerp(0.7f, 2.6f, magnitude);

            // Mostly white, occasionally pulled toward one of the two accents. White is neutral
            // here rather than a third hue, and a starfield of pure cyan looks synthetic.
            Color accent = (Random.value < 0.5f) ? starColor : magentaColor;
            Color tint = Color.Lerp(Color.white, accent, Random.value * starTintStrength);

            Vector3 normal = -dir;
            Vector3 tangent = Vector3.Cross(normal, Vector3.up);
            if (tangent.sqrMagnitude < 0.001f) tangent = Vector3.Cross(normal, Vector3.right);
            tangent.Normalize();
            Vector3 bitangent = Vector3.Cross(normal, tangent);

            Vector3 right = tangent * size;
            Vector3 up = bitangent * size;

            int vi = i * 4;
            vertices[vi + 0] = center - right - up;
            vertices[vi + 1] = center + right - up;
            vertices[vi + 2] = center + right + up;
            vertices[vi + 3] = center - right + up;

            uvs[vi + 0] = new Vector2(0f, 0f);
            uvs[vi + 1] = new Vector2(1f, 0f);
            uvs[vi + 2] = new Vector2(1f, 1f);
            uvs[vi + 3] = new Vector2(0f, 1f);

            // Per-star twinkle phase and rate, so the field shimmers instead of blinking together.
            var phase = new Vector2(Random.value, Random.Range(0.6f, 1.6f));
            phases[vi + 0] = phase; phases[vi + 1] = phase; phases[vi + 2] = phase; phases[vi + 3] = phase;

            var vertexColor = new Color(tint.r, tint.g, tint.b, brightness);
            colors[vi + 0] = vertexColor; colors[vi + 1] = vertexColor;
            colors[vi + 2] = vertexColor; colors[vi + 3] = vertexColor;

            // Single winding only: the shader culls nothing, and drawing each quad twice under
            // additive blending would just double every star's brightness.
            int ti = i * 6;
            triangles[ti + 0] = vi + 0; triangles[ti + 1] = vi + 2; triangles[ti + 2] = vi + 1;
            triangles[ti + 3] = vi + 0; triangles[ti + 4] = vi + 3; triangles[ti + 5] = vi + 2;
        }

        var mesh = new Mesh { name = "Starfield" };
        mesh.indexFormat = (vertices.Length > 65000) ? IndexFormat.UInt32 : IndexFormat.UInt16;
        mesh.vertices = vertices;
        mesh.uv = uvs;
        mesh.uv2 = phases;
        mesh.colors = colors;
        mesh.triangles = triangles;
        mesh.RecalculateBounds();

        var starsGO = new GameObject("Starfield");
        starsGO.transform.SetParent(transform, false);
        var mf = starsGO.AddComponent<MeshFilter>();
        mf.sharedMesh = mesh;
        var mr = starsGO.AddComponent<MeshRenderer>();
        mr.sharedMaterial = CreateStarfieldMaterial();
        mr.shadowCastingMode = ShadowCastingMode.Off;
        mr.receiveShadows = false;
    }

    private Material CreateStarfieldMaterial()
    {
        var mat = new Material(Shader.Find("Custom/Starfield"));
        mat.SetTexture("_BaseMap", BuildStarSprite(64));
        mat.SetFloat("_Intensity", Mathf.Clamp(starEmissionIntensity, 0f, MaxEmissionIntensity));
        mat.SetFloat("_TwinkleAmount", starTwinkleAmount);
        mat.SetFloat("_TwinkleSpeed", starTwinkleSpeed);
        return mat;
    }

    /// A round star sprite: a tight bright core over a much wider, much fainter halo. The two
    /// terms matter — a single falloff gives either a hard dot with visible quad edges or a soft
    /// blob, whereas core-plus-halo is what actually reads as a point of light at this size.
    private static Texture2D BuildStarSprite(int size)
    {
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, true)
        {
            name = "StarSprite",
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
                float halo = Mathf.Pow(Mathf.Clamp01(1f - r), 5f) * 0.22f;
                float alpha = Mathf.Clamp01(core + halo);

                pixels[y * size + x] = new Color32(255, 255, 255, (byte)Mathf.RoundToInt(alpha * 255f));
            }
        }

        tex.SetPixels32(pixels);
        tex.Apply(true, false);
        return tex;
    }

    private void BuildShips()
    {
        int count = Mathf.Max(0, shipCount);
        shipTransforms = new Transform[count];
        shipBasePos = new Vector3[count];
        shipPhase = new float[count];
        if (count == 0) return;

        // Near-black silhouettes with just a hint of cyan rim, not a third hue.
        Material shipMat = CreateNeonMaterial(new Color(0f, 0.898f, 1f), 0.12f);

        for (int i = 0; i < count; i++)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = "Ship_" + i;
            Destroy(go.GetComponent<Collider>());
            go.transform.SetParent(transform, false);

            float side = (i % 2 == 0) ? -1f : 1f;
            Vector3 basePos = new Vector3(
                side * Random.Range(14f, 24f),
                Random.Range(4f, 9f),
                Random.Range(tunnelLength * 0.25f, tunnelLength * 0.7f));

            go.transform.localPosition = basePos;
            go.transform.localRotation = Quaternion.Euler(0f, Random.Range(-20f, 20f), Random.Range(-5f, 5f));
            go.transform.localScale = new Vector3(1.8f, 0.18f, 0.5f);

            var mr = go.GetComponent<MeshRenderer>();
            mr.sharedMaterial = shipMat;
            mr.shadowCastingMode = ShadowCastingMode.Off;
            mr.receiveShadows = false;

            shipTransforms[i] = go.transform;
            shipBasePos[i] = basePos;
            shipPhase[i] = Random.Range(0f, Mathf.PI * 2f);
        }
    }

    private Material CreateNeonMaterial(Color color, float intensity)
    {
        var mat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
        mat.SetColor("_BaseColor", color * 0.12f); // faint tinted surface; the glow itself comes from emission below
        mat.SetFloat("_Smoothness", 0.15f);
        mat.SetFloat("_Metallic", 0f);
        ApplyCappedEmission(mat, color, intensity);
        return mat;
    }

    private void ApplyCappedEmission(Material mat, Color color, float intensity)
    {
        float clamped = Mathf.Clamp(intensity, 0f, MaxEmissionIntensity);
        mat.EnableKeyword("_EMISSION");
        mat.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
        mat.SetColor("_EmissionColor", color * clamped);
    }
}
