using UnityEngine;
using UnityEngine.Rendering;

/// Theme_GoldDisco's mirror ball: a big faceted sphere hanging over the far end of the road,
/// turning with the music (one full turn per 32 beats, positioned from ClubBeat so it is on the
/// song's clock) and flashing its facets on the beat, with a fan of light rays leaving it across
/// the tunnel. A fixed backdrop like the cyberpunk moon, so it never scrolls.
/// Colours come from this component, set on the theme's environment prefab (gold and white).
public class DiscoMirrorBall : MonoBehaviour
{
    [SerializeField] private Vector3 position = new Vector3(0f, 12f, 60f);
    [SerializeField] private float radius = 4.5f;
    [SerializeField] private Color gold = new Color(1f, 0.72f, 0.18f);
    [SerializeField] private Color white = new Color(1f, 0.97f, 0.88f);
    [SerializeField, Range(0f, 2.5f)] private float emission = 1.1f;
    [SerializeField] private int rays = 14;
    [SerializeField] private float rayLength = 30f;

    private Transform ball, rayRoot;
    private Material ballMat, rayMat;
    private Transform[] rayTs;
    private static readonly int EmissionId = Shader.PropertyToID("_EmissionColor");

    private void Start()
    {
        var root = new GameObject("MirrorBall").transform;
        root.SetParent(transform, false);
        root.localPosition = position;

        var sphere = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        Destroy(sphere.GetComponent<Collider>());
        sphere.name = "Ball";
        ball = sphere.transform;
        ball.SetParent(root, false);
        ball.localScale = Vector3.one * radius * 2f;

        // Facet texture: a grid of tiles in random brightness, so the turning sphere sparkles.
        const int w = 48, h = 24;
        var tex = new Texture2D(w, h, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Repeat };
        var rng = new System.Random(11);
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                float v = 0.25f + 0.75f * (float)rng.NextDouble();
                Color c = Color.Lerp(gold, white, (float)rng.NextDouble() * 0.6f) * v;
                tex.SetPixel(x, y, new Color(c.r, c.g, c.b, 1f));
            }
        tex.Apply();
        ballMat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
        ballMat.SetTexture("_BaseMap", tex);
        ballMat.SetColor("_BaseColor", Color.white);
        ballMat.SetFloat("_Metallic", 1f);
        ballMat.SetFloat("_Smoothness", 0.95f);
        ballMat.EnableKeyword("_EMISSION");
        ballMat.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
        ballMat.SetTexture("_EmissionMap", tex);
        ballMat.SetColor(EmissionId, Color.white * emission);
        ball.GetComponent<MeshRenderer>().sharedMaterial = ballMat;

        // The string it hangs from.
        var cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
        Destroy(cube.GetComponent<Collider>());
        cube.transform.SetParent(root, false);
        cube.transform.localPosition = new Vector3(0f, radius + 6f, 0f);
        cube.transform.localScale = new Vector3(0.12f, 12f, 0.12f);
        cube.GetComponent<MeshRenderer>().sharedMaterial = ballMat;

        // Rays: long thin additive quads from the ball, fanned toward the camera side.
        rayMat = new Material(Shader.Find("Universal Render Pipeline/Particles/Unlit"));
        rayMat.SetFloat("_Surface", 1f);
        rayMat.SetFloat("_Blend", 2f);
        rayMat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        rayMat.EnableKeyword("_BLENDMODE_ADD");
        rayMat.SetFloat("_SrcBlend", (float)BlendMode.One);
        rayMat.SetFloat("_DstBlend", (float)BlendMode.One);
        rayMat.SetFloat("_ZWrite", 0f);
        rayMat.renderQueue = (int)RenderQueue.Transparent;
        rayMat.SetColor("_BaseColor", gold * 0.35f);

        rayRoot = new GameObject("Rays").transform;
        rayRoot.SetParent(root, false);
        rayTs = new Transform[rays];
        for (int i = 0; i < rays; i++)
        {
            var q = GameObject.CreatePrimitive(PrimitiveType.Quad);
            Destroy(q.GetComponent<Collider>());
            q.transform.SetParent(rayRoot, false);
            var mr = q.GetComponent<MeshRenderer>();
            mr.sharedMaterial = rayMat;
            mr.shadowCastingMode = ShadowCastingMode.Off;
            // Quad faces -Z; stretch along local Y, pivot at the ball by offsetting half a length.
            float yaw = (i / (float)rays) * 360f;
            float pitch = Mathf.Lerp(-70f, -20f, (i % 3) / 2f);
            var holder = new GameObject("Ray").transform;
            holder.SetParent(rayRoot, false);
            holder.localRotation = Quaternion.Euler(pitch, yaw, 0f);
            q.transform.SetParent(holder, false);
            q.transform.localPosition = new Vector3(0f, 0f, rayLength * 0.5f);
            q.transform.localRotation = Quaternion.Euler(0f, 0f, 0f);
            q.transform.localScale = new Vector3(0.18f, rayLength, 1f);
            q.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            rayTs[i] = holder;
        }
    }

    private void Update()
    {
        if (ball == null) return;
        float beats = ClubBeat.BeatIndex + ClubBeat.BeatPhase;
        float angle = beats * (360f / 32f);
        ball.localRotation = Quaternion.Euler(0f, angle, 0f);
        rayRoot.localRotation = Quaternion.Euler(0f, -angle * 2f, 0f);
        float energy = Mathf.Clamp(ClubBeat.Energy, 0f, 1.5f);
        float pump = 0.6f + 0.4f * ClubBeat.Pulse;
        ballMat.SetColor(EmissionId, Color.white * (emission * pump * (0.7f + 0.5f * energy)));
        rayMat.SetColor("_BaseColor", gold * (0.12f + 0.3f * energy * ClubBeat.Pulse));
    }
}
