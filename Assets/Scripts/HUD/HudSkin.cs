using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// Dresses the HUD in one of several HudLooks. HudController and HudBanner decide what is shown
/// and when; they hand every piece of styling to this component, so a look is changed here and
/// nowhere else. Switching `activeLook` in the Inspector during Play restyles the HUD on the spot,
/// which is how the looks are meant to be compared.
///
/// Everything a look adds on top of the scene's own objects - the progress bar, the plates, the
/// glitch copies, the stars - is built at runtime and never saved into the scene. The shapes those
/// need (a chamfered plate, a pixel frame, a pixel star) are drawn into small textures in code for
/// the same reason: there is nothing to import and nothing to keep in step with the look.
///
/// All effect timing runs on HudBeatClock.Now - the song clock's smoothed time - and is measured in
/// beats, so an effect keeps its musical length on any track and plays the same in a recording.
[DisallowMultipleComponent]
public class HudSkin : MonoBehaviour
{
    [SerializeField] private HudLook[] looks;
    [SerializeField] private int activeLook;

    [Header("Wiring")]
    [SerializeField] private HudController controller;
    [SerializeField] private RectTransform comboGroup;
    [SerializeField] private TMP_Text comboNumber;
    [SerializeField] private TMP_Text comboLabel;
    [SerializeField] private RectTransform banner;
    [SerializeField] private Image bannerPlate;
    [SerializeField] private TMP_Text bannerWord;

    private HudLook look;
    private int builtFor = -1;
    private readonly List<GameObject> extras = new List<GameObject>();

    // Theme colours, as last handed over by the controller.
    private Color primary = new Color(1f, 0f, 0.8f);
    private Color secondary = new Color(0f, 0.9f, 1f);
    private Color comboColor = new Color(1f, 0f, 0.8f);

    // A: progress bar
    private Image barFill;
    private TMP_Text barNext;
    private float barShown;
    private float barTarget;
    private float barFrom;
    private float barStart = float.NegativeInfinity;

    // B: glitch copies, banner border
    private TMP_Text ghostA, ghostB;
    private float glitchStart = float.NegativeInfinity;
    private Vector2 numberRest;
    private Image bannerBorder;

    // A: banner rules
    private Image ruleTop, ruleBottom;

    // Banner entrance (B typing, C stars)
    private float bannerStart = float.NegativeInfinity;
    private Color bannerColor;
    private readonly List<RectTransform> stars = new List<RectTransform>();

    public HudLook Active => look;
    public int ActiveIndex => activeLook;
    public int LookCount => looks != null ? looks.Length : 0;

    private HudBeatClock Beats => controller != null ? controller.Beats : null;
    private float Now => Beats != null ? Beats.Now : Time.time;
    private float Seconds(float beats) => Beats != null ? Beats.Seconds(beats) : beats * 0.5f;

    /// Picks a look by index and restyles everything at once.
    public void SetLook(int index)
    {
        activeLook = index;
        Rebuild();
    }

    private void OnValidate()
    {
        // Lets the Inspector field switch looks live during Play.
        if (Application.isPlaying && isActiveAndEnabled && builtFor >= 0 && builtFor != activeLook) Rebuild();
    }

    private void Rebuild()
    {
        builtFor = -1;
        EnsureBuilt();
        if (controller != null) controller.Restyle();
    }

    private bool EnsureBuilt()
    {
        if (looks == null || looks.Length == 0) return false;
        int index = Mathf.Clamp(activeLook, 0, looks.Length - 1);
        if (builtFor == index && look != null) return true;
        look = looks[index];
        if (look == null) return false;
        builtFor = index;
        ClearExtras();
        Layout();
        BuildExtras();
        return true;
    }

    // --- what the controller and the banner call ---

    public string FormatCombo(int combo)
    {
        if (!EnsureBuilt() || string.IsNullOrEmpty(look.comboFormat)) return combo.ToString();
        return string.Format(look.comboFormat, combo);
    }

