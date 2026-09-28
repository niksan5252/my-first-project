using System.Collections.Generic;
using DG.Tweening;
using UnityEngine;
using UnityEngine.Rendering;

/// Theme_Ocean's environment, "Ocean": a road along the sea floor.
///
///   * the water (the theme's skybox, Tunnel/OceanSky): deep navy below, the fog's blue at eye
///     level, a shimmering window of daylight overhead;
///   * the sea bed either side of the road (Tunnel/OceanCaustics, like the road itself): dark sand
///     under drifting sunlight caustics that travel with the road and run faster on each drop;
///   * shafts of sunlight slanting down from the surface (Kenney light-mask streaks through
///     Tunnel/ClubGlow), travelling with the road, swaying slowly and flaring on the strong beats
///     (1 and 3 of each bar);
///   * boulders and branching corals (both procedural: the kits' rocks are blocky and read as
///     boxes in silhouette) along both sides - dark
///     silhouettes against the blue, the corals' tips glowing softly and breathing over each bar;
///   * glowing jellyfish drifting along the road (procedural bells and tentacles, Tunnel/OceanJelly):
///     on every beat each one contracts, jets up a little and flares, then relaxes;
///   * bubbles (Kenney particle-pack ring) rising from the sea bed either side, a burst on every
///     kick while the track is going, a bigger one through each drop;
///
/// Everything in time with the music goes through SongTweens (DOTween, paused, durations in beats,
/// positioned every frame from ClubBeat's song time), and scales with ClubBeat.Energy.
public class OceanEnvironmentDecorator : MonoBehaviour, IEnvironmentDecorator, IThemePalette
{
    [Header("Materials")]
    [SerializeField] private Material seabedMaterial;
    [SerializeField] private Shader glowShader;
    [SerializeField] private Texture2D[] rayMasks;
    [SerializeField] private Texture2D tipMask;
    [SerializeField] private Shader jellyShader;
    [SerializeField] private Texture2D bubbleTexture;

    [Header("Colours")]
    // The sea's own light (rays, jellyfish, polyps). The theme's two colours are both warm - they
    // colour the cues, which must read on the blue and away from the cyan trainer.
    [SerializeField] private Color waterAccent = new Color(0.25f, 0.9f, 0.85f);
    [SerializeField] private Color jellyLavender = new Color(0.7f, 0.55f, 1f);

    [Header("Sea bed")]
    [SerializeField] private Vector2 seabedSize = new Vector2(300f, 360f);
    [SerializeField] private float seabedY = -0.08f;
    [SerializeField] private float causticRate = 0.35f;     // drift phase per second of song
    [SerializeField] private float causticDropBoost = 6f;   // extra phase over each drop
    [SerializeField] private float causticDropBeats = 16f;
    [SerializeField] private float causticKick = 0.35f;

    [Header("Sun rays")]
    [SerializeField] private int rayCount = 18;
    [SerializeField] private float rayTop = 19f;
    [SerializeField] private float rayTilt = 16f;
    [SerializeField] private float rayBase = 0.22f;
    [SerializeField] private float rayFlash = 0.5f;
    [SerializeField] private float behindCamera = 12f;

    [Header("Rocks and corals")]
    [SerializeField] private Vector2 reefX = new Vector2(7f, 15f);
    [SerializeField] private float reefSpacing = 7f;
    [SerializeField] private Color silhouetteColor = new Color(0.004f, 0.02f, 0.04f);

    [Header("Jellyfish")]
    [SerializeField] private int jellyCount = 16;
    [SerializeField] private float jellyDrift = 0.8f;       // share of the road's speed
    [SerializeField] private float jellyPulse = 0.9f;

    [Header("Bubbles")]
    [SerializeField] private int bubblesPerKick = 10;
    [SerializeField] private int bubblesPerDropKick = 26;

    [Header("Water")]
    [SerializeField] private float surfaceGlow = 0.55f;

    private Color primary = new Color(1f, 0.4f, 0.45f);
    private Color secondary = new Color(0.15f, 0.85f, 0.8f);
    private float emission = 1.2f;

    private bool built;
    private float scrollSpeed, tunnelLength;
    private Material sky, savedSky;
    private readonly SongTweens tweens = new SongTweens();

