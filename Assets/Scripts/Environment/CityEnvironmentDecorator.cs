using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

/// The cyberpunk street set-dressing: rows of Kenney City Kit buildings, industrial details and
/// street poles running down both sides of the tunnel, scrolling at exactly the tunnel's own
/// speed so the road and the city read as one moving world.
///
/// Instantiated by TunnelThemeApplier as the active theme's environmentPrefab, exactly like
/// SciFiEnvironmentDecorator. The tunnel's own geometry, scrolling, recycling and hit detection
/// are untouched by this file: it only ever moves objects it created itself, parented under its
/// own transform.
///
/// Why it scrolls itself rather than riding something the tunnel moves: the tunnel does not move
/// a container, it recycles individual rings by teleporting them to the far end. A street has to
/// do the same thing, and a prop's lifetime is not a ring's, so the street owns its own loop. The
/// one thing it needs from the tunnel - the speed to match - arrives via Initialize().
public class CityEnvironmentDecorator : MonoBehaviour, IThemePalette, IEnvironmentDecorator
{
    private const float MaxEmissionIntensity = 2.5f; // same ceiling the rest of the scene's neon obeys

    /// One row of props on one side of the road. ALL the layout policy lives in these structs:
    /// how far out a row sits, how densely it is packed, how big its props are and how much neon
    /// they carry. The build/scroll/recycle machinery below reads them and knows nothing else
    /// about what a "city" should look like, so a different skyline is a data edit here rather
    /// than a code change.
    [System.Serializable]
    public struct RowSpec
    {
        public string name;
        // Clear distance from the road centre to the NEAREST FACE of this row's props, not to
        // their pivots. Measuring to the pivot is what put a 13-unit-wide block with its wall
        // inside the tunnel the first time: the same offset means something different for every
        // model and every scale, so the row has to be placed by its edge.
        public float offsetX;
        public float offsetXJitter;    // per-prop random depth away from the road
        public float spacing;          // average gap along Z between props
        public float spacingJitter;
        public float scaleMin, scaleMax;
        public Pool pool;              // which model pool this row draws from
        [Range(0f, 1f)] public float signChance;      // per-prop chance of carrying neon
        [Range(0f, 1f)] public float faceRoadChance;  // chance the prop is turned to face the road
        public bool castShadows;
    }

    public enum Pool { Buildings, Details, Poles }

    /// The shapes a prop's neon can take. Not decoration for its own sake: a street where every
    /// sign is the same filled rectangle reads as a pattern, and the eye stops seeing individual
    /// buildings. Each of these is assembled from thin bars, one masked card and one glow card.
    public enum SignKind
    {
        VerticalBanner, // a tall thin panel hanging down the facade
        HorizontalStrip,// a wide short panel across it
        Frame,          // four thin bars: a neon outline around a dark centre
        RoundSign,      // a ring or concentric-circle light mask - the round/oval shop sign
        Triangle,       // three thin bars closing into a triangle outline
        Cornice,        // a line along the roof edge plus one down a corner of the building
        TwinStrips,     // two thin horizontal lines, a step apart
        RoofBeam,       // a searchlight cone standing on the roof, over a small emissive base
    }

    [System.Serializable]
    public struct SignSpec
    {
        public SignKind kind;
        [Range(0f, 4f)] public float weight; // relative frequency; 0 removes the shape entirely
    }

    /// The reusable pieces of one prop's sign. Created on demand and then re-used for every shape
    /// that prop ever shows, because a prop re-rolls its sign on every lap and building a fresh
    /// GameObject each time would churn the heap for no visual gain.
    private class SignRig
    {
        public Transform root;             // oriented so +Z points at the road, +X across the facade
        public Transform[] bars;           // filled panels and neon tubing
        public Transform card;             // masked light-mask quad (round signs)
        public Transform halo;             // additive glow behind the sign
        public Transform beam;             // additive searchlight cone on the roof
    }

    [Header("Model sources (Kenney City Kit / Roads)")]
    [SerializeField] private GameObject[] buildingPrefabs;
    [SerializeField] private GameObject[] detailPrefabs;
    [SerializeField] private GameObject[] polePrefabs;

    [Header("Palette (overwritten by the theme via ApplyPalette)")]
    [SerializeField] private Color primaryColor = new Color(1f, 0f, 0.8f);
    [SerializeField] private Color secondaryColor = new Color(0f, 0.898f, 1f);
    [SerializeField, Range(0f, MaxEmissionIntensity)] private float accentEmissionIntensity = 1.5f;

    [Header("Rows (the entire layout policy)")]
    [SerializeField]
    private RowSpec[] rows = new RowSpec[]
    {
        new RowSpec { name = "kerb-details", offsetX = 4.6f,  offsetXJitter = 0.6f,  spacing =  7f, spacingJitter = 2f,   scaleMin = 1.2f, scaleMax = 2.2f, pool = Pool.Details,   signChance = 0.2f,  faceRoadChance = 0.5f, castShadows = false },
        new RowSpec { name = "street-poles", offsetX = 4.4f,  offsetXJitter = 0.15f, spacing = 11f, spacingJitter = 1.5f, scaleMin = 1.6f, scaleMax = 2.1f, pool = Pool.Poles,     signChance = 0f,    faceRoadChance = 1f,   castShadows = false },
        new RowSpec { name = "near-blocks",  offsetX = 8.5f,  offsetXJitter = 1.6f,  spacing =  8f, spacingJitter = 2.5f, scaleMin = 3.5f, scaleMax = 5.5f, pool = Pool.Buildings, signChance = 0.85f, faceRoadChance = 0.85f, castShadows = false },
        new RowSpec { name = "mid-blocks",   offsetX = 16f,   offsetXJitter = 3f,    spacing = 11f, spacingJitter = 4f,   scaleMin = 6f,   scaleMax = 11f,  pool = Pool.Buildings, signChance = 0.55f, faceRoadChance = 0.6f, castShadows = false },
        new RowSpec { name = "skyline",      offsetX = 32f,   offsetXJitter = 8f,    spacing = 17f, spacingJitter = 6f,   scaleMin = 11f,  scaleMax = 20f,  pool = Pool.Buildings, signChance = 0.3f,  faceRoadChance = 0.5f, castShadows = false },
    };

    [Header("Extent")]
    // How far behind the camera a prop travels before it is recycled to the far end. Far enough
    // back that the recycle, and the re-roll that goes with it, happens off-screen.
    [SerializeField] private float recycleZ = -25f;
    // How far past the tunnel's own length the street keeps going. The tunnel fogs out long
    // before this; the extra length is what stops the far end looking like a wall of nothing.
    [SerializeField] private float extraLengthBeyondTunnel = 60f;

    [Header("Model tint")]
    // The kit's own atlas is daylight concrete: pale, warm, and completely wrong for a night
    // street - lit at an intensity high enough to read, the facades turn into white slabs that
    // out-shout the neon. Tinting every model renderer through a property block (NOT by editing
    // the shared imported material, which belongs to the asset, not to this theme) pushes them
    // back down to dark cool silhouettes that the signs can sit on top of.
    [SerializeField] private Color modelTint = new Color(0.26f, 0.29f, 0.42f);

