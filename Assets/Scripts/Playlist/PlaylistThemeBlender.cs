using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.UI;

/// The picture side of a playlist's transition from one segment to the next, all on song time:
///
///   * GATE - well before the transition the tunnel starts rebuilding its rings in the new style
///     as they recycle, timed so the first new arch reaches the camera at the transition's middle;
///     a gate (the new theme's gatePrefab, or its arch made large and bright) flies in at that same
///     boundary, so the new style is seen coming out of the distance;
///   * VEIL - over the transition the fog closes in and a veil over the far distance and the sky
///     thickens to the fog's colour, peaking at the middle and clearing again after it;
///   * SWAP - at the middle, under the veil, the whole theme is put in place in one frame (sky,
///     light, post-processing, environment, cue colours; TunnelThemeApplier.SwapTo), the next
///     environment having been instantiated, inactive, a few seconds earlier;
///   * BLEND - all the way through, fog, ambient, road, HUD (and with it the trainer's panel) and
///     the post-processing move from the old theme's values to the new one's;
///   * FLASH - on a thunder transition, a lightning flash over the whole screen on the clap.
public class PlaylistThemeBlender : MonoBehaviour
{
    // The environment is instantiated (inactive) this long before the transition starts.
    private const float PrepareLead = 6f;
    private const float VeilDistance = 160f;

    private PlaylistDirector director;
    private TunnelThemeApplier applier;
    private TunnelTheme blend;          // a scratch theme the HUD is fed while colours move
    private Volume blendVolume;         // the second volume that crossfades the post-processing
    private Transform veil;
    private Material veilMat;
    private Image flash;
    private int done = 0;               // transitions finished (index of the last segment reached)
    private int ringsQueuedFor = 0, preparedFor = 0, gateFor = 0, swappedFor = 0;
    private GameObject prepared;
    private Transform gate;
    private float gateBaseScale = 1f;

    public void Configure(PlaylistDirector owner, TunnelThemeApplier themeApplier)
    {
        director = owner;
        applier = themeApplier;
        blend = ScriptableObject.CreateInstance<TunnelTheme>();
        blend.name = "Playlist blend";
    }

    private void OnDestroy()
    {
        if (blend != null) Destroy(blend);
        if (veilMat != null) Destroy(veilMat);
        if (veil != null) Destroy(veil.gameObject);
        if (flash != null) Destroy(flash.canvas.gameObject);
        if (blendVolume != null) Destroy(blendVolume.gameObject);
        if (gate != null) Destroy(gate.gameObject);
    }

    private void LateUpdate()
    {
        var spawner = applier != null ? applier.Spawner : null;
        if (director == null || director.Timeline == null || spawner == null || !spawner.Clock.IsRunning) return;
        float now = spawner.Clock.SongTime;
        var spans = director.Timeline.spans;

        for (int k = 1; k < spans.Count; k++)
        {
            var span = spans[k];
            var next = span.segment.theme;
            if (next == null) continue;
            var tunnel = applier.Tunnel;
            float travel = tunnel != null ? tunnel.TunnelLength / Mathf.Max(0.01f, tunnel.ScrollSpeed) : 12f;

            if (ringsQueuedFor < k && now >= span.inMiddle - travel)
            {
                ringsQueuedFor = k;
                if (tunnel != null) tunnel.QueueRingStyle(next);
            }
            if (gateFor < k && now >= span.inMiddle - travel && now < span.inMiddle + 1f)
            {
                gateFor = k;
                BuildGate(next);
            }
            if (preparedFor < k && now >= span.inStart - PrepareLead && now < span.inMiddle)
            {
                preparedFor = k;
                prepared = applier.PrepareEnvironment(next);
            }
        }

        UpdateGate(now);

        // The transition we are in, if any.
        int active = -1;
        for (int k = 1; k < spans.Count; k++)
            if (now >= spans[k].inStart && now <= spans[k].inEnd) { active = k; break; }

        if (active < 0)
        {
            SetVeil(0f, Color.black);
            SetFlash(0f, Color.white);
            // Catch up with a transition that was jumped over (a start part-way through).
            int seg = director.Timeline.SegmentAt(now);
            if (swappedFor < seg && seg > 0) Swap(seg);
            if (done < seg && swappedFor >= seg) Finish(seg);
            return;
        }

        var s = spans[active];
        var from = spans[active - 1].segment.theme;
        var to = s.segment.theme;
        float p = Mathf.Clamp01((now - s.inStart) / Mathf.Max(0.01f, s.inEnd - s.inStart));
        float e = p * p * (3f - 2f * p);
        float veilAmount = Mathf.Pow(Mathf.Sin(p * Mathf.PI), 0.7f);

        if (swappedFor < active && now >= s.inMiddle) Swap(active);

        Blend(from, to, e, veilAmount);
        if (blendVolume != null)
        {
            // Before the swap the second volume carries the NEW profile rising; after it, the main
            // volume has the new one and the second carries the OLD one falling - the same mix.
            bool after = swappedFor >= active;
            blendVolume.sharedProfile = after ? from.postProcessingProfile : to.postProcessingProfile;
            blendVolume.weight = after ? 1f - e : e;
        }

        if (s.segment.transitionIn == PlaylistTransition.Thunder)
        {
            float since = now - s.inMiddle;
            // A pre-flicker, the strike, and a second stroke; each falls away fast.
            float f = 0f;
            if (since > -0.35f && since < -0.28f) f = 0.35f;
            if (since >= 0f) f = Mathf.Exp(-since / 0.12f) + 0.6f * (since > 0.16f ? Mathf.Exp(-(since - 0.16f) / 0.1f) : 0f);
            Color c = Color.Lerp(Color.white, to.secondaryColor, 0.25f);
            SetFlash(Mathf.Clamp01(f) * director.Playlist.thunderFlash, c);
        }
        else SetFlash(0f, Color.white);
    }

