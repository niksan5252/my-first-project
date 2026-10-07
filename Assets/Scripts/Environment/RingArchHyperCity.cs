using UnityEngine;

/// Theme HyperCity's ring (video 11): a highway gantry over the road - two pillars 6.8 m out, a
/// beam across at 7.6 m, braces in the corners - lit by a row of segment lights along the beam that
/// chase across once a bar, the lit one flashing on the kick. Skins differ only in the trim:
///   * SunsetPink: a second, lower beam;
///   * ToxicGreen: hazard-striped pillars (alternating lit and dark blocks);
///   * RioCarnival: lights in turn primary/secondary/gold;
///   * IceStorm: a sharp chevron on top of the beam;
///   * GoldFinal: a gold double beam and a second row of lights.
/// Light from ClubBeat (the song clock), so it is on the music in Play and in a recording.
public class RingArchHyperCity : MonoBehaviour, IThemeAccent
{
    [SerializeField] private HyperCitySkin skin;
    [SerializeField] private float halfBase = 6.8f;
    [SerializeField] private float top = 7.6f;
    [SerializeField] private float thickness = 0.18f;
    [SerializeField] private Color color = new Color(1f, 0.3f, 0.7f);
    [SerializeField, Range(0f, RemixGeo.MaxEmission)] private float emissionIntensity = 1.3f;

    private const int Lights = 12;

    private Material frameMat, lightMat, trimMat;
    private MeshRenderer[] frame, lights, lights2;
    private MaterialPropertyBlock mpb;

    private void Awake()
    {
        mpb = new MaterialPropertyBlock();
        frameMat = RemixGeo.Neon(color, emissionIntensity);
        lightMat = RemixGeo.Neon(Color.white, emissionIntensity);
        trimMat = RemixGeo.Neon(skin == HyperCitySkin.GoldFinal ? new Color(1f, 0.78f, 0.3f) : Color.white, emissionIntensity);
        Mesh cube = RemixGeo.Primitive(PrimitiveType.Cube), sphere = RemixGeo.Primitive(PrimitiveType.Sphere);
        var parts = new System.Collections.Generic.List<MeshRenderer>();

        float x = halfBase, y0 = -0.5f;
        if (skin == HyperCitySkin.ToxicGreen)
        {
            // Hazard pillars: blocks alternating lit / trim.
            for (int side = -1; side <= 1; side += 2)
                for (int i = 0; i < 8; i++)
                {
                    float a = Mathf.Lerp(y0, top, i / 8f), b = Mathf.Lerp(y0, top, (i + 1) / 8f);
                    parts.Add(RemixGeo.Beam(transform, "Pillar", cube, i % 2 == 0 ? frameMat : trimMat, new Vector3(side * x, a, 0f), new Vector3(side * x, b, 0f), thickness * 1.6f));
                }
        }
        else
        {
            parts.Add(RemixGeo.Beam(transform, "PillarL", cube, frameMat, new Vector3(-x, y0, 0f), new Vector3(-x, top, 0f), thickness * 1.4f));
            parts.Add(RemixGeo.Beam(transform, "PillarR", cube, frameMat, new Vector3(x, y0, 0f), new Vector3(x, top, 0f), thickness * 1.4f));
        }
        parts.Add(RemixGeo.Beam(transform, "Beam", cube, frameMat, new Vector3(-x, top, 0f), new Vector3(x, top, 0f), thickness * 1.6f));
        // Corner braces.
        parts.Add(RemixGeo.Beam(transform, "BraceL", cube, frameMat, new Vector3(-x, top - 1.6f, 0f), new Vector3(-x + 1.6f, top, 0f), thickness * 0.7f));
        parts.Add(RemixGeo.Beam(transform, "BraceR", cube, frameMat, new Vector3(x, top - 1.6f, 0f), new Vector3(x - 1.6f, top, 0f), thickness * 0.7f));

        if (skin == HyperCitySkin.SunsetPink || skin == HyperCitySkin.GoldFinal)
            parts.Add(RemixGeo.Beam(transform, "Beam2", cube, skin == HyperCitySkin.GoldFinal ? trimMat : frameMat,
                new Vector3(-x, top + 0.55f, 0f), new Vector3(x, top + 0.55f, 0f), thickness * 0.8f));
        if (skin == HyperCitySkin.IceStorm)
        {
            parts.Add(RemixGeo.Beam(transform, "ChevL", cube, trimMat, new Vector3(-2.4f, top + 0.2f, 0f), new Vector3(0f, top + 1.5f, 0f), thickness));
            parts.Add(RemixGeo.Beam(transform, "ChevR", cube, trimMat, new Vector3(2.4f, top + 0.2f, 0f), new Vector3(0f, top + 1.5f, 0f), thickness));
        }
        frame = parts.ToArray();

        lights = new MeshRenderer[Lights];
        for (int i = 0; i < Lights; i++)
        {
            float lx = Mathf.Lerp(-x + 0.9f, x - 0.9f, i / (float)(Lights - 1));
            lights[i] = RemixGeo.Beam(transform, "Light", cube, lightMat, new Vector3(lx - 0.22f, top - 0.32f, 0f), new Vector3(lx + 0.22f, top - 0.32f, 0f), 0.2f);
        }
        if (skin == HyperCitySkin.GoldFinal)
        {
            lights2 = new MeshRenderer[Lights];
            for (int i = 0; i < Lights; i++)
            {
                float lx = Mathf.Lerp(-x + 0.9f, x - 0.9f, i / (float)(Lights - 1));
                lights2[i] = RemixGeo.Beam(transform, "Light2", sphere, lightMat, new Vector3(lx, top + 0.95f, 0f), new Vector3(lx, top + 0.95f, 0f), 0.24f);
            }
        }
    }

