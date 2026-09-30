using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;
using UnityEngine.Rendering.Universal;
using UnityEngine.UI;

/// The trainer: a neon silhouette in a window on the left of the screen, facing the player like an
/// instructor in a mirror, that shows every note calmly and exactly on it - a hand reaching to the
/// cube, a foot set down on the tile, a step and a lean away from a wall - and bounces on the beat
/// in between.
///
/// How it is put together, all at runtime so nothing of it is saved into the scene:
///   * the character from a SilhouetteMotionSet, instanced far below the tunnel on its own layer;
///   * a camera in front of it that sees only that layer and renders into a RenderTexture;
///   * a Screen Space Overlay canvas with the panel - dark glass, a chamfered neon frame in the
///     theme colour, the figure, and a contour/halo made from the figure's alpha (SilhouetteGlow).
///
/// Motion: SilhouettePlanner lays the whole track out once the chart is loaded - clip segments
/// (the relaxed stance, the jump, the crouch) and procedural gestures. Every frame this evaluates a
/// manual PlayableGraph at SongClock's smoothed time: a mixer of the clips, then SilhouettePoseJob,
/// which poses body, hands and feet from the gestures by IK. The pose is therefore a pure function
/// of song time - identical in Play and in a recording.
[DefaultExecutionOrder(100)]
public class SilhouetteTrainer : MonoBehaviour
{
    [Header("Wiring")]
    [SerializeField] private SilhouetteMotionSet motionSet;
    [SerializeField] private CueSpawner cueSpawner;
    // Theme colours come from the HUD, which TunnelThemeApplier already keeps up to date.
    [SerializeField] private HudController hud;
    [SerializeField] private Shader bodyShader;
    [SerializeField] private Shader glowShader;

    // A fifth of the width and ~0.6 of the height, a little above centre: at full 2/3 height
    // the panel's foot runs into the left lane's hit pad, and the lanes must stay clear.
    [Header("Panel (reference 2560 x 1440)")]
    [SerializeField] private Vector2 panelSize = new Vector2(500f, 800f);
    [SerializeField] private float panelLeftMargin = 36f;
    [SerializeField] private float panelRaise = 110f;
    [SerializeField, Range(0f, 1f)] private float glassAlpha = 0.85f;
    [SerializeField] private float frameWidth = 3f;
    [SerializeField] private float frameCorner = 26f;
    [SerializeField, Range(0f, 1f)] private float frameGlow = 0.45f;
    // The trainer keeps its own colour (cyan) whatever the tunnel's theme, so it reads against
    // every palette; turn this on to follow the theme instead.
    [SerializeField] private bool followThemeColor;
    [SerializeField] private Color trainerColor = new Color(0f, 0.85f, 1f);
    // With followThemeColor: false = the theme's secondary colour, true = its primary.
    [SerializeField] private bool usePrimaryColor;
    // true: the figure is drawn as its contour only - a line drawing, the glass visible inside.
    [SerializeField] private bool outlineOnly = true;
    // Contour width as a fraction of the panel's height in pixels: ~3.5 px at 1440p, twice at 4K.
    [SerializeField, Range(0.001f, 0.012f)] private float contourWidth = 0.0045f;

    [Header("Figure camera")]
    // 180: in front of the figure, looking back at it - the instructor faces the player. What the
    // figure does to its own left then shows on the screen's right, exactly as in a mirror, which
    // is what lets every side below be the SCREEN's side.
    [SerializeField] private float cameraYaw = 180f;
    [SerializeField] private float cameraPitch = 4f;
    // Metres of the figure's world that fit the panel's height. The figure is ~1.8 m; the rest is
    // room for a jump, a raised arm, and an arm held out sideways in a dodge.
    [SerializeField] private float viewHeight = 3f;
    [SerializeField] private float viewCenterY = 1.2f;
    [SerializeField] private float viewAheadZ = 0f;
    // Frame the figure by its own height instead of the two numbers above, so a shorter or taller
    // character stands full-length in the middle of the panel with the same room for a jump and a
    // raised arm. viewHeight = height x frameHeights, centre at height x frameCenter.
    [SerializeField] private bool autoFrame = true;
    [SerializeField] private float frameHeights = 1.7f;
    [SerializeField] private float frameCenter = 0.66f;
    // Share of a step aside the panel's camera follows. At 0 a tap thrown outward from the
    // stepped-out figure leaves the panel; at 1 the step would not show at all.
    [SerializeField, Range(0f, 1f)] private float stepAsideFollow = 0.4f;
    private Vector3 cameraRest;

    [Header("Textured character (a playlist's own trainer)")]
    [SerializeField] private Shader texturedShader;
    // The panel's backdrop in textured mode: the segment's two colours from the sides into a dark
    // middle, like a stage lit from both wings; its strength at the edges.
    [SerializeField, Range(0f, 1f)] private float backdropStrength = 0.55f;
    // The soft light around the character's edge, from the figure's alpha (SilhouetteGlow).
    [SerializeField, Range(0f, 2f)] private float texturedHalo = 1.1f;

    [Header("Diagnostics")]
    // The first N notes log planned vs observed impact time, once, and write it to
    // Screenshots/silhouette_sync.txt. 0 turns it off.
    [SerializeField] private int syncReportNotes = 10;

    private const int StageLayerFallback = 31;
    private static readonly Vector3 StagePosition = new Vector3(0f, -400f, 0f);

    private GameObject stage;
    private Transform figure;
    private Animator animator;
    private Camera figureCamera;
    private RenderTexture rt;
    private RawImage figureImage, figureGlow;
    private Image frameEdge, frameHalo;
    private RectTransform panel;
    private Canvas canvas;
    private Material bodyMat, glowMat;
    // Textured mode: the character keeps its own texture and gets a rim light in the segment's
    // colour; the panel behind it is lit in the same colours. Set by UseMotionSet.
    private bool textured;
    private readonly List<Material> texturedMats = new List<Material>();
    private RawImage backdrop;
    private Texture2D backdropTex;
    private Color backdropShownA = Color.clear, backdropShownB = Color.clear, frameShown = Color.clear;
    private Color themeShown = Color.clear;