    /// Everything that is interpolated rather than swapped.
    private void Blend(TunnelTheme a, TunnelTheme b, float e, float veilAmount)
    {
        var tunnel = applier.Tunnel;
        float length = tunnel != null ? tunnel.TunnelLength : 144f;
        Color fog = Color.Lerp(a.fogColor, b.fogColor, e);
        float fogStart = length * Mathf.Lerp(a.fogStartRatio, b.fogStartRatio, e);
        float fogEnd = length * Mathf.Lerp(a.fogEndRatio, b.fogEndRatio, e);
        float veilEnd = director.Playlist.veilFogEnd;
        RenderSettings.fogColor = fog;
        RenderSettings.fogStartDistance = Mathf.Lerp(fogStart, veilEnd * 0.2f, veilAmount);
        RenderSettings.fogEndDistance = Mathf.Lerp(fogEnd, veilEnd, veilAmount);
        RenderSettings.ambientLight = fog * 0.3f;
        if (applier.TargetCamera != null) applier.TargetCamera.backgroundColor = Color.Lerp(a.cameraBackgroundColor, b.cameraBackgroundColor, e);
        SetVeil(veilAmount, fog);

        blend.primaryColor = Color.Lerp(a.primaryColor, b.primaryColor, e);
        blend.secondaryColor = Color.Lerp(a.secondaryColor, b.secondaryColor, e);
        blend.accentEmissionIntensity = Mathf.Lerp(a.accentEmissionIntensity, b.accentEmissionIntensity, e);
        blend.hudComboTextColor = Color.Lerp(a.hudComboTextColor, b.hudComboTextColor, e);
        blend.hudBannerBorderColor = Color.Lerp(a.hudBannerBorderColor, b.hudBannerBorderColor, e);
        blend.laneLineColor = Color.Lerp(a.laneLineColor, b.laneLineColor, e);
        blend.laneLineEmissionIntensity = Mathf.Lerp(a.laneLineEmissionIntensity, b.laneLineEmissionIntensity, e);
        blend.floorGridLineColor = Color.Lerp(a.floorGridLineColor, b.floorGridLineColor, e);
        blend.floorGridGlintIntensity = Mathf.Lerp(a.floorGridGlintIntensity, b.floorGridGlintIntensity, e);
        blend.floorColor = Color.Lerp(a.floorColor, b.floorColor, e);
        blend.floorMetallic = Mathf.Lerp(a.floorMetallic, b.floorMetallic, e);
        blend.floorSmoothness = Mathf.Lerp(a.floorSmoothness, b.floorSmoothness, e);
        if (applier.Hud != null) applier.Hud.ApplyTheme(blend);
        if (tunnel != null) tunnel.ApplyRoadPalette(blend);

        var light = applier.DirectionalLight;
        if (light != null)
        {
            float li = Mathf.Lerp(a.lightIntensity, b.lightIntensity, e);
            light.enabled = li > 0.0001f;
            light.intensity = li;
            light.color = Color.Lerp(a.lightColor, b.lightColor, e);
        }
    }

    /// Under the veil: the new theme in one frame. The fog colour is set to the new theme's
    /// first, because environments read it when they build (their sky's horizon, their fades).
    private void Swap(int k)
    {
        swappedFor = k;
        var theme = director.Timeline.spans[k].segment.theme;
        EnsureBlendVolume();
        RenderSettings.fogColor = theme.fogColor;
        applier.SwapTo(theme, prepared);
        prepared = null;
        Debug.Log("[Playlist] look swapped to " + theme.name + " at song " + applier.Spawner.Clock.SongTime.ToString("F2") + " s", this);
    }

