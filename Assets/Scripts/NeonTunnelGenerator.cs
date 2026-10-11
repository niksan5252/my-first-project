using UnityEngine;
using UnityEngine.Rendering;

/// Procedurally builds a pooled, scrolling neon octagon tunnel at runtime and recycles
/// its ring/lane-dash segments instead of ever instantiating new ones after Start.
/// Also builds the static lane markings and a subtle parallax background (planet,
/// starfield, drifting ship silhouettes).
public class NeonTunnelGenerator : MonoBehaviour
{
    private const float MaxEmissionIntensity = 2.5f; // hard ceiling so neon glow can never blow out to white

    [Header("Tunnel Layout")]
    [SerializeField] private int ringCount = 36;
    [SerializeField] private float ringSpacing = 4f;
    [SerializeField] private float ringRadius = 5f;
    [SerializeField] private float beamThickness = 0.14f;
    [SerializeField] private float scrollSpeed = 12f;
    // How far below the floor's Y=0 surface each ring's lowest point sits, so the ring reads
    // as an overhead arch (only its upper arc visible) instead of a closed shape crossing the road.
    [SerializeField] private float ringGroundBurial = 0.9f;
    // Rings recycle (teleport to the tunnel's far end) once none of their beam segments are
    // still inside the camera's frustum, rather than at a fixed world-space Z. A flat distance
    // threshold (the previous approach) has to be hand-re-derived every time ringRadius, the
    // camera's FOV, or its position changes — it was, twice — and drifting stale silently
    // reintroduces either the road-overlap bug (too close) or a visible early "pop" (too far,
    // recycling while the ring still fills most of the screen — the FOV 70->72 change exposed
    // exactly this: the ratio was tuned for a narrower FOV/larger ringRadius and became far too
    // conservative once those changed). Testing the actual renderer bounds against the actual
    // camera frustum each frame makes the recycle point self-correcting instead.
    //
    // Swept frame-by-frame with the current ringRadius (3.5) and FOV (72): the ring stays
    // completely clear of the floor from Z=8 down through Z=2.5, and every one of its beam
    // renderers has left the frustum by Z=2 — so recycling only once truly invisible can never
    // reintroduce the overlap illusion (that only ever showed up much closer to the camera than
    // where the ring now naturally vanishes).
    [SerializeField] private float ringSpawnNearZ = 4f; // only seeds the INITIAL layout (closest ring's starting Z); steady-state spacing is governed entirely by the frustum test afterward
    // Guaranteed fallback recycle point (e.g. if Camera.main is ever missing) so a ring can
    // never end up stuck drifting behind the camera indefinitely.
    private const float RecycleHardFloorZ = -3f;
    private static readonly int EmissionColorId = Shader.PropertyToID("_EmissionColor");
    private Camera mainCamera;
    private MeshRenderer[][] ringRenderers;
    // Per-renderer emission each ring sits at when nothing is happening to it, captured once at
    // build time. FlashNear() adds to this and then restores exactly this value, which is why it
    // has to be recorded rather than recomputed: a themed arch prefab colors its own parts
    // through IThemeAccent and they are not all the same accent, so there is no single formula
    // to fall back to. Clearing the property block instead of restoring would wipe whatever
    // else that prefab put there.
    private Color[][] ringBaseEmission;
    private float[] ringFlashTimers;
    private Color[] ringFlashColors;
    private float ringFlashDuration;
    private float ringFlashStrength;
    private MaterialPropertyBlock ringFlashBlock;
    // Set by Configure(theme) before Build(). Null = the procedural octagon-beam rings below
    // (default). Non-null = BuildRings() instantiates this prefab per ring instead, letting a
    // theme swap the ring shape/mesh entirely without touching the recycle/frustum logic, which
    // works identically either way since it only depends on each ring's MeshRenderers.
    private GameObject ringArchPrefab;

    private bool RingOutOfFrustum(int index, Plane[] frustumPlanes)
    {
        var renderers = ringRenderers[index];
        for (int i = 0; i < renderers.Length; i++)
        {
            if (GeometryUtility.TestPlanesAABB(frustumPlanes, renderers[i].bounds)) return false;
        }
        return true;
    }

    [Header("Neon Ring Colors")]
    // Theme-sourced (see Configure): the only two accent hues in the whole palette — everything
    // else in this file is either one of these two, or near-black. Defaults here match the
    // original sci-fi palette (#FF00CC / #00E5FF) so the tunnel still renders sensibly if used
    // without ever calling Configure().
    private Color magentaColor = new Color(1f, 0f, 0.8f);
    private Color cyanColor = new Color(0f, 0.898f, 1f);
    private float ringEmissionIntensity = 1.5f;