    private PlayableGraph graph;
    private AnimationMixerPlayable mixer;
    private AnimationScriptPlayable poser;
    private readonly Dictionary<AnimationClip, int> inputOf = new Dictionary<AnimationClip, int>();
    private readonly List<AnimationClipPlayable> inputs = new List<AnimationClipPlayable>();
    private float[] weights;
    private int stanceInput = -1;                 // the first calm idle: the pose measured at start
    private readonly List<int> stanceInputs = new List<int>();

    // The figure's own proportions, measured once from its stance, in stage space.
    private bool measured;
    private Vector3 lShoulder, rShoulder, lFootRest, rFootRest;
    private Transform lHandBone, rHandBone, lFootBone, rFootBone;

    private List<SilhouettePlanner.Segment> plan;
    private List<SilhouettePlanner.Gesture> gestures;
    private float beatAnchor, secondsPerBeat = 0.5f;
    private int cursor;
    private bool built, planned;

    // What the pose job was last told, for the sync report's reach check.
    private Vector3 lastLHand, lastRHand, lastLFoot, lastRFoot;

    // Sync report
    private readonly List<string> syncRows = new List<string>();
    private readonly HashSet<int> syncSeen = new HashSet<int>();
    private bool syncWritten;

    // Screenshots on demand: song time -> file.
    private static readonly List<KeyValuePair<float, string>> captures = new List<KeyValuePair<float, string>>();

    public IReadOnlyList<SilhouettePlanner.Segment> Plan => plan;
    public IReadOnlyList<SilhouettePlanner.Gesture> Gestures => gestures;

    /// Asks for a screenshot on the first frame at or after `songTime`. For the visual checks.
    public static void CaptureAt(float songTime, string path) => captures.Add(new KeyValuePair<float, string>(songTime, path));

    /// Keeps the character and its contour colour, but lights the panel - its backdrop and the
    /// frame's glow - in the theme's two colours, following them through a playlist.
    public void UsePanelPalette()
    {
        if (built) return;
        panelPalette = true;
    }
    private bool panelPalette;

    /// Swaps the character before the trainer is built - a playlist bringing its own. `asTextured`
    /// draws it as itself, textured, with a rim light and a backdrop in the theme's colours, rather
    /// than as a neon contour.
    public void UseMotionSet(SilhouetteMotionSet set, bool asTextured)
    {
        if (built || set == null) return;
        motionSet = set;
        textured = asTextured;
        if (textured)
        {
            outlineOnly = false;
            followThemeColor = true;
        }
    }

    private float Now => cueSpawner != null && cueSpawner.Clock.IsRunning ? cueSpawner.Clock.SongTime : -1000f + Time.time;

    private void Start()
    {
        if (motionSet == null || motionSet.model == null)
        {
            Debug.LogWarning("[Silhouette] no motion set / model - trainer off", this);
            enabled = false;
            return;
        }
        Build();
    }

    private void OnDestroy()
    {
        if (graph.IsValid()) graph.Destroy();
        if (rt != null) { rt.Release(); Destroy(rt); }
        if (stage != null) Destroy(stage);
        if (canvas != null) Destroy(canvas.gameObject);
        if (bodyMat != null) Destroy(bodyMat);
        if (glowMat != null) Destroy(glowMat);
        foreach (var m in texturedMats) if (m != null) Destroy(m);
        if (backdropTex != null) Destroy(backdropTex);
    }

    // --- building ---

    private void Build()
    {
        int layer = LayerMask.NameToLayer("Silhouette");
        if (layer < 0) layer = StageLayerFallback;

        stage = new GameObject("SilhouetteStage");
        stage.transform.position = StagePosition;

        var model = Instantiate(motionSet.model, stage.transform);
        model.name = "Figure";
        figure = model.transform;
        figure.localPosition = Vector3.zero;
        figure.localRotation = Quaternion.identity;
        SetLayer(model, layer);

        bodyMat = new Material(bodyShader != null ? bodyShader : Shader.Find("Tunnel/SilhouetteBody"));
        var texShader = texturedShader != null ? texturedShader : Shader.Find("Tunnel/SilhouetteTextured");
        foreach (var r in model.GetComponentsInChildren<Renderer>(true))
        {
            var mats = new Material[r.sharedMaterials.Length];
            for (int i = 0; i < mats.Length; i++) mats[i] = textured ? TexturedFrom(r.sharedMaterials[i], texShader) : bodyMat;
            r.sharedMaterials = mats;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
            if (r is SkinnedMeshRenderer smr) smr.updateWhenOffscreen = true;
        }

        animator = model.GetComponent<Animator>();
        if (animator == null) animator = model.AddComponent<Animator>();
        if (motionSet.avatar != null) animator.avatar = motionSet.avatar;
        animator.runtimeAnimatorController = null;
        animator.applyRootMotion = false;
        animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
        if (animator.isHuman)
        {
            lHandBone = animator.GetBoneTransform(HumanBodyBones.LeftHand);
            rHandBone = animator.GetBoneTransform(HumanBodyBones.RightHand);
            lFootBone = animator.GetBoneTransform(HumanBodyBones.LeftFoot);
            rFootBone = animator.GetBoneTransform(HumanBodyBones.RightFoot);
        }

        BuildGraph();
        BuildCamera(layer);
        BuildPanel();

        // The tunnel camera must never see the stage, whatever it is pointed at.
        if (Camera.main != null) Camera.main.cullingMask &= ~(1 << layer);
        built = true;
    }