    // Written by the tweens, applied in LateUpdate.
    private float beatKick;            // 1 on the beat
    private float dropPhase;           // extra caustic drift over the latest drop
    private Tween dropTween;
    private float rayFlashValue;       // 1 on beats 1 and 3, falling over two beats
    private float raySway;             // -1..1 over four bars

    private class Ray { public Transform t; public MeshRenderer r; public float z, x, width, strength; }
    private readonly List<Ray> rays = new List<Ray>();
    private float span;
    private float reefBreath;          // 0..1..0 over a bar

    private class ReefPiece { public Transform t; public float z; }
    private readonly List<ReefPiece> reef = new List<ReefPiece>();
    private readonly List<MeshRenderer> coralTips = new List<MeshRenderer>();

    private class Jelly
    {
        public Transform root, bell;
        public MeshRenderer bellR, tentR;
        public Color color;
        public float z, x, y, size;
        public float squash, glow, bob;   // written by this jelly's tweens
    }
    private readonly List<Jelly> jellies = new List<Jelly>();
    private ParticleSystem[] bubbles;
    private int lastBubbleBeat = int.MinValue;
    private Camera cam;
    private MaterialPropertyBlock mpb;
    private static readonly int ColorId = Shader.PropertyToID("_Color");
    private static readonly int IntensityId = Shader.PropertyToID("_Intensity");

    private static readonly int ShimmerId = Shader.PropertyToID("_Shimmer");
    private static readonly int SurfaceGlowId = Shader.PropertyToID("_SurfaceGlow");
    private static readonly int HorizonId = Shader.PropertyToID("_HorizonColor");
    private static readonly int FadeColorId = Shader.PropertyToID("_FadeColor");
    private static readonly int ScrollSpeedId = Shader.PropertyToID("_SynthScrollSpeed");
    private static readonly int CausticPhaseId = Shader.PropertyToID("_OceanCausticPhase");
    private static readonly int CausticGainId = Shader.PropertyToID("_OceanCausticGain");

    public void ApplyPalette(Color primaryColor, Color secondaryColor, float emissionIntensity)
    {
        primary = primaryColor;
        secondary = secondaryColor;
        emission = Mathf.Min(emissionIntensity, 1.5f);
    }

    public void Initialize(float tunnelLength, float scrollSpeed)
    {
        if (built) return;
        built = true;
        this.tunnelLength = tunnelLength;
        this.scrollSpeed = scrollSpeed;
        if (FindAnyObjectByType<ClubBeat>() == null) gameObject.AddComponent<ClubBeat>();

        // The theme's skybox is an asset; animate a copy so Play never writes into it.
        savedSky = RenderSettings.skybox;
        if (savedSky != null && savedSky.shader.name == "Tunnel/OceanSky")
        {
            sky = new Material(savedSky) { name = savedSky.name + " (live)" };
            sky.SetColor(HorizonId, RenderSettings.fogColor);
            RenderSettings.skybox = sky;
        }
        Shader.SetGlobalFloat(ScrollSpeedId, scrollSpeed);
        Shader.SetGlobalFloat(CausticGainId, 1f);

        mpb = new MaterialPropertyBlock();
        span = tunnelLength + behindCamera + 10f;
        BuildSeabed();
        BuildRays();
        BuildReef();
        BuildJellies();
        BuildBubbles();
        BuildTweens();
    }

    private void BuildSeabed()
    {
        if (seabedMaterial == null) return;
        var bed = GameObject.CreatePrimitive(PrimitiveType.Plane);
        Destroy(bed.GetComponent<Collider>());
        bed.name = "Seabed";
        bed.transform.SetParent(transform, false);
        bed.transform.localPosition = new Vector3(0f, seabedY, seabedSize.y * 0.5f - 30f);
        bed.transform.localScale = new Vector3(seabedSize.x / 10f, 1f, seabedSize.y / 10f);
        var r = bed.GetComponent<MeshRenderer>();
        var m = new Material(seabedMaterial);
        m.SetColor(FadeColorId, RenderSettings.fogColor);
        r.sharedMaterial = m;
        r.shadowCastingMode = ShadowCastingMode.Off;
        r.receiveShadows = false;
    }