    [Header("Floor & Lanes")]
    [SerializeField] private float floorWidth = 6f;
    // Grid "rungs" are real geometry (thin emissive beams, like the lane dividers), not a
    // texture: TunnelScene has no Light components, so the floor's diffuse path (_BaseColor/
    // _BaseMap) never shows regardless of what's set there, and _EmissionMap (a textured
    // emission mask) was confirmed, through extensive isolated testing, not to render at all on
    // this project's material instances — while plain _EmissionColor (no map) works immediately,
    // which is exactly how the rings/lane lines already glow. Geometry sidesteps both dead ends.
    [SerializeField] private float floorGridCellWorldSize = 2f;
    [SerializeField] private float floorGridRungWidth = 0.06f;
    [SerializeField] private int laneCount = 4;
    [SerializeField] private float laneLineWidth = 0.08f;
    [SerializeField] private float edgeLineWidth = 0.14f;
    [SerializeField] private float dashLength = 1f;
    [SerializeField] private float dashGap = 1f;

    // Theme-sourced (see Configure).
    private Material floorMaterialOverride;
    private Color floorColor = new Color(0.025f, 0.02f, 0.04f);
    private float floorMetallic = 0.6f;
    private float floorSmoothness = 0.85f;
    private Color floorGridLineColor = new Color(0f, 0.898f, 1f);
    private float floorGridGlintIntensity = 0.5f;
    private Color laneLineColor = new Color(1f, 0f, 0.8f);
    private float laneLineEmissionIntensity = 0.55f;

    // Theme-sourced (see Configure).
    private Color fogColor = new Color(0.05f, 0f, 0.1f);
    private float fogStartRatio = 0.35f;
    private float fogEndRatio = 0.95f;
    private Color ambientColor = new Color(0.015f, 0f, 0.03f);

    // Theme-sourced (see Configure). Air particle appearance/tuning; disabled when the theme
    // provides no preset (or the preset itself is disabled).
    private bool buildAirParticles;
    private ParticlePreset airParticles;

    // The floor is metallic/smooth (see CreateFloorMaterial) so it needs something to reflect.
    // With no scene Light, environment reflections fall back to RenderSettings.skybox — a
    // generic default sky, not our tunnel — unless a probe captures the actual rings. The
    // tunnel only exists once this script builds it at runtime, so an edit-time bake would see
    // nothing; RenderProbe() below bakes one real snapshot right after Build() instead.
    [Header("Reflection Probe (captures the rings onto the floor)")]
    [SerializeField] private bool buildReflectionProbe = true;
    [SerializeField] private float reflectionProbeZ = 6f;
    [SerializeField] private float reflectionProbeBoxSize = 14f;
    [SerializeField] private int reflectionProbeResolution = 128;

    private Transform[] ringRoots;
    private Transform[][] laneDashLines;
    private Mesh cubeMesh;
    private Material magentaMat;
    private Material cyanMat;
    private Material floorMat;
    private Material generatedFloorMat;
    private MeshRenderer floorRenderer;
    private Material rungMat;
    private Material laneMat;
    private float tunnelLength;
    private float recycleZ;
    private float dashSpan;
    private bool built;

    // Read-only layout info other systems (e.g. a cue spawner) sync against.
    // Pure getters over the serialized fields, valid before Build() ever runs.
    public float TunnelLength => ringCount * ringSpacing;
    public float ScrollSpeed => scrollSpeed;
    /// A playlist's speed (PlaylistDirector.Configure), before Build: the default is the scene's 12 m/s.
    public void SetScrollSpeed(float speed) { if (speed > 0.1f) scrollSpeed = speed; }
    public float FloorWidth => floorWidth;
    public int LaneCount => laneCount;
    // The floor's own grid pitch, so a cue that wants to be "N cells long" can be sized against
    // the squares the player actually sees rolling past rather than a second, drifting constant.
    public float FloorGridCellWorldSize => floorGridCellWorldSize;

    // Evenly divides floorWidth into laneCount equal lanes and returns lane i's center X.
    // For floorWidth=6, laneCount=4 this yields -2.25, -0.75, 0.75, 2.25: symmetric about
    // x=0 and spaced exactly one lane-width (1.5) apart.
    public float GetLaneCenterX(int laneIndex)
    {
        float laneWidth = floorWidth / laneCount;
        return -floorWidth * 0.5f + laneWidth * (laneIndex + 0.5f);
    }

