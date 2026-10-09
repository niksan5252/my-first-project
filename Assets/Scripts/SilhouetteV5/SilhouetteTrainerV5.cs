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
///   * the character from a SilhouetteMotionSetV5, instanced far below the tunnel on its own layer;
///   * a camera in front of it that sees only that layer and renders into a RenderTexture;
///   * a Screen Space Overlay canvas with the panel - dark glass, a chamfered neon frame in the
///     theme colour, the figure, and a contour/halo made from the figure's alpha (SilhouetteGlow).
///
/// Motion: SilhouettePlannerV5 lays the whole track out once the chart is loaded - clip segments
/// (the relaxed stance, the jump, the crouch) and procedural gestures. Every frame this evaluates a
/// manual PlayableGraph at SongClock's smoothed time: a mixer of the clips, then SilhouettePoseJobV5,
/// which poses body, hands and feet from the gestures by IK. The pose is therefore a pure function
/// of song time - identical in Play and in a recording.
[DefaultExecutionOrder(100)]
public class SilhouetteTrainerV5 : MonoBehaviour
{
    [Header("Wiring")]
    [SerializeField] private SilhouetteMotionSetV5 motionSet;
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
    // false: the theme's secondary colour (the cyan of the default theme), true: its primary.
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

    private List<SilhouettePlannerV5.SegmentV5> plan;
    private List<SilhouettePlannerV5.GestureV5> gestures;
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

    public IReadOnlyList<SilhouettePlannerV5.SegmentV5> Plan => plan;
    public IReadOnlyList<SilhouettePlannerV5.GestureV5> Gestures => gestures;

    /// Asks for a screenshot on the first frame at or after `songTime`. For the visual checks.
    public static void CaptureAt(float songTime, string path) => captures.Add(new KeyValuePair<float, string>(songTime, path));

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