    private void BuildRays()
    {
        if (glowShader == null || rayMasks == null || rayMasks.Length == 0) return;
        var rnd = new System.Random(3);
        var stage = new GameObject("SunRays").transform;
        stage.SetParent(transform, false);
        var sunTint = Color.Lerp(waterAccent, Color.white, 0.45f);
        for (int i = 0; i < rayCount; i++)
        {
            var q = GameObject.CreatePrimitive(PrimitiveType.Quad);
            Destroy(q.GetComponent<Collider>());
            q.name = "SunRay";
            q.transform.SetParent(stage, false);
            var r = q.GetComponent<MeshRenderer>();
            var m = new Material(glowShader);
            m.SetTexture("_MainTex", rayMasks[i % rayMasks.Length]);
            m.SetFloat("_UseAlpha", 1f);
            // The masks are bright at the bottom; a shaft is brightest where it enters the water.
            m.SetTextureScale("_MainTex", new Vector2(1f, -1f));
            m.SetTextureOffset("_MainTex", new Vector2(0f, 1f));
            r.sharedMaterial = m;
            r.shadowCastingMode = ShadowCastingMode.Off;
            r.receiveShadows = false;
            mpb.Clear();
            mpb.SetColor(ColorId, sunTint);
            mpb.SetFloat(IntensityId, 0f);
            r.SetPropertyBlock(mpb);
            float side = (i % 2 == 0 ? -1f : 1f);
            rays.Add(new Ray
            {
                t = q.transform, r = r,
                z = (i + (float)rnd.NextDouble() * 0.6f) * span / rayCount,
                x = side * (1.5f + (float)rnd.NextDouble() * 9f),
                width = 2.5f + (float)rnd.NextDouble() * 3.5f,
                strength = 0.6f + 0.4f * (float)rnd.NextDouble(),
            });
        }
    }

    private void BuildReef()
    {
        var rnd = new System.Random(9);
        var stage = new GameObject("Reef").transform;
        stage.SetParent(transform, false);
        var dark = new Material(Shader.Find("Universal Render Pipeline/Unlit")) { name = "ReefSilhouette" };
        dark.SetColor("_BaseColor", silhouetteColor);
        var corals = new Mesh[4];
        var tipLists = new List<Vector3>[4];
        var boulders = new Mesh[3];
        for (int v = 0; v < boulders.Length; v++) boulders[v] = BoulderMesh(rnd);
        for (int v = 0; v < corals.Length; v++)
        {
            tipLists[v] = new List<Vector3>();
            corals[v] = CoralMesh(rnd, tipLists[v]);
        }
        int n = 0;
        for (int side = -1; side <= 1; side += 2)
            for (float z = 2f; z < span - behindCamera; z += reefSpacing * (0.7f + 0.6f * (float)rnd.NextDouble()))
            {
                var piece = new GameObject("ReefPiece").transform;
                piece.SetParent(stage, false);
                float x = side * Mathf.Lerp(reefX.x, reefX.y, (float)rnd.NextDouble());
                piece.localPosition = new Vector3(x, 0f, z);
                piece.localRotation = Quaternion.Euler(0f, (float)rnd.NextDouble() * 360f, 0f);
                bool coral = (n++ % 3) != 0;
                if (coral)
                {
                    int v = rnd.Next(corals.Length);
                    var go = new GameObject("Coral");
                    go.transform.SetParent(piece, false);
                    go.transform.localScale = Vector3.one * (1.1f + 0.9f * (float)rnd.NextDouble());
                    go.AddComponent<MeshFilter>().sharedMesh = corals[v];
                    var r = go.AddComponent<MeshRenderer>();
                    r.sharedMaterial = dark;
                    r.shadowCastingMode = ShadowCastingMode.Off;
                    // Glowing polyps at the branch tips, coral pink or aqua per coral.
                    var tint = rnd.NextDouble() < 0.5 ? primary : waterAccent;
                    foreach (var tip in tipLists[v])
                    {
                        var q = GlowQuad(go.transform, "Polyp", tipMask, tint);
                        q.transform.localPosition = tip;
                        q.transform.localScale = Vector3.one * 0.45f;
                        coralTips.Add(q);
                    }
                }
                else
                {
                    var rock = new GameObject("Boulder");
                    rock.transform.SetParent(piece, false);
                    float size = 1.5f + 2.2f * (float)rnd.NextDouble();
                    rock.transform.localScale = new Vector3(size * 1.5f, size, size * 1.2f);
                    rock.AddComponent<MeshFilter>().sharedMesh = boulders[rnd.Next(boulders.Length)];
                    var r = rock.AddComponent<MeshRenderer>();
                    r.sharedMaterial = dark;
                    r.shadowCastingMode = ShadowCastingMode.Off;
                }
                reef.Add(new ReefPiece { t = piece, z = z });
            }
    }

