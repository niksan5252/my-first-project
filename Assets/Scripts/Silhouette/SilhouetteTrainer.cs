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

    [Header("Video trainer")]
    // The trainer as filmed clips (VideoSilhouette) in the same panel instead of the 3D character.
    // Off brings the 3D trainer back as it was.
    [SerializeField] private bool useVideoSilhouette;
    [SerializeField] private VideoSilhouetteSet videoSet;
    private bool video;

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

    // Motion capture (a set with exercises, see SilhouetteExercisePlanner): takes on the body and on
    // each arm, the stance under them, where the figure stands.
    private bool mocap;
    private List<SilhouetteExercisePlanner.Move> moves;
    private List<SilhouetteExercisePlanner.HomeStep> home;
    private SilhouettePlanner.StanceTrack stanceTrack;
    private AnimationLayerMixerPlayable layers;
    private readonly AnimationMixerPlayable[] armMixers = new AnimationMixerPlayable[2];
    private readonly List<AnimationClipPlayable>[] armInputs = { new List<AnimationClipPlayable>(), new List<AnimationClipPlayable>() };
    private float[][] armWeights = { new float[0], new float[0] };
    // (clip, slot) -> input: slot 0 the stance, 1/2 two copies on the body, 3/4 the left/right arm.
    private readonly Dictionary<(AnimationClip, int), int> slotInput = new Dictionary<(AnimationClip, int), int>();
    private float travelScale = 1f, maxMoveLength = 1f;
    private System.Func<float, float> beatAt;
    private float lastRootX;

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

    /// Video 9: the trainer as a person filmed on camera - a solid black figure, softly backlit in
    /// the theme's two colours (a vertical wash behind it and a halo round its edge), in a thin
    /// rounded frame, with a light motion trail. Keeps the motion set; call before the build.
    /// Video 10: the 3D character on mocap even where the scene is set to the filmed clips, in the
    /// filmed look (solid black, backlit) but with no motion trail - one figure, never a ghost.
    public void UseCharacterNotVideo()
    {
        if (built) return;
        useVideoSilhouette = false;
        UseFilmedLook();
        filmedTrail = 0f;
    }

    public void UseFilmedLook()
    {
        if (built) return;
        filmed = true;
        panelPalette = true;
        textured = false;
        outlineOnly = false;
        followThemeColor = true;
    }
    private bool filmed;
    private RawImage trailImage;
    private RenderTexture trailRt;
    [SerializeField, Range(0f, 1f)] private float filmedTrail = 0.38f;

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
        if (useVideoSilhouette && videoSet != null)
        {
            BuildVideo();
            return;
        }
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
        // A run stopped before the report filled up still leaves what it measured.
        if (!syncWritten && syncRows.Count > 0) WriteSync();
        if (graph.IsValid()) graph.Destroy();
        if (rt != null) { rt.Release(); Destroy(rt); }
        if (trailRt != null) { trailRt.Release(); Destroy(trailRt); }
        if (stage != null) Destroy(stage);
        if (canvas != null) Destroy(canvas.gameObject);
        if (bodyMat != null) Destroy(bodyMat);
        if (glowMat != null) Destroy(glowMat);
        foreach (var m in texturedMats) if (m != null) Destroy(m);
        if (backdropTex != null) Destroy(backdropTex);
    }

    // --- building ---

    /// The panel alone, its figure fed by VideoSilhouette: no stage, no character, no camera.
    private void BuildVideo()
    {
        video = true;
        BuildPanel();
        figureGlow.enabled = false;               // the clips carry their own neon contour
        if (trailImage != null) trailImage.enabled = false;
        var player = gameObject.AddComponent<VideoSilhouette>();
        player.Init(videoSet, cueSpawner, figureImage, panel);
        built = true;
    }

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
        if (filmed)
        {
            // A person against the light: almost black, the faintest rim in the theme's colour.
            bodyMat.SetColor("_FillColor", new Color(0.012f, 0.012f, 0.016f, 1f));
            bodyMat.SetFloat("_RimStrength", 0.14f);
            bodyMat.SetFloat("_RimPower", 3.5f);
        }
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
            p.SetApplyFootIK(!motionSet.noFootIK);
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

        // Over the body: one layer per arm, masked, for a punch thrown over a leg exercise.
        layers = AnimationLayerMixerPlayable.Create(graph, 3);
        graph.Connect(mixer, 0, layers, 0);
        layers.SetInputWeight(0, 1f);
        for (int arm = 0; arm < 2; arm++)
        {
            armMixers[arm] = AnimationMixerPlayable.Create(graph, 0);
            graph.Connect(armMixers[arm], 0, layers, arm + 1);
            layers.SetInputWeight(arm + 1, 0f);
            var mask = new AvatarMask();
            for (int part = 0; part < (int)AvatarMaskBodyPart.LastBodyPart; part++) mask.SetHumanoidBodyPartActive((AvatarMaskBodyPart)part, false);
            mask.SetHumanoidBodyPartActive(arm == 0 ? AvatarMaskBodyPart.LeftArm : AvatarMaskBodyPart.RightArm, true);
            mask.SetHumanoidBodyPartActive(arm == 0 ? AvatarMaskBodyPart.LeftFingers : AvatarMaskBodyPart.RightFingers, true);
            layers.SetLayerMaskFromAvatarMask((uint)(arm + 1), mask);
        }

        poser = AnimationScriptPlayable.Create(graph, new SilhouettePoseJob { forward = Vector3.forward, pin = 1f }, 1);
        graph.Connect(layers, 0, poser, 0);
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

        float halo = filmed ? 26f : 40f;
        frameHalo = PanelImage("FrameGlow", filmed ? SilhouettePanelSprites.GlowRound(frameCorner, halo) : SilhouettePanelSprites.Glow(frameCorner, halo), Color.clear, -halo);
        var glass = PanelImage("Glass", filmed ? SilhouettePanelSprites.FillRound(frameCorner) : SilhouettePanelSprites.Fill(frameCorner),
                               new Color(0.01f, 0.012f, 0.03f, filmed ? 1f : glassAlpha), 0f);
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
        // The motion trail: the previous frame of the figure under this one, half see-through.
        if (filmed && filmedTrail > 0f) trailImage = PanelRaw("FigureTrail", null);
        figureImage = PanelRaw("Figure", null);
        frameEdge = PanelImage("Frame", filmed ? SilhouettePanelSprites.EdgeRound(frameCorner, Mathf.Min(frameWidth, 2f))
                                               : SilhouettePanelSprites.Edge(frameCorner, frameWidth), Color.white, 0f);
        if (!video) EnsureTexture();
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
        if (filmed)
        {
            // A solid figure lit from behind: no contour line, a soft halo round the edge.
            glowMat.SetFloat("_Outline", 0f);
            glowMat.SetFloat("_Halo", 0.9f);
            if (trailImage != null)
            {
                if (trailRt != null) { trailRt.Release(); Destroy(trailRt); }
                trailRt = new RenderTexture(w, h, 0, RenderTextureFormat.ARGB32) { name = "SilhouetteTrailRT", filterMode = FilterMode.Bilinear };
                trailRt.Create();
                trailImage.texture = trailRt;
                trailImage.color = new Color(1f, 1f, 1f, filmedTrail);
            }
        }
        glowMat.SetFloat("_ContourPx", Mathf.Max(1.5f, h * contourWidth));
        glowMat.SetFloat("_ClosePx", h * contourWidth);
    }


    // --- per frame ---

    private void Update()
    {
        if (!built || video || planned || cueSpawner == null) return;
        var chart = cueSpawner.Sequencer;
        var meta = cueSpawner.BeatmapMeta;
        if (chart == null || meta == null) return;

        secondsPerBeat = 60f / (meta.bpm >= 20f ? meta.bpm : 120f);
        beatAnchor = SilhouettePlanner.BeatAnchor(chart);
        // A playlist changes tempo from track to track: each beat-length is the one of the track
        // playing at that moment.
        var timeline = PlaylistDirector.Active != null ? PlaylistDirector.Active.Timeline : null;
        float spb = secondsPerBeat;
        beatAt = _ => spb;
        if (timeline != null && timeline.spans.Count > 0)
            beatAt = t =>
            {
                var span = timeline.spans[0];
                foreach (var sp in timeline.spans) if (t >= sp.audioStart - 0.01f) span = sp;
                return span.secondsPerBeat;
            };
        mocap = motionSet.HasExercises && animator.isHuman;
        string seed = meta.track ?? cueSpawner.name;
        if (mocap)
        {
            travelScale = animator.humanScale / Mathf.Max(0.01f, motionSet.analysisHipsHeight);
            SilhouetteExercisePlanner.Plan(motionSet, chart, beatAt, cueSpawner.LaneCount, seed, cueSpawner.ObstacleTiming, travelScale,
                                           out moves, out gestures, out home);
            stanceTrack = new SilhouettePlanner.StanceTrack(motionSet, chart, secondsPerBeat, new System.Random(SilhouettePlanner.StableHash(seed)));
            BuildMoveInputs();
            plan = new List<SilhouettePlanner.Segment>();
        }
        else
        {
            SilhouettePlanner.Plan(motionSet, chart, meta.bpm, cueSpawner.LaneCount, seed,
                                   cueSpawner.ObstacleTiming, out plan, out gestures);
        }
        planned = true;
        cursor = 0;
        LogPlan();
    }

    private void LateUpdate()
    {
        if (!built) return;
        if (video) { ApplyTheme(); ServeCaptures(Now); return; }
        EnsureTexture();
        // Last frame's figure, before this frame renders over it: the motion trail.
        if (filmed && trailRt != null && rt != null && measured) Graphics.Blit(rt, trailRt);
        ApplyTheme();

        float now = Now;
        float stance = mocap && planned ? EvaluateMocap(now) : plan == null || plan.Count == 0 ? EvaluateStanceOnly(now) : EvaluatePlan(now);

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
        // The first frame may be a held squat (a chart that opens on one): never frame on less than
        // a standing figure, or the camera zooms in and crops the trainer for the whole run.
        if (motionSet.analysisHipsHeight > 0f) height = Mathf.Max(height, motionSet.analysisHipsHeight * 1.55f);
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

    /// Inputs for every take of the plan: two copies of a clip on the body (a take can start again
    /// while its last one is still fading out), one per arm.
    private void BuildMoveInputs()
    {
        var lastEnd = new Dictionary<AnimationClip, (float end, int inst)>();
        foreach (var mv in moves)
        {
            maxMoveLength = Mathf.Max(maxMoveLength, mv.end - mv.start);
            int slot;
            if (mv.layer == 0)
            {
                int inst = 0;
                bool seen = lastEnd.TryGetValue(mv.motion.clip, out var le);
                if (seen && mv.start < le.end) inst = 1 - le.inst;
                mv.instance = inst;
                lastEnd[mv.motion.clip] = (seen ? Mathf.Max(mv.end, le.end) : mv.end, inst);
                slot = 1 + inst;
            }
            else slot = 2 + mv.layer;
            Input(mv.motion.clip, slot);
        }
        foreach (var piece in stanceTrack.Pieces) if (piece.motion != null && piece.motion.Valid) Input(piece.motion.clip, 0);
        weights = new float[mixer.GetInputCount()];
        for (int arm = 0; arm < 2; arm++) armWeights[arm] = new float[armMixers[arm].GetInputCount()];
    }

    private int Input(AnimationClip clip, int slot)
    {
        if (slot == 0 && inputOf.TryGetValue(clip, out int st)) return st;
        if (slotInput.TryGetValue((clip, slot), out int i)) return i;
        var p = AnimationClipPlayable.Create(graph, clip);
        p.SetApplyFootIK(!motionSet.noFootIK);
        p.Pause();
        if (slot <= 2)
        {
            i = mixer.GetInputCount();
            mixer.SetInputCount(i + 1);
            graph.Connect(p, 0, mixer, i);
            inputs.Add(p);
        }
        else
        {
            var mx = armMixers[slot - 3];
            i = mx.GetInputCount();
            mx.SetInputCount(i + 1);
            graph.Connect(p, 0, mx, i);
            armInputs[slot - 3].Add(p);
        }
        slotInput[(clip, slot)] = i;
        return i;
    }

    /// The body: the takes under way, newest on top (each takes its fade's share of what the newer
    /// ones left), the stance under all of them. Where the figure stands comes out of the same
    /// weights. The arms: their own takes, over the body by their summed weight.
    private float EvaluateMocap(float now)
    {
        System.Array.Clear(weights, 0, weights.Length);
        for (int arm = 0; arm < 2; arm++) System.Array.Clear(armWeights[arm], 0, armWeights[arm].Length);
        float homeX = StepAside(now, out _, out _) + SilhouetteExercisePlanner.Home(home, now);
        float remaining = 1f, stance = 0f, rootX = 0f;
        float[] armLeft = { 1f, 1f };

        int hi = moves.Count - 1;
        while (hi >= 0 && moves[hi].start > now) hi--;
        for (int k = hi; k >= 0; k--)
        {
            var mv = moves[k];
            if (mv.start < now - maxMoveLength) break;
            float e = mv.Envelope(now);
            if (e <= 0f) continue;
            if (mv.layer == 0)
            {
                if (remaining <= 1e-4f) continue;
                float w = remaining * e;
                remaining -= w;
                int i = Input(mv.motion.clip, 1 + mv.instance);
                float clipT = mv.ClipTime(now);
                if (weights[i] == 0f) inputs[i].SetTime(clipT);
                weights[i] += w * mv.amp;
                stance += w * (1f - mv.amp);
                // A side step moves the hips over by itself: the figure stays where the step
                // started from, and is carried the rest of the way by the take.
                float root = mv.carries ? homeX - mv.motion.TravelAt(clipT) * travelScale : homeX;
                rootX += w * mv.amp * root + w * (1f - mv.amp) * homeX;
            }
            else
            {
                int arm = mv.layer - 1;
                if (armLeft[arm] <= 1e-4f) continue;
                float w = armLeft[arm] * e;
                armLeft[arm] -= w;
                int i = Input(mv.motion.clip, 2 + mv.layer);
                if (armWeights[arm][i] == 0f) armInputs[arm][i].SetTime(mv.ClipTime(now));
                armWeights[arm][i] += w;
            }
        }
        stance += remaining;
        rootX += remaining * homeX;
        lastRootX = rootX;

        // The stance: the piece the clock is in, crossfading from the one before.
        var pieces = stanceTrack.Pieces;
        int p = 0;
        while (p + 1 < pieces.Count && pieces[p + 1].start <= now) p++;
        if (pieces.Count > 0 && stance > 0f)
        {
            float fade = Mathf.Max(0.01f, stanceTrack.Fade);
            float a = p > 0 ? Smooth01((now - pieces[p].start) / fade) : 1f;
            StanceWeight(pieces[p], now, stance * a);
            if (a < 1f) StanceWeight(pieces[p - 1], now, stance * (1f - a));
        }
        PushWeights();
        for (int arm = 0; arm < 2; arm++)
        {
            float sum = 0f;
            foreach (float w in armWeights[arm]) sum += w;
            for (int i = 0; i < armWeights[arm].Length; i++) armMixers[arm].SetInputWeight(i, sum > 0f ? armWeights[arm][i] / sum : 0f);
            layers.SetInputWeight(arm + 1, Mathf.Clamp01(sum));
        }
        return Mathf.Clamp01(stance);
    }

    private void StanceWeight(SilhouettePlanner.StanceTrack.Piece piece, float now, float w)
    {
        if (w <= 0f || piece.motion == null || !piece.motion.Valid) return;
        int i = Input(piece.motion.clip, 0);
        float t = piece.motion.holdFrame >= 0f ? piece.motion.holdFrame : Mathf.Repeat(now - piece.start, piece.motion.Length);
        if (weights[i] == 0f) inputs[i].SetTime(t);
        weights[i] += w;
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
        var job = new SilhouettePoseJob { forward = stage.transform.forward, pin = stance, faceFront = mocap && planned ? s.faceFront : 0f };

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
                float w = g.Weight(now) * (mocap ? 1f : stance);
                if (mocap && s.mocapOnly) w = 0f;      // the takes alone: no reach, no procedural pose
                if (mocap)
                {
                    // Over a take the reach or the tap is only a touch-up at the note.
                    switch (g.kind)
                    {
                        case SilhouettePlanner.GestureKind.Punch:
                        case SilhouettePlanner.GestureKind.PunchHold:
                        case SilhouettePlanner.GestureKind.PunchHigh:
                            w *= s.assistHand; break;
                        case SilhouettePlanner.GestureKind.Kick:
                        case SilhouettePlanner.GestureKind.KickHold:
                            w *= s.assistFoot; break;
                        case SilhouettePlanner.GestureKind.Dodge:
                            w = 0f; break;   // the side step's take turns the body itself
                    }
                }
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
                        float lift = mocap ? 0f : s.kickLift * 4f * w * (1f - w);
                        Vector3 step = new Vector3(x * out_ * w, lift, mocap ? 0f : s.kickForward * w);
                        if (w > footW[limb]) { footW[limb] = w; foot[limb] = step; }
                        if (g.outer && !mocap) body += new Vector3(x * out_ * s.kickBodyFollowOuter * w, 0f, 0f);
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
        if (mocap && planned)
        {
            // The side step's take does the stepping and the leaning; the figure goes where the
            // takes' weights put it (EvaluateMocap).
            asideX = lastRootX;
        }
        else
        {
            float vx = (StepAside(now + 0.02f, out _, out _) - StepAside(now - 0.02f, out _, out _)) / 0.04f;
            // Positive roll tips the top toward -X: lean INTO the travel, only while travelling.
            lean += -s.stepLeanPerSpeed * vx / Mathf.Max(0.1f, s.gestureScale);
            foot[0] += stepL;
            foot[1] += stepR;
        }
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
            float dist = X(g.side) * StepAsideDistance();
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

    /// How far a step aside goes: with a side step take, exactly its own travel in this character's
    /// size, so the figure arrives where the take puts its hips.
    private float StepAsideDistance()
    {
        if (mocap)
            foreach (var m in motionSet.sideSteps)
                if (m != null && m.Valid && m.clip.name.StartsWith("Side Step Short Right")) return Mathf.Abs(m.travel) * travelScale;
        return motionSet.stepAsideShift * motionSet.gestureScale;
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
        if (!video)
        {
            bodyMat.SetColor("_RimColor", c);
            foreach (var m in texturedMats) m.SetColor("_RimColor", c);
        }
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
                if (filmed)
                {
                    // A vertical wash, one colour at the top and the other at the floor, brightest
                    // in a soft column behind the figure - a studio backlight.
                    float column = Mathf.Exp(-Mathf.Pow((u - 0.5f) / 0.3f, 2f));
                    c = Color.Lerp(b, a, v) * (0.38f + 0.62f * column) * 0.95f;
                }
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
    /// Mocap: for each note a take lands on, the first frame at or after it - where the take was
    /// (its clip time against the planned impact frame) and which hand or foot the solved figure
    /// actually delivers it with (the one furthest out), against the one the note asks for.
    private int syncFirst = -1;
    private void CheckSyncMocap(float now)
    {
        if (syncReportNotes <= 0 || syncWritten || moves == null) return;
        if (syncFirst < 0) syncFirst = Mathf.Max(0, moves.FindIndex(m => m.end >= now));
        for (int k = syncFirst; k < moves.Count && moves[k].start <= now; k++)
        {
            var mv = moves[k];
            foreach (var h in mv.hits)
            {
                if (now < h.song || now > h.song + 0.1f || syncSeen.Contains(h.note * 4 + mv.layer)) continue;
                syncSeen.Add(h.note * 4 + mv.layer);
                var cue = cueSpawner.Sequencer[h.note];
                float planned = mv.RawTime(h.song);
                float atFrame = mv.RawTime(now);
                string actual = "-";
                bool hand = cue.type == CueType.Punch || cue.type == CueType.PunchHigh || cue.type == CueType.PunchHold || cue.type == CueType.PunchSweep;
                if (h.limb >= 0 && animator.isHuman)
                {
                    int got;
                    if (hand)
                    {
                        // How far the hand is out from its shoulder plus how far above it: a
                        // reach or a punch scores its length, an arm hanging straight down ~0
                        // (by distance alone a relaxed straight arm beats a bent punching one).
                        float Reach(Transform hand, HumanBodyBones sh)
                        {
                            var d = hand.position - animator.GetBoneTransform(sh).position;
                            return d.magnitude + d.y;
                        }
                        float l = Reach(lHandBone, HumanBodyBones.LeftUpperArm);
                        float r = Reach(rHandBone, HumanBodyBones.RightUpperArm);
                        got = l > r ? 0 : 1;
                    }
                    else
                    {
                        var hips = animator.GetBoneTransform(HumanBodyBones.Hips).position;
                        float Out(Transform f) { var d = f.position - hips; float y = d.y; d.y = 0f; return d.magnitude + y; }
                        got = Out(lFootBone) > Out(rFootBone) ? 0 : 1;
                    }
                    actual = (got == h.limb ? "ok " : "WRONG ") + (got == 0 ? "L" : "R") + " (asked " + (h.limb == 0 ? "L" : "R") + ")";
                }
                syncRows.Add(string.Format("{0,3} | {1,-9} | lane {2} | {3,8:F3} | {4,8:F3} | {5,6:+0.0;-0.0;0.0} | {6,-40} | mark {7:F3} planned {8:F3} frame {9:F3} | {10}",
                    syncRows.Count + 1, cue.type, cue.lane, cue.time, now, (now - cue.time) * 1000f, mv.label.Length > 40 ? mv.label.Substring(0, 40) : mv.label,
                    h.clip, planned, atFrame, actual));
                if (syncRows.Count >= syncReportNotes) { WriteSync(); return; }
            }
        }
    }

    private void CheckSync(float now)
    {
        if (mocap) { CheckSyncMocap(now); return; }
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
        if (mocap)
            sb.AppendLine("  # | type      | lane |  note s  | observed |  d ms  | take                                     | clip s: impact mark, planned at the note, at the frame | limb delivering");
        else
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
                      + (figure != null ? " figure x " + figure.localPosition.x.ToString("F3") : ""));
            captures.RemoveAt(i);
        }
    }

    private void LogPlan()
    {
        if (mocap)
        {
            var mb = new StringBuilder("[Silhouette] mocap plan: " + moves.Count + " takes, " + gestures.Count + " gestures, " + home.Count + " home steps, travel x" + travelScale.ToString("F2") + "\n");
            for (int i = 0; i < moves.Count && i < 80; i++)
            {
                var mv = moves[i];
                mb.Append("  ").Append(mv.start.ToString("F2")).Append("..").Append(mv.end.ToString("F2")).Append(" L").Append(mv.layer)
                  .Append(mv.amp < 1f ? " amp" + mv.amp.ToString("F2") : "").Append("  ").Append(mv.label).Append('\n');
            }
            Debug.Log(mb.ToString(), this);
            try { Directory.CreateDirectory("Screenshots"); File.WriteAllText("Screenshots/trainer_v6_plan.txt", mb.ToString()); } catch { }
            return;
        }
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