    /// The character's own material, re-made on the textured shader: its texture and tint kept.
    private Material TexturedFrom(Material source, Shader shader)
    {
        var m = new Material(shader);
        if (source != null)
        {
            Texture tex = source.HasProperty("_BaseMap") ? source.GetTexture("_BaseMap") : null;
            if (tex == null && source.HasProperty("_MainTex")) tex = source.GetTexture("_MainTex");
            if (tex != null) m.SetTexture("_BaseMap", tex);
            if (source.HasProperty("_BaseColor")) m.SetColor("_BaseColor", source.GetColor("_BaseColor"));
            else if (source.HasProperty("_Color")) m.SetColor("_BaseColor", source.GetColor("_Color"));
        }
        texturedMats.Add(m);
        return m;
    }

    private static void SetLayer(GameObject go, int layer)
    {
        go.layer = layer;
        foreach (Transform t in go.transform) SetLayer(t.gameObject, layer);
    }

    private void BuildGraph()
    {
        graph = PlayableGraph.Create("Silhouette");
        graph.SetTimeUpdateMode(DirectorUpdateMode.Manual);
        var output = AnimationPlayableOutput.Create(graph, "Figure", animator);

        var clips = new List<AnimationClip>();
        void Add(SilhouetteMotion m) { if (m != null && m.Valid && !clips.Contains(m.clip)) clips.Add(m.clip); }
        if (motionSet.idle != null) foreach (var m in motionSet.idle) Add(m);
        if (motionSet.idleLively != null) foreach (var m in motionSet.idleLively) Add(m);
        if (motionSet.curb != null) foreach (var m in motionSet.curb) Add(m);
        Add(motionSet.duckDown); Add(motionSet.duckUp);

        mixer = AnimationMixerPlayable.Create(graph, clips.Count);
        for (int i = 0; i < clips.Count; i++)
        {
            var p = AnimationClipPlayable.Create(graph, clips[i]);
            p.SetApplyFootIK(true);
            p.Pause();   // time is set by hand every frame
            graph.Connect(p, 0, mixer, i);
            mixer.SetInputWeight(i, 0f);
            inputOf[clips[i]] = i;
            inputs.Add(p);
        }
        weights = new float[clips.Count];
        foreach (var m in motionSet.idle) if (m != null && m.Valid) { stanceInput = inputOf[m.clip]; break; }
        void Stance(List<SilhouetteMotion> l) { if (l != null) foreach (var m in l) if (m != null && m.Valid && !stanceInputs.Contains(inputOf[m.clip])) stanceInputs.Add(inputOf[m.clip]); }
        Stance(motionSet.idle);
        Stance(motionSet.idleLively);

        poser = AnimationScriptPlayable.Create(graph, new SilhouettePoseJob { forward = Vector3.forward, pin = 1f }, 1);
        graph.Connect(mixer, 0, poser, 0);
        poser.SetInputWeight(0, 1f);
        output.SetSourcePlayable(poser);
    }

    private void BuildCamera(int layer)
    {
        var go = new GameObject("FigureCamera");
        go.transform.SetParent(stage.transform, false);
        figureCamera = go.AddComponent<Camera>();
        figureCamera.orthographic = true;
        figureCamera.orthographicSize = viewHeight * 0.5f;
        figureCamera.cullingMask = 1 << layer;
        figureCamera.clearFlags = CameraClearFlags.SolidColor;
        figureCamera.backgroundColor = new Color(0f, 0f, 0f, 0f);
        figureCamera.allowHDR = false;
        figureCamera.nearClipPlane = 0.1f;
        figureCamera.farClipPlane = 30f;
        figureCamera.depth = -50f;
        var data = go.AddComponent<UniversalAdditionalCameraData>();
        data.renderPostProcessing = false;
        data.renderShadows = false;
        data.antialiasing = AntialiasingMode.None;
        data.requiresColorTexture = false;
        data.requiresDepthTexture = false;

        var rot = Quaternion.Euler(cameraPitch, cameraYaw, 0f);
        Vector3 target = new Vector3(0f, viewCenterY, viewAheadZ);
        go.transform.localRotation = rot;
        go.transform.localPosition = cameraRest = target - rot * Vector3.forward * 10f;
    }