    private void BuildJellies()
    {
        if (jellyShader == null) return;
        var rnd = new System.Random(21);
        var stage = new GameObject("Jellyfish").transform;
        stage.SetParent(transform, false);
        var bellMesh = BellMesh();
        var tentMesh = TentacleMesh(rnd);
        Color[] tints = { waterAccent, primary, jellyLavender };
        for (int i = 0; i < jellyCount; i++)
        {
            var j = new Jelly();
            j.root = new GameObject("Jelly").transform;
            j.root.SetParent(stage, false);
            j.bell = new GameObject("Bell").transform;
            j.bell.SetParent(j.root, false);
            j.color = tints[i % tints.Length];
            j.bellR = JellyPart(j.bell, bellMesh, 0.12f, 1.2f);
            j.tentR = JellyPart(j.root, tentMesh, 0.5f, 0f);
            float side = i % 2 == 0 ? -1f : 1f;
            // Clear of the lanes and the notes: out to the sides, or high over the road.
            bool high = rnd.NextDouble() < 0.35;
            j.x = high ? side * (1f + 2.5f * (float)rnd.NextDouble()) : side * (4.2f + 5f * (float)rnd.NextDouble());
            j.y = high ? 5f + 2f * (float)rnd.NextDouble() : 2.2f + 4f * (float)rnd.NextDouble();
            j.z = (i + (float)rnd.NextDouble() * 0.7f) * span / jellyCount;
            j.size = 1.3f + 0.9f * (float)rnd.NextDouble();
            j.root.localScale = Vector3.one * j.size;
            j.root.localRotation = Quaternion.Euler((float)rnd.NextDouble() * 16f - 8f, (float)rnd.NextDouble() * 360f, (float)rnd.NextDouble() * 16f - 8f);
            jellies.Add(j);
        }
    }

    private MeshRenderer JellyPart(Transform parent, Mesh mesh, float core, float rim)
    {
        var go = new GameObject(mesh.name);
        go.transform.SetParent(parent, false);
        go.AddComponent<MeshFilter>().sharedMesh = mesh;
        var r = go.AddComponent<MeshRenderer>();
        var m = new Material(jellyShader);
        m.SetFloat("_Core", core);
        m.SetFloat("_Rim", rim);
        r.sharedMaterial = m;
        r.shadowCastingMode = ShadowCastingMode.Off;
        r.receiveShadows = false;
        return r;
    }

    /// A jellyfish bell: a flattened dome, open underneath, its rim gently scalloped.
    private static Mesh BellMesh()
    {
        const int rings = 9, seg = 24;
        var verts = new List<Vector3>();
        var tris = new List<int>();
        for (int i = 0; i <= rings; i++)
        {
            float th = i / (float)rings * 1.75f;          // 0 at the top to ~100 degrees
            for (int k = 0; k <= seg; k++)
            {
                float ph = k / (float)seg * Mathf.PI * 2f;
                float r = Mathf.Sin(th) * 0.55f * (i == rings ? 1f + 0.06f * Mathf.Cos(ph * 8f) : 1f);
                verts.Add(new Vector3(Mathf.Cos(ph) * r, Mathf.Cos(th) * 0.42f, Mathf.Sin(ph) * r));
            }
        }
        for (int i = 0; i < rings; i++)
            for (int k = 0; k < seg; k++)
            {
                int a = i * (seg + 1) + k, b = a + 1, c = a + seg + 1, d = c + 1;
                tris.Add(a); tris.Add(b); tris.Add(c);
                tris.Add(b); tris.Add(d); tris.Add(c);
            }
        var m = new Mesh { name = "JellyBell" };
        m.SetVertices(verts);
        m.SetTriangles(tris, 0);
        m.RecalculateNormals();
        m.RecalculateBounds();
        return m;
    }

