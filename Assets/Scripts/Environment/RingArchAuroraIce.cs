using DG.Tweening;
using UnityEngine;
using UnityEngine.Rendering;

/// Theme_Aurora's ring: a pointed arch of faceted ice, lit from inside - long hexagonal crystal
/// beams following a gothic outline, with small crystal clusters breaking out at the shoulders
/// and the apex. One mesh per ring, flat-shaded, through Tunnel/AuroraCrystal.
///
/// In time with the music, through SongTweens (DOTween, paused, positioned from the song clock):
/// on every kick the inner light rises softly and fades back over the beat - a glow, never a
/// strobe - and the whole tunnel of arches breathes a little with each bar. Scaled by ClubBeat's
/// energy. The generator alternates the theme's two colours ring by ring; the arch maps them onto
/// the aurora's own emerald and violet.
///
/// Clearances: the feet stand 4.2 m out and the arch leans out to 4.6 m at 2.6 m up, clear of the
/// road, the outer lanes' high notes (2.25 m out, 2.45 m up) and a Dodge wall (3 m, 2.1 m); at
/// 3.4 m up it is 3.8 m out; the apex is at 5.9 m.
public class RingArchAuroraIce : MonoBehaviour, IThemeAccent
{
    [SerializeField] private Color emerald = new Color(0.15f, 1f, 0.55f);
    [SerializeField] private Color violet = new Color(0.75f, 0.35f, 1f);
    [SerializeField] private float beamRadius = 0.14f;
    [SerializeField] private float baseGlow = 0.55f;
    [SerializeField] private float kickGlow = 0.5f;

    private Color color;
    private MeshRenderer mr;
    private Material mat;
    private readonly SongTweens tweens = new SongTweens();
    private float kick, breath;
    private static Mesh sharedMesh;
    private static readonly int ColorId = Shader.PropertyToID("_Color");
    private static readonly int GlowId = Shader.PropertyToID("_Glow");

    private void Awake()
    {
        color = emerald;
        if (sharedMesh == null) sharedMesh = ArchMesh(beamRadius);
        var go = new GameObject("IceArch");
        go.transform.SetParent(transform, false);
        go.AddComponent<MeshFilter>().sharedMesh = sharedMesh;
        mr = go.AddComponent<MeshRenderer>();
        // Its own material rather than a property block: the generator's ring flash writes property
        // blocks on ring renderers and would wipe a colour kept there.
        mat = new Material(Shader.Find("Tunnel/AuroraCrystal"));
        mr.sharedMaterial = mat;
        mr.shadowCastingMode = ShadowCastingMode.Off;
        mr.receiveShadows = false;

        tweens.Loop(DOTween.Sequence()
            .Append(DOTween.To(x => kick = x, 0f, 1f, 0.12f).SetEase(Ease.OutSine))
            .Append(DOTween.To(x => kick = x, 1f, 0f, 0.88f).SetEase(Ease.InOutSine)), 1f);
        tweens.Loop(DOTween.Sequence()
            .Append(DOTween.To(x => breath = x, 0f, 1f, 2f).SetEase(Ease.InOutSine))
            .Append(DOTween.To(x => breath = x, 1f, 0f, 2f).SetEase(Ease.InOutSine)), 4f);
        Apply();
    }

    private void OnDestroy()
    {
        tweens.KillAll();
        if (mat != null) Destroy(mat);
    }

    public void ApplyAccent(Color newColor, float newIntensity)
    {
        // Rose (reddish) -> the aurora's violet; spring green -> its emerald.
        color = newColor.r > newColor.g ? violet : emerald;
        Apply();
    }

    private void Update()
    {
        if (mr == null) return;
        tweens.SeekLoops(ClubBeat.BeatIndex + ClubBeat.BeatPhase);
        Apply();
    }

    private void Apply()
    {
        if (mat == null) return;
        float e = Mathf.Clamp01(ClubBeat.Energy);
        mat.SetColor(ColorId, color);
        mat.SetFloat(GlowId, baseGlow * (0.75f + 0.25f * e) + kickGlow * kick * e + 0.1f * breath);
    }