    private void BuildPanel()
    {
        var cgo = new GameObject("SilhouettePanelCanvas");
        canvas = cgo.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 50;   // under the HUD (100)
        var scaler = cgo.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(2560f, 1440f);
        scaler.matchWidthOrHeight = 1f;

        panel = new GameObject("Panel", typeof(RectTransform)).GetComponent<RectTransform>();
        panel.SetParent(cgo.transform, false);
        panel.anchorMin = panel.anchorMax = new Vector2(0f, 0.5f);
        panel.pivot = new Vector2(0f, 0.5f);
        panel.anchoredPosition = new Vector2(panelLeftMargin, panelRaise);
        panel.sizeDelta = panelSize;

        float halo = 40f;
        frameHalo = PanelImage("FrameGlow", SilhouettePanelSprites.Glow(frameCorner, halo), Color.clear, -halo);
        var glass = PanelImage("Glass", SilhouettePanelSprites.Fill(frameCorner), new Color(0.01f, 0.012f, 0.03f, glassAlpha), 0f);
        if (textured || panelPalette)
        {
            // The backdrop: a texture recoloured whenever the theme's colours move (ApplyTheme),
            // masked to the panel's chamfered shape by the glass under it.
            backdropTex = new Texture2D(64, 32, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
            backdrop = PanelRaw("Backdrop", null);
            backdrop.texture = backdropTex;
            var mask = glass.gameObject.AddComponent<Mask>();
            mask.showMaskGraphic = true;
            backdrop.rectTransform.SetParent(glass.rectTransform, false);
        }

        glowMat = new Material(glowShader != null ? glowShader : Shader.Find("Tunnel/SilhouetteGlow"));
        figureGlow = PanelRaw("FigureGlow", glowMat);
        figureImage = PanelRaw("Figure", null);
        frameEdge = PanelImage("Frame", SilhouettePanelSprites.Edge(frameCorner, frameWidth), Color.white, 0f);
        EnsureTexture();
    }

    private Image PanelImage(string name, Sprite sprite, Color color, float outset)
    {
        var img = new GameObject(name, typeof(RectTransform), typeof(Image)).GetComponent<Image>();
        img.rectTransform.SetParent(panel, false);
        img.rectTransform.anchorMin = Vector2.zero;
        img.rectTransform.anchorMax = Vector2.one;
        img.rectTransform.offsetMin = new Vector2(outset, outset);
        img.rectTransform.offsetMax = new Vector2(-outset, -outset);
        img.sprite = sprite;
        img.type = Image.Type.Sliced;
        img.color = color;
        img.raycastTarget = false;
        return img;
    }

    private RawImage PanelRaw(string name, Material mat)
    {
        var img = new GameObject(name, typeof(RectTransform), typeof(RawImage)).GetComponent<RawImage>();
        img.rectTransform.SetParent(panel, false);
        img.rectTransform.anchorMin = Vector2.zero;
        img.rectTransform.anchorMax = Vector2.one;
        img.rectTransform.offsetMin = img.rectTransform.offsetMax = Vector2.zero;
        img.material = mat;
        img.raycastTarget = false;
        return img;
    }

    /// The RenderTexture follows the panel's size on screen, so the figure is drawn 1:1 at any
    /// output resolution - 1440p, 4K - and recreated only when that size changes.
    private void EnsureTexture()
    {
        float scale = canvas != null ? canvas.scaleFactor : 1f;
        int w = Mathf.Max(64, Mathf.RoundToInt(panel.rect.width * scale));
        int h = Mathf.Max(64, Mathf.RoundToInt(panel.rect.height * scale));
        if (rt != null && rt.width == w && rt.height == h) return;
        if (rt != null) { rt.Release(); Destroy(rt); }
        rt = new RenderTexture(w, h, 24, RenderTextureFormat.ARGB32)
        {
            name = "SilhouetteRT",
            useMipMap = true,
            autoGenerateMips = true,
            antiAliasing = 4,
            filterMode = FilterMode.Trilinear,
        };
        rt.Create();
        figureCamera.targetTexture = rt;
        figureCamera.aspect = w / (float)h;
        figureImage.texture = rt;
        figureImage.enabled = !outlineOnly;
        figureGlow.texture = rt;
        if (textured)
        {
            glowMat.SetFloat("_Outline", 0f);
            glowMat.SetFloat("_Halo", texturedHalo);
        }
        glowMat.SetFloat("_ContourPx", Mathf.Max(1.5f, h * contourWidth));
        glowMat.SetFloat("_ClosePx", h * contourWidth);
    }


    // --- per frame ---

    private void Update()
    {
        if (!built || planned || cueSpawner == null) return;
        var chart = cueSpawner.Sequencer;
        var meta = cueSpawner.BeatmapMeta;
        if (chart == null || meta == null) return;

        SilhouettePlanner.Plan(motionSet, chart, meta.bpm, cueSpawner.LaneCount, meta.track ?? cueSpawner.name,
                               cueSpawner.ObstacleTiming, out plan, out gestures);
        secondsPerBeat = 60f / (meta.bpm >= 20f ? meta.bpm : 120f);
        beatAnchor = SilhouettePlanner.BeatAnchor(chart);
        planned = true;
        cursor = 0;
        LogPlan();
    }

    private void LateUpdate()
    {
        if (!built) return;
        EnsureTexture();
        ApplyTheme();

        float now = Now;
        float stance = plan == null || plan.Count == 0 ? EvaluateStanceOnly(now) : EvaluatePlan(now);

        // First frame: the stance alone, unposed, to measure the figure by.
        if (!measured)
        {
            poser.SetJobData(new SilhouettePoseJob { forward = Vector3.forward, pin = 1f });
            graph.Evaluate(0f);
            Measure();
            return;
        }

        poser.SetJobData(Pose(now, stance));
        graph.Evaluate(0f);
        if (planned) CheckSync(now);
        ServeCaptures(now);
    }

    private void Measure()
    {
        var a = animator;
        if (!a.isHuman) { measured = true; return; }
        lShoulder = stage.transform.InverseTransformPoint(a.GetBoneTransform(HumanBodyBones.LeftUpperArm).position);
        rShoulder = stage.transform.InverseTransformPoint(a.GetBoneTransform(HumanBodyBones.RightUpperArm).position);
        lFootRest = stage.transform.InverseTransformPoint(lFootBone.position);
        rFootRest = stage.transform.InverseTransformPoint(rFootBone.position);
        if (autoFrame) FrameToFigure();
        measured = true;
    }

    /// Fits the camera to the figure's standing height, measured from its skinned meshes.
    private void FrameToFigure()
    {
        float top = float.NegativeInfinity;
        foreach (var r in figure.GetComponentsInChildren<SkinnedMeshRenderer>())
            top = Mathf.Max(top, r.bounds.max.y);
        if (float.IsNegativeInfinity(top)) return;
        float height = top - stage.transform.position.y;
        viewHeight = height * (motionSet.frameHeights > 0f ? motionSet.frameHeights : frameHeights);
        viewCenterY = height * (motionSet.frameCenter > 0f ? motionSet.frameCenter : frameCenter);
        figureCamera.orthographicSize = viewHeight * 0.5f;
        var rot = figureCamera.transform.localRotation;
        figureCamera.transform.localPosition = cameraRest = new Vector3(0f, viewCenterY, viewAheadZ) - rot * Vector3.forward * 10f;
        Debug.Log("[Silhouette] figure " + height.ToString("F2") + " m tall, view " + viewHeight.ToString("F2") + " m, centre " + viewCenterY.ToString("F2"), this);
    }

    private float EvaluateStanceOnly(float now)
    {
        System.Array.Clear(weights, 0, weights.Length);
        if (stanceInput < 0) return 1f;
        weights[stanceInput] = 1f;
        var still = motionSet.idle[0];
        inputs[stanceInput].SetTime(still.holdFrame >= 0f ? still.holdFrame : Mathf.Repeat(Mathf.Max(0f, now), still.Length));
        PushWeights();
        return 1f;
    }

    /// The segment the clock is in, blended over the ones it is still fading in from. Each
    /// segment's own fade decides its share; what it leaves goes to the one before, and so on.
    /// Returns the stance's share - how much the legs belong to the stance rather than a clip move.
    private float EvaluatePlan(float now)
    {
        while (cursor + 1 < plan.Count && plan[cursor + 1].start <= now) cursor++;
        while (cursor > 0 && plan[cursor].start > now) cursor--;   // a seek back

        System.Array.Clear(weights, 0, weights.Length);
        float remaining = 1f;
        for (int k = cursor; k >= 0 && k > cursor - 4 && remaining > 0.0001f; k--)
        {
            var seg = plan[k];
            float share = seg.fade <= 0f ? 1f : Smooth01((now - seg.start) / seg.fade);
            if (k == cursor - 3 || k == 0) share = 1f;
            float w = remaining * share;
            remaining -= w;
            if (!inputOf.TryGetValue(seg.motion.clip, out int i)) continue;
            // The same clip twice in the stack: the newest wins the time, the weights add.
            if (weights[i] == 0f) inputs[i].SetTime(seg.ClipTime(now));
            weights[i] += w;
        }
        PushWeights();
        if (stanceInputs.Count == 0) return 1f;
        float stance = 0f;
        foreach (int i in stanceInputs) stance += weights[i];
        return Mathf.Clamp01(stance);
    }

    private static float Smooth01(float x)
    {
        x = Mathf.Clamp01(x);
        return x * x * (3f - 2f * x);
    }

    private void PushWeights()
    {
        for (int i = 0; i < weights.Length; i++) mixer.SetInputWeight(i, weights[i]);
    }

    // --- the pose ---

    // Screen side to the figure's own X. The figure faces +Z and the camera looks back at it, so
    // the screen's right is the figure's -X - its left.
    private static float X(int screenSide) => -screenSide;

    private struct Limb
    {
        public float w;
        public Vector3 target, elbow;
    }

    /// Everything the pose job needs for this moment, from the gestures and the beat.
    private SilhouettePoseJob Pose(float now, float stance)
    {
        var s = motionSet;
        var job = new SilhouettePoseJob { forward = stage.transform.forward, pin = stance };

        // The mocap stance is what keeps the body alive; on top of it the knees only give a little
        // on every beat (lowest ON the beat), so the beat is felt without a mechanical rhythm.
        float beats = (now - beatAnchor) / Mathf.Max(0.05f, secondsPerBeat);
        float dip = s.bounceDepth * (0.5f + 0.5f * Mathf.Cos(beats * 2f * Mathf.PI));
        Vector3 body = new Vector3(0f, -dip, 0f) * stance;
        float lean = 0f, turn = 0f, tapping = 0f;
        var hand = new Limb[2];      // 0 = figure's left, 1 = right
        var foot = new Vector3[2];
        float[] footW = new float[2];

        if (gestures != null)
        {
            foreach (var g in gestures)
            {
                // A gesture only shapes the stance: while a jump or a crouch owns the body the
                // gesture gives way, or the tail of a wall's lean would twist the jump.
                float w = g.Weight(now) * stance;
                if (w <= 0f) continue;
                int limb = g.side > 0 ? 0 : 1;           // the screen's right is the figure's left
                float x = X(g.side);
                Vector3 shoulder = limb == 0 ? lShoulder : rShoulder;

                switch (g.kind)
                {
                    case SilhouettePlanner.GestureKind.Punch:
                    case SilhouettePlanner.GestureKind.PunchHold:
                    {
                        bool high = g.kind == SilhouettePlanner.GestureKind.PunchHold;
                        Vector3 t = high
                            ? shoulder + new Vector3(x * s.punchOutInner, s.punchHoldRaise, s.punchForward * 0.35f)
                            : shoulder + new Vector3(x * (g.outer ? s.punchOutOuter : s.punchOutInner), -s.punchDrop, s.punchForward);
                        if (high && now >= g.impact && now <= g.holdEnd)
                            t.y += s.punchHoldPulse * (0.5f + 0.5f * Mathf.Cos(beats * 2f * Mathf.PI));
                        Vector3 elbow = shoulder + new Vector3(x * (high ? 0.3f : 0.22f), high ? 0.05f : -0.3f, -0.12f);
                        Take(ref hand[limb], w, t, elbow);
                        break;
                    }
                    case SilhouettePlanner.GestureKind.Kick:
                    case SilhouettePlanner.GestureKind.KickHold:
                    {
                        float out_ = g.outer ? s.kickOutOuter : s.kickOutInner;
                        // Lifted on the way there and back, flat on the tile at the note. Only
                        // this foot moves; the other stays pinned where the stance put it.
                        float lift = s.kickLift * 4f * w * (1f - w);
                        Vector3 step = new Vector3(x * out_ * w, lift, s.kickForward * w);
                        if (w > footW[limb]) { footW[limb] = w; foot[limb] = step; }
                        if (g.outer) body += new Vector3(x * out_ * s.kickBodyFollowOuter * w, 0f, 0f);
                        tapping = Mathf.Max(tapping, w);
                        break;
                    }
                    case SilhouettePlanner.GestureKind.Sweep:
                    {
                        // Along the tail from its start to its end over the note's length.
                        float span = Mathf.Max(0.01f, g.holdEnd - g.impact);
                        float k = Smooth01((now - g.impact) / span);
                        Vector3 from = new Vector3(x * s.sweepFrom.x, s.sweepFrom.y, s.sweepFrom.z);
                        Vector3 to = new Vector3(x * s.sweepTo.x, s.sweepTo.y, s.sweepTo.z);
                        Vector3 t = shoulder + Vector3.Lerp(from, to, k);
                        Vector3 elbow = shoulder + new Vector3(x * Mathf.Lerp(0.25f, 0.4f, k), 0.02f, -0.12f);
                        Take(ref hand[limb], w, t, elbow);
                        break;
                    }
                    case SilhouettePlanner.GestureKind.ChordJump:
                    {
                        // Its own shape rather than the weight: up and down in the air before the
                        // note, landing ON it, then a give at the knees that recovers.
                        float air = (now - g.start) / Mathf.Max(0.01f, g.impact - g.start);
                        float y = 0f, spread = 0f;
                        if (air >= 0f && air < 1f)
                        {
                            y = s.chordJumpHeight * Mathf.Sin(air * Mathf.PI);
                            spread = Smooth01(air);
                        }
                        else if (now >= g.impact && now < g.end)
                        {
                            float land = (now - g.impact) / Mathf.Max(0.01f, g.end - g.impact);
                            y = -s.chordDip * Mathf.Sin(Mathf.Min(1f, land * 2f) * Mathf.PI * 0.5f) * (1f - Smooth01(land));
                            spread = 1f - Smooth01(land);
                        }
                        // Toward the side of the pair it lands on (screen side -> the figure's -X).
                        float shiftX = X(1) * g.shift * s.chordShift * w;
                        body += new Vector3(shiftX, y * stance, 0f);
                        // Both feet leave the ground with the body and land apart - wider the more
                        // lanes lie between the pair.
                        float lift = Mathf.Max(0f, y);
                        float apart = s.chordFeetOut + Mathf.Max(0, g.spread - 1) * s.chordFeetPerLane;
                        for (int f = 0; f < 2; f++)
                        {
                            float fx = (f == 0 ? -1f : 1f) * apart * spread;   // figure left is -X: feet apart, not crossed
                            if (w >= footW[f]) { footW[f] = w; foot[f] = new Vector3(fx + shiftX, lift, 0f); }
                        }
                        // A jumping jack's clap over the head on the wide landing.
                        if (s.jackClap && g.spread >= 3)
                        {
                            Take(ref hand[0], w, lShoulder + new Vector3(X(1) * 0.1f, s.punchHighRaise, 0.08f), lShoulder + new Vector3(X(1) * 0.25f, 0.3f, -0.1f));
                            Take(ref hand[1], w, rShoulder + new Vector3(X(-1) * 0.1f, s.punchHighRaise, 0.08f), rShoulder + new Vector3(X(-1) * 0.25f, 0.3f, -0.1f));
                        }
                        tapping = 1f;
                        break;
                    }
                    case SilhouettePlanner.GestureKind.PunchHigh:
                    {
                        // Straight up above the head, the elbow out to the side.
                        Vector3 t = shoulder + new Vector3(x * 0.14f, s.punchHighRaise, 0.1f);
                        Take(ref hand[limb], w, t, shoulder + new Vector3(x * 0.3f, 0.28f, -0.1f));
                        break;
                    }
                    case SilhouettePlanner.GestureKind.StarArms:
                    {
                        // Both arms up and out: X(-1) is the figure's left... each arm to its own side.
                        Take(ref hand[0], w, lShoulder + new Vector3(X(1) * s.starArm.x, s.starArm.y, s.starArm.z), lShoulder + new Vector3(X(1) * 0.3f, 0.15f, -0.1f));
                        Take(ref hand[1], w, rShoulder + new Vector3(X(-1) * s.starArm.x, s.starArm.y, s.starArm.z), rShoulder + new Vector3(X(-1) * 0.3f, 0.15f, -0.1f));
                        break;
                    }
                    case SilhouettePlanner.GestureKind.Straddle:
                    case SilhouettePlanner.GestureKind.Wide:
                    {
                        for (int f = 0; f < 2; f++)
                        {
                            Vector3 out_ = new Vector3((f == 0 ? -1f : 1f) * s.straddleOut * w, 0f, 0f);
                            if (w >= footW[f]) { footW[f] = w; foot[f] = out_; }
                        }
                        tapping = Mathf.Max(tapping, w);
                        break;
                    }
                    case SilhouettePlanner.GestureKind.OneLeg:
                    {
                        // Stepped over onto the free lane (StepAside), on the leg of that side; the
                        // other knee comes up once the feet are together, and down before the
                        // step back.
                        int lifted = 1 - limb;
                        float k = w * w * w;
                        Vector3 up = new Vector3(-X(g.side) * 0.05f * k, s.oneLegLift * k, s.oneLegForward * k);
                        if (k >= footW[lifted]) { footW[lifted] = k; foot[lifted] = up; }
                        tapping = Mathf.Max(tapping, w);
                        break;
                    }
                    case SilhouettePlanner.GestureKind.Dodge:
                    {
                        // The step itself is StepAside; the shoulders turn a little toward it.
                        // Positive yaw turns the figure's front toward +X, i.e. toward x.
                        turn += s.dodgeTurnDegrees * x * w;
                        break;
                    }
                }
            }
        }

        // While a foot taps, the knees stop giving on the beat: a dip on top of a tap is what made
        // it read as a hop from foot to foot.
        body.y += dip * stance * tapping;

        // Stepping aside: the whole figure goes over (so everything above - a tap, a reach - is
        // thrown from where it now stands), the feet carried there by their own step curves.
        float asideX = StepAside(now, out Vector3 stepL, out Vector3 stepR);
        float vx = (StepAside(now + 0.02f, out _, out _) - StepAside(now - 0.02f, out _, out _)) / 0.04f;
        // Positive roll tips the top toward -X: lean INTO the travel, only while travelling.
        lean += -s.stepLeanPerSpeed * vx / Mathf.Max(0.1f, s.gestureScale);
        foot[0] += stepL;
        foot[1] += stepR;
        // The moves in the character's own proportions: offsets from the shoulders, the feet and
        // the body scaled together.
        float scale = s.gestureScale;
        if (!Mathf.Approximately(scale, 1f))
        {
            body *= scale;
            for (int f = 0; f < 2; f++) foot[f] *= scale;
            for (int h = 0; h < 2; h++)
            {
                Vector3 sh = h == 0 ? lShoulder : rShoulder;
                hand[h].target = sh + (hand[h].target - sh) * scale;
                hand[h].elbow = sh + (hand[h].elbow - sh) * scale;
            }
        }
        job.bodyOffset = body;
        job.leanDegrees = lean;
        job.turnDegrees = turn;
        figure.localPosition = new Vector3(asideX, 0f, 0f);
        figureCamera.transform.localPosition = cameraRest + new Vector3(asideX * stepAsideFollow, 0f, 0f);
        // Targets are the figure's own: they go with it when it steps aside.
        Vector3 W(Vector3 local) => figure.TransformPoint(local);
        // The hand's target rides with the body (its bounce and its shift).
        job.lHandW = hand[0].w; job.lHand = W(hand[0].target + body); job.lElbow = W(hand[0].elbow + body);
        job.rHandW = hand[1].w; job.rHand = W(hand[1].target + body); job.rElbow = W(hand[1].elbow + body);
        job.lFootOffset = foot[0];
        job.rFootOffset = foot[1];
        lastLHand = hand[0].w > 0f ? job.lHand : Vector3.zero;
        lastRHand = hand[1].w > 0f ? job.rHand : Vector3.zero;
        lastLFoot = foot[0];
        lastRFoot = foot[1];
        return job;
    }

    /// Where the figure stands sideways because of walls and kerbs (stage X, metres, already in
    /// the character's scale), and each foot's offset from being carried along with it: the foot
    /// on the side of the move steps first over the first stepLeadShare of the move, the other is
    /// brought up to it over the last share - and on the way back the inner foot leads. A foot in
    /// the air is lifted a little.
    private float StepAside(float now, out Vector3 left, out Vector3 right)
    {
        left = right = Vector3.zero;
        if (gestures == null) return 0f;
        var s = motionSet;
        float shift = 0f;
        foreach (var g in gestures)
        {
            if (g.kind != SilhouettePlanner.GestureKind.Dodge && g.kind != SilhouettePlanner.GestureKind.OneLeg) continue;
            if (now <= g.start || now >= g.end) continue;
            float dist = X(g.side) * s.stepAsideShift * s.gestureScale;
            float body = g.Weight(now);
            shift += dist * body;
            // Which foot is on the outside of the move: toward +X that is the figure's right (1).
            int outer = dist > 0f ? 1 : 0;
            bool goingOut = now < g.impact;
            bool comingBack = now > g.holdEnd;
            if (!goingOut && !comingBack) continue;
            float a = goingOut ? g.start : g.holdEnd, b = goingOut ? g.impact : g.end;
            float u = Mathf.Clamp01((now - a) / Mathf.Max(1e-4f, b - a));
            float share = s.stepLeadShare;
            float leadU = Smooth01(u / share), trailU = Smooth01((u - (1f - share)) / share);
            int lead = goingOut ? outer : 1 - outer;
            for (int f = 0; f < 2; f++)
            {
                float fu = f == lead ? leadU : trailU;
                // The foot's own weight along the move, against the body's.
                float footW = goingOut ? fu : 1f - fu;
                float off = dist * (footW - body);
                float lift = s.stepFootLift * s.gestureScale * Mathf.Sin(Mathf.PI * fu);
                var v = new Vector3(off, lift, 0f);
                if (f == 0) left += v; else right += v;
            }
        }
        return shift;
    }

    private static void Take(ref Limb l, float w, Vector3 target, Vector3 elbow)
    {
        if (w <= l.w) return;
        l.w = w; l.target = target; l.elbow = elbow;
    }

    private void ApplyTheme()
    {
        Color c = followThemeColor && hud != null ? (usePrimaryColor ? hud.PrimaryColor : hud.SecondaryColor) : trainerColor;
        if ((textured || panelPalette) && hud != null) PaintBackdrop(hud.PrimaryColor, hud.SecondaryColor);
        if (panelPalette && hud != null)
        {
            // The frame glows in the theme; the figure keeps its own colour (trainerColor).
            var fc = Color.Lerp(hud.PrimaryColor, hud.SecondaryColor, 0.5f);
            if (fc != frameShown)
            {
                frameShown = fc;
                frameEdge.color = fc;
                var hh = fc; hh.a = frameGlow;
                frameHalo.color = hh;
            }
        }
        if (c == themeShown) return;
        themeShown = c;
        bodyMat.SetColor("_RimColor", c);
        foreach (var m in texturedMats) m.SetColor("_RimColor", c);
        glowMat.SetColor("_GlowColor", c);
        if (!panelPalette)
        {
            frameEdge.color = c;
            var h = c; h.a = frameGlow;
            frameHalo.color = h;
        }
    }

    /// The backdrop's pixels: `a` from the left, `b` from the right, dark in the middle, a little
    /// brighter toward the floor, like light spilling in from both wings.
    private void PaintBackdrop(Color a, Color b)
    {
        if (backdropTex == null || (a == backdropShownA && b == backdropShownB)) return;
        backdropShownA = a;
        backdropShownB = b;
        int w = backdropTex.width, h = backdropTex.height;
        var px = new Color32[w * h];
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                float u = (x + 0.5f) / w, v = (y + 0.5f) / h;
                float left = Mathf.Pow(1f - u, 2.2f), right = Mathf.Pow(u, 2.2f);
                float lift = 0.75f + 0.25f * (1f - v);
                Color c = (a * left + b * right) * backdropStrength * lift;
                c.a = 1f;
                px[y * w + x] = c;
            }
        backdropTex.SetPixels32(px);
        backdropTex.Apply(false);
    }