    /// Tentacles hanging under the bell: thin wavy ribbons, crossed so they show from any side.
    private static Mesh TentacleMesh(System.Random rnd)
    {
        var verts = new List<Vector3>();
        var tris = new List<int>();
        var normals = new List<Vector3>();
        for (int t = 0; t < 7; t++)
        {
            float ang = t * Mathf.PI * 2f / 7f;
            var root = new Vector3(Mathf.Cos(ang) * 0.35f, -0.05f, Mathf.Sin(ang) * 0.35f);
            float len = 1.2f + 0.9f * (float)rnd.NextDouble();
            float phase = (float)rnd.NextDouble() * 6f;
            for (int plane = 0; plane < 2; plane++)
            {
                var across = plane == 0 ? Vector3.right : Vector3.forward;
                int start = verts.Count;
                const int steps = 10;
                for (int s = 0; s <= steps; s++)
                {
                    float f = s / (float)steps;
                    var c = root + new Vector3(Mathf.Sin(f * 5f + phase) * 0.12f * f, -f * len, Mathf.Cos(f * 4f + phase) * 0.1f * f);
                    float w = 0.035f * (1f - f * 0.7f);
                    verts.Add(c - across * w); verts.Add(c + across * w);
                    var n = Vector3.Cross(across, Vector3.down);
                    normals.Add(n); normals.Add(n);
                }
                for (int s = 0; s < steps; s++)
                {
                    int a = start + s * 2;
                    tris.Add(a); tris.Add(a + 2); tris.Add(a + 1);
                    tris.Add(a + 1); tris.Add(a + 2); tris.Add(a + 3);
                }
            }
        }
        var m = new Mesh { name = "JellyTentacles" };
        m.SetVertices(verts);
        m.SetNormals(normals);
        m.SetTriangles(tris, 0);
        m.RecalculateBounds();
        return m;
    }