    // No self-building Awake(): a TunnelThemeApplier owns startup order for this scene — it
    // calls Configure(theme) to push the active theme's values in, then Build() — so the theme
    // is always fully applied before any geometry/materials get created. Called directly with
    // no theme (or never called), the tunnel still builds fine using the defaults above.

    // Copies everything this generator needs out of the given theme. Must be called before
    // Build(); has no effect afterward, same as every other Configure-time setting here.
    public void Configure(TunnelTheme theme)
    {
        if (theme == null) return;

        magentaColor = theme.primaryColor;
        cyanColor = theme.secondaryColor;
        ringEmissionIntensity = theme.accentEmissionIntensity;

        ringArchPrefab = theme.ringArchPrefab;

        floorMaterialOverride = theme.floorMaterialOverride;
        floorColor = theme.floorColor;
        floorMetallic = theme.floorMetallic;
        floorSmoothness = theme.floorSmoothness;
        floorGridLineColor = theme.floorGridLineColor;
        floorGridGlintIntensity = theme.floorGridGlintIntensity;
        laneLineColor = theme.laneLineColor;
        laneLineEmissionIntensity = theme.laneLineEmissionIntensity;

        fogColor = theme.fogColor;
        fogStartRatio = theme.fogStartRatio;
        fogEndRatio = theme.fogEndRatio;
        ambientColor = theme.AmbientColor;

        airParticles = theme.airParticles;
        buildAirParticles = theme.airParticles != null && theme.airParticles.enabled;
    }

    private void Update()
    {
        if (!built) return;

        float delta = scrollSpeed * Time.deltaTime;

        UpdateRingFlashes();

        Plane[] frustumPlanes = mainCamera != null ? GeometryUtility.CalculateFrustumPlanes(mainCamera) : null;
        for (int i = 0; i < ringRoots.Length; i++)
        {
            Transform t = ringRoots[i];
            Vector3 pos = t.localPosition;
            pos.z -= delta;

            bool recycle = pos.z < RecycleHardFloorZ;
            if (!recycle && frustumPlanes != null && pos.z < ringSpawnNearZ)
                recycle = RingOutOfFrustum(i, frustumPlanes);

            if (recycle) pos.z += tunnelLength;
            t.localPosition = pos;
            // A playlist moving on to its next look: every ring that goes round from here comes
            // back in the new style, so the new arches arrive out of the distance, one by one.
            if (recycle && ringStyleSerial[i] != pendingRingSerial) RestyleRing(i);
        }

        for (int d = 0; d < laneDashLines.Length; d++)
        {
            Transform[] line = laneDashLines[d];
            for (int i = 0; i < line.Length; i++)
            {
                Transform t = line[i];
                Vector3 pos = t.localPosition;
                pos.z -= delta;
                if (pos.z < recycleZ) pos.z += dashSpan;
                t.localPosition = pos;
            }
        }
    }

    // Public: called explicitly by TunnelThemeApplier after Configure(), instead of self-firing
    // from Awake(). Post-processing and the environment prefab are no longer this generator's
    // concern at all — the Applier owns the Global Volume profile swap and the environment
    // Instantiate() directly, so this only ever builds the tunnel itself.
    public void Build()
    {
        if (built) return;
        built = true;

        tunnelLength = ringCount * ringSpacing;
        recycleZ = -ringSpacing;
        mainCamera = Camera.main;

        cubeMesh = GetSharedCubeMesh();
        magentaMat = CreateNeonMaterial(magentaColor, ringEmissionIntensity);
        cyanMat = CreateNeonMaterial(cyanColor, ringEmissionIntensity);
        if (floorMaterialOverride == null) generatedFloorMat = CreateFloorMaterial(floorColor, floorMetallic, floorSmoothness);
        floorMat = floorMaterialOverride != null ? floorMaterialOverride : generatedFloorMat;
        laneMat = CreateNeonMaterial(laneLineColor, laneLineEmissionIntensity);

        BuildRings();
        BuildFloor();
        BuildFloorGridRungs();
        BuildLaneLines();
        ConfigureFog();
        BuildReflectionProbe();
        BuildAirParticles();
    }

    private ReflectionProbe reflectionProbe;

    /// Re-takes the probe's snapshot - after a playlist has changed the sky and the scenery, or
    /// every smooth surface would go on reflecting the first theme's sky.
    public void RenderReflectionProbe()
    {
        if (reflectionProbe != null) reflectionProbe.RenderProbe();
    }

