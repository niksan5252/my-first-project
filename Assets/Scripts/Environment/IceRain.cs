using UnityEngine;
using UnityEngine.Rendering;

/// Theme_IceCity's rain: pale blue streaks falling along the whole tunnel, a little heavier on the
/// beat (ClubBeat). Lives on the theme's environment prefab and builds itself in Start, taking the
/// tunnel's length and speed from the NeonTunnelGenerator, so the prefab's own decorator stays the
/// Cyberpunk street's.
public class IceRain : MonoBehaviour
{
    [SerializeField] private Color rainColor = new Color(0.65f, 0.82f, 1f, 0.55f);
    [SerializeField] private float baseRate = 1400f;
    [SerializeField] private float beatRate = 900f;

    private ParticleSystem rain;

    private void Start()
    {
        var gen = FindAnyObjectByType<NeonTunnelGenerator>();
        float length = gen != null ? gen.TunnelLength : 120f;
        float speed = gen != null ? gen.ScrollSpeed : 12f;

        var go = new GameObject("Rain");
        go.transform.SetParent(transform, false);
        go.transform.localPosition = new Vector3(0f, 14f, length * 0.35f);
        rain = go.AddComponent<ParticleSystem>();
        rain.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        var main = rain.main;
        main.loop = true;
        main.startLifetime = 1.1f;
        main.startSpeed = 0f;
        main.startSize = new ParticleSystem.MinMaxCurve(0.03f, 0.05f);
        main.gravityModifier = 2.4f;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.maxParticles = 8000;
        main.startColor = rainColor;
        var shape = rain.shape;
        shape.shapeType = ParticleSystemShapeType.Box;
        shape.scale = new Vector3(22f, 0.1f, length * 0.7f);
        var vel = rain.velocityOverLifetime;
        vel.enabled = true;
        vel.space = ParticleSystemSimulationSpace.World;
        vel.x = new ParticleSystem.MinMaxCurve(-0.5f);
        vel.y = new ParticleSystem.MinMaxCurve(-6f);
        vel.z = new ParticleSystem.MinMaxCurve(-speed * 0.9f);
        var r = go.GetComponent<ParticleSystemRenderer>();
        var m = new Material(Shader.Find("Universal Render Pipeline/Particles/Unlit"));
        m.SetFloat("_Surface", 1f);
        m.SetFloat("_Blend", 2f);
        m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        m.EnableKeyword("_BLENDMODE_ADD");
        m.SetFloat("_SrcBlend", (float)BlendMode.One);
        m.SetFloat("_DstBlend", (float)BlendMode.One);
        m.SetFloat("_ZWrite", 0f);
        m.renderQueue = (int)RenderQueue.Transparent;
        r.sharedMaterial = m;
        r.renderMode = ParticleSystemRenderMode.Stretch;
        r.velocityScale = 0.04f;
        r.lengthScale = 4f;
        r.shadowCastingMode = ShadowCastingMode.Off;
        rain.Play();
    }

    private void Update()
    {
        if (rain == null) return;
        var em = rain.emission;
        em.rateOverTime = baseRate + beatRate * ClubBeat.Pulse * Mathf.Clamp(ClubBeat.Energy, 0f, 1.5f);
    }
}