    private void BuildBubbles()
    {
        if (bubbleTexture == null) return;
        var mat = new Material(Shader.Find("Universal Render Pipeline/Particles/Unlit")) { name = "Bubbles" };
        mat.SetTexture("_BaseMap", bubbleTexture);
        mat.SetColor("_BaseColor", new Color(0.55f, 0.85f, 0.95f, 1f));
        // Additive: the particle pack's shapes are white on black.
        mat.SetFloat("_Surface", 1f);
        mat.SetFloat("_Blend", 2f);
        mat.SetOverrideTag("RenderType", "Transparent");
        mat.SetInt("_SrcBlend", (int)BlendMode.One);
        mat.SetInt("_DstBlend", (int)BlendMode.One);
        mat.SetInt("_ZWrite", 0);
        mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        mat.renderQueue = (int)RenderQueue.Transparent;
        bubbles = new ParticleSystem[2];
        for (int s = 0; s < 2; s++)
        {
            var go = new GameObject("Bubbles");
            go.transform.SetParent(transform, false);
            go.transform.localPosition = new Vector3(s == 0 ? -4.6f : 4.6f, 0.1f, 26f);
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main;
            main.loop = false;
            main.playOnAwake = false;
            main.startLifetime = new ParticleSystem.MinMaxCurve(2.5f, 4.5f);
            main.startSpeed = 0f;
            main.startSize = new ParticleSystem.MinMaxCurve(0.07f, 0.22f);
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = 1500;
            main.gravityModifier = -0.05f;
            var emissionModule = ps.emission;
            emissionModule.rateOverTime = 0f;
            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Box;
            shape.scale = new Vector3(2.5f, 0.2f, 44f);
            var noise = ps.noise;
            noise.enabled = true;
            noise.strength = 0.35f;
            noise.frequency = 0.6f;
            // Rise, and travel with the road while rising.
            var vel = ps.velocityOverLifetime;
            vel.enabled = true;
            vel.space = ParticleSystemSimulationSpace.World;
            vel.x = new ParticleSystem.MinMaxCurve(-0.1f, 0.1f);
            vel.y = new ParticleSystem.MinMaxCurve(1.2f, 2.8f);
            vel.z = new ParticleSystem.MinMaxCurve(-scrollSpeed, -scrollSpeed);
            var col = ps.colorOverLifetime;
            col.enabled = true;
            var g = new Gradient();
            g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                      new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.1f), new GradientAlphaKey(1f, 0.8f), new GradientAlphaKey(0f, 1f) });
            col.color = g;
            var r = go.GetComponent<ParticleSystemRenderer>();
            r.material = mat;
            r.shadowCastingMode = ShadowCastingMode.Off;
            ps.Play();
            bubbles[s] = ps;
        }
    }

    /// A lumpy boulder: a unit sphere pushed in and out by noise, flattened at the bottom.
    private static Mesh BoulderMesh(System.Random rnd)
    {
        var temp = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        var src = temp.GetComponent<MeshFilter>().sharedMesh;
        Destroy(temp);
        var verts = src.vertices;
        float o = (float)rnd.NextDouble() * 50f;
        for (int i = 0; i < verts.Length; i++)
        {
            Vector3 d = verts[i].normalized;
            float n = Mathf.PerlinNoise(o + d.x * 1.7f + d.z * 0.9f, o + d.y * 1.7f - d.z * 0.6f) * 0.55f
                    + Mathf.PerlinNoise(o * 2f + d.x * 4.3f, d.y * 4.3f + d.z * 3.1f) * 0.18f;
            Vector3 p = d * (0.35f + n);
            if (p.y < -0.05f) p.y = -0.05f + (p.y + 0.05f) * 0.2f;
            verts[i] = p + Vector3.up * 0.1f;
        }
        var m = new Mesh { name = "Boulder" };
        m.vertices = verts;
        m.triangles = src.triangles;
        m.RecalculateBounds();
        return m;
    }

    /// A branching coral: a trunk splitting two or three ways, each branch splitting again, as
    /// tapered tubes; the tips are returned for the glowing polyps.
    private static Mesh CoralMesh(System.Random rnd, List<Vector3> tips)
    {
        var verts = new List<Vector3>();
        var tris = new List<int>();
        void Tube(Vector3 a, Vector3 b, float ra, float rb)
        {
            const int sides = 6;
            Vector3 dir = (b - a).normalized;
            Vector3 u = Vector3.Cross(dir, Mathf.Abs(dir.y) < 0.9f ? Vector3.up : Vector3.right).normalized;
            Vector3 w = Vector3.Cross(dir, u);
            int start = verts.Count;
            for (int k = 0; k < sides; k++)
            {
                float ang = k * Mathf.PI * 2f / sides;
                Vector3 off = u * Mathf.Cos(ang) + w * Mathf.Sin(ang);
                verts.Add(a + off * ra);
                verts.Add(b + off * rb);
            }
            for (int k = 0; k < sides; k++)
            {
                int i0 = start + k * 2, i1 = i0 + 1, j0 = start + ((k + 1) % sides) * 2, j1 = j0 + 1;
                tris.Add(i0); tris.Add(i1); tris.Add(j0);
                tris.Add(j0); tris.Add(i1); tris.Add(j1);
            }
        }
        void Branch(Vector3 from, Vector3 dir, float length, float radius, int depth)
        {
            Vector3 to = from + dir * length;
            Tube(from, to, radius, radius * 0.7f);
            if (depth == 0) { tips.Add(to); return; }
            int kids = 2 + rnd.Next(2);
            for (int c = 0; c < kids; c++)
            {
                float yaw = (c + (float)rnd.NextDouble() * 0.5f) * 360f / kids;
                float pitch = 20f + 25f * (float)rnd.NextDouble();
                Vector3 nd = Quaternion.AngleAxis(yaw, Vector3.up) * Quaternion.AngleAxis(pitch, Vector3.right) * Vector3.up;
                nd = (nd + dir * 0.6f).normalized;
                Branch(to, nd, length * (0.62f + 0.15f * (float)rnd.NextDouble()), radius * 0.68f, depth - 1);
            }
        }
        Branch(Vector3.zero, Vector3.up, 1.1f, 0.16f, 3);
        var m = new Mesh { name = "Coral" };
        m.SetVertices(verts);
        m.SetTriangles(tris, 0);
        m.RecalculateBounds();
        return m;
    }

    private MeshRenderer GlowQuad(Transform parent, string name, Texture2D mask, Color color)
    {
        var q = GameObject.CreatePrimitive(PrimitiveType.Quad);
        Destroy(q.GetComponent<Collider>());
        q.name = name;
        q.transform.SetParent(parent, false);
        var r = q.GetComponent<MeshRenderer>();
        var m = new Material(glowShader);
        if (mask != null) m.SetTexture("_MainTex", mask);
        m.SetFloat("_UseAlpha", 1f);
        r.sharedMaterial = m;
        r.shadowCastingMode = ShadowCastingMode.Off;
        r.receiveShadows = false;
        mpb.Clear();
        mpb.SetColor(ColorId, color);
        mpb.SetFloat(IntensityId, 0f);
        r.SetPropertyBlock(mpb);
        return r;
    }

    private void BuildTweens()
    {
        tweens.Loop(DOTween.To(x => beatKick = x, 1f, 0f, 1f).SetEase(Ease.OutQuad), 1f);
        // On a drop the caustics race: extra drift phase, fastest in the middle of the span.
        // Sun rays: a flare on the strong beats (1 and 3), a slow sway over four bars.
        tweens.Loop(DOTween.To(x => rayFlashValue = x, 1f, 0f, 2f).SetEase(Ease.OutQuad), 2f);
        tweens.Loop(DOTween.Sequence()
            .Append(DOTween.To(x => raySway = x, -1f, 1f, 8f).SetEase(Ease.InOutSine))
            .Append(DOTween.To(x => raySway = x, 1f, -1f, 8f).SetEase(Ease.InOutSine)), 16f);
        // Coral polyps breathe over each bar.
        tweens.Loop(DOTween.Sequence()
            .Append(DOTween.To(x => reefBreath = x, 0f, 1f, 2f).SetEase(Ease.InOutSine))
            .Append(DOTween.To(x => reefBreath = x, 1f, 0f, 2f).SetEase(Ease.InOutSine)), 4f);
        // Jellyfish: every beat a contraction - squashed and bright on the beat, relaxing through
        // it - and a slow bob over two bars, each on its own phase.
        for (int i = 0; i < jellies.Count; i++)
        {
            var j = jellies[i];
            float offset = (i % 4) * 0.12f;   // a little ripple across the swarm, not unison
            tweens.Loop(DOTween.To(x => j.squash = x, 1f, 0f, 1f).SetEase(Ease.OutCubic), 1f, offset);
            tweens.Loop(DOTween.To(x => j.glow = x, 1f, 0f, 1f).SetEase(Ease.OutQuad), 1f, offset);
            tweens.Loop(DOTween.Sequence()
                .Append(DOTween.To(x => j.bob = x, -1f, 1f, 4f).SetEase(Ease.InOutSine))
                .Append(DOTween.To(x => j.bob = x, 1f, -1f, 4f).SetEase(Ease.InOutSine)), 8f, i * 1.7f % 8f);
        }
        dropTween = tweens.Once(DOTween.To(x => dropPhase = x, 0f, causticDropBoost, causticDropBeats).SetEase(Ease.InOutSine));
    }

    private void UpdateRays(float songTime, float energy)
    {
        if (rays.Count == 0) return;
        if (cam == null) cam = Camera.main;
        Vector3 camPos = cam != null ? cam.transform.position : Vector3.zero;
        float travel = songTime * scrollSpeed;
        float tilt = rayTilt + 4f * raySway;
        float level = rayBase + rayFlash * rayFlashValue * Mathf.Clamp01(energy);
        float length = rayTop / Mathf.Cos(tilt * Mathf.Deg2Rad) + 2f;
        foreach (var ray in rays)
        {
            float z = Mathf.Repeat(ray.z - travel + behindCamera, span) - behindCamera;
            // The shaft's axis runs from high up on one side down to the sea bed.
            Vector3 axis = Quaternion.Euler(0f, 0f, tilt) * Vector3.down;
            Vector3 top = new Vector3(ray.x - axis.x * length * 0.5f, rayTop, z);
            Vector3 centre = top + axis * (length * 0.5f);
            ray.t.position = transform.TransformPoint(centre);
            // Turn about its own axis to face the camera (a cylindrical billboard).
            Vector3 up = -axis;
            Vector3 toCam = camPos - ray.t.position;
            Vector3 normal = Vector3.ProjectOnPlane(toCam, up).normalized;
            if (normal.sqrMagnitude > 0.001f) ray.t.rotation = Quaternion.LookRotation(-normal, up);
            ray.t.localScale = new Vector3(ray.width, length, 1f);
            ray.r.GetPropertyBlock(mpb);
            mpb.SetFloat(IntensityId, Mathf.Min(level * ray.strength, 1.2f));
            ray.r.SetPropertyBlock(mpb);
        }
    }

    private void UpdateReef(float songTime, float energy)
    {
        float travel = songTime * scrollSpeed;
        foreach (var p in reef)
        {
            var pos = p.t.localPosition;
            pos.z = Mathf.Repeat(p.z - travel + behindCamera, span) - behindCamera;
            p.t.localPosition = pos;
        }
        if (cam == null) cam = Camera.main;
        Vector3 camPos = cam != null ? cam.transform.position : Vector3.zero;
        float level = 0.35f + 0.45f * reefBreath * (0.5f + 0.5f * Mathf.Clamp01(energy));
        foreach (var tip in coralTips)
        {
            tip.transform.rotation = Quaternion.LookRotation(tip.transform.position - camPos);
            tip.GetPropertyBlock(mpb);
            mpb.SetFloat(IntensityId, level);
            tip.SetPropertyBlock(mpb);
        }
    }

    private void UpdateJellies(float songTime, float energy)
    {
        float travel = songTime * scrollSpeed * jellyDrift;
        float e = Mathf.Clamp01(energy);
        foreach (var j in jellies)
        {
            float z = Mathf.Repeat(j.z - travel + behindCamera, span) - behindCamera;
            float jet = 0.25f * (1f - j.squash);          // rises after each contraction
            j.root.localPosition = new Vector3(j.x, j.y + 0.35f * j.bob + jet * j.size, z);
            float sq = jellyPulse * 0.22f * j.squash * (0.4f + 0.6f * e);
            j.bell.localScale = new Vector3(1f + sq * 0.6f, 1f - sq, 1f + sq * 0.6f);
            float level = 0.8f + 0.9f * j.glow * e;
            SetJelly(j.bellR, j.color, level);
            SetJelly(j.tentR, j.color, level * 0.6f);
        }
    }

    private void SetJelly(MeshRenderer r, Color c, float level)
    {
        r.GetPropertyBlock(mpb);
        mpb.SetColor(ColorId, c);
        mpb.SetFloat(IntensityId, Mathf.Min(level, 1.8f));
        r.SetPropertyBlock(mpb);
    }

    /// A burst on every kick (i.e. beat) while the track is going; bigger through a drop.
    private void UpdateBubbles(float energy)
    {
        if (bubbles == null) return;
        int beat = ClubBeat.BeatIndex;
        if (beat == lastBubbleBeat) return;
        lastBubbleBeat = beat;
        if (energy < 0.7f) { if ((beat & 3) == 0) foreach (var ps in bubbles) ps.Emit(3); return; }
        int n = energy > 1.05f ? bubblesPerDropKick : bubblesPerKick;
        foreach (var ps in bubbles) ps.Emit(n);
    }

    private void OnDestroy()
    {
        tweens.KillAll();
        if (sky != null)
        {
            if (RenderSettings.skybox == sky) RenderSettings.skybox = savedSky;
            Destroy(sky);
        }
    }

    private void LateUpdate()
    {
        if (!built) return;
        float beats = ClubBeat.BeatIndex + ClubBeat.BeatPhase;
        tweens.SeekLoops(beats);
        // Before the first drop there is no boost yet (BeatsSinceDrop is +inf, which Seek reads as 'long over').
        float sinceDrop = ClubBeat.BeatsSinceDrop;
        SongTweens.Seek(dropTween, float.IsInfinity(sinceDrop) ? -1f : sinceDrop);
        float energy = Mathf.Clamp(ClubBeat.Energy, 0f, 1.6f);
        float t = ClubBeat.SongTime;

        // Phase = steady drift + a full boost for every earlier drop + the current drop's share.
        float phase = t * causticRate + ClubBeat.DropsBefore * causticDropBoost + dropPhase;
        Shader.SetGlobalFloat(CausticPhaseId, phase);
        Shader.SetGlobalFloat(CausticGainId, 0.75f + 0.25f * energy + causticKick * beatKick * energy);

        UpdateRays(t, energy);
        UpdateReef(t, energy);
        UpdateJellies(t, energy);
        UpdateBubbles(energy);

        if (sky != null)
        {
            sky.SetFloat(ShimmerId, phase * 0.6f);
            sky.SetFloat(SurfaceGlowId, surfaceGlow * (0.85f + 0.15f * energy));
        }
    }
}