        bodyMat = new Material(bodyShader != null ? bodyShader : Shader.Find("Tunnel/SilhouetteBodyV5"));
        foreach (var r in model.GetComponentsInChildren<Renderer>(true))
        {
            var mats = new Material[r.sharedMaterials.Length];
            for (int i = 0; i < mats.Length; i++) mats[i] = bodyMat;
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
        void Add(SilhouetteMotionV5 m) { if (m != null && m.Valid && !clips.Contains(m.clip)) clips.Add(m.clip); }
        if (motionSet.idle != null) foreach (var m in motionSet.idle) Add(m);
        if (motionSet.idleLively != null) foreach (var m in motionSet.idleLively) Add(m);
        if (motionSet.curb != null) foreach (var m in motionSet.curb) Add(m);
        Add(motionSet.duckDown); Add(motionSet.duckUp);
        Add(motionSet.stepScreenRight); Add(motionSet.stepScreenLeft);
        Add(motionSet.jumpForward); Add(motionSet.jumpSideScreenRight); Add(motionSet.jumpSideScreenLeft);

        mixer = AnimationMixerPlayable.Create(graph, clips.Count);
        for (int i = 0; i < clips.Count; i++)
        {
            var p = AnimationClipPlayable.Create(graph, clips[i]);
            bool mocapStep = (motionSet.stepScreenRight.Valid && clips[i] == motionSet.stepScreenRight.clip) || (motionSet.stepScreenLeft.Valid && clips[i] == motionSet.stepScreenLeft.clip);
            p.SetApplyFootIK(!mocapStep);
            p.Pause();   // time is set by hand every frame
            graph.Connect(p, 0, mixer, i);
            mixer.SetInputWeight(i, 0f);
            inputOf[clips[i]] = i;
            inputs.Add(p);
        }
        weights = new float[clips.Count];
        foreach (var m in motionSet.idle) if (m != null && m.Valid) { stanceInput = inputOf[m.clip]; break; }
        void Stance(List<SilhouetteMotionV5> l) { if (l != null) foreach (var m in l) if (m != null && m.Valid && !stanceInputs.Contains(inputOf[m.clip])) stanceInputs.Add(inputOf[m.clip]); }
        Stance(motionSet.idle);
        Stance(motionSet.idleLively);

        poser = AnimationScriptPlayable.Create(graph, new SilhouettePoseJobV5 { forward = Vector3.forward, pin = 1f }, 1);
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
        go.transform.localPosition = target - rot * Vector3.forward * 10f;
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
        frameHalo = PanelImage("FrameGlow", SilhouettePanelSpritesV5.Glow(frameCorner, halo), Color.clear, -halo);
        PanelImage("Glass", SilhouettePanelSpritesV5.Fill(frameCorner), new Color(0.01f, 0.012f, 0.03f, glassAlpha), 0f);

        glowMat = new Material(glowShader != null ? glowShader : Shader.Find("Tunnel/SilhouetteGlowV5"));
        figureGlow = PanelRaw("FigureGlow", glowMat);
        figureImage = PanelRaw("Figure", null);
        frameEdge = PanelImage("Frame", SilhouettePanelSpritesV5.Edge(frameCorner, frameWidth), Color.white, 0f);
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

        SilhouettePlannerV5.Plan(motionSet, chart, meta.bpm, cueSpawner.LaneCount, meta.track ?? cueSpawner.name,
                               cueSpawner.ObstacleTiming, out plan, out gestures);
        secondsPerBeat = 60f / (meta.bpm >= 20f ? meta.bpm : 120f);
        beatAnchor = SilhouettePlannerV5.BeatAnchor(chart);
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
            poser.SetJobData(new SilhouettePoseJobV5 { forward = Vector3.forward, pin = 1f });
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
        viewHeight = height * frameHeights;
        viewCenterY = height * frameCenter;
        figureCamera.orthographicSize = viewHeight * 0.5f;
        var rot = figureCamera.transform.localRotation;
        figureCamera.transform.localPosition = new Vector3(0f, viewCenterY, viewAheadZ) - rot * Vector3.forward * 10f;
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
        // The step takes are mocap with no IK goals: pinning the feet to them would fold the legs.
        foreach (var m in new[] { motionSet.stepScreenRight, motionSet.stepScreenLeft })
            if (m != null && m.Valid && inputOf.TryGetValue(m.clip, out int si) && weights[si] > 0f) return 0f;
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

    private struct LimbV5
    {
        public float w;
        public Vector3 target, elbow;
    }

    /// Everything the pose job needs for this moment, from the gestures and the beat.
    // The weighted yaw correction of the takes playing now (SilhouetteMotionV5.yawFix).
    private float YawFix()
    {
        if (weights == null) return 0f;
        float y = 0f;
        void One(SilhouetteMotionV5 m) { if (m != null && m.Valid && m.yawFix != 0f && inputOf.TryGetValue(m.clip, out int i)) y += weights[i] * m.yawFix; }
        if (motionSet.curb != null) foreach (var m in motionSet.curb) One(m);
        One(motionSet.duckDown); One(motionSet.duckUp); One(motionSet.stepScreenRight); One(motionSet.stepScreenLeft);
        One(motionSet.jumpForward); One(motionSet.jumpSideScreenRight); One(motionSet.jumpSideScreenLeft);
        return y;
    }

    private SilhouettePoseJobV5 Pose(float now, float stance)
    {
        var s = motionSet;
        var job = new SilhouettePoseJobV5 { forward = stage.transform.forward, pin = stance };

        // The mocap stance is what keeps the body alive; on top of it the knees only give a little
        // on every beat (lowest ON the beat), so the beat is felt without a mechanical rhythm.
        float beats = (now - beatAnchor) / Mathf.Max(0.05f, secondsPerBeat);
        float dip = s.bounceDepth * (0.5f + 0.5f * Mathf.Cos(beats * 2f * Mathf.PI));
        Vector3 body = new Vector3(0f, -dip, 0f) * stance;
        float lean = 0f, turn = 0f, tapping = 0f;
        var hand = new LimbV5[2];      // 0 = figure's left, 1 = right
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
                    case SilhouettePlannerV5.GestureKindV5.Punch:
                    case SilhouettePlannerV5.GestureKindV5.PunchHold:
                    {
                        bool holdHigh = g.kind == SilhouettePlannerV5.GestureKindV5.PunchHold;
                        bool high = holdHigh || g.high;
                        Vector3 t = holdHigh
                            ? shoulder + new Vector3(x * s.punchOutInner, s.punchHoldRaise, s.punchForward * 0.35f)
                            : g.high ? shoulder + new Vector3(x * (g.outer ? s.punchOutOuter : s.punchOutInner) * 0.8f, s.punchHoldRaise * 0.9f, s.punchForward)
                            : shoulder + new Vector3(x * (g.outer ? s.punchOutOuter : s.punchOutInner), -s.punchDrop, s.punchForward);
                        if (holdHigh && now >= g.impact && now <= g.holdEnd)
                            t.y += s.punchHoldPulse * (0.5f + 0.5f * Mathf.Cos(beats * 2f * Mathf.PI));
                        Vector3 elbow = shoulder + new Vector3(x * (high ? 0.3f : 0.22f), high ? 0.05f : -0.3f, -0.12f);
                        Take(ref hand[limb], w, t, elbow);
                        break;
                    }
                    case SilhouettePlannerV5.GestureKindV5.Kick:
                    case SilhouettePlannerV5.GestureKindV5.KickHold:
                    {
                        if (g.kind == SilhouettePlannerV5.GestureKindV5.Kick && !g.outer)
                        {
                            // An inner lane (the user's call): the knee simply comes up and the foot
                            // goes down on the note, in place - no reach out to the side and back.
                            float up = now < g.impact ? s.kickLift * 1.2f * 4f * w * (1f - w) : 0f;
                            Vector3 march = new Vector3(0f, up, up * 0.35f);
                            if (w > footW[limb]) { footW[limb] = w; foot[limb] = march; }
                            tapping = Mathf.Max(tapping, w);
                            break;
                        }
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
                    case SilhouettePlannerV5.GestureKindV5.Wide:
                    {
                        // Both feet far out to the sides, the body down between them (side 2 =
                        // SquatHold: a deeper, wider sumo).
                        bool squat = g.side == 2;
                        float o = (squat ? s.wideOutSquat : s.wideOut) * w;
                        for (int k = 0; k < 2; k++)
                        {
                            Vector3 f = new Vector3(k == 0 ? -o : o, 0f, 0f);     // the figure's left foot out to -X (see X())
                            if (w > footW[k]) { footW[k] = w; foot[k] = f; }
                        }
                        body += new Vector3(0f, -(squat ? s.wideSinkSquat : s.wideSink) * w, 0f);
                        tapping = Mathf.Max(tapping, w);
                        break;
                    }
                    case SilhouettePlannerV5.GestureKindV5.Hop:
                    {
                        // A jumping jack: in the air from take-off to the landing on the note, the
                        // feet landing wide (side 1) or together (side 0).
                        float air = now > g.start && now < g.impact ? Mathf.Sin(Mathf.PI * (now - g.start) / Mathf.Max(0.01f, g.impact - g.start)) : 0f;
                        // ...and the knees give on the landing, so it reads as a jump, not a twitch.
                        float land = now >= g.impact && now < g.end ? Mathf.Sin(Mathf.PI * (now - g.impact) / Mathf.Max(0.01f, g.end - g.impact)) : 0f;
                        float lift = air * s.jackHop - land * 0.12f;
                        float o = g.side == 1 ? s.wideOut * w : 0f;
                        for (int k = 0; k < 2; k++)
                        {
                            Vector3 f = new Vector3(k == 0 ? -o : o, air * s.jackHop, 0f);     // the figure's left foot out to -X (see X())
                            if (w > footW[k]) { footW[k] = w; foot[k] = f; }
                        }
                        body += new Vector3(0f, lift - (g.side == 1 ? s.wideSink * 0.5f * w : 0f), 0f);
                        tapping = Mathf.Max(tapping, w);
                        break;
                    }
                    case SilhouettePlannerV5.GestureKindV5.HopOne:
                    {
                        // A hop on one foot: this knee up, the body (and the standing foot) in the
                        // air before the note, landing on the other foot with the knees giving.
                        float air = now > g.start && now < g.impact ? Mathf.Sin(Mathf.PI * (now - g.start) / Mathf.Max(0.01f, g.impact - g.start)) : 0f;
                        float land = now >= g.impact && now < g.end ? Mathf.Sin(Mathf.PI * (now - g.impact) / Mathf.Max(0.01f, g.end - g.impact)) : 0f;
                        float hop = air * s.jackHop;
                        Vector3 up = new Vector3(0f, s.oneLegLift * 1.3f * w + hop, s.oneLegForward * 1.6f * w);   // the knee well up (the user's call)
                        if (w > footW[limb]) { footW[limb] = w; foot[limb] = up; }
                        int other = 1 - limb;
                        if (w > footW[other]) { footW[other] = w; foot[other] = new Vector3(0f, hop, 0f); }
                        body += new Vector3(-x * s.oneLegShift * w, hop - land * 0.12f, 0f);
                        tapping = Mathf.Max(tapping, w);
                        break;
                    }
                    case SilhouettePlannerV5.GestureKindV5.OneLeg:
                    {
                        // This knee up and held, the other leg standing; the body leans a touch
                        // over the standing leg.
                        Vector3 up = new Vector3(0f, s.oneLegLift * w, s.oneLegForward * w);
                        if (w > footW[limb]) { footW[limb] = w; foot[limb] = up; }
                        body += new Vector3(-x * s.oneLegShift * w, 0f, 0f);
                        tapping = Mathf.Max(tapping, w);
                        break;
                    }
                    case SilhouettePlannerV5.GestureKindV5.Dodge:
                    {
                        body += new Vector3(x * s.dodgeShift * w, -0.02f * w, 0f);
                        // Positive roll about +Z tips the top toward -X, i.e. toward the screen's right.
                        lean += s.dodgeLeanDegrees * g.side * w;
                        // Positive yaw turns the figure's front toward +X; the turn is toward the
                        // step, so toward x. The arms stay the stance's, relaxed at the sides.
                        turn += s.dodgeTurnDegrees * x * w;
                        int other = 1 - limb;
                        Vector3 lead = new Vector3(x * s.dodgeLeadFoot * w, 0f, 0f), trail = new Vector3(x * s.dodgeTrailFoot * w, 0f, 0f);
                        if (w > footW[limb]) { footW[limb] = w; foot[limb] = lead; }
                        if (w > footW[other]) { footW[other] = w; foot[other] = trail; }
                        break;
                    }
                }
            }
        }

