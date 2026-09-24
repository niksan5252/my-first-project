using UnityEngine;
using UnityEngine.Rendering;

/// The desert/western background set-dressing for Theme_Desert — cacti silhouettes, wooden
/// fence posts along the road, and three glowing shop signs (SALOON/BANK/HOTEL: an emissive
/// outline frame + real TextMeshPro 3D text, since TMP's SDF shader renders correctly in this
/// scene with zero Light components, unlike a textured Lit material — see the class comment on
/// SciFiEnvironmentDecorator for that constraint). Built the same way as the sci-fi
/// environment: runtime primitives with flat emissive materials, no imported mesh/texture
/// assets, so it matches the established technique everywhere else in this project.
public class DesertEnvironmentDecorator : MonoBehaviour, IThemePalette, IEnvironmentDecorator
{
    private const float MaxEmissionIntensity = 2.5f;

    // Set via ApplyPalette (theme's primary/secondary), defaulted to the Theme_Desert values so
    // this still renders sensibly if used without ever calling ApplyPalette.
    [SerializeField] private Color primaryColor = new Color(1f, 0.478f, 0.102f);
    [SerializeField] private Color secondaryColor = new Color(0.102f, 0.722f, 0.69f);
    [SerializeField] private float accentEmissionIntensity = 1.4f;

    [Header("Cacti")]
    [SerializeField] private int cactusCount = 14;
    [SerializeField] private Vector2 cactusHeightRange = new Vector2(2.5f, 4.5f);
    [SerializeField] private Vector2 cactusSideOffsetRange = new Vector2(4f, 9f);
    // Dim teal silhouette (not full accent brightness) — reads as a shape catching starlight,
    // not a glowing neon cactus; same "near-black with a hint of color" technique as the sci-fi
    // ships.
    [SerializeField, Range(0f, MaxEmissionIntensity)] private float cactusEmissionIntensity = 0.5f;

    [Header("Fence Posts")]
    [SerializeField] private float fencePostSpacing = 5f;
    [SerializeField] private float fenceRoadHalfWidth = 3.3f;
    [SerializeField] private float fencePostHeight = 0.9f;
    [SerializeField] private float fenceRailHeight = 0.6f;
    // Warm wood brown — visible (this scene has zero Lights, so anything with zero emission is
    // simply invisible) without reading as another neon accent.
    [SerializeField] private Color fenceColor = new Color(0.35f, 0.2f, 0.08f);
    [SerializeField, Range(0f, MaxEmissionIntensity)] private float fenceEmissionIntensity = 0.35f;

    [Header("Signs")]
    [SerializeField] private float signWidth = 3.2f;
    [SerializeField] private float signHeight = 1.4f;
    [SerializeField] private float signOutlineThickness = 0.08f;
    [SerializeField] private float signFontSize = 6f;

    private float tunnelLength;
    private bool built;

    private void OnValidate()
    {
        accentEmissionIntensity = Mathf.Clamp(accentEmissionIntensity, 0f, MaxEmissionIntensity);
        cactusEmissionIntensity = Mathf.Clamp(cactusEmissionIntensity, 0f, MaxEmissionIntensity);
        fenceEmissionIntensity = Mathf.Clamp(fenceEmissionIntensity, 0f, MaxEmissionIntensity);
    }

    public void ApplyPalette(Color primary, Color secondary, float emissionIntensity)
    {
        primaryColor = primary;
        secondaryColor = secondary;
        accentEmissionIntensity = emissionIntensity;
    }

    public void Initialize(float tunnelLength, float scrollSpeed)
    {
        if (built) return;
        built = true;

        this.tunnelLength = tunnelLength;

        BuildCacti();
        BuildFencePosts();
        // The environment is a fixed backdrop (unlike the rings/lanes, it never scrolls —
        // the camera itself never moves either), so wherever these land is where they stay for
        // the whole session. Keep all three comfortably inside the fog-free near range (fog
        // starts at 0.35 * tunnelLength) instead of spreading them out toward the fogged-out far
        // end, so at least one sign is always clearly visible without hunting for it.
        BuildSign("SALOON", primaryColor, new Vector3(-6f, 2.2f, 14f));
        BuildSign("BANK", secondaryColor, new Vector3(6f, 2.2f, 26f));
        BuildSign("HOTEL", primaryColor, new Vector3(-6f, 2.2f, 40f));
    }