    // --- diagnostics ---

    /// For each of the first notes, the first frame at or after its impact: when that was against
    /// the note, and - for a hand - how far the solved hand ended up from where it was sent, which
    /// is the check that the IK really reached the pose the timing promises.
    private void CheckSync(float now)
    {
        if (syncReportNotes <= 0 || syncWritten || gestures == null) return;
        foreach (var g in gestures)
        {
            if (g.kind == SilhouettePlanner.GestureKind.Dodge || syncSeen.Contains(g.note)) continue;
            if (now < g.impact || now > g.end) continue;
            syncSeen.Add(g.note);
            var cue = cueSpawner.Sequencer[g.note];
            string reach = "-";
            bool isHand = g.kind == SilhouettePlanner.GestureKind.Punch || g.kind == SilhouettePlanner.GestureKind.PunchHold;
            if (isHand)
            {
                var bone = g.side > 0 ? lHandBone : rHandBone;
                Vector3 sent = g.side > 0 ? lastLHand : lastRHand;
                if (bone != null && sent != Vector3.zero) reach = "hand " + (Vector3.Distance(bone.position, sent) * 1000f).ToString("0") + " mm from goal";
            }
            else
            {
                // A step: how far the foot really moved from the stance, against the step asked for.
                bool left = g.side > 0;
                var bone = left ? lFootBone : rFootBone;
                Vector3 moved = stage.transform.InverseTransformPoint(bone.position) - (left ? lFootRest : rFootRest);
                Vector3 asked = left ? lastLFoot : lastRFoot;
                reach = "foot moved x " + (moved.x * 1000f).ToString("0") + " / asked " + (asked.x * 1000f).ToString("0") + " mm";
            }
            syncRows.Add(string.Format("{0,3} | {1,-9} | lane {2} {3,-5} | {4,8:F3} | {5,8:F3} | {6,6:+0.0;-0.0;0.0} | w {7:F2} | {8}",
                syncRows.Count + 1, cue.type, cue.lane, g.outer ? "outer" : "inner", cue.time, now, (now - cue.time) * 1000f, g.Weight(now), reach));
            if (syncRows.Count >= syncReportNotes) { WriteSync(); return; }
        }
    }