    /// The arch: hexagonal beams along a pointed outline, plus crystal clusters at the joints.
    private static Mesh ArchMesh(float r)
    {
        var verts = new System.Collections.Generic.List<Vector3>();
        var tris = new System.Collections.Generic.List<int>();
        void Face(Vector3 a, Vector3 b, Vector3 c)
        {
            int i = verts.Count;
            verts.Add(a); verts.Add(b); verts.Add(c);
            tris.Add(i); tris.Add(i + 1); tris.Add(i + 2);
        }
        void Beam(Vector3 a, Vector3 b, float radius)
        {
            Vector3 dir = (b - a).normalized;
            Vector3 u = Vector3.Cross(dir, Vector3.forward).normalized;
            if (u.sqrMagnitude < 0.01f) u = Vector3.right;
            Vector3 w = Vector3.Cross(dir, u);
            var ra = new Vector3[6];
            var rb = new Vector3[6];
            for (int s = 0; s < 6; s++)
            {
                float ang = s * Mathf.PI / 3f;
                Vector3 o = (u * Mathf.Cos(ang) + w * Mathf.Sin(ang)) * radius;
                ra[s] = a + o;
                rb[s] = b + o;
            }
            for (int s = 0; s < 6; s++)
            {
                int s2 = (s + 1) % 6;
                Face(ra[s], rb[s], ra[s2]);
                Face(ra[s2], rb[s], rb[s2]);
                Face(a - dir * radius, ra[s], ra[s2]);       // pointed caps, like a crystal's end
                Face(b + dir * radius, rb[s2], rb[s]);
            }
        }
        void Spike(Vector3 at, Vector3 dir, float length, float radius)
        {
            dir.Normalize();
            Vector3 u = Vector3.Cross(dir, Vector3.forward).normalized;
            if (u.sqrMagnitude < 0.01f) u = Vector3.right;
            Vector3 w = Vector3.Cross(dir, u);
            var ring = new Vector3[6];
            for (int s = 0; s < 6; s++)
            {
                float ang = s * Mathf.PI / 3f;
                ring[s] = at + (u * Mathf.Cos(ang) + w * Mathf.Sin(ang)) * radius + dir * length * 0.7f;
            }
            Vector3 tip = at + dir * length;
            for (int s = 0; s < 6; s++)
            {
                int s2 = (s + 1) % 6;
                Face(at, ring[s], ring[s2]);
                Face(ring[s], tip, ring[s2]);
            }
        }

        Vector3[] outline =
        {
            new Vector3(-4.2f, -0.3f, 0f), new Vector3(-4.6f, 2.6f, 0f), new Vector3(-2.3f, 5.0f, 0f),
            new Vector3(0f, 5.9f, 0f),
            new Vector3(2.3f, 5.0f, 0f), new Vector3(4.6f, 2.6f, 0f), new Vector3(4.2f, -0.3f, 0f),
        };
        for (int i = 0; i < outline.Length - 1; i++) Beam(outline[i], outline[i + 1], r);
        // Crystal clusters breaking out of the joints, pointing away from the road.
        for (int side = -1; side <= 1; side += 2)
        {
            var shoulder = new Vector3(side * 4.6f, 2.6f, 0f);
            Spike(shoulder, new Vector3(side * 1f, 0.35f, 0.1f), 0.9f, 0.14f);
            Spike(shoulder, new Vector3(side * 0.7f, 0.8f, -0.2f), 0.65f, 0.1f);
            var upper = new Vector3(side * 2.3f, 5.0f, 0f);
            Spike(upper, new Vector3(side * 0.6f, 1f, 0.15f), 0.7f, 0.11f);
        }
        var apex = new Vector3(0f, 5.9f, 0f);
        Spike(apex, Vector3.up, 1.1f, 0.16f);
        Spike(apex, new Vector3(0.45f, 1f, 0f), 0.7f, 0.1f);
        Spike(apex, new Vector3(-0.45f, 1f, 0f), 0.7f, 0.1f);

        var m = new Mesh { name = "IceArch" };
        m.SetVertices(verts);
        m.SetTriangles(tris, 0);
        m.RecalculateNormals();
        m.RecalculateBounds();
        return m;
    }
}
