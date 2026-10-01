using System.Collections.Generic;
using DG.Tweening;
using UnityEngine;

/// Theme_IceCity's ring: a hexagon where the Cyberpunk street has an octagon - two vertical legs
/// into the road's edge, then the hexagon's upper three sides - with a thinner inner stroke and
/// three icicles hanging from the top edge. A glint chases around the top vertices on the
/// eighths and the ring brightens on the beat (SongTweens, beats from ClubBeat).
///
/// Clearances: side vertices at +-4.6 m out, 1.6 m up; the top edge is 5.6 m up. The legs stand
/// clear of the 6 m road, the outer lanes' high notes (2.25 m out, 2.45 m up) and a Dodge wall
/// (3 m, 2.1 m); the icicles end 4.8 m up.
public class RingArchIceHex : MonoBehaviour, IThemeAccent
{
    [SerializeField] private float radius = 4.6f;
    [SerializeField] private float centerHeight = 1.6f;
    [SerializeField] private float beamThickness = 0.13f;
    [SerializeField] private Color color = new Color(0.7f, 0.92f, 1f);
    [SerializeField, Range(0f, RemixGeo.MaxEmission)] private float emissionIntensity = 1.3f;

    private Material frameMat, glintMat;
    private MeshRenderer[] frame, glints;
    private MaterialPropertyBlock mpb;
    private readonly SongTweens tweens = new SongTweens();
    private float kick;

    private void Awake()
    {
        mpb = new MaterialPropertyBlock();
        frameMat = RemixGeo.Neon(color, emissionIntensity);
        glintMat = RemixGeo.Neon(Color.white, emissionIntensity);
        Mesh cube = RemixGeo.Primitive(PrimitiveType.Cube), sphere = RemixGeo.Primitive(PrimitiveType.Sphere);

        // Regular hexagon, vertices at 0, 60 ... 300 deg around (0, centerHeight); only the top half is drawn.
        Vector3 V(float rad, int i)
        {
            float a = i * 60f * Mathf.Deg2Rad;
            return new Vector3(Mathf.Cos(a) * rad, centerHeight + Mathf.Sin(a) * rad, 0f);
        }
        var outer = new Vector3[4];
        for (int i = 0; i < 4; i++) outer[i] = V(radius, i);

        var list = new List<MeshRenderer>();
        list.Add(RemixGeo.Beam(transform, "LegR", cube, frameMat, new Vector3(radius, 0f, 0f), outer[0], beamThickness));
        for (int i = 0; i < 3; i++) list.Add(RemixGeo.Beam(transform, "Side", cube, frameMat, outer[i], outer[i + 1], beamThickness));
        list.Add(RemixGeo.Beam(transform, "LegL", cube, frameMat, outer[3], new Vector3(-radius, 0f, 0f), beamThickness));
        float ir = radius - 0.4f;
        for (int i = 0; i < 3; i++)
            list.Add(RemixGeo.Beam(transform, "Inner", cube, frameMat, V(ir, i), V(ir, i + 1), beamThickness * 0.5f));
        float top = centerHeight + radius * Mathf.Sin(60f * Mathf.Deg2Rad) - 0.05f;
        foreach (float x in new[] { -1.6f, 0.2f, 1.7f })
            list.Add(RemixGeo.Beam(transform, "Icicle", cube, frameMat, new Vector3(x, top, 0f), new Vector3(x + 0.05f, top - 0.75f, 0f), beamThickness * 0.45f));
        frame = list.ToArray();

        glints = new MeshRenderer[4];
        for (int i = 0; i < 4; i++)
        {
            var g = RemixGeo.Beam(transform, "Glint", sphere, glintMat, outer[i], outer[i], 0.3f);
            g.transform.localPosition = outer[i] + Vector3.back * 0.05f;
            glints[i] = g;
        }

        tweens.Loop(DOTween.To(x => kick = x, 1f, 0f, 1f).SetEase(Ease.OutExpo), 1f);
    }

    private void OnDestroy() { tweens.KillAll(); }

    public void ApplyAccent(Color newColor, float newIntensity)
    {
        color = newColor;
        emissionIntensity = Mathf.Min(newIntensity, 1.4f);
        if (frameMat == null) return;
        RemixGeo.SetEmission(frameMat, color, emissionIntensity);
    }

    private void Update()
    {
        if (frame == null) return;
        float beats = ClubBeat.BeatIndex + ClubBeat.BeatPhase;
        tweens.SeekLoops(beats);
        float energy = Mathf.Clamp(ClubBeat.Energy, 0f, 1.7f);
        mpb.SetColor(RemixGeo.EmissionId, color * Mathf.Min(emissionIntensity * (0.55f + 0.45f * kick * energy), RemixGeo.MaxEmission));
        foreach (var r in frame) r.SetPropertyBlock(mpb);

        int step = Mathf.FloorToInt(beats * 2f);
        for (int i = 0; i < glints.Length; i++)
        {
            int d = (((step - i) % 4) + 4) % 4;
            float lit = d == 0 ? 1f : d == 1 ? 0.4f : 0.08f;
            mpb.SetColor(RemixGeo.EmissionId, Color.white * Mathf.Min(emissionIntensity * lit * (0.6f + 0.5f * energy), RemixGeo.MaxEmission));
            glints[i].SetPropertyBlock(mpb);
        }
    }
}