    private void BuildReflectionProbe()
    {
        if (!buildReflectionProbe) return;

        var go = new GameObject("Tunnel Reflection Probe");
        go.transform.SetParent(transform, false);
        go.transform.localPosition = new Vector3(0f, 1.6f, reflectionProbeZ);

        var probe = go.AddComponent<ReflectionProbe>();
        reflectionProbe = probe;
        probe.mode = UnityEngine.Rendering.ReflectionProbeMode.Baked;
        probe.size = new Vector3(reflectionProbeBoxSize, reflectionProbeBoxSize * 0.6f, reflectionProbeBoxSize);
        probe.center = Vector3.zero;
        probe.resolution = reflectionProbeResolution;
        probe.hdr = true;
        probe.intensity = 1f;
        probe.boxProjection = false;

        // A one-time runtime render, not a continuous cost: the rings/floor already exist at
        // this point in Build(), so this captures a real snapshot of them rather than the
        // empty scene an edit-time bake would see. It won't track the rings as they keep
        // scrolling afterward, but that's an accepted tradeoff for a baked (not realtime) probe.
        probe.RenderProbe();
    }

    // Small glowing motes drifting past the camera for a sense of speed/depth — a single
    // looping ParticleSystem, not per-frame Instantiate. The emitter's box shape spans the
    // tunnel's cross-section with a long Z depth so particles keep streaming continuously as
    // the camera "flies" through it, rather than emitting one puff and going empty.
    private void BuildAirParticles()
    {
        if (!buildAirParticles || airParticles == null) return;

        var go = new GameObject("Air Particles");
        go.transform.SetParent(transform, false);
        go.transform.localPosition = new Vector3(0f, 1.6f, tunnelLength * 0.35f);
        // Box shape's default emission direction is local +Z; facing the emitter backward makes
        // that world -Z, i.e. toward the camera, so plain Start Speed carries them the right way.
        go.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);