    /// After the transition: the new theme's own values exactly, nothing left of the blend.
    private void Finish(int k)
    {
        done = k;
        var theme = director.Timeline.spans[k].segment.theme;
        var tunnel = applier.Tunnel;
        float length = tunnel != null ? tunnel.TunnelLength : 144f;
        RenderSettings.fogColor = theme.fogColor;
        RenderSettings.fogStartDistance = length * theme.fogStartRatio;
        RenderSettings.fogEndDistance = length * theme.fogEndRatio;
        RenderSettings.ambientLight = theme.fogColor * 0.3f;
        if (applier.Hud != null) applier.Hud.ApplyTheme(theme);
        if (tunnel != null) tunnel.ApplyRoadPalette(theme);
        if (blendVolume != null) blendVolume.weight = 0f;
        if (gate != null) { Destroy(gate.gameObject); gate = null; }
    }

    // --- the gate ---

    private void BuildGate(TunnelTheme next)
    {
        if (gate != null) Destroy(gate.gameObject);
        GameObject go = null;
        if (next.gatePrefab != null)
        {
            go = Instantiate(next.gatePrefab);
            gateBaseScale = 1f;
        }
        else if (next.ringArchPrefab != null)
        {
            go = Instantiate(next.ringArchPrefab);
            if (go.TryGetComponent<IThemeAccent>(out var accent)) accent.ApplyAccent(next.primaryColor, Mathf.Min(2.5f, next.accentEmissionIntensity * 1.6f));
            gateBaseScale = 1.25f;
        }
        if (go == null) return;
        go.name = "Playlist Gate (" + next.name + ")";
        gate = go.transform;
        gate.localScale = Vector3.one * gateBaseScale;
    }

    private void UpdateGate(float now)
    {
        if (gate == null) return;
        var spans = director.Timeline.spans;
        int k = Mathf.Clamp(gateFor, 1, spans.Count - 1);
        var tunnel = applier.Tunnel;
        float speed = tunnel != null ? tunnel.ScrollSpeed : 12f;
        // Level with the camera exactly at the transition's middle, like the first new ring.
        float z = (spans[k].inMiddle - now) * speed;
        var p = gate.localPosition;
        gate.localPosition = new Vector3(p.x, p.y, z);
        if (z < -8f) { Destroy(gate.gameObject); gate = null; }
    }

    // --- veil and flash ---

    private void EnsureBlendVolume()
    {
        if (blendVolume != null || applier.GlobalVolume == null) return;
        var go = new GameObject("Playlist Blend Volume");
        go.transform.SetParent(transform, false);
        blendVolume = go.AddComponent<Volume>();
        blendVolume.isGlobal = true;
        blendVolume.priority = applier.GlobalVolume.priority + 1f;
        blendVolume.weight = 0f;
    }

    private void SetVeil(float amount, Color color)
    {
        var cam = applier.TargetCamera;
        if (amount <= 0.001f)
        {
            if (veil != null) veil.gameObject.SetActive(false);
            return;
        }
        if (veil == null && cam != null)
        {
            var q = GameObject.CreatePrimitive(PrimitiveType.Quad);
            Destroy(q.GetComponent<Collider>());
            q.name = "Playlist Veil";
            veil = q.transform;
            veil.SetParent(cam.transform, false);
            float h = 2f * VeilDistance * Mathf.Tan(cam.fieldOfView * 0.5f * Mathf.Deg2Rad) * 1.6f;
            veil.localPosition = new Vector3(0f, 0f, VeilDistance);
            veil.localScale = new Vector3(h * 2.4f, h, 1f);
            veilMat = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
            veilMat.SetFloat("_Surface", 1f);
            veilMat.SetFloat("_Blend", 0f);
            veilMat.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
            veilMat.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
            veilMat.SetFloat("_ZWrite", 0f);
            veilMat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            veilMat.renderQueue = (int)RenderQueue.Transparent - 50;
            var r = q.GetComponent<MeshRenderer>();
            r.sharedMaterial = veilMat;
            r.shadowCastingMode = ShadowCastingMode.Off;
            r.receiveShadows = false;
        }
        if (veil == null) return;
        veil.gameObject.SetActive(true);
        color.a = Mathf.Clamp01(amount);
        veilMat.SetColor("_BaseColor", color);
    }

    private void SetFlash(float amount, Color color)
    {
        if (amount <= 0.001f)
        {
            if (flash != null) flash.enabled = false;
            return;
        }
        if (flash == null)
        {
            var cgo = new GameObject("Playlist Flash");
            var canvas = cgo.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 90;   // over the trainer's panel, under the HUD
            flash = new GameObject("Flash", typeof(RectTransform), typeof(Image)).GetComponent<Image>();
            flash.rectTransform.SetParent(cgo.transform, false);
            flash.rectTransform.anchorMin = Vector2.zero;
            flash.rectTransform.anchorMax = Vector2.one;
            flash.rectTransform.offsetMin = flash.rectTransform.offsetMax = Vector2.zero;
            flash.raycastTarget = false;
        }
        flash.enabled = true;
        color.a = Mathf.Clamp01(amount);
        flash.color = color;
    }
}