    [Header("Neon signs")]
    [SerializeField] private float signHeightRatio = 0.55f;  // where up the facade a banner sits
    [SerializeField] private float bannerWidthRatio = 0.16f; // fraction of the facade's width
    [SerializeField] private float bannerHeightRatio = 0.5f;
    [SerializeField] private float stripHeightRatio = 0.09f;
    [SerializeField] private float signDepth = 0.06f;
    // Thickness of an outline's bars, as a fraction of the facade's width: frames, triangles and
    // cornice lines are drawn from bars this thin, which is what makes them read as neon tubing
    // rather than as another filled panel.
    [SerializeField, Range(0.004f, 0.06f)] private float tubeThicknessRatio = 0.018f;
    // The whole "what shapes does this city's neon come in" policy, as a weighted table. Every
    // shape below is assembled from the same three pieces (a bar, a masked card, a glow card), so
    // changing the city's character is a weight edit here rather than new code.
    [SerializeField]
    private SignSpec[] signKinds = new SignSpec[]
    {
        new SignSpec { kind = SignKind.VerticalBanner, weight = 1.0f },
        new SignSpec { kind = SignKind.HorizontalStrip, weight = 0.9f },
        new SignSpec { kind = SignKind.Frame,          weight = 0.9f },
        new SignSpec { kind = SignKind.RoundSign,      weight = 0.9f },
        new SignSpec { kind = SignKind.Triangle,       weight = 0.5f },
        new SignSpec { kind = SignKind.Cornice,        weight = 1.0f },
        new SignSpec { kind = SignKind.TwinStrips,     weight = 0.6f },
        new SignSpec { kind = SignKind.RoofBeam,       weight = 0.4f },
    };
    // Kenney light masks. Round/oval signs are drawn straight from the ring and concentric-circle
    // masks; the halo is the soft circle the lamps already use; the beam is a cone.
    [SerializeField] private Texture2D[] roundSignMasks;
    [SerializeField] private Texture2D signHaloMask;
    [SerializeField] private Texture2D signBeamMask;
    [SerializeField, Range(0f, 1f)] private float haloChance = 0.45f;
    [SerializeField, Range(0f, 3f)] private float haloStrength = 0.55f;
    [SerializeField] private float haloSizeRatio = 2.2f;   // multiples of the sign's own size
    [SerializeField, Range(0f, 3f)] private float beamStrength = 1.1f;
    [SerializeField] private float beamWidthRatio = 0.5f;  // of the facade width
    [SerializeField] private float beamHeightRatio = 0.9f; // of the facade height

    [Header("Street lamps")]
    // The light in this theme comes from the street itself, never from turning the sun up: the
    // directional light stays at the theme's low intensity so the tunnel's own neon keeps its
    // contrast, and everything that makes the city readable is a local source standing in it.
    [SerializeField] private bool buildLamps = true;
    [SerializeField] private Color lampWarmColor = new Color(1f, 0.74f, 0.42f);
    [SerializeField] private Color lampCoolColor = new Color(0.55f, 0.85f, 1f);
    [SerializeField, Range(0f, 1f)] private float lampWarmChance = 0.55f;
    [SerializeField] private float lampIntensity = 16f;
    [SerializeField] private float lampRange = 18f;
    [SerializeField] private float lampHeadSize = 0.22f;   // in the pole's own local units
    // Only lamps this close to the camera keep a real Light component switched on. Past it the
    // pool of lit pixels is too small to see anyway, and URP's per-object additional-light limit
    // (4 on this project's PC asset) is better spent on the lamps actually in frame.
    [SerializeField] private float lampLitDistance = 70f;
    [SerializeField] private Texture2D lampGlowMask;       // circle: the bulb's halo
    [SerializeField] private Texture2D lampConeMask;       // cone: the beam hanging in the air
    [SerializeField] private float coneWidth = 1.9f;
    [SerializeField] private float coneHeight = 2.0f;
    [SerializeField, Range(0f, 3f)] private float coneStrength = 2.4f;

    [Header("Lit windows")]
    // One quad per facade carrying a procedurally generated window grid, NOT one object per
    // window: a few shared textures give genuinely random lit/dark windows while staying a
    // handful of instanced draw calls instead of several hundred little boxes.
    [SerializeField] private bool buildWindows = true;
    [SerializeField] private int windowVariants = 8;
    // A fine grid, not a coarse one. The first pass used 6x9 cells stretched over a whole
    // facade, so one lit "window" was a sixth of the building wide and the city read as
    // Minecraft. Small cells are what make a lit window read as a window.
    [SerializeField] private Vector2Int windowGrid = new Vector2Int(9, 14);
    // Per-cell chance INSIDE whatever pattern the facade drew (see WindowPattern). A facade is
    // mostly dark: the lit ones are the accent, and the previous 0.32 lit a third of every wall.
    [SerializeField, Range(0f, 1f)] private float windowLitChance = 0.1f;
    [SerializeField] private Color windowWarmColor = new Color(1f, 0.78f, 0.45f);
    [SerializeField] private Color windowCoolColor = new Color(0.6f, 0.9f, 1f);
    [SerializeField, Range(0f, 4f)] private float windowEmission = 0.85f;
    [SerializeField, Range(0f, 1f)] private float windowCoverage = 0.62f;
    // Pixels per grid cell in the generated sheet. The sheet is bilinear-filtered, so more than
    // one pixel per cell is what gives a window a soft edge instead of a hard tile.
    [SerializeField, Range(2, 16)] private int windowCellPixels = 8;
    // How far a lit window's glow bleeds past its own cell, in cell widths. This is the whole
    // difference between "glowing window" and "white square".
    [SerializeField, Range(0f, 1f)] private float windowGlowBleed = 0.45f;

    [Header("Haze")]
    [SerializeField] private bool buildHaze = true;
    [SerializeField] private Texture2D hazeMask;           // soft circle
    [SerializeField] private int hazeCards = 16;
    [SerializeField] private Color hazeColor = new Color(0.3f, 0.5f, 0.85f);
    [SerializeField, Range(0f, 2f)] private float hazeStrength = 0.22f;
    [SerializeField] private float hazeSizeMin = 8f;
    [SerializeField] private float hazeSizeMax = 16f;
    [SerializeField] private float hazeHeight = 1.6f;
    // Nearest a haze card is allowed to sit. Closer than this and one card is the whole frame.
    [SerializeField] private float hazeNearZ = 18f;

    [Header("Ground")]
    [SerializeField] private bool buildGroundPlanes = true;
    [SerializeField] private Color groundColor = new Color(0.075f, 0.08f, 0.10f);
    [SerializeField] private float groundWidth = 90f;
    [SerializeField, Range(0f, 1f)] private float groundSmoothness = 0.1f;

    [Header("Sky")]
    // The night sky above the skyline - starfield and moon. A separate component on a child of
    // this prefab because it is a fixed backdrop with nothing in common with the street's
    // scroll/recycle loop; all it needs from here is the theme's palette and one build call, so
    // the theme still owns it through this prefab rather than through anything in the scene.
    [SerializeField] private CityNightSkyDecorator nightSky;

    [Header("Determinism")]
    [SerializeField] private int seed = 20260920;

    // --- runtime state ---
    private readonly List<Transform> props = new List<Transform>();
    private readonly List<int> propRow = new List<int>();      // which RowSpec each prop came from
    private readonly List<float> propSide = new List<float>(); // -1 left, +1 right
    // One reusable sign rig per prop, created once and then re-shaped, moved and recoloured on
    // every recycle. Rebuilding it instead would churn GameObjects per prop per lap for no gain.
    private readonly List<SignRig> propSign = new List<SignRig>();
    // The prop model's own local bounds, measured once when it is instantiated and BEFORE any of
    // the generated children (sign, windows, lamp) are attached. Measuring it per lap instead is
    // what let those children feed back into the measurement and push every prop a little
    // further from the road on each recycle.
    private readonly List<Bounds> propBounds = new List<Bounds>();
    // Facade window grid per prop (buildings only) and the lamp rig per prop (poles only). Both
    // are children of the prop, so they ride its scroll and recycle for free.
    private readonly List<Transform> propWindows = new List<Transform>();
    private readonly List<Light> propLamp = new List<Light>();
    private readonly List<Transform> propCone = new List<Transform>();
    private readonly List<Transform> hazeProps = new List<Transform>();
    private Material[] windowMaterials;
    private Material lampHeadWarm, lampHeadCool, lampGlowWarm, lampGlowCool, coneWarm, coneCool, hazeMaterial;
    private Camera viewCamera;
    private MaterialPropertyBlock mpb;
    private float scrollSpeed;
    private float tunnelLength;
    private float span;   // total Z length of the street loop
    private Material neonPrimary, neonSecondary, groundMaterial;
    // Round-sign materials, laid out as [mask0 primary, mask0 secondary, mask1 primary, ...] so
    // a shape and a colour are one index each rather than a lookup.
    private Material[] roundSignMaterials;
    private Material haloPrimary, haloSecondary, beamPrimary, beamSecondary;
    private System.Random rng;
    private bool built;