    /// Styles the counter in the theme's colours and returns its resting glow power, which the
    /// controller swings on every hit.
    public float StyleCombo(Color themeCombo, Color themePrimary, Color themeSecondary)
    {
        comboColor = themeCombo;
        primary = themePrimary;
        secondary = themeSecondary;
        if (!EnsureBuilt() || comboNumber == null) return 0.45f;

        switch (look.kind)
        {
            case HudLookKind.SynthChrome:
                // Chrome: a light top fading into the accent, a thin bright edge, the glow in the
                // other accent. The top is tinted, never white, so nothing clips.
                Gradient(comboNumber, Color.Lerp(secondary, Color.white, 0.45f), primary);
                Material(comboNumber, glow: primary, glowPower: look.comboGlow,
                         outline: Color.Lerp(secondary, Color.white, 0.25f), outlineWidth: 0.08f);
                Plain(comboLabel, Color.Lerp(secondary, Color.white, 0.2f));
                Material(comboLabel, glow: secondary, glowPower: 0.2f, outline: Dark, outlineWidth: 0.1f);
                break;

            case HudLookKind.CyberGlitch:
                Plain(comboNumber, Color.Lerp(comboColor, Color.white, 0.72f));
                Material(comboNumber, glow: comboColor, glowPower: look.comboGlow, outline: Dark, outlineWidth: 0.1f);
                Plain(comboLabel, Color.Lerp(secondary, Color.white, 0.15f));
                Material(comboLabel, glow: secondary, glowPower: 0.15f, outline: Dark, outlineWidth: 0.05f);
                if (ghostA != null) ghostA.color = new Color(primary.r, primary.g, primary.b, 0f);
                if (ghostB != null) ghostB.color = new Color(secondary.r, secondary.g, secondary.b, 0f);
                break;

            case HudLookKind.ArcadeRetro:
                Plain(comboNumber, Color.Lerp(secondary, Color.white, 0.3f));
                Material(comboNumber, glow: secondary, glowPower: look.comboGlow, outline: Dark, outlineWidth: 0f,
                         shadow: new Color(primary.r * 0.45f, primary.g * 0.45f, primary.b * 0.45f, 1f));
                Plain(comboLabel, Color.Lerp(primary, Color.white, 0.25f));
                Material(comboLabel, glow: primary, glowPower: 0.1f, outline: Dark, outlineWidth: 0f, shadow: Dark);
                break;
        }

        TintExtras();
        return look.comboGlow;
    }

    /// Styles a banner as it starts, and starts its entrance effect. Returns the resting glow.
    public float StyleBanner(string word, Color color)
    {
        bannerColor = color;
        bannerStart = Now;
        if (!EnsureBuilt() || bannerWord == null) return 0.5f;

        bannerWord.maxVisibleCharacters = 99999;
        switch (look.kind)
        {
            case HudLookKind.SynthChrome:
                Gradient(bannerWord, Color.Lerp(color, Color.white, 0.5f), color);
                Material(bannerWord, glow: color, glowPower: look.bannerGlow, outline: Dark, outlineWidth: 0.1f);
                if (bannerPlate != null) bannerPlate.color = Color.clear;
                if (ruleTop != null) ruleTop.color = new Color(color.r, color.g, color.b, 0.9f);
                if (ruleBottom != null) ruleBottom.color = new Color(color.r, color.g, color.b, 0.9f);
                break;

            case HudLookKind.CyberGlitch:
                Plain(bannerWord, Color.Lerp(color, Color.white, 0.6f));
                Material(bannerWord, glow: color, glowPower: look.bannerGlow, outline: Dark, outlineWidth: 0.08f);
                if (bannerPlate != null) bannerPlate.color = new Color(0.02f, 0.02f, 0.06f, 0.78f);
                if (bannerBorder != null) bannerBorder.color = color;
                bannerWord.maxVisibleCharacters = 0;     // typed in by Update
                break;

            case HudLookKind.ArcadeRetro:
                Plain(bannerWord, Color.Lerp(color, Color.white, 0.35f));
                Material(bannerWord, glow: color, glowPower: look.bannerGlow, outline: Dark, outlineWidth: 0f,
                         shadow: new Color(0.02f, 0.02f, 0.06f, 1f));
                if (bannerPlate != null) bannerPlate.color = color;
                break;
        }
        return look.bannerGlow;
    }