    private void OnDestroy()
    {
        if (frameMat != null) Destroy(frameMat);
        if (lightMat != null) Destroy(lightMat);
        if (trimMat != null) Destroy(trimMat);
    }

    public void ApplyAccent(Color newColor, float newIntensity)
    {
        color = newColor;
        emissionIntensity = Mathf.Min(newIntensity, 1.4f);
    }

    private static readonly Color Gold = new Color(1f, 0.78f, 0.3f);

    private void Update()
    {
        if (frame == null) return;
        float beats = ClubBeat.BeatIndex + ClubBeat.BeatPhase;
        float energy = Mathf.Clamp(ClubBeat.Energy, 0f, 1.7f);
        float kick = ClubBeat.Pulse;
        float barPulse = Mathf.Pow(1f - Mathf.Repeat(beats, 4f) / 4f, 3f);

        float level = Mathf.Min(RemixGeo.MaxEmission, emissionIntensity * (0.5f + 0.45f * kick * energy + 0.3f * barPulse * energy));
        RemixGeo.SetEmission(frameMat, color, level);
        RemixGeo.SetEmission(trimMat, skin == HyperCitySkin.GoldFinal ? Gold : Color.Lerp(color, Color.white, 0.6f),
            Mathf.Min(RemixGeo.MaxEmission, emissionIntensity * (0.35f + 0.6f * barPulse * energy)));

        // The chase: across once per bar (back on odd bars), the lit light and its trail.
        int bar = Mathf.FloorToInt(beats / 4f);
        float head = Mathf.Repeat(beats, 4f) / 4f * Lights;
        if ((bar & 1) == 1) head = Lights - head;
        for (int i = 0; i < Lights; i++)
        {
            float d = Mathf.Abs(head - i);
            float trail = d < 2.5f ? 1f - d / 2.5f : 0f;
            Color lc = skin == HyperCitySkin.RioCarnival ? (i % 3 == 0 ? color : i % 3 == 1 ? new Color(0.1f, 1f, 0.4f) : Gold)
                     : Color.Lerp(color, Color.white, 0.55f);
            float l = Mathf.Min(RemixGeo.MaxEmission, emissionIntensity * (0.45f + 1.5f * trail * (0.6f + 0.4f * kick) * Mathf.Max(0.6f, energy)));
            mpb.SetColor(RemixGeo.EmissionId, lc * l);
            lights[i].SetPropertyBlock(mpb);
            if (lights2 != null)
            {
                float l2 = Mathf.Min(RemixGeo.MaxEmission, emissionIntensity * (0.4f + (((i + ClubBeat.BeatIndex) & 1) == 0 ? 1.4f * kick : 0f)));
                mpb.SetColor(RemixGeo.EmissionId, Gold * l2);
                lights2[i].SetPropertyBlock(mpb);
            }
        }
    }
}