    private void OnValidate()
    {
        accentEmissionIntensity = Mathf.Clamp(accentEmissionIntensity, 0f, MaxEmissionIntensity);
    }

    /// Theme palette hook - called by TunnelThemeApplier BEFORE Initialize, so the neon materials
    /// built during the layout pass already carry the theme's two accent colours.
    public void ApplyPalette(Color primary, Color secondary, float emissionIntensity)
    {
        primaryColor = primary;
        secondaryColor = secondary;
        accentEmissionIntensity = Mathf.Clamp(emissionIntensity, 0f, MaxEmissionIntensity);
    }

    public void Initialize(float tunnelLength, float scrollSpeed)
    {
        if (built) return;
        built = true;

        this.tunnelLength = tunnelLength;
        this.scrollSpeed = scrollSpeed;
        span = Mathf.Max(20f, tunnelLength + extraLengthBeyondTunnel - recycleZ);
        rng = new System.Random(seed);
        mpb = new MaterialPropertyBlock();

        neonPrimary = CreateNeonMaterial(primaryColor);
        neonSecondary = CreateNeonMaterial(secondaryColor);
        BuildSignMaterials();

        viewCamera = Camera.main;

        lampHeadWarm = CreateNeonMaterial(lampWarmColor);
        lampHeadCool = CreateNeonMaterial(lampCoolColor);
        lampGlowWarm = CreateAdditiveMaterial(lampGlowMask, lampWarmColor, 0.45f);
        lampGlowCool = CreateAdditiveMaterial(lampGlowMask, lampCoolColor, 0.45f);
        coneWarm = CreateAdditiveMaterial(lampConeMask, lampWarmColor, coneStrength);
        coneCool = CreateAdditiveMaterial(lampConeMask, lampCoolColor, coneStrength);
        // Kenney draws the cone with its bright apex at the bottom of the image. The beam hangs
        // FROM the lamp, so the card has to sample the mask flipped, or the street gets a wedge
        // that is brightest at the pavement and widest in the sky.
        FlipVertically(coneWarm);
        FlipVertically(coneCool);
        if (buildWindows) BuildWindowMaterials();

        if (buildGroundPlanes) BuildGround();

        for (int r = 0; r < rows.Length; r++)
        {
            BuildRow(r, -1f);
            BuildRow(r, +1f);
        }

        if (buildHaze) BuildHaze();

        // Built last and with the theme's colours, so the sky is tinted by the same palette the
        // street's neon just used.
        if (nightSky != null) nightSky.Build(primaryColor, secondaryColor);
    }

    /// The whole street moves as one: every prop steps back by the tunnel's own scroll distance
    /// this frame, and anything past the camera is sent to the far end and re-rolled, so the loop
    /// never reads as the same block coming round again.
    private void Update()
    {
        if (!built || scrollSpeed <= 0f) return;

        float delta = scrollSpeed * Time.deltaTime;
        float camZ = viewCamera != null ? viewCamera.transform.position.z : 0f;
        float camYaw = viewCamera != null ? viewCamera.transform.eulerAngles.y : 0f;

        for (int i = 0; i < props.Count; i++)
        {
            Transform t = props[i];
            Vector3 p = t.localPosition;
            p.z -= delta;
            if (p.z < recycleZ)
            {
                p.z += span;
                RerollProp(i, ref p);
            }
            t.localPosition = p;

            // A rooftop beam is a flat card like the lamp cones, so it has to keep its face to
            // the camera or it goes edge-on and vanishes as its building slides past.
            SignRig rig = propSign[i];
            if (rig != null && rig.beam != null && rig.beam.gameObject.activeSelf)
                rig.beam.rotation = Quaternion.Euler(0f, camYaw, 0f);

            // A lamp is only worth a real light while it is near enough for its pool to be seen.
            Light lamp = propLamp[i];
            if (lamp != null)
            {
                bool lit = p.z - camZ < lampLitDistance && p.z > recycleZ * 0.5f;
                if (lamp.enabled != lit) lamp.enabled = lit;
                Transform cone = propCone[i];
                if (cone != null)
                {
                    if (cone.gameObject.activeSelf != lit) cone.gameObject.SetActive(lit);
                    // The beam is a flat card, so it has to keep its face to the camera or it
                    // would vanish edge-on every time the street slid past.
                    if (lit) cone.rotation = Quaternion.Euler(0f, camYaw, 0f);
                }
            }
        }

        for (int i = 0; i < hazeProps.Count; i++)
        {
            Transform h = hazeProps[i];
            Vector3 p = h.localPosition;
            p.z -= delta;
            if (p.z < hazeNearZ - span) p.z += span;
            h.localPosition = p;
            h.rotation = Quaternion.Euler(0f, camYaw, 0f);
        }
    }

    // --- layout ---

    private void BuildRow(int rowIndex, float side)
    {
        RowSpec spec = rows[rowIndex];
        GameObject[] pool = PoolFor(spec.pool);
        if (pool == null || pool.Length == 0) return;

        var parent = new GameObject((side < 0f ? "L_" : "R_") + (string.IsNullOrEmpty(spec.name) ? "row" + rowIndex : spec.name));
        parent.transform.SetParent(transform, false);

        float z = recycleZ;
        float end = tunnelLength + extraLengthBeyondTunnel;
        while (z < end)
        {
            GameObject prefab = pool[rng.Next(pool.Length)];
            if (prefab != null)
            {
                var go = Instantiate(prefab, parent.transform);
                var t = go.transform;

                Vector3 pos = new Vector3(side * spec.offsetX, 0f, z);
                props.Add(t);
                propRow.Add(rowIndex);
                propSide.Add(side);
                propBounds.Add(LocalBounds(t));
                propSign.Add(spec.signChance > 0f ? CreateSignRig(t) : null);
                propWindows.Add(buildWindows && spec.pool == Pool.Buildings ? CreateWindowPane(t) : null);
                AttachLamp(t, spec);
                RerollProp(props.Count - 1, ref pos);
                t.localPosition = pos;
            }

            z += Mathf.Max(1.5f, spec.spacing + (float)(rng.NextDouble() * 2.0 - 1.0) * spec.spacingJitter);
        }
    }

    /// Everything about a prop that can change without re-instantiating it: its depth off the
    /// road, its facing, its size and its neon. Called once at build time and again every time
    /// the prop recycles behind the camera, which is what keeps the loop from looking like a loop.
    private void RerollProp(int index, ref Vector3 pos)
    {
        RowSpec spec = rows[propRow[index]];
        float side = propSide[index];
        Transform t = props[index];

        float scale = Mathf.Lerp(spec.scaleMin, spec.scaleMax, (float)rng.NextDouble());
        t.localScale = Vector3.one * scale;

        // Facing: either square-on to the road, so facades and signs point at the camera, or a
        // free spin - per the row's own faceRoadChance.
        float yaw = rng.NextDouble() < spec.faceRoadChance
            ? (side < 0f ? 90f : -90f) + (float)(rng.NextDouble() * 20.0 - 10.0)
            : (float)(rng.NextDouble() * 360.0);
        t.localRotation = Quaternion.Euler(0f, yaw, 0f);

        // Edge alignment: how much of this prop sticks out toward the road depends on which way
        // it ended up facing, so the half-width is measured along the road-facing axis AFTER the
        // rotation is set, and the pivot is pushed back by exactly that much.
        Bounds local = propBounds[index];
        Vector3 towardRoadLocal = t.InverseTransformDirection(new Vector3(-side, 0f, 0f));
        float reach = Mathf.Abs(towardRoadLocal.x) * local.extents.x + Mathf.Abs(towardRoadLocal.z) * local.extents.z;
        reach *= scale;

        pos.x = side * (spec.offsetX + reach + (float)rng.NextDouble() * spec.offsetXJitter);
        pos.y = 0f;

        ApplyRenderFlags(t, spec.castShadows);
        TintModel(t);
        PlaceSign(index, spec, local, towardRoadLocal);
        PlaceWindows(index, local, towardRoadLocal);
        RerollLamp(index, local, towardRoadLocal);
    }