    /// Every scored note: `progress` is how far the count is from the last milestone to the next.
    public void OnCombo(int combo, float progress)
    {
        glitchStart = Now;
        barFrom = barShown;
        barTarget = Mathf.Clamp01(progress);
        barStart = Now;
    }

    public void SetNextMilestone(int next)
    {
        if (barNext != null) barNext.text = next > 0 ? "NEXT " + next : "MAX";
    }

    // --- per frame ---

    private void LateUpdate()
    {
        if (look == null) return;
        switch (look.kind)
        {
            case HudLookKind.SynthChrome: AnimateBar(); break;
            case HudLookKind.CyberGlitch: AnimateGlitch(); AnimateTyping(); break;
            case HudLookKind.ArcadeRetro: AnimateStars(); break;
        }
    }

    private void AnimateBar()
    {
        if (barFill == null) return;
        float u = Mathf.Clamp01((Now - barStart) / Mathf.Max(0.01f, Seconds(look.progressEaseBeats)));
        u = 1f - (1f - u) * (1f - u);
        // A filled bar that resets at a milestone drops straight to the new value instead of
        // draining backwards through the whole bar.
        barShown = barTarget < barFrom ? barTarget : Mathf.Lerp(barFrom, barTarget, u);
        barFill.fillAmount = barShown;
        barFill.color = Color.Lerp(primary, secondary, barShown);
    }

    /// B: for a quarter of a beat after a hit, two copies of the number in the two accents split
    /// apart and snap back, and the number itself jumps sideways - in steps of a sixteenth of a
    /// beat, the way a corrupted frame looks, rather than sliding.
    private void AnimateGlitch()
    {
        if (ghostA == null || comboNumber == null) return;
        ghostA.text = comboNumber.text;
        ghostB.text = comboNumber.text;
        float length = Mathf.Max(0.01f, Seconds(look.glitchBeats));
        float e = Now - glitchStart;
        var numberRect = comboNumber.rectTransform;
        if (e < 0f || e >= length)
        {
            SetAlpha(ghostA, 0f);
            SetAlpha(ghostB, 0f);
            numberRect.anchoredPosition = numberRest;
            return;
        }
        float fade = 1f - e / length;
        int step = Mathf.FloorToInt(e / Mathf.Max(0.005f, Seconds(0.0625f)));
        float jitter = (Hash(step, 1) - 0.5f) * 14f * fade;
        float split = 10f + 8f * Hash(step, 2);
        SetAlpha(ghostA, 0.85f * fade);
        SetAlpha(ghostB, 0.85f * fade);
        ghostA.rectTransform.anchoredPosition = numberRest + new Vector2(-split * fade + jitter, (Hash(step, 3) - 0.5f) * 6f);
        ghostB.rectTransform.anchoredPosition = numberRest + new Vector2(split * fade + jitter, (Hash(step, 4) - 0.5f) * 6f);
        numberRect.anchoredPosition = numberRest + new Vector2(jitter * 0.5f, 0f);
    }

    /// B: the banner types itself in, one letter per `typeBeatsPerLetter`.
    private void AnimateTyping()
    {
        if (bannerWord == null) return;
        float per = Mathf.Max(0.005f, Seconds(look.typeBeatsPerLetter));
        int shown = 1 + Mathf.FloorToInt(Mathf.Max(0f, Now - bannerStart) / per);
        if (bannerWord.maxVisibleCharacters != shown) bannerWord.maxVisibleCharacters = shown;
    }