    private void BuildCacti()
    {
        Material mat = CreateNeonMaterial(secondaryColor, cactusEmissionIntensity);

        // Same fixed-backdrop reasoning as the signs (see Initialize): the first few are pinned
        // to a guaranteed-near band instead of a fully random roll across the whole tunnel, so
        // there's always at least one obviously close to camera regardless of luck. The rest
        // scatter further out for depth/variety.
        int guaranteedNear = Mathf.Min(4, cactusCount);
        for (int i = 0; i < cactusCount; i++)
        {
            float side = (i % 2 == 0) ? -1f : 1f;
            float x = side * Random.Range(cactusSideOffsetRange.x, cactusSideOffsetRange.y);
            float z = (i < guaranteedNear)
                ? Random.Range(6f, 20f)
                : Random.Range(tunnelLength * 0.15f, tunnelLength * 0.85f);
            float height = Random.Range(cactusHeightRange.x, cactusHeightRange.y);

            var root = new GameObject("Cactus");
            root.transform.SetParent(transform, false);
            root.transform.localPosition = new Vector3(x, 0f, z);

            // Trunk + two arms — a simple, recognizable saguaro silhouette out of stretched cubes.
            AttachCactusLimb(root.transform, mat, new Vector3(0f, height * 0.5f, 0f), new Vector3(0.35f, height, 0.35f));
            float armHeight = height * 0.55f;
            AttachCactusLimb(root.transform, mat, new Vector3(-0.32f, armHeight, 0f), new Vector3(0.22f, height * 0.35f, 0.22f));
            AttachCactusLimb(root.transform, mat, new Vector3(0.3f, armHeight * 0.8f, 0f), new Vector3(0.22f, height * 0.3f, 0.22f));
        }
    }