    /// Pushes the kit's daylight concrete down to a dark cool tone. A property block, so the
    /// imported material asset is never touched and every prop can still share one material.
    private void TintModel(Transform t)
    {
        var renderers = t.GetComponentsInChildren<MeshRenderer>(true);
        for (int i = 0; i < renderers.Length; i++)
        {
            if (renderers[i].transform.name == "Neon") continue;
            mpb.Clear();
            mpb.SetColor("_BaseColor", modelTint);
            renderers[i].SetPropertyBlock(mpb);
        }
    }

    private GameObject[] PoolFor(Pool pool)
    {
        switch (pool)
        {
            case Pool.Buildings: return buildingPrefabs;
            case Pool.Details: return detailPrefabs;
            default: return polePrefabs;
        }
    }

    private void ApplyRenderFlags(Transform t, bool castShadows)
    {
        var renderers = t.GetComponentsInChildren<MeshRenderer>(true);
        for (int i = 0; i < renderers.Length; i++)
        {
            renderers[i].shadowCastingMode = castShadows ? ShadowCastingMode.On : ShadowCastingMode.Off;
            renderers[i].receiveShadows = false;
        }
    }

    // --- neon ---

    /// The rig is an empty root oriented so that +X runs across the facade, +Y up it and +Z out
    /// toward the road. Every shape below is then laid out in plain 2D inside that frame, which
    /// is the only reason a triangle or a frame is as easy to place as a rectangle: without it
    /// each piece would have to be rotated into the prop's own re-rolled yaw by hand.
    private SignRig CreateSignRig(Transform parent)
    {
        var go = new GameObject("Neon");
        go.transform.SetParent(parent, false);
        return new SignRig { root = go.transform, bars = new Transform[4] };
    }

    /// A filled bar. Still a box rather than a quad: a quad has one visible side, and which side
    /// that is depends on the prop's yaw, which is re-rolled every lap - the first pass did that
    /// and every sign ended up on the wall facing away from the road.
    private Transform EnsureBar(SignRig rig, int slot)
    {
        if (rig.bars[slot] != null) return rig.bars[slot];

        var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
        go.name = "Bar" + slot;
        Destroy(go.GetComponent<Collider>());
        var r = go.GetComponent<MeshRenderer>();
        r.shadowCastingMode = ShadowCastingMode.Off;
        r.receiveShadows = false;
        go.transform.SetParent(rig.root, false);
        rig.bars[slot] = go.transform;
        return go.transform;
    }