    /// C: pixel stars thrown out of the plate when a banner lands, falling and fading over
    /// `starBeats`. Each star's path is a function of the time since the banner started, so it
    /// plays identically at any frame rate.
    private void AnimateStars()
    {
        float length = Mathf.Max(0.01f, Seconds(look.starBeats));
        float e = Now - bannerStart;
        bool live = e >= 0f && e < length;
        float u = live ? e / length : 1f;
        for (int i = 0; i < stars.Count; i++)
        {
            var star = stars[i];
            var image = star.GetComponent<Image>();
            if (!live) { image.color = Color.clear; continue; }
            float angle = (i / (float)stars.Count) * Mathf.PI * 2f + Hash(i, 7) * 0.5f;
            float speed = 260f + 160f * Hash(i, 8);
            // Out fast, then pulled down: a firework, not a ring.
            float travel = speed * (1f - (1f - u) * (1f - u));
            Vector2 p = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle) * 0.6f) * travel + new Vector2(0f, -140f * u * u);
            star.anchoredPosition = p;
            // Turned in quarter steps, like a sprite on an old cabinet.
            star.localRotation = Quaternion.Euler(0f, 0f, 90f * Mathf.Floor(u * 6f + i));
            Color c = (i % 3 == 0) ? Color.Lerp(bannerColor, Color.white, 0.6f) : (i % 3 == 1 ? primary : secondary);
            c.a = u < 0.7f ? 1f : 1f - (u - 0.7f) / 0.3f;
            image.color = c;
        }
    }

    // --- layout ---

    private void Layout()
    {
        if (comboGroup != null)
        {
            SetCorner(comboGroup, look.comboPosition, look.comboBox);
            comboGroup.localScale = Vector3.one;
        }

        if (comboNumber != null)
        {
            comboNumber.font = look.comboFont != null ? look.comboFont : comboNumber.font;
            comboNumber.fontSize = look.comboSize;
            comboNumber.enableAutoSizing = false;
            comboNumber.characterSpacing = look.comboSpacing;
            comboNumber.fontStyle = look.comboItalic ? FontStyles.Italic : FontStyles.Normal;
            comboNumber.textWrappingMode = TextWrappingModes.NoWrap;
            comboNumber.overflowMode = TextOverflowModes.Overflow;
        }
        if (comboLabel != null)
        {
            comboLabel.font = look.labelFont != null ? look.labelFont : comboLabel.font;
            comboLabel.text = look.labelText;
            comboLabel.fontSize = look.labelSize;
            comboLabel.characterSpacing = look.labelSpacing;
            comboLabel.fontStyle = FontStyles.Normal;
            comboLabel.textWrappingMode = TextWrappingModes.NoWrap;
        }

        float w = look.comboBox.x, size = look.comboSize;
        switch (look.kind)
        {
            case HudLookKind.SynthChrome:
                Place(comboNumber, new Vector2(0f, 0f), new Vector2(w, size * 1.05f), TextAlignmentOptions.TopRight);
                Place(comboLabel, new Vector2(-8f, -size * 1.02f), new Vector2(w, look.labelSize * 1.4f), TextAlignmentOptions.TopRight);
                break;
            case HudLookKind.CyberGlitch:
                Place(comboLabel, new Vector2(-26f, -20f), new Vector2(w - 52f, look.labelSize * 1.4f), TextAlignmentOptions.TopLeft);
                Place(comboNumber, new Vector2(-28f, -44f), new Vector2(w - 56f, size * 1.05f), TextAlignmentOptions.TopRight);
                break;
            case HudLookKind.ArcadeRetro:
                Place(comboLabel, new Vector2(0f, -34f), new Vector2(w, look.labelSize * 1.4f), TextAlignmentOptions.Top);
                Place(comboNumber, new Vector2(0f, -34f - look.labelSize * 1.9f), new Vector2(w, size * 1.1f), TextAlignmentOptions.Top);
                break;
        }
        if (comboNumber != null) numberRest = comboNumber.rectTransform.anchoredPosition;

        if (banner != null) SetCorner(banner, look.bannerPosition, look.bannerBox);
        if (bannerWord != null)
        {
            bannerWord.font = look.bannerFont != null ? look.bannerFont : bannerWord.font;
            bannerWord.enableAutoSizing = true;
            bannerWord.fontSizeMax = look.bannerSize;
            bannerWord.fontSizeMin = 18f;
            bannerWord.fontStyle = look.bannerItalic ? FontStyles.Italic : FontStyles.Normal;
            bannerWord.alignment = TextAlignmentOptions.Center;
            bannerWord.margin = look.kind == HudLookKind.ArcadeRetro ? new Vector4(26f, 14f, 26f, 10f) : new Vector4(22f, 6f, 22f, 6f);
            bannerWord.maxVisibleCharacters = 99999;
        }
        if (bannerPlate != null)
        {
            bannerPlate.sprite = look.kind == HudLookKind.CyberGlitch ? ChamferSprite(true)
                               : look.kind == HudLookKind.ArcadeRetro ? PixelFrameSprite(false)
                               : bannerPlate.sprite;
            bannerPlate.type = Image.Type.Sliced;
            bannerPlate.pixelsPerUnitMultiplier = look.kind == HudLookKind.ArcadeRetro ? PixelScale : 1f;
        }
    }

    private void BuildExtras()
    {
        barFill = null; barNext = null; ghostA = ghostB = null; bannerBorder = null;
        ruleTop = ruleBottom = null;
        stars.Clear();

        switch (look.kind)
        {
            case HudLookKind.SynthChrome:
            {
                // Progress bar to the next milestone, under the label. Inside comboGroup, so it
                // pulses with the number and the label instead of being run over by them.
                var root = NewRect("Skin_Progress", comboGroup);
                Vector2 corner = new Vector2(-8f, -look.comboSize * 1.02f - look.labelSize * 1.5f);
                SetCorner(root, corner, new Vector2(380f, 30f));
                var bg = NewImage("Track", root, WhiteSprite(), new Color(0f, 0f, 0f, 0.5f));
                SetCorner(bg.rectTransform, Vector2.zero, new Vector2(380f, 8f));
                barFill = NewImage("Fill", root, WhiteSprite(), primary);
                SetCorner(barFill.rectTransform, Vector2.zero, new Vector2(380f, 8f));
                barFill.type = Image.Type.Filled;
                barFill.fillMethod = Image.FillMethod.Horizontal;
                barFill.fillOrigin = (int)Image.OriginHorizontal.Left;
                barFill.fillAmount = barShown;
                barNext = NewText("Next", root, look.labelFont, 18f, TextAlignmentOptions.TopRight);
                SetCorner(barNext.rectTransform, new Vector2(0f, -12f), new Vector2(380f, 24f));
                barNext.characterSpacing = 20f;
                barNext.text = "";

                // On the plate, so the rules tilt with the word.
                ruleTop = NewImage("RuleTop", bannerPlate.rectTransform, WhiteSprite(), Color.clear);
                SetCorner(ruleTop.rectTransform, new Vector2(0f, 0f), new Vector2(look.bannerBox.x, 4f));
                ruleBottom = NewImage("RuleBottom", bannerPlate.rectTransform, WhiteSprite(), Color.clear);
                SetCorner(ruleBottom.rectTransform, new Vector2(0f, -look.bannerBox.y + 4f), new Vector2(look.bannerBox.x * 0.7f, 4f));
                break;
            }

            case HudLookKind.CyberGlitch:
            {
                // Plate behind the counter: dark fill, neon chamfered edge.
                var fill = NewImage("Skin_Plate", comboGroup, ChamferSprite(true), new Color(0.02f, 0.02f, 0.06f, 0.72f));
                Stretch(fill.rectTransform);
                fill.transform.SetAsFirstSibling();
                var edge = NewImage("Skin_PlateEdge", comboGroup, ChamferSprite(false), secondary);
                Stretch(edge.rectTransform);
                edge.transform.SetSiblingIndex(1);
                // The two glitch copies go behind the number.
                ghostA = CloneText(comboNumber, "Skin_GhostA");
                ghostB = CloneText(comboNumber, "Skin_GhostB");
                ghostA.transform.SetSiblingIndex(2);
                ghostB.transform.SetSiblingIndex(3);

                bannerBorder = NewImage("Skin_BannerEdge", bannerPlate.rectTransform, ChamferSprite(false), Color.clear);
                Stretch(bannerBorder.rectTransform);
                bannerBorder.transform.SetAsFirstSibling();
                break;
            }

            case HudLookKind.ArcadeRetro:
            {
                var fill = NewImage("Skin_Frame", comboGroup, PixelFrameSprite(true), new Color(0.02f, 0.02f, 0.06f, 0.75f));
                Stretch(fill.rectTransform);
                fill.pixelsPerUnitMultiplier = PixelScale;
                fill.transform.SetAsFirstSibling();
                var edge = NewImage("Skin_FrameEdge", comboGroup, PixelFrameSprite(false), secondary);
                Stretch(edge.rectTransform);
                edge.pixelsPerUnitMultiplier = PixelScale;
                edge.transform.SetSiblingIndex(1);

                var burst = NewRect("Skin_Stars", banner);
                burst.anchorMin = burst.anchorMax = new Vector2(0.5f, 0.5f);
                burst.anchoredPosition = Vector2.zero;
                for (int i = 0; i < Mathf.Max(0, look.starCount); i++)
                {
                    var star = NewImage("Star", burst, PixelStarSprite(), Color.clear);
                    star.rectTransform.anchorMin = star.rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
                    star.rectTransform.pivot = new Vector2(0.5f, 0.5f);
                    star.rectTransform.sizeDelta = new Vector2(28f, 28f);
                    stars.Add(star.rectTransform);
                }
                break;
            }
        }
    }

    private void TintExtras()
    {
        foreach (var go in extras)
        {
            if (go == null) continue;
            if (go.name == "Skin_PlateEdge" || go.name == "Skin_FrameEdge")
                go.GetComponent<Image>().color = secondary;
        }
    }

    private void ClearExtras()
    {
        foreach (var go in extras) if (go != null) Destroy(go);
        extras.Clear();
        if (comboNumber != null) comboNumber.rectTransform.anchoredPosition = numberRest;
    }

    // --- styling helpers ---

    private static readonly Color Dark = new Color(0.02f, 0.02f, 0.06f, 1f);

    private static void Gradient(TMP_Text text, Color top, Color bottom)
    {
        if (text == null) return;
        text.color = Color.white;
        text.enableVertexGradient = true;
        text.colorGradient = new VertexGradient(top, top, bottom, bottom);
    }

    private static void Plain(TMP_Text text, Color color)
    {
        if (text == null) return;
        text.enableVertexGradient = false;
        text.color = color;
    }

    /// Glow, edge and hard shadow on the label's own material instance - the same rules HudGlow
    /// lives by (glow outside the glyph, core never clipped), with the edge colour and a shadow
    /// added for the looks that want them.
    private static void Material(TMP_Text text, Color glow, float glowPower, Color outline, float outlineWidth,
                                 Color? shadow = null)
    {
        if (text == null) return;
        HudGlow.Apply(text, glow, glowPower);
        var mat = text.fontMaterial;
        mat.SetColor(ShaderUtilities.ID_OutlineColor, outline);
        mat.SetFloat(ShaderUtilities.ID_OutlineWidth, outlineWidth);
        if (shadow.HasValue)
        {
            mat.EnableKeyword(ShaderUtilities.Keyword_Underlay);
            mat.SetColor(ShaderUtilities.ID_UnderlayColor, shadow.Value);
            mat.SetFloat(ShaderUtilities.ID_UnderlayOffsetX, 0.6f);
            mat.SetFloat(ShaderUtilities.ID_UnderlayOffsetY, -0.6f);
            mat.SetFloat(ShaderUtilities.ID_UnderlaySoftness, 0f);
            mat.SetFloat(ShaderUtilities.ID_UnderlayDilate, 0.1f);
        }
        else mat.DisableKeyword(ShaderUtilities.Keyword_Underlay);
    }

    private static void SetAlpha(TMP_Text text, float a)
    {
        var c = text.color; c.a = a; text.color = c;
    }

    /// A cheap, repeatable 0..1 value per (step, channel): the glitch and the stars must look
    /// random but be the same on every run and at every frame rate.
    private static float Hash(int step, int channel)
    {
        uint h = (uint)(step * 374761393 + channel * 668265263);
        h = (h ^ (h >> 13)) * 1274126177u;
        return (h ^ (h >> 16)) / (float)uint.MaxValue;
    }

    // --- building ---

    private static void SetCorner(RectTransform rect, Vector2 position, Vector2 size)
    {
        rect.anchorMin = rect.anchorMax = new Vector2(1f, 1f);
        rect.pivot = new Vector2(1f, 1f);
        rect.anchoredPosition = position;
        rect.sizeDelta = size;
    }

    private static void Place(TMP_Text text, Vector2 position, Vector2 size, TextAlignmentOptions align)
    {
        if (text == null) return;
        SetCorner(text.rectTransform, position, size);
        text.alignment = align;
    }

    private static void Stretch(RectTransform rect)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.offsetMin = rect.offsetMax = Vector2.zero;
    }

    private RectTransform NewRect(string name, Transform parent)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.hideFlags = HideFlags.DontSave;
        go.transform.SetParent(parent, false);
        extras.Add(go);
        return (RectTransform)go.transform;
    }

    private Image NewImage(string name, Transform parent, Sprite sprite, Color color)
    {
        var rect = NewRect(name, parent);
        var image = rect.gameObject.AddComponent<Image>();
        image.sprite = sprite;
        image.type = Image.Type.Sliced;
        image.color = color;
        image.raycastTarget = false;
        return image;
    }

    private TMP_Text NewText(string name, Transform parent, TMP_FontAsset font, float size, TextAlignmentOptions align)
    {
        var rect = NewRect(name, parent);
        var text = rect.gameObject.AddComponent<TextMeshProUGUI>();
        if (font != null) text.font = font;
        text.fontSize = size;
        text.alignment = align;
        text.textWrappingMode = TextWrappingModes.NoWrap;
        text.raycastTarget = false;
        text.color = new Color(1f, 1f, 1f, 0.7f);
        return text;
    }

    private TMP_Text CloneText(TMP_Text source, string name)
    {
        var text = NewText(name, source.transform.parent, source.font, source.fontSize, source.alignment);
        var from = source.rectTransform;
        var to = text.rectTransform;
        to.anchorMin = from.anchorMin; to.anchorMax = from.anchorMax; to.pivot = from.pivot;
        to.anchoredPosition = from.anchoredPosition; to.sizeDelta = from.sizeDelta;
        text.characterSpacing = source.characterSpacing;
        text.fontStyle = source.fontStyle;
        text.color = Color.clear;
        return text;
    }

    // --- procedural sprites ---

    // One texel of the pixel-art sprites is this many canvas pixels.
    private const float PixelTexel = 6f;
    private static float PixelScale => 1f / PixelTexel;

    private static Sprite white, chamferFill, chamferEdge, pixelFill, pixelEdge, pixelStar;

    private static Sprite WhiteSprite()
    {
        if (white != null) return white;
        var tex = new Texture2D(4, 4, TextureFormat.RGBA32, false) { hideFlags = HideFlags.DontSave };
        var px = new Color32[16];
        for (int i = 0; i < px.Length; i++) px[i] = new Color32(255, 255, 255, 255);
        tex.SetPixels32(px); tex.Apply();
        white = Sprite.Create(tex, new Rect(0, 0, 4, 4), new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect, new Vector4(1, 1, 1, 1));
        white.hideFlags = HideFlags.DontSave;
        return white;
    }

    /// B's plate: a rectangle with the top-left and bottom-right corners cut at 45 degrees. The
    /// fill version is solid; the edge version is a 3-pixel neon line along the same outline.
    private static Sprite ChamferSprite(bool fill)
    {
        if (fill && chamferFill != null) return chamferFill;
        if (!fill && chamferEdge != null) return chamferEdge;
        const int size = 96, cut = 26;
        const float border = 3f;
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false) { hideFlags = HideFlags.DontSave, wrapMode = TextureWrapMode.Clamp };
        var px = new Color32[size * size];
        for (int y = 0; y < size; y++)
        for (int x = 0; x < size; x++)
        {
            float fx = x + 0.5f, fy = y + 0.5f;
            float d = Mathf.Min(Mathf.Min(fx, fy), Mathf.Min(size - fx, size - fy));
            d = Mathf.Min(d, (fx + (size - fy) - cut) * 0.7071f);   // top-left cut
            d = Mathf.Min(d, ((size - fx) + fy - cut) * 0.7071f);   // bottom-right cut
            float inside = Mathf.Clamp01(d + 0.5f);
            float a = fill ? inside : inside * Mathf.Clamp01(border - d + 0.5f);
            px[y * size + x] = new Color32(255, 255, 255, (byte)(a * 255f));
        }
        tex.SetPixels32(px); tex.Apply();
        var sprite = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f, 0,
                                   SpriteMeshType.FullRect, new Vector4(cut + 4, cut + 4, cut + 4, cut + 4));
        sprite.hideFlags = HideFlags.DontSave;
        if (fill) chamferFill = sprite; else chamferEdge = sprite;
        return sprite;
    }

    /// C's frame: a two-texel pixel border with notched corners, like a cabinet's score box.
    private static Sprite PixelFrameSprite(bool fill)
    {
        if (fill && pixelFill != null) return pixelFill;
        if (!fill && pixelEdge != null) return pixelEdge;
        const int size = 12;
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false)
            { hideFlags = HideFlags.DontSave, filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp };
        var px = new Color32[size * size];
        for (int y = 0; y < size; y++)
        for (int x = 0; x < size; x++)
        {
            int ex = Mathf.Min(x, size - 1 - x), ey = Mathf.Min(y, size - 1 - y);
            bool corner = ex == 0 && ey == 0;                      // notch
            bool ring = !corner && (ex < 2 || ey < 2) && !(ex == 0 && ey == 1) && !(ex == 1 && ey == 0);
            bool inside = !corner && !(ex == 0 && ey == 1) && !(ex == 1 && ey == 0);
            bool on = fill ? inside : ring;
            px[y * size + x] = on ? new Color32(255, 255, 255, 255) : new Color32(255, 255, 255, 0);
        }
        tex.SetPixels32(px); tex.Apply();
        var sprite = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f, 0,
                                   SpriteMeshType.FullRect, new Vector4(3, 3, 3, 3));
        sprite.hideFlags = HideFlags.DontSave;
        if (fill) pixelFill = sprite; else pixelEdge = sprite;
        return sprite;
    }

    private static Sprite PixelStarSprite()
    {
        if (pixelStar != null) return pixelStar;
        string[] rows = { "...#...", "...#...", "..###..", "#######", "..###..", "..#.#..", ".#...#." };
        const int size = 7;
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false)
            { hideFlags = HideFlags.DontSave, filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp };
        var px = new Color32[size * size];
        for (int y = 0; y < size; y++)
        for (int x = 0; x < size; x++)
            px[(size - 1 - y) * size + x] = rows[y][x] == '#' ? new Color32(255, 255, 255, 255) : new Color32(255, 255, 255, 0);
        tex.SetPixels32(px); tex.Apply();
        pixelStar = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f);
        pixelStar.hideFlags = HideFlags.DontSave;
        return pixelStar;
    }
}