    private void WriteSync()
    {
        syncWritten = true;
        var sb = new StringBuilder();
        sb.AppendLine("[Silhouette] impact sync, first " + syncRows.Count + " notes (" + (SongClock.Capturing ? "recording" : "play") + ", " + (1f / Mathf.Max(1e-4f, Time.smoothDeltaTime)).ToString("0") + " fps)");
        sb.AppendLine("  # | type      | lane         |  note s  | observed |  d ms  | weight | pose check");
        foreach (var r in syncRows) sb.AppendLine(r);
        sb.AppendLine("The gesture's weight is 1 exactly at the note (planned error 0); observed = first frame at or after it.");
        sb.AppendLine("hand = distance of the solved hand bone from its IK goal; foot = sideways travel of the foot bone from the stance vs the step asked for.");
        Debug.Log(sb.ToString(), this);
        try
        {
            Directory.CreateDirectory("Screenshots");
            File.WriteAllText(Path.Combine("Screenshots", SongClock.Capturing ? "silhouette_sync_record.txt" : "silhouette_sync.txt"), sb.ToString());
        }
        catch (System.Exception e) { Debug.LogWarning("[Silhouette] could not write sync table: " + e.Message); }
    }

    private void ServeCaptures(float now)
    {
        for (int i = captures.Count - 1; i >= 0; i--)
        {
            if (now < captures[i].Key) continue;
            ScreenCapture.CaptureScreenshot(captures[i].Value);
            var cam = Camera.main != null ? Camera.main.transform : null;
            Debug.Log("[Silhouette] capture " + captures[i].Value + " at song " + now.ToString("F3") + " (asked " + captures[i].Key.ToString("F3") + ")"
                      + (cam != null ? " camera x " + cam.localPosition.x.ToString("F3") + " roll " + Mathf.DeltaAngle(0f, cam.localEulerAngles.z).ToString("F1") : "")
                      + " figure x " + figure.localPosition.x.ToString("F3"));
            captures.RemoveAt(i);
        }
    }

    private void LogPlan()
    {
        var sb = new StringBuilder("[Silhouette] plan: " + plan.Count + " clip segments, " + gestures.Count + " gestures, beat grid from " + beatAnchor.ToString("F3") + "\n");
        foreach (var p in plan)
            sb.Append("  clip ").Append(p.start.ToString("F2")).Append(" x").Append(p.speed.ToString("F2"))
              .Append(float.IsNaN(p.impactAt) ? "" : " impact@" + p.impactAt.ToString("F3")).Append("  ").Append(p.label).Append('\n');
        for (int i = 0; i < gestures.Count && i < 40; i++)
        {
            var g = gestures[i];
            sb.Append("  ").Append(g.kind).Append(g.side < 0 ? " L" : " R").Append(g.outer ? " outer" : " inner")
              .Append(" ").Append(g.start.ToString("F2")).Append(" -> ").Append(g.impact.ToString("F3")).Append(" .. ").Append(g.end.ToString("F2")).Append('\n');
        }
        Debug.Log(sb.ToString(), this);
    }
}