        var ps = go.AddComponent<ParticleSystem>();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear); // a freshly added system starts playing; main.* can't be touched until stopped

        var main = ps.main;
        main.loop = true;
        main.playOnAwake = true;
        main.startLifetime = new ParticleSystem.MinMaxCurve(airParticles.lifetime.x, airParticles.lifetime.y);
        main.startSpeed = new ParticleSystem.MinMaxCurve(airParticles.speed.x, airParticles.speed.y);
        main.startSize = new ParticleSystem.MinMaxCurve(airParticles.size.x, airParticles.size.y);
        main.startColor = new ParticleSystem.MinMaxGradient(magentaColor, cyanColor); // alternates per-particle, picked at random from the theme's two accent hues
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.maxParticles = 400;

        var emission = ps.emission;
        emission.rateOverTime = airParticles.rate;

        var shape = ps.shape;
        shape.shapeType = airParticles.shapeType;
        shape.scale = new Vector3(ringRadius * 1.6f, ringRadius * 1.2f, 60f);

        var psRenderer = ps.GetComponent<ParticleSystemRenderer>();
        // A theme can supply its own particle material (different texture/shape entirely); the
        // default is a plain unlit material tinted by each particle's own start color, capped so
        // the multiply can't push either accent hue toward white.
        Material mat = airParticles.particleMaterial;
        if (mat == null)
        {
            mat = new Material(Shader.Find("Universal Render Pipeline/Particles/Unlit"));
            float clamped = Mathf.Clamp(airParticles.emissionIntensity, 0f, MaxEmissionIntensity);
            mat.SetColor("_BaseColor", Color.white * clamped);
        }
        psRenderer.material = mat;
        psRenderer.renderMode = ParticleSystemRenderMode.Billboard;
        psRenderer.shadowCastingMode = ShadowCastingMode.Off;

        ps.Play();
    }

    private void BuildRings()
    {
        ringRoots = new Transform[ringCount];
        ringRenderers = new MeshRenderer[ringCount][];
        ringBaseEmission = new Color[ringCount][];
        ringFlashTimers = new float[ringCount];
        ringFlashColors = new Color[ringCount];
        ringStyleSerial = new int[ringCount];
        ringFlashBlock = new MaterialPropertyBlock();
        for (int i = 0; i < ringCount; i++)
            BuildRing(i, new Vector3(0f, 0f, ringSpawnNearZ + i * ringSpacing));
    }

    /// One ring in the current style at `localPos`: the theme's arch prefab, or the procedural
    /// octagon of beams when it has none.
    private void BuildRing(int i, Vector3 localPos)
    {
        Color accent = (i % 2 == 0) ? magentaColor : cyanColor;

        if (ringArchPrefab != null)
        {
            var archInstance = Instantiate(ringArchPrefab, transform).transform;
            archInstance.localPosition = localPos;
            ringRoots[i] = archInstance;
            if (archInstance.TryGetComponent<IThemeAccent>(out var themedAccent))
                themedAccent.ApplyAccent(accent, ringEmissionIntensity);
            ringRenderers[i] = archInstance.GetComponentsInChildren<MeshRenderer>();
            ringBaseEmission[i] = CaptureEmission(ringRenderers[i]);
            return;
        }

        Vector3[] localPoints = BuildOctagonPoints(ringRadius);
        var ringGO = new GameObject("Ring_" + i.ToString("00"));
        ringGO.transform.SetParent(transform, false);
        ringGO.transform.localPosition = localPos;
        ringRoots[i] = ringGO.transform;

        Material mat = (i % 2 == 0) ? magentaMat : cyanMat;
        for (int p = 0; p < localPoints.Length; p++)
        {
            Vector3 a = localPoints[p];
            Vector3 b = localPoints[(p + 1) % localPoints.Length];
            CreateBeam(ringGO.transform, a, b, beamThickness, mat);
        }
        ringRenderers[i] = ringGO.GetComponentsInChildren<MeshRenderer>();
        ringBaseEmission[i] = CaptureEmission(ringRenderers[i]);
    }

    // --- Changing style while running (a playlist moving to its next segment) ---
    //
    // The recycle logic above is untouched: a ring still goes round exactly when it leaves the
    // frustum. What changes is only what it is made of when it comes back - RestyleRing swaps
    // the ring's own objects for the queued style's, at the position the recycle gave it.
    private int[] ringStyleSerial;
    private int pendingRingSerial;

    /// From now on every ring that recycles is rebuilt in `theme`'s style (arch prefab and the
    /// two accents). Since rings recycle behind the camera and reappear at the far end, the new
    /// arches reach the camera one tunnel-length of travel later - TunnelLength / ScrollSpeed.
    public void QueueRingStyle(TunnelTheme theme)
    {
        if (theme == null || !built) return;
        magentaColor = theme.primaryColor;
        cyanColor = theme.secondaryColor;
        ringEmissionIntensity = theme.accentEmissionIntensity;
        ringArchPrefab = theme.ringArchPrefab;
        if (ringArchPrefab == null)
        {
            magentaMat = CreateNeonMaterial(magentaColor, ringEmissionIntensity);
            cyanMat = CreateNeonMaterial(cyanColor, ringEmissionIntensity);
        }
        pendingRingSerial++;
    }

    private void RestyleRing(int i)
    {
        ringStyleSerial[i] = pendingRingSerial;
        Vector3 pos = ringRoots[i].localPosition;
        Destroy(ringRoots[i].gameObject);
        ringFlashTimers[i] = 0f;
        BuildRing(i, pos);
    }

    /// Recolours the road - lane lines, grid rungs and (the generated) floor - toward `theme`.
    /// Cheap enough to call every frame of a transition, which is how a playlist blends it.
    /// A theme with its own floor material is switched to with SetFloorMaterial instead.
    public void ApplyRoadPalette(TunnelTheme theme)
    {
        if (!built || theme == null) return;
        if (laneMat != null) ApplyCappedEmission(laneMat, theme.laneLineColor, theme.laneLineEmissionIntensity);
        if (laneMat != null) laneMat.SetColor("_BaseColor", theme.laneLineColor * 0.12f);
        if (rungMat != null) ApplyCappedEmission(rungMat, theme.floorGridLineColor, theme.floorGridGlintIntensity);
        if (rungMat != null) rungMat.SetColor("_BaseColor", theme.floorGridLineColor * 0.12f);
        if (floorMat != null && floorMat == generatedFloorMat)
        {
            floorMat.SetColor("_BaseColor", theme.floorColor);
            floorMat.SetFloat("_Smoothness", theme.floorSmoothness);
            floorMat.SetFloat("_Metallic", theme.floorMetallic);
        }
    }

    /// The road's surface: `material`, or null for the generated one (coloured by ApplyRoadPalette).
    public void SetFloorMaterial(Material material)
    {
        if (!built || floorRenderer == null) return;
        if (generatedFloorMat == null) generatedFloorMat = CreateFloorMaterial(floorColor, floorMetallic, floorSmoothness);
        floorMat = material != null ? material : generatedFloorMat;
        floorRenderer.sharedMaterial = floorMat;
    }

    /// Reads each renderer's effective emission — its per-instance override if it has one, else
    /// its material's. A property block that has never been written comes back empty rather than
    /// pre-filled with the material's values, so the two cases have to be told apart explicitly.
    private static Color[] CaptureEmission(MeshRenderer[] renderers)
    {
        var block = new MaterialPropertyBlock();
        var result = new Color[renderers.Length];

        for (int i = 0; i < renderers.Length; i++)
        {
            renderers[i].GetPropertyBlock(block);
            var mat = renderers[i].sharedMaterial;
            // A themed arch may carry additive light sprites (ClubGlow), which have no emission.
            result[i] = (!block.isEmpty && block.HasColor(EmissionColorId)) ? block.GetColor(EmissionColorId)
                      : mat != null && mat.HasProperty(EmissionColorId) ? mat.GetColor(EmissionColorId)
                      : Color.black;
        }
        return result;
    }

    /// Briefly brightens the rings around `worldPos` — the environment reacting to a cue being
    /// destroyed there. This exists instead of relying on the Point Light the effect also spawns
    /// because every surface in this tunnel has a near-black albedo (accent * 0.12) by design, so
    /// no amount of light intensity moves it; adding to emission is the only thing that reads.
    /// Calling it again on an already-flashing ring restarts that ring rather than stacking.
    public void FlashNear(Vector3 worldPos, Color color, float radius, float strength, float duration)
    {
        if (!built || ringRoots == null || duration <= 0f) return;

        ringFlashDuration = duration;
        ringFlashStrength = strength;
        float sqrRadius = radius * radius;
        Vector3 localPos = transform.InverseTransformPoint(worldPos);

        for (int i = 0; i < ringRoots.Length; i++)
        {
            if ((ringRoots[i].localPosition - localPos).sqrMagnitude > sqrRadius) continue;
            ringFlashTimers[i] = duration;
            ringFlashColors[i] = color;
        }
    }

    private void UpdateRingFlashes()
    {
        if (ringFlashTimers == null) return;

        for (int i = 0; i < ringFlashTimers.Length; i++)
        {
            if (ringFlashTimers[i] <= 0f) continue;

            ringFlashTimers[i] -= Time.deltaTime;
            float t = Mathf.Clamp01(ringFlashTimers[i] / ringFlashDuration);
            // Squared, like the light's own falloff, so the pulse snaps rather than lingers.
            float boost = ringFlashStrength * t * t;

            var renderers = ringRenderers[i];
            var baseEmission = ringBaseEmission[i];
            for (int r = 0; r < renderers.Length; r++)
            {
                // Light sprites on a themed arch have no emission to lift, and clearing their
                // property block would wipe the colour and intensity the arch gives them.
                var sm = renderers[r].sharedMaterial;
                if (sm == null || !sm.HasProperty(EmissionColorId)) continue;
                Color target = baseEmission[r] + ringFlashColors[i] * boost;

                // Scale the whole color back to the ceiling rather than letting channels clip
                // independently: a ring already near the cap has its weakest channel raised
                // fastest by clipping, which drags a saturated magenta ring to flat white — the
                // one thing the emission ceiling exists to prevent.
                float peak = Mathf.Max(target.r, Mathf.Max(target.g, target.b));
                if (peak > MaxEmissionIntensity) target *= MaxEmissionIntensity / peak;

                ringFlashBlock.Clear();
                ringFlashBlock.SetColor(EmissionColorId, target);
                renderers[r].SetPropertyBlock(ringFlashBlock);
            }

            if (ringFlashTimers[i] <= 0f) ringFlashTimers[i] = 0f;
        }
    }

    // Flat-bottom/flat-top octagon (22.5 deg offset), shifted up so its lowest edge sits
    // ringGroundBurial below y = 0 — buried under the opaque floor slab so only the upper
    // arc of the ring is ever visible, never crossing the road surface.
    //
    // The two LOWER DIAGONALS are then straightened into vertical legs, and this is the whole
    // reason this method is not just a circle sampler. As a true octagon, those diagonals run
    // from (+-cos(22.5)*radius, above the floor) down to (+-sin(22.5)*radius, buried), so they
    // break the floor plane at x = sin(22.5)*radius + ringGroundBurial — 2.24 at the shipping
    // radius 3.5 and burial 0.9, which is INSIDE the road's own half-width of 3. Burying the
    // ring does not hide that: the part of the diagonal above y = 0 stands over the road, and
    // once the ring comes close enough that its arch has left the top of the frame (measured:
    // from z ~= 5 down), those two stubs are all that is left on screen — two bright neon bars
    // lying across the road with nothing visibly joining them. Recycling cannot save it either,
    // because a ring is only retired when every one of its beams has left the frustum, measured
    // at z ~= 0.5, long after the damage is done.
    //
    // Pulling the two bottom vertices out to the octagon's widest x makes those edges vertical,
    // so they pierce the floor at x = legX, outside the road, and can no longer project onto it
    // at ANY distance, FOV or aspect ratio — the fix is geometric rather than a tuned threshold.
    // The bottom edge between them just gets wider, and it is buried, so it is never seen.
    private Vector3[] BuildOctagonPoints(float radius)
    {
        const int sides = 8;
        var pts = new Vector3[sides];
        float minY = float.MaxValue;
        for (int i = 0; i < sides; i++)
        {
            float angle = (i * 45f + 22.5f) * Mathf.Deg2Rad;
            pts[i] = new Vector3(Mathf.Cos(angle) * radius, Mathf.Sin(angle) * radius, 0f);
            if (pts[i].y < minY) minY = pts[i].y;
        }
        float floorOffset = -minY - ringGroundBurial;
        for (int i = 0; i < sides; i++) pts[i].y += floorOffset;

        // Vertices 5 (247.5 deg) and 6 (292.5 deg) are the bottom pair; edges 4 and 6 run from
        // the widest vertices down into them. Widening the pair to legX turns both edges
        // vertical. Taking the max against the road keeps the invariant true if floorWidth is
        // ever raised past the ring itself — the legs then splay outward instead of silently
        // reintroducing the overlap, which is exactly how the old tuned constant went stale.
        float legX = Mathf.Max(Mathf.Cos(22.5f * Mathf.Deg2Rad) * radius, floorWidth * 0.5f + beamThickness);
        pts[5].x = -legX;
        pts[6].x = legX;
        return pts;
    }

    private void CreateBeam(Transform parent, Vector3 a, Vector3 b, float thickness, Material mat)
    {
        var go = new GameObject("Beam");
        go.transform.SetParent(parent, false);

        Vector3 edge = b - a;
        float length = edge.magnitude;
        go.transform.localPosition = (a + b) * 0.5f;
        go.transform.localRotation = Quaternion.FromToRotation(Vector3.up, edge.normalized);
        go.transform.localScale = new Vector3(thickness, length, thickness);

        var mf = go.AddComponent<MeshFilter>();
        mf.sharedMesh = cubeMesh;
        var mr = go.AddComponent<MeshRenderer>();
        mr.sharedMaterial = mat;
        mr.shadowCastingMode = ShadowCastingMode.Off;
        mr.receiveShadows = false;
    }

    private void BuildFloor()
    {
        float startZ = recycleZ - ringSpacing;
        float endZ = tunnelLength + ringSpacing * 2f;
        float length = endZ - startZ;

        var floorGO = GameObject.CreatePrimitive(PrimitiveType.Cube);
        floorGO.name = "Floor";
        Destroy(floorGO.GetComponent<Collider>());
        floorGO.transform.SetParent(transform, false);
        floorGO.transform.localPosition = new Vector3(0f, -0.05f, (startZ + endZ) * 0.5f);
        floorGO.transform.localScale = new Vector3(floorWidth, 0.1f, length);

        var mr = floorGO.GetComponent<MeshRenderer>();
        floorRenderer = mr;
        mr.sharedMaterial = floorMat;
        mr.shadowCastingMode = ShadowCastingMode.Off;
        mr.receiveShadows = false;
    }

    // Perpendicular "rungs" across the floor at regular Z intervals — real emissive geometry
    // (identical technique to the lane dividers/edges), not a texture. TunnelScene has no Light
    // components, so the floor's diffuse path (_BaseColor/_BaseMap) never shows regardless of
    // what's set there, and a textured _EmissionMap was confirmed, through extensive isolated
    // testing (paused time, disabled every other emissive object, swept tiling/brightness), not
    // to render at all on this project's material instances — while plain _EmissionColor (no
    // map, same as the rings/lane lines) lit up immediately. Geometry with flat emission color
    // sidesteps both dead ends and reuses a technique already proven to work everywhere else.
    private void BuildFloorGridRungs()
    {
        rungMat = CreateNeonMaterial(floorGridLineColor, floorGridGlintIntensity);

        float startZ = recycleZ - ringSpacing;
        float endZ = tunnelLength + ringSpacing * 2f;
        int rungCount = Mathf.CeilToInt((endZ - startZ) / floorGridCellWorldSize) + 1;

        for (int i = 0; i < rungCount; i++)
        {
            float z = startZ + i * floorGridCellWorldSize;

            var go = new GameObject("FloorGridRung");
            go.transform.SetParent(transform, false);
            go.transform.localPosition = new Vector3(0f, 0.001f, z);
            go.transform.localScale = new Vector3(floorWidth, 0.02f, floorGridRungWidth);

            var mf = go.AddComponent<MeshFilter>();
            mf.sharedMesh = cubeMesh;
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = rungMat;
            mr.shadowCastingMode = ShadowCastingMode.Off;
            mr.receiveShadows = false;
        }
    }

    // 4 equal lanes = 3 dashed dividers (which scroll/recycle like the rings) plus 2 solid,
    // static outer edges. The edges are what makes the lane boundaries read as straight and
    // parallel at a glance, independent of the diagonal ring beams crossing near the floor.
    private void BuildLaneLines()
    {
        int dividerCount = laneCount - 1;
        float laneWidth = floorWidth / laneCount;
        float period = dashLength + dashGap;
        int dashesPerLine = Mathf.CeilToInt((tunnelLength + ringSpacing * 4f) / period) + 2;
        dashSpan = dashesPerLine * period;

        laneDashLines = dividerCount > 0 ? new Transform[dividerCount][] : new Transform[0][];
        for (int d = 0; d < dividerCount; d++)
        {
            float x = -floorWidth * 0.5f + laneWidth * (d + 1);
            laneDashLines[d] = new Transform[dashesPerLine];
            for (int i = 0; i < dashesPerLine; i++)
            {
                var go = new GameObject("LaneDash");
                go.transform.SetParent(transform, false);
                go.transform.localPosition = new Vector3(x, 0.001f, recycleZ + i * period);
                go.transform.localScale = new Vector3(laneLineWidth, 0.02f, dashLength);

                var mf = go.AddComponent<MeshFilter>();
                mf.sharedMesh = cubeMesh;
                var mr = go.AddComponent<MeshRenderer>();
                mr.sharedMaterial = laneMat;
                mr.shadowCastingMode = ShadowCastingMode.Off;
                mr.receiveShadows = false;

                laneDashLines[d][i] = go.transform;
            }
        }

        BuildLaneEdge(-floorWidth * 0.5f);
        BuildLaneEdge(floorWidth * 0.5f);
    }

    private void BuildLaneEdge(float x)
    {
        float startZ = recycleZ - ringSpacing;
        float endZ = tunnelLength + ringSpacing * 2f;
        float length = endZ - startZ;

        var go = new GameObject("LaneEdge");
        go.transform.SetParent(transform, false);
        go.transform.localPosition = new Vector3(x, 0.001f, (startZ + endZ) * 0.5f);
        go.transform.localScale = new Vector3(edgeLineWidth, 0.02f, length);

        var mf = go.AddComponent<MeshFilter>();
        mf.sharedMesh = cubeMesh;
        var mr = go.AddComponent<MeshRenderer>();
        mr.sharedMaterial = laneMat;
        mr.shadowCastingMode = ShadowCastingMode.Off;
        mr.receiveShadows = false;
    }

    private void ConfigureFog()
    {
        RenderSettings.fog = true;
        RenderSettings.fogMode = FogMode.Linear;
        RenderSettings.fogColor = fogColor;
        RenderSettings.fogStartDistance = tunnelLength * fogStartRatio;
        RenderSettings.fogEndDistance = tunnelLength * fogEndRatio;

        // Unity's default skybox ambient washes flat-dark materials (like the floor) out to a
        // visible grey. A deep-space tunnel has no sun to bounce ambient light around, so pin
        // ambient to a near-black flat color matching the fog instead of the default skybox probe.
        RenderSettings.ambientMode = AmbientMode.Flat;
        RenderSettings.ambientLight = ambientColor;
    }


    private Mesh GetSharedCubeMesh()
    {
        var temp = GameObject.CreatePrimitive(PrimitiveType.Cube);
        Mesh mesh = temp.GetComponent<MeshFilter>().sharedMesh;
        Destroy(temp);
        return mesh;
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

    private Material CreateFloorMaterial(Color color, float metallic, float smoothness)
    {
        var mat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
        mat.SetColor("_BaseColor", color);
        // Reflective wet-road look (theme-tunable): high smoothness + metallic so the
        // environment (rings, via the reflection probe added for this) mirrors faintly on the
        // floor. Needs no scene Light — environment reflections sample the skybox/reflection-
        // probe cubemap, which works even with zero Lights (unlike the diffuse _BaseColor path).
        mat.SetFloat("_Smoothness", smoothness);
        mat.SetFloat("_Metallic", metallic);
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