    /// A light-mask quad. Facing its own -Z, so it is turned round inside the rig to look out at
    /// the road.
    private Transform EnsureCard(Transform parent, string name)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Quad);
        go.name = name;
        Destroy(go.GetComponent<Collider>());
        var r = go.GetComponent<MeshRenderer>();
        r.shadowCastingMode = ShadowCastingMode.Off;
        r.receiveShadows = false;
        var t = go.transform;
        t.SetParent(parent, false);
        t.localRotation = Quaternion.Euler(0f, 180f, 0f);
        return t;
    }

    /// Picks this lap's shape for the prop's sign and lays it out on whichever face ended up
    /// pointing at the road. All the geometry below is in the rig's own frame, in the prop's
    /// local units, so it survives any scale and any yaw the re-roll produced.
    private void PlaceSign(int index, RowSpec spec, Bounds local, Vector3 towardRoadLocal)
    {
        SignRig rig = propSign[index];
        if (rig == null) return;

        HideSign(rig);

        if (local.size.y <= 0.01f || rng.NextDouble() >= spec.signChance)
        {
            rig.root.gameObject.SetActive(false);
            return;
        }

        rig.root.gameObject.SetActive(true);

        // The facade's own width is whatever axis lies across the road-facing direction, and its
        // reach is the extent along that direction. Both fall out of the same local vector, so
        // the sign fits the face it sits on whatever the yaw turned out to be.
        Vector3 dir = towardRoadLocal.normalized;
        float reach = Mathf.Abs(dir.x) * local.extents.x + Mathf.Abs(dir.z) * local.extents.z;
        float faceWidth = Mathf.Abs(dir.x) * local.size.z + Mathf.Abs(dir.z) * local.size.x;
        float h = local.size.y;

        rig.root.localRotation = Quaternion.LookRotation(dir, Vector3.up);
        rig.root.localPosition = new Vector3(local.center.x, local.center.y, local.center.z) + dir * reach;

        bool primary = rng.NextDouble() < 0.5;
        Material neon = primary ? neonPrimary : neonSecondary;
        float tube = Mathf.Max(0.02f, faceWidth * tubeThicknessRatio);

        switch (PickSignKind())
        {
            case SignKind.HorizontalStrip:
                PlaceBar(rig, 0, neon, 0f, h * (0.05f + (float)rng.NextDouble() * 0.35f),
                    faceWidth * 0.75f, h * stripHeightRatio);
                MaybeHalo(rig, primary, 0f, 0f, faceWidth * 0.5f);
                break;

            case SignKind.Frame:
            {
                float fw = faceWidth * (0.45f + (float)rng.NextDouble() * 0.25f);
                float fh = h * (0.22f + (float)rng.NextDouble() * 0.2f);
                float cy = h * ((float)rng.NextDouble() * 0.3f);
                PlaceBar(rig, 0, neon, 0f, cy + fh * 0.5f, fw, tube);         // top
                PlaceBar(rig, 1, neon, 0f, cy - fh * 0.5f, fw, tube);         // bottom
                PlaceBar(rig, 2, neon, -fw * 0.5f, cy, tube, fh);             // left
                PlaceBar(rig, 3, neon, fw * 0.5f, cy, tube, fh);              // right
                MaybeHalo(rig, primary, 0f, cy, Mathf.Max(fw, fh));
                break;
            }

            case SignKind.RoundSign:
            {
                Material masked = PickRoundSignMaterial(primary);
                if (masked == null) goto default;
                float size = Mathf.Min(faceWidth * 0.55f, h * 0.45f);
                float cy = h * (0.05f + (float)rng.NextDouble() * 0.25f);
                if (rig.card == null) rig.card = EnsureCard(rig.root, "RoundSign");
                rig.card.gameObject.SetActive(true);
                rig.card.GetComponent<MeshRenderer>().sharedMaterial = masked;
                rig.card.localPosition = new Vector3(0f, cy, signDepth);
                // Ovals as well as circles: a street of perfect circles reads as a template.
                rig.card.localScale = new Vector3(size * (0.75f + (float)rng.NextDouble() * 0.5f), size, 1f);
                MaybeHalo(rig, primary, 0f, cy, size);
                break;
            }

            case SignKind.Triangle:
            {
                float side = Mathf.Min(faceWidth * 0.5f, h * 0.35f);
                float cy = h * ((float)rng.NextDouble() * 0.25f);
                float half = side * 0.5f;
                float height = side * 0.866f;
                PlaceBar(rig, 0, neon, 0f, cy - height * 0.5f, side, tube);   // base
                // The two sides meet at the apex: each is the full edge length, tilted 60 degrees
                // and offset to the midpoint of its own edge.
                PlaceBar(rig, 1, neon, -half * 0.5f, cy, side, tube, 60f);
                PlaceBar(rig, 2, neon, half * 0.5f, cy, side, tube, -60f);
                MaybeHalo(rig, primary, 0f, cy, side * 1.2f);
                break;
            }

            case SignKind.Cornice:
            {
                // A line along the roof edge and one down a corner: the building's own outline
                // picked out in neon, which is what a filled panel can never do.
                PlaceBar(rig, 0, neon, 0f, h * 0.5f - tube, faceWidth * 0.98f, tube);
                float side = rng.NextDouble() < 0.5 ? -1f : 1f;
                float drop = h * (0.35f + (float)rng.NextDouble() * 0.5f);
                PlaceBar(rig, 1, neon, side * (faceWidth * 0.5f - tube), h * 0.5f - drop * 0.5f, tube, drop);
                if (rng.NextDouble() < 0.4)
                    PlaceBar(rig, 2, neon, -side * (faceWidth * 0.5f - tube), h * 0.5f - drop * 0.35f, tube, drop * 0.7f);
                break;
            }

            case SignKind.TwinStrips:
            {
                float w = faceWidth * (0.4f + (float)rng.NextDouble() * 0.4f);
                float cy = h * ((float)rng.NextDouble() * 0.3f);
                float gap = h * 0.06f;
                PlaceBar(rig, 0, neon, 0f, cy + gap, w, tube * 1.6f);
                PlaceBar(rig, 1, neon, 0f, cy - gap, w * 0.6f, tube * 1.6f);
                break;
            }

            case SignKind.RoofBeam:
            {
                Material beamMat = primary ? beamPrimary : beamSecondary;
                if (beamMat == null) goto case SignKind.HorizontalStrip;
                // A small emissive housing on the roof, and the beam standing out of it.
                PlaceBar(rig, 0, neon, 0f, h * 0.5f - tube, faceWidth * 0.25f, tube * 2f);
                if (rig.beam == null) rig.beam = EnsureCard(rig.root, "SignBeam");
                rig.beam.gameObject.SetActive(true);
                rig.beam.GetComponent<MeshRenderer>().sharedMaterial = beamMat;
                float beamH = h * beamHeightRatio;
                rig.beam.localPosition = new Vector3(0f, h * 0.5f + beamH * 0.5f, 0f);
                rig.beam.localScale = new Vector3(faceWidth * beamWidthRatio, beamH, 1f);
                break;
            }

            default: // VerticalBanner
                PlaceBar(rig, 0, neon, 0f, h * (signHeightRatio - 0.5f),
                    faceWidth * bannerWidthRatio, h * bannerHeightRatio);
                MaybeHalo(rig, primary, 0f, h * (signHeightRatio - 0.5f), faceWidth * bannerWidthRatio * 2f);
                break;
        }
    }

    /// Weighted pick over the signKinds table. Falls back to the plain banner if every weight has
    /// been zeroed, so an empty table cannot silently blank the whole city.
    private SignKind PickSignKind()
    {
        if (signKinds == null || signKinds.Length == 0) return SignKind.VerticalBanner;

        float total = 0f;
        for (int i = 0; i < signKinds.Length; i++) total += Mathf.Max(0f, signKinds[i].weight);
        if (total <= 0f) return SignKind.VerticalBanner;

        float roll = (float)rng.NextDouble() * total;
        for (int i = 0; i < signKinds.Length; i++)
        {
            roll -= Mathf.Max(0f, signKinds[i].weight);
            if (roll <= 0f) return signKinds[i].kind;
        }
        return signKinds[signKinds.Length - 1].kind;
    }

    /// One bar of a shape, in the rig's 2D frame: centre, size and an optional roll about the
    /// facade's normal (which is what lets three identical bars close into a triangle).
    private void PlaceBar(SignRig rig, int slot, Material material, float x, float y, float width, float height, float roll = 0f)
    {
        Transform bar = EnsureBar(rig, slot);
        bar.gameObject.SetActive(true);
        bar.GetComponent<MeshRenderer>().sharedMaterial = material;
        bar.localRotation = Quaternion.Euler(0f, 0f, roll);
        bar.localPosition = new Vector3(x, y, signDepth * 0.5f);
        bar.localScale = new Vector3(width, height, signDepth);
    }

    /// A soft glow card sitting between the wall and the sign. Behind the sign on purpose: the
    /// bars and the masked card are opaque and write depth, so the glow only survives where it
    /// spills past the shape's own edge - a halo, not a wash over the sign itself.
    private void MaybeHalo(SignRig rig, bool primary, float x, float y, float size)
    {
        Material halo = primary ? haloPrimary : haloSecondary;
        if (halo == null || rng.NextDouble() >= haloChance) return;

        if (rig.halo == null) rig.halo = EnsureCard(rig.root, "Halo");
        rig.halo.gameObject.SetActive(true);
        rig.halo.GetComponent<MeshRenderer>().sharedMaterial = halo;
        rig.halo.localPosition = new Vector3(x, y, signDepth * 0.25f);
        rig.halo.localScale = Vector3.one * size * haloSizeRatio;
    }

    private Material PickRoundSignMaterial(bool primary)
    {
        if (roundSignMaterials == null || roundSignMaterials.Length == 0) return null;
        int masks = roundSignMaterials.Length / 2;
        return roundSignMaterials[rng.Next(masks) * 2 + (primary ? 0 : 1)];
    }

    private static void HideSign(SignRig rig)
    {
        for (int i = 0; i < rig.bars.Length; i++)
            if (rig.bars[i] != null) rig.bars[i].gameObject.SetActive(false);
        if (rig.card != null) rig.card.gameObject.SetActive(false);
        if (rig.halo != null) rig.halo.gameObject.SetActive(false);
        if (rig.beam != null) rig.beam.gameObject.SetActive(false);
    }

    private static Bounds LocalBounds(Transform t)
    {
        var filters = t.GetComponentsInChildren<MeshFilter>(true);
        var b = new Bounds();
        bool first = true;
        for (int i = 0; i < filters.Length; i++)
        {
            // The prop's own sign is excluded: it is placed FROM these bounds, so letting it
            // count would grow them a little further out on every lap.
            if (filters[i].sharedMesh == null || filters[i].transform.name == "Neon") continue;
            Bounds mb = filters[i].sharedMesh.bounds;
            if (filters[i].transform != t) mb.center += filters[i].transform.localPosition;
            if (first) { b = mb; first = false; }
            else b.Encapsulate(mb);
        }
        return b;
    }

    /// The materials the shape table draws on: one alpha-clipped emissive material per light mask
    /// per accent colour, plus the halo and beam cards.
    private void BuildSignMaterials()
    {
        if (roundSignMasks != null && roundSignMasks.Length > 0)
        {
            roundSignMaterials = new Material[roundSignMasks.Length * 2];
            for (int i = 0; i < roundSignMasks.Length; i++)
            {
                roundSignMaterials[i * 2 + 0] = CreateMaskedNeonMaterial(roundSignMasks[i], primaryColor);
                roundSignMaterials[i * 2 + 1] = CreateMaskedNeonMaterial(roundSignMasks[i], secondaryColor);
            }
        }

        if (signHaloMask != null)
        {
            haloPrimary = CreateAdditiveMaterial(signHaloMask, primaryColor, haloStrength);
            haloSecondary = CreateAdditiveMaterial(signHaloMask, secondaryColor, haloStrength);
        }

        if (signBeamMask != null)
        {
            beamPrimary = CreateAdditiveMaterial(signBeamMask, primaryColor, beamStrength);
            beamSecondary = CreateAdditiveMaterial(signBeamMask, secondaryColor, beamStrength);
        }
    }

    /// A light mask turned into a neon shape: the mask's alpha cuts the quad to the shape and its
    /// own brightness drives the emission, so a ring mask becomes a ring of neon tubing.
    ///
    /// Alpha-clipped Lit rather than an additive card, for the reason the windows learned the
    /// hard way: an additive card has nothing for linear fog to fade toward, so a sign 150 units
    /// down the street would burn at full strength instead of receding into the fog with the
    /// building it hangs on. Clipping also means the dark half of the mask never draws a black
    /// rectangle over the facade.
    private Material CreateMaskedNeonMaterial(Texture texture, Color color)
    {
        var mat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
        mat.SetTexture("_BaseMap", texture);
        // Alpha stays at 1 here. URP's Lit takes its clip alpha from _BaseMap.a * _BaseColor.a,
        // so dimming the tint with `color * 0.15f` (which scales alpha too) dropped every pixel
        // below the cutoff and the sign vanished entirely.
        mat.SetColor("_BaseColor", new Color(color.r * 0.15f, color.g * 0.15f, color.b * 0.15f, 1f));
        mat.SetFloat("_Smoothness", 0f);
        mat.SetFloat("_AlphaClip", 1f);
        mat.SetFloat("_Cutoff", 0.3f);
        mat.EnableKeyword("_ALPHATEST_ON");
        mat.EnableKeyword("_EMISSION");
        mat.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
        mat.SetTexture("_EmissionMap", texture);
        mat.SetColor("_EmissionColor", color * Mathf.Clamp(accentEmissionIntensity, 0f, MaxEmissionIntensity));
        mat.renderQueue = (int)UnityEngine.Rendering.RenderQueue.AlphaTest;
        mat.enableInstancing = true;
        return mat;
    }

    private Material CreateNeonMaterial(Color color)
    {
        var mat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
        mat.SetColor("_BaseColor", color * 0.25f);
        mat.SetFloat("_Smoothness", 0.4f);
        mat.EnableKeyword("_EMISSION");
        mat.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
        mat.SetColor("_EmissionColor", color * Mathf.Clamp(accentEmissionIntensity, 0f, MaxEmissionIntensity));
        mat.enableInstancing = true;
        return mat;
    }

    // --- street lamps ---

    /// Builds the lamp rig on a pole: an emissive head, a halo card, a beam card and a real point
    /// light. Every piece is a child of the pole, so the whole rig scrolls and recycles with it
    /// and no separate bookkeeping is needed for where a lamp "is".
    private void AttachLamp(Transform prop, RowSpec spec)
    {
        if (!buildLamps || spec.pool != Pool.Poles)
        {
            propLamp.Add(null);
            propCone.Add(null);
            return;
        }

        var head = GameObject.CreatePrimitive(PrimitiveType.Cube);
        head.name = "LampHead";
        Destroy(head.GetComponent<Collider>());
        var hr = head.GetComponent<MeshRenderer>();
        hr.shadowCastingMode = ShadowCastingMode.Off;
        hr.receiveShadows = false;
        head.transform.SetParent(prop, false);

        var lightGO = new GameObject("LampLight");
        lightGO.transform.SetParent(head.transform, false);
        var light = lightGO.AddComponent<Light>();
        light.type = LightType.Point;
        light.shadows = LightShadows.None;   // 40-odd street lamps, none of them worth a shadow map
        light.renderMode = LightRenderMode.Auto;

        Transform cone = null;
        if (lampConeMask != null)
        {
            var coneGO = GameObject.CreatePrimitive(PrimitiveType.Quad);
            coneGO.name = "LampCone";
            Destroy(coneGO.GetComponent<Collider>());
            var cr = coneGO.GetComponent<MeshRenderer>();
            cr.shadowCastingMode = ShadowCastingMode.Off;
            cr.receiveShadows = false;
            coneGO.transform.SetParent(prop, false);
            cone = coneGO.transform;
        }

        if (lampGlowMask != null)
        {
            var glow = GameObject.CreatePrimitive(PrimitiveType.Quad);
            glow.name = "LampGlow";
            Destroy(glow.GetComponent<Collider>());
            var gr = glow.GetComponent<MeshRenderer>();
            gr.shadowCastingMode = ShadowCastingMode.Off;
            gr.receiveShadows = false;
            glow.transform.SetParent(head.transform, false);
            glow.transform.localScale = Vector3.one * 2.2f;
        }

        propLamp.Add(light);
        propCone.Add(cone);
    }

    /// Re-aims and re-colours a pole's lamp after the pole has been re-rolled: the head goes to
    /// the top of whatever model this now is, leaning out over the road, and the whole rig picks
    /// warm or cool for this lap.
    private void RerollLamp(int index, Bounds local, Vector3 towardRoadLocal)
    {
        Light light = propLamp[index];
        if (light == null) return;

        Transform head = light.transform.parent;
        Vector3 dir = towardRoadLocal.normalized;
        float reach = Mathf.Abs(dir.x) * local.extents.x + Mathf.Abs(dir.z) * local.extents.z;

        head.localPosition = new Vector3(local.center.x, local.max.y * 0.94f, local.center.z) + dir * reach * 0.55f;
        head.localScale = Vector3.one * lampHeadSize;
        head.localRotation = Quaternion.identity;

        bool warm = rng.NextDouble() < lampWarmChance;
        Color color = warm ? lampWarmColor : lampCoolColor;

        head.GetComponent<MeshRenderer>().sharedMaterial = warm ? lampHeadWarm : lampHeadCool;
        light.color = color;
        light.intensity = lampIntensity;
        light.range = lampRange;

        foreach (Transform child in head)
            if (child.name == "LampGlow")
                child.GetComponent<MeshRenderer>().sharedMaterial = warm ? lampGlowWarm : lampGlowCool;

        Transform cone = propCone[index];
        if (cone == null) return;

        cone.GetComponent<MeshRenderer>().sharedMaterial = warm ? coneWarm : coneCool;
        // Hung from the lamp head down to the ground, wide at the bottom. The mask's bright end
        // is its apex, so the card is flipped to put that end at the lamp.
        float headY = local.max.y * 0.94f;
        cone.localPosition = new Vector3(local.center.x, headY * 0.5f, local.center.z) + dir * reach * 0.55f;
        cone.localScale = new Vector3(coneWidth, Mathf.Max(0.2f, headY * coneHeight * 0.5f), 1f);
    }

    // --- windows ---

    /// What a lit facade looks like. This enum and FillWindowPattern below are the ENTIRE policy
    /// for the city's windows: how many light up, in what arrangement, and in what shape. The
    /// sheet builder underneath knows only how to paint a cell grid softly and does not care
    /// which pattern produced it, so a different-looking city is an edit to one function.
    private enum WindowPattern
    {
        Scattered,    // a few isolated windows, the classic late-night office
        VerticalRuns, // one or two lit stairwell/lift columns running up the facade
        Floors,       // a whole storey lit, reading as a horizontal band
        WideUnits,    // sparse two-cell-wide windows: apartments rather than offices
        EdgeColumn,   // the corner column lit top to bottom, an outline down the building's edge
    }

    /// The per-facade lighting policy. Writes brightness per grid cell, 0 = dark. Every pattern
    /// stays deliberately sparse: the point of the rework is that a facade is mostly dark and the
    /// lit windows are accents, instead of the solid mosaic the flat per-cell random gave.
    private void FillWindowPattern(float[] cells, int cols, int rowsCount, WindowPattern pattern)
    {
        // A lit window is never uniformly lit: per-window variation is what stops a facade
        // reading as a printed checkerboard.
        float Vary() => 0.5f + (float)rng.NextDouble() * 0.5f;

        switch (pattern)
        {
            case WindowPattern.VerticalRuns:
            {
                int runs = 1 + rng.Next(2);
                for (int r = 0; r < runs; r++)
                {
                    int x = rng.Next(cols);
                    int y0 = rng.Next(rowsCount / 2);
                    int y1 = Mathf.Min(rowsCount - 1, y0 + 2 + rng.Next(rowsCount - y0));
                    for (int y = y0; y <= y1; y++)
                        if (rng.NextDouble() < 0.55) cells[y * cols + x] = Vary();
                }
                break;
            }
            case WindowPattern.Floors:
            {
                int floors = 1 + rng.Next(2);
                for (int f = 0; f < floors; f++)
                {
                    int y = rng.Next(rowsCount);
                    int x0 = rng.Next(Mathf.Max(1, cols / 3));
                    int x1 = Mathf.Min(cols - 1, x0 + cols / 2 + rng.Next(cols / 2));
                    for (int x = x0; x <= x1; x++)
                        if (rng.NextDouble() < 0.55) cells[y * cols + x] = Vary();
                }
                break;
            }
            case WindowPattern.WideUnits:
            {
                int units = Mathf.Max(2, Mathf.RoundToInt(cols * rowsCount * windowLitChance * 0.4f));
                for (int u = 0; u < units; u++)
                {
                    int x = rng.Next(cols - 1);
                    int y = rng.Next(rowsCount);
                    float b = Vary();
                    cells[y * cols + x] = b;
                    cells[y * cols + x + 1] = b;
                }
                break;
            }
            case WindowPattern.EdgeColumn:
            {
                int x = rng.NextDouble() < 0.5 ? 0 : cols - 1;
                for (int y = 0; y < rowsCount; y++)
                    if (rng.NextDouble() < 0.6) cells[y * cols + x] = Vary() * 0.8f;
                // Plus a handful of ordinary windows, so the edge does not stand entirely alone.
                int extra = Mathf.RoundToInt(cols * rowsCount * windowLitChance * 0.3f);
                for (int i = 0; i < extra; i++)
                    cells[rng.Next(rowsCount) * cols + rng.Next(cols)] = Vary();
                break;
            }
            default:
            {
                for (int y = 0; y < rowsCount; y++)
                    for (int x = 0; x < cols; x++)
                        if (rng.NextDouble() < windowLitChance) cells[y * cols + x] = Vary();
                break;
            }
        }
    }

    /// A handful of shared window sheets, one per variant, so two buildings side by side rarely
    /// light up the same way and every facade in the city can carry one for a few draw calls.
    ///
    /// Each sheet is painted at several pixels per cell and bilinear-filtered, NOT one pixel per
    /// window with point filtering as the first pass did: a point-filtered pixel stretched across
    /// a facade IS a hard-edged square, which is what made the skyline look like Minecraft. Here
    /// a lit cell is a soft-edged rectangle with a little glow bleeding past its own cell, which
    /// is how a lit window actually looks from across a street.
    private void BuildWindowMaterials()
    {
        int variants = Mathf.Max(1, windowVariants);
        windowMaterials = new Material[variants];
        var patterns = (WindowPattern[])System.Enum.GetValues(typeof(WindowPattern));

        for (int v = 0; v < variants; v++)
        {
            int cols = Mathf.Max(1, windowGrid.x);
            int rowsCount = Mathf.Max(1, windowGrid.y);

            var cells = new float[cols * rowsCount];
            FillWindowPattern(cells, cols, rowsCount, patterns[v % patterns.Length]);

            var tex = PaintWindowSheet(cells, cols, rowsCount, v % 2 == 0 ? windowWarmColor : windowCoolColor);

            // Emissive Lit, not additive Unlit. An additive card ignores distance: the first pass
            // had lit windows on buildings 150 units away burning at full strength as little white
            // squares hanging in the sky, because additive blending has nothing for linear fog to
            // fade toward. Emission on a Lit surface fogs with everything else in the street.
            var mat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            mat.SetColor("_BaseColor", Color.black);
            mat.SetFloat("_Smoothness", 0f);
            // Clipped against the sheet's own alpha, so only the lit windows exist as surface at
            // all and the dark wall between them is the building itself.
            mat.SetTexture("_BaseMap", tex);
            mat.SetFloat("_AlphaClip", 1f);
            mat.SetFloat("_Cutoff", 0.05f);
            mat.EnableKeyword("_ALPHATEST_ON");
            mat.renderQueue = (int)UnityEngine.Rendering.RenderQueue.AlphaTest;
            mat.EnableKeyword("_EMISSION");
            mat.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
            mat.SetTexture("_EmissionMap", tex);
            mat.SetColor("_EmissionColor", Color.white * windowEmission);
            mat.enableInstancing = true;
            windowMaterials[v] = mat;
        }
    }

    /// Paints a cell grid into a bilinear sheet: every lit cell becomes a soft-edged rectangle
    /// with a faint glow spilling into its neighbours. The spill is done by accumulating each
    /// cell's contribution into the pixel buffer rather than by blurring afterwards, so a lone
    /// window keeps a defined core while a run of them merges into one band of light.
    private Texture2D PaintWindowSheet(float[] cells, int cols, int rowsCount, Color litColor)
    {
        int cell = Mathf.Clamp(windowCellPixels, 2, 16);
        int w = cols * cell;
        int h = rowsCount * cell;
        var acc = new float[w * h];

        // Inset: the dark gap between windows. Without it a run of lit cells is one solid slab.
        float inset = 0.22f;
        float bleed = Mathf.Max(0.001f, windowGlowBleed);

        for (int cy = 0; cy < rowsCount; cy++)
        {
            for (int cx = 0; cx < cols; cx++)
            {
                float b = cells[cy * cols + cx];
                if (b <= 0f) continue;

                // The window's own rectangle, in cell units, and the box it can bleed into.
                float x0 = cx + inset, x1 = cx + 1f - inset;
                float y0 = cy + inset, y1 = cy + 1f - inset;

                int px0 = Mathf.Max(0, Mathf.FloorToInt((x0 - bleed) * cell));
                int px1 = Mathf.Min(w - 1, Mathf.CeilToInt((x1 + bleed) * cell));
                int py0 = Mathf.Max(0, Mathf.FloorToInt((y0 - bleed) * cell));
                int py1 = Mathf.Min(h - 1, Mathf.CeilToInt((y1 + bleed) * cell));

                for (int py = py0; py <= py1; py++)
                {
                    float v = (py + 0.5f) / cell;
                    float dy = Mathf.Max(0f, Mathf.Max(y0 - v, v - y1));
                    for (int px = px0; px <= px1; px++)
                    {
                        float u = (px + 0.5f) / cell;
                        float dx = Mathf.Max(0f, Mathf.Max(x0 - u, u - x1));
                        // Distance outside the window rectangle, in cell units: 0 inside the
                        // pane, rising toward the bleed radius, where the glow reaches zero.
                        float d = Mathf.Sqrt(dx * dx + dy * dy);
                        float falloff = d <= 0f ? 1f : Mathf.Pow(Mathf.Clamp01(1f - d / bleed), 2.2f) * 0.75f;
                        int i = py * w + px;
                        acc[i] = Mathf.Max(acc[i], b * falloff);
                    }
                }
            }
        }

        var tex = new Texture2D(w, h, TextureFormat.RGBA32, true)
        {
            name = "WindowSheet",
            filterMode = FilterMode.Bilinear,
            wrapMode = TextureWrapMode.Clamp,
        };
        var pixels = new Color32[w * h];
        for (int i = 0; i < pixels.Length; i++)
        {
            Color c = litColor * acc[i];
            // Alpha carries the same brightness, scaled up so a window's soft edge still clips in
            // rather than out. The pane is alpha-clipped against it (see BuildWindowMaterials):
            // an opaque pane would paint a black rectangle over the facade wherever a window is
            // dark, and with the rework's much sparser lighting that rectangle is most of it -
            // it swallowed the street lamps' pools on every near wall.
            pixels[i] = new Color32(
                (byte)(Mathf.Clamp01(c.r) * 255f),
                (byte)(Mathf.Clamp01(c.g) * 255f),
                (byte)(Mathf.Clamp01(c.b) * 255f),
                (byte)(Mathf.Clamp01(acc[i] * 4f) * 255f));
        }
        tex.SetPixels32(pixels);
        tex.Apply();
        return tex;
    }

    private Transform CreateWindowPane(Transform parent)
    {
        if (windowMaterials == null || windowMaterials.Length == 0) return null;

        var go = GameObject.CreatePrimitive(PrimitiveType.Quad);
        go.name = "Windows";
        Destroy(go.GetComponent<Collider>());
        var r = go.GetComponent<MeshRenderer>();
        r.shadowCastingMode = ShadowCastingMode.Off;
        r.receiveShadows = false;
        go.transform.SetParent(parent, false);
        return go.transform;
    }

    private void PlaceWindows(int index, Bounds local, Vector3 towardRoadLocal)
    {
        Transform pane = propWindows[index];
        if (pane == null) return;

        Vector3 dir = towardRoadLocal.normalized;
        float reach = Mathf.Abs(dir.x) * local.extents.x + Mathf.Abs(dir.z) * local.extents.z;
        float faceWidth = Mathf.Abs(dir.x) * local.size.z + Mathf.Abs(dir.z) * local.size.x;
        float h = local.size.y;

        pane.GetComponent<MeshRenderer>().sharedMaterial = windowMaterials[rng.Next(windowMaterials.Length)];
        pane.localRotation = Quaternion.LookRotation(-dir, Vector3.up); // quad faces its own -Z
        pane.localPosition = new Vector3(local.center.x, local.min.y + h * 0.52f, local.center.z) + dir * (reach + 0.03f);
        pane.localScale = new Vector3(faceWidth * windowCoverage, h * windowCoverage, 1f);
    }

    // --- haze ---

    /// Soft cards lying low along the street. They are what turns the lamps' light into something
    /// that occupies the air instead of just landing on surfaces, and they are the cheapest depth
    /// cue available: a dozen and a half quads for the whole city.
    private void BuildHaze()
    {
        if (hazeMask == null) return;

        hazeMaterial = CreateAdditiveMaterial(hazeMask, hazeColor, hazeStrength);
        var parent = new GameObject("Haze");
        parent.transform.SetParent(transform, false);

        for (int i = 0; i < hazeCards; i++)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Quad);
            go.name = "HazeCard";
            Destroy(go.GetComponent<Collider>());
            var r = go.GetComponent<MeshRenderer>();
            r.sharedMaterial = hazeMaterial;
            r.shadowCastingMode = ShadowCastingMode.Off;
            r.receiveShadows = false;

            var t = go.transform;
            t.SetParent(parent.transform, false);
            float size = Mathf.Lerp(hazeSizeMin, hazeSizeMax, (float)rng.NextDouble());
            t.localScale = new Vector3(size, size * 0.45f, 1f);
            // Kept out of the camera's lap and off the road: a card this size passing through the
            // near plane fills the frame with one white smear, which is exactly what the first
            // pass did. They belong over the pavement, at a distance, reading as depth.
            float x = (float)(rng.NextDouble() * 2.0 - 1.0) * 22f;
            if (Mathf.Abs(x) < 7f) x = Mathf.Sign(x == 0f ? 1f : x) * 7f;
            t.localPosition = new Vector3(
                x,
                hazeHeight * (0.5f + (float)rng.NextDouble()),
                hazeNearZ + (float)rng.NextDouble() * (span - (hazeNearZ - recycleZ)));
            hazeProps.Add(t);
        }
    }

    private static void FlipVertically(Material mat)
    {
        mat.SetTextureScale("_BaseMap", new Vector2(1f, -1f));
        mat.SetTextureOffset("_BaseMap", new Vector2(0f, 1f));
    }

    /// One unlit additive card material. Additive rather than alpha-blended on purpose: these are
    /// light, not objects, so black in the mask has to mean "nothing here" and the card must never
    /// darken what is behind it or need sorting against the city it hangs in front of.
    private Material CreateAdditiveMaterial(Texture texture, Color color, float strength)
    {
        var mat = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
        mat.SetFloat("_Surface", 1f);                  // transparent
        mat.SetFloat("_Blend", 2f);                    // additive
        mat.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
        mat.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.One);
        mat.SetFloat("_ZWrite", 0f);
        mat.SetFloat("_AlphaClip", 0f);
        mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        mat.DisableKeyword("_ALPHAPREMULTIPLY_ON");
        mat.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
        if (texture != null) mat.SetTexture("_BaseMap", texture);
        mat.SetColor("_BaseColor", color * Mathf.Max(0f, strength));
        mat.enableInstancing = true;
        return mat;
    }

    // --- ground ---

    /// Two dark slabs either side of the road. Without them the city stands on nothing and the
    /// buildings read as floating cut-outs; with them the tunnel looks like it runs THROUGH a
    /// place. Static on purpose: a featureless slab has nothing to betray that it is not moving,
    /// so scrolling it would only cost draw calls and risk a seam.
    private void BuildGround()
    {
        groundMaterial = new Material(Shader.Find("Universal Render Pipeline/Lit"));
        groundMaterial.SetColor("_BaseColor", groundColor);
        groundMaterial.SetFloat("_Smoothness", groundSmoothness);
        // Matte, and deliberately so. A slab this size facing straight up turns any smoothness
        // into one enormous specular sheen off the directional light and the reflection probe,
        // and the first pass read as a pale concrete field brighter than the road it framed.
        groundMaterial.SetFloat("_Metallic", 0f);
        groundMaterial.enableInstancing = true;

        float length = tunnelLength + extraLengthBeyondTunnel - recycleZ;
        float centreZ = (tunnelLength + extraLengthBeyondTunnel + recycleZ) * 0.5f;

        for (int s = -1; s <= 1; s += 2)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = s < 0 ? "Ground_L" : "Ground_R";
            Destroy(go.GetComponent<Collider>());
            go.transform.SetParent(transform, false);
            // Sits a hair below the tunnel floor so the road surface always wins the depth test.
            go.transform.localPosition = new Vector3(s * (groundWidth * 0.5f + 3f), -0.06f, centreZ);
            go.transform.localScale = new Vector3(groundWidth, 0.1f, length);
            var r = go.GetComponent<MeshRenderer>();
            r.sharedMaterial = groundMaterial;
            r.shadowCastingMode = ShadowCastingMode.Off;
            r.receiveShadows = false;
        }
    }

    private void OnDestroy()
    {
        if (neonPrimary != null) Destroy(neonPrimary);
        if (neonSecondary != null) Destroy(neonSecondary);
        if (groundMaterial != null) Destroy(groundMaterial);

        // Every material and texture here was made at runtime with new Material/new Texture2D, so
        // nothing else will ever collect them. A theme swap destroys this object, and that has to
        // take the whole set with it.
        Destroy(lampHeadWarm); Destroy(lampHeadCool);
        Destroy(lampGlowWarm); Destroy(lampGlowCool);
        Destroy(coneWarm); Destroy(coneCool);
        Destroy(hazeMaterial);
        Destroy(haloPrimary); Destroy(haloSecondary);
        Destroy(beamPrimary); Destroy(beamSecondary);
        if (roundSignMaterials != null)
            for (int i = 0; i < roundSignMaterials.Length; i++)
                if (roundSignMaterials[i] != null) Destroy(roundSignMaterials[i]);

        if (windowMaterials == null) return;
        for (int i = 0; i < windowMaterials.Length; i++)
        {
            if (windowMaterials[i] == null) continue;
            // The sheet is the material's emission map, not its base map - destroying "_BaseMap"
            // here (which is null) left every generated window texture alive after a theme swap.
            Destroy(windowMaterials[i].GetTexture("_EmissionMap"));
            Destroy(windowMaterials[i]);
        }
    }
}
