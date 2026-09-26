using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;
using UnityEngine.Rendering.Universal;
using UnityEngine.UI;

/// The trainer: a neon silhouette in a window on the left of the screen that performs every note a
/// moment before the player has to - no, exactly WITH the player: each clip's impact frame lands on
/// its note.
///
/// How it is put together, all at runtime so nothing of it is saved into the scene:
///   * the character from a SilhouetteMotionSet, instanced far below the tunnel on its own layer;
///   * a camera that sees only that layer and renders into a RenderTexture;
///   * a Screen Space Overlay canvas with the panel - dark glass, a chamfered neon frame in the
///     theme colour, the figure, and a contour/halo made from the figure's alpha (SilhouetteGlow).
///
/// Motion: SilhouettePlanner lays the whole track out as clip segments on the song clock once the
/// chart is loaded, and every frame this evaluates a manual PlayableGraph at SongClock's smoothed
/// time. The pose is therefore a pure function of song time - identical in Play and in a recording.
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
    // false: the theme's secondary colour (the cyan of the default theme), true: its primary.
    [SerializeField] private bool usePrimaryColor;

    [Header("Figure camera")]
    // Seen from behind-left at 45 degrees: the figure faces down the tunnel with the player (its
    // left is toward the screen's left), a punch or a kick - straight ahead - crosses the screen
    // instead of vanishing behind the body as in a view from the back, and a side step still
    // moves across it too.
    [SerializeField] private float cameraYaw = 45f;
    [SerializeField] private float cameraPitch = 6f;
    // Metres of the figure's world that fit the panel's height. The figure is ~1.8 m; a jump and a
    // high kick need the rest.
    [SerializeField] private float viewHeight = 2.5f;
    [SerializeField] private float viewCenterY = 1.05f;
    // Aim a little AHEAD of the figure: every strike reaches forward, and forward is toward the
    // panel's left edge in this view, so a centred figure would put a jab's fist through the frame.
    [SerializeField] private float viewAheadZ = 0.35f;

    [Header("Diagnostics")]
    // The first N notes log planned vs observed impact time, once, and write it to
    // Screenshots/silhouette_sync.txt. 0 turns it off.
    [SerializeField] private int syncReportNotes = 10;

    private const int StageLayerFallback = 31;
    private static readonly Vector3 StagePosition = new Vector3(0f, -400f, 0f);

    private GameObject stage;
    private Transform figure;
    private Animator animator;
    private Transform hips;
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
    private readonly Dictionary<AnimationClip, int> inputOf = new Dictionary<AnimationClip, int>();
    private readonly List<AnimationClipPlayable> inputs = new List<AnimationClipPlayable>();
    private float[] weights;

    private List<SilhouettePlanner.Segment> plan;
    private List<SilhouettePlanner.Stretch> stretches;
    private int cursor;
    private bool built, planned;

    // Sync report
    private readonly List<string> syncRows = new List<string>();
    private readonly HashSet<int> syncSeen = new HashSet<int>();
    private bool syncWritten;

    // Screenshots on demand: song time -> file.
    private static readonly List<KeyValuePair<float, string>> captures = new List<KeyValuePair<float, string>>();

    public IReadOnlyList<SilhouettePlanner.Segment> Plan => plan;

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

        bodyMat = new Material(bodyShader != null ? bodyShader : Shader.Find("Tunnel/SilhouetteBody"));
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
        hips = animator.isHuman ? animator.GetBoneTransform(HumanBodyBones.Hips) : null;

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
        void Add(SilhouetteMotion m) { if (m != null && m.Valid && !clips.Contains(m.clip)) clips.Add(m.clip); }
        void AddAll(List<SilhouetteMotion> l) { if (l != null) foreach (var m in l) Add(m); }
        AddAll(motionSet.punch); AddAll(motionSet.kick); AddAll(motionSet.punchHold); AddAll(motionSet.kickHold);
        AddAll(motionSet.curb); AddAll(motionSet.dodgeLeft); AddAll(motionSet.dodgeRight);
        Add(motionSet.duckDown); Add(motionSet.duckUp); AddAll(motionSet.idle); AddAll(motionSet.dance);

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
        output.SetSourcePlayable(mixer);
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
        frameHalo = PanelImage("FrameGlow", SilhouettePanelSprites.Glow(frameCorner, halo), Color.clear, -halo);
        PanelImage("Glass", SilhouettePanelSprites.Fill(frameCorner), new Color(0.01f, 0.012f, 0.03f, glassAlpha), 0f);

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
        figureGlow.texture = rt;
    }

    // --- per frame ---

    private void Update()
    {
        if (!built || planned || cueSpawner == null) return;
        var chart = cueSpawner.Sequencer;
        var meta = cueSpawner.BeatmapMeta;
        if (chart == null || meta == null) return;

        float songEnd = cueSpawner.Music != null ? cueSpawner.Music.length : chart.LastCueTime + 8f;
        plan = SilhouettePlanner.Plan(motionSet, chart, meta.bpm, songEnd, meta.track ?? cueSpawner.name,
                                      cueSpawner.ObstacleTiming, out stretches);
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
        if (plan == null || plan.Count == 0) EvaluateIdle(now);
        else EvaluatePlan(now);

        graph.Evaluate(0f);
        ClampDrift();
        if (planned) CheckSync(now);
        ServeCaptures(now);
    }

    private void EvaluateIdle(float now)
    {
        System.Array.Clear(weights, 0, weights.Length);
        if (motionSet.idle.Count == 0 || !motionSet.idle[0].Valid) return;
        int i = inputOf[motionSet.idle[0].clip];
        weights[i] = 1f;
        inputs[i].SetTime(Mathf.Repeat(Mathf.Max(0f, now), motionSet.idle[0].Length));
        PushWeights();
    }

    /// The segment the clock is in, blended over the ones it is still fading in from. Each
    /// segment's own fade decides its share; what it leaves goes to the one before, and so on -
    /// three deep is always enough, since a fade never outlasts the gap between two notes by more.
    private void EvaluatePlan(float now)
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
            // The same clip twice in the stack (a dance resumed after a jump): the newest wins the
            // time, the weights add.
            if (weights[i] == 0f) inputs[i].SetTime(seg.ClipTime(now));
            weights[i] += w;
        }
        PushWeights();
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

    /// Keeps the figure in its window: the few clips that travel (the long side steps, ~0.6 m) are
    /// eased back to within maxDrift of the centre instead of walking out of frame.
    private void ClampDrift()
    {
        if (hips == null) return;
        figure.localPosition = Vector3.zero;
        // Every clip is baked on its original root, so standing still is the hips over the root.
        Vector3 p = figure.InverseTransformPoint(hips.position);
        float lim = motionSet.maxDrift;
        float SoftX(float v) => lim * (float)System.Math.Tanh(v / lim);
        figure.localPosition = new Vector3(SoftX(p.x) - p.x, 0f, SoftX(p.z) - p.z);
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

    private void CheckSync(float now)
    {
        if (syncReportNotes <= 0 || syncWritten) return;
        for (int k = Mathf.Max(0, cursor - 3); k <= cursor; k++)
        {
            var seg = plan[k];
            if (seg.note < 0 || syncSeen.Contains(seg.note) || float.IsNaN(seg.impactAt)) continue;
            if (seg.ClipTime(now) + 1e-4f < seg.motion.impact) continue;
            syncSeen.Add(seg.note);
            float noteTime = cueSpawner.Sequencer[seg.note].time;
            syncRows.Add(string.Format("{0,3} | {1,-9} | {2,-24} | {3,8:F3} | {4,8:F3} | {5,8:F3} | {6,6:+0.0;-0.0;0.0}",
                syncRows.Count + 1, cueSpawner.Sequencer[seg.note].type, seg.motion.clip.name,
                noteTime, seg.impactAt, now, (now - noteTime) * 1000f));
            if (syncRows.Count >= syncReportNotes) WriteSync();
        }
    }

    private void WriteSync()
    {
        syncWritten = true;
        var sb = new StringBuilder();
        sb.AppendLine("[Silhouette] impact sync, first " + syncRows.Count + " notes (" + (SongClock.Capturing ? "recording" : "play") + ", " + (1f / Mathf.Max(1e-4f, Time.smoothDeltaTime)).ToString("0") + " fps)");
        sb.AppendLine("  # | type      | clip                     |  note s  | planned  | observed |  d ms");
        foreach (var r in syncRows) sb.AppendLine(r);
        sb.AppendLine("planned = song time the plan puts the impact frame at; observed = first frame whose clip time reached it.");
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
        var sb = new StringBuilder("[Silhouette] plan: " + plan.Count + " segments, " + stretches.Count + " dance stretches\n");
        foreach (var s in stretches)
            sb.Append("  dance ").Append(s.start.ToString("F1")).Append("..").Append(s.end.ToString("F1")).Append(' ')
              .Append(s.dance != null && s.dance.Valid ? s.dance.clip.name : "-").Append(s.quiet ? " (walls ride along)" : "").Append('\n');
        for (int i = 0; i < plan.Count && i < 60; i++)
        {
            var p = plan[i];
            sb.Append("  ").Append(p.start.ToString("F2")).Append(" fade ").Append(p.fade.ToString("F2"))
              .Append(" x").Append(p.speed.ToString("F2")).Append(" in ").Append(p.clipIn.ToString("F2"))
              .Append(float.IsNaN(p.impactAt) ? "" : " impact@" + p.impactAt.ToString("F3")).Append("  ").Append(p.label).Append('\n');
        }
        Debug.Log(sb.ToString(), this);
    }
}