    private void AttachCactusLimb(Transform parent, Material mat, Vector3 localPos, Vector3 localScale)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
        go.name = "Limb";
        Destroy(go.GetComponent<Collider>());
        go.transform.SetParent(parent, false);
        go.transform.localPosition = localPos;
        go.transform.localScale = localScale;
        var mr = go.GetComponent<MeshRenderer>();
        mr.sharedMaterial = mat;
        mr.shadowCastingMode = ShadowCastingMode.Off;
        mr.receiveShadows = false;
    }

    // A dashed post+rail fence along both road edges, echoing the tunnel's own lane dashes but
    // static (this environment doesn't scroll/recycle — it's a fixed backdrop, not part of the
    // tunnel's pooled geometry).
    private void BuildFencePosts()
    {
        Material mat = CreateNeonMaterial(fenceColor, fenceEmissionIntensity);
        int count = Mathf.Max(0, Mathf.FloorToInt(tunnelLength / fencePostSpacing));

        foreach (float side in new[] { -1f, 1f })
        {
            float x = side * fenceRoadHalfWidth;
            for (int i = 0; i < count; i++)
            {
                float z = i * fencePostSpacing;

                var post = GameObject.CreatePrimitive(PrimitiveType.Cube);
                post.name = "FencePost";
                Destroy(post.GetComponent<Collider>());
                post.transform.SetParent(transform, false);
                post.transform.localPosition = new Vector3(x, fencePostHeight * 0.5f, z);
                post.transform.localScale = new Vector3(0.12f, fencePostHeight, 0.12f);
                var postMr = post.GetComponent<MeshRenderer>();
                postMr.sharedMaterial = mat;
                postMr.shadowCastingMode = ShadowCastingMode.Off;
                postMr.receiveShadows = false;

                if (i == count - 1) continue; // no rail past the last post

                var rail = GameObject.CreatePrimitive(PrimitiveType.Cube);
                rail.name = "FenceRail";
                Destroy(rail.GetComponent<Collider>());
                rail.transform.SetParent(transform, false);
                rail.transform.localPosition = new Vector3(x, fenceRailHeight, z + fencePostSpacing * 0.5f);
                rail.transform.localScale = new Vector3(0.06f, 0.06f, fencePostSpacing);
                var railMr = rail.GetComponent<MeshRenderer>();
                railMr.sharedMaterial = mat;
                railMr.shadowCastingMode = ShadowCastingMode.Off;
                railMr.receiveShadows = false;
            }
        }
    }

    // A thin rectangular outline frame (same 4-bar technique as CueSpawner's hit-pad markers) on
    // a single support post, with real glowing TextMeshPro 3D text centered inside it.
    private void BuildSign(string label, Color color, Vector3 position)
    {
        Material frameMat = CreateNeonMaterial(color, accentEmissionIntensity);

        var root = new GameObject("Sign_" + label);
        root.transform.SetParent(transform, false);
        root.transform.localPosition = position;
        // TMP 3D text reads correctly when the object's forward axis points AWAY from the
        // viewer (empirically verified — the opposite convention from what "face the road"
        // intuition suggests); the flat outline frame is symmetric so this flip doesn't affect it.
        root.transform.localRotation = Quaternion.LookRotation(Vector3.right * Mathf.Sign(position.x), Vector3.up);

        float hw = signWidth * 0.5f, hh = signHeight * 0.5f;
        CreateFlatBar(root.transform, frameMat, new Vector3(0f, hh, 0f), new Vector3(signWidth, 0.02f, signOutlineThickness));
        CreateFlatBar(root.transform, frameMat, new Vector3(0f, -hh, 0f), new Vector3(signWidth, 0.02f, signOutlineThickness));
        CreateFlatBar(root.transform, frameMat, new Vector3(hw, 0f, 0f), new Vector3(signOutlineThickness, 0.02f, signHeight));
        CreateFlatBar(root.transform, frameMat, new Vector3(-hw, 0f, 0f), new Vector3(signOutlineThickness, 0.02f, signHeight));

        // Spans from ground (local Y = -position.y, since the root sits at world Y = position.y)
        // up to the frame's bottom edge (local Y = -hh).
        float postHeight = position.y - hh;
        var post = GameObject.CreatePrimitive(PrimitiveType.Cube);
        post.name = "SignPost";
        Destroy(post.GetComponent<Collider>());
        post.transform.SetParent(root.transform, false);
        post.transform.localPosition = new Vector3(0f, -position.y + postHeight * 0.5f, 0f);
        post.transform.localScale = new Vector3(0.12f, postHeight, 0.12f);
        var postMr = post.GetComponent<MeshRenderer>();
        postMr.sharedMaterial = frameMat;
        postMr.shadowCastingMode = ShadowCastingMode.Off;
        postMr.receiveShadows = false;

        var textGO = new GameObject("Text");
        textGO.transform.SetParent(root.transform, false);
        textGO.transform.localPosition = new Vector3(0f, 0f, -0.03f);
        var tmp = textGO.AddComponent<TMPro.TextMeshPro>();
        tmp.text = label;
        tmp.fontSize = signFontSize;
        tmp.alignment = TMPro.TextAlignmentOptions.Center;
        tmp.color = color;
        tmp.rectTransform.sizeDelta = new Vector2(signWidth, signHeight);
    }

    private MeshRenderer CreateFlatBar(Transform parent, Material mat, Vector3 localPos, Vector3 localScale)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
        go.name = "Bar";
        Destroy(go.GetComponent<Collider>());
        go.transform.SetParent(parent, false);
        go.transform.localPosition = localPos;
        go.transform.localScale = localScale;
        var mr = go.GetComponent<MeshRenderer>();
        mr.sharedMaterial = mat;
        mr.shadowCastingMode = ShadowCastingMode.Off;
        mr.receiveShadows = false;
        return mr;
    }

    private Material CreateNeonMaterial(Color color, float intensity)
    {
        var mat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
        mat.SetColor("_BaseColor", color * 0.12f);
        mat.SetFloat("_Smoothness", 0.15f);
        mat.SetFloat("_Metallic", 0f);
        float clamped = Mathf.Clamp(intensity, 0f, MaxEmissionIntensity);
        mat.EnableKeyword("_EMISSION");
        mat.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
        mat.SetColor("_EmissionColor", color * clamped);
        return mat;
    }
}