        // While a foot taps, the knees stop giving on the beat: a dip on top of a tap is what made
        // it read as a hop from foot to foot.
        body.y += dip * stance * tapping;
        job.bodyOffset = body;
        job.leanDegrees = lean;
        job.turnDegrees = turn + YawFix();
        Vector3 W(Vector3 local) => stage.transform.TransformPoint(local);
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

    private static void Take(ref LimbV5 l, float w, Vector3 target, Vector3 elbow)
    {
        if (w <= l.w) return;
        l.w = w; l.target = target; l.elbow = elbow;
    }

    private void ApplyTheme()
    {
        Color c = hud != null ? (usePrimaryColor ? hud.PrimaryColor : hud.SecondaryColor) : new Color(0f, 0.85f, 1f);
        if (c == themeShown) return;
        themeShown = c;
        bodyMat.SetColor("_RimColor", c);
        glowMat.SetColor("_GlowColor", c);
        frameEdge.color = c;
        var h = c; h.a = frameGlow;
        frameHalo.color = h;
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
            if (g.kind == SilhouettePlannerV5.GestureKindV5.Dodge || syncSeen.Contains(g.note)) continue;
            if (now < g.impact || now > g.end) continue;
            syncSeen.Add(g.note);
            var cue = cueSpawner.Sequencer[g.note];
            string reach = "-";
            bool isHand = g.kind == SilhouettePlannerV5.GestureKindV5.Punch || g.kind == SilhouettePlannerV5.GestureKindV5.PunchHold;
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
            Debug.Log("[Silhouette] capture " + captures[i].Value + " at song " + now.ToString("F3") + " (asked " + captures[i].Key.ToString("F3") + ")");
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
