using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

/// Kenney CC0 models (Assets/ThirdParty/Kenney) placed by Video 18's worlds: an instance is sized to
/// a height, its colormap is kept and also fed to the emission, so the low-poly props read in the
/// dark tunnel in their own colours (a tint shifts them to the world's palette).
public class FiestaKit
{
    private readonly Dictionary<string, GameObject> models = new Dictionary<string, GameObject>();
    private readonly Dictionary<(Material, Color, float), Material> mats = new Dictionary<(Material, Color, float), Material>();
    private readonly List<Object> owned;

    public FiestaKit(IEnumerable<GameObject> list, List<Object> owned)
    {
        this.owned = owned;
        if (list != null)
            foreach (var m in list)
                if (m != null) models[m.name] = m;
    }

    public bool Has(string name) => models.ContainsKey(name);

    /// An instance of `name` standing on `pos`, `height` tall, turned `yaw` degrees. Null if the
    /// model is missing (the world then simply goes without it).
    public Transform Spawn(string name, Transform parent, Vector3 pos, float height, float yaw, Color tint, float glow)
    {
        if (!models.TryGetValue(name, out var model)) return null;
        var go = Object.Instantiate(model, parent, false);
        go.name = name;
        var t = go.transform;
        t.localPosition = pos;
        t.localRotation = Quaternion.Euler(0f, yaw, 0f);
        t.localScale = Vector3.one;
        foreach (var c in go.GetComponentsInChildren<Collider>()) Object.Destroy(c);
        foreach (var a in go.GetComponentsInChildren<Animator>()) Object.Destroy(a);

        float h = 0f;
        foreach (var mf in go.GetComponentsInChildren<MeshFilter>())
        {
            if (mf.sharedMesh == null) continue;
            var b = mf.sharedMesh.bounds;
            var m = t.worldToLocalMatrix * mf.transform.localToWorldMatrix;
            for (int i = 0; i < 8; i++)
            {
                var corner = b.center + Vector3.Scale(b.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                h = Mathf.Max(h, m.MultiplyPoint(corner).y);
            }
        }
        if (h > 1e-3f) t.localScale = Vector3.one * (height / h);

        foreach (var r in go.GetComponentsInChildren<Renderer>())
        {
            r.shadowCastingMode = ShadowCastingMode.Off;
            r.receiveShadows = false;
            var shared = r.sharedMaterials;
            for (int i = 0; i < shared.Length; i++) shared[i] = Tinted(shared[i], tint, glow);
            r.sharedMaterials = shared;
        }
        return t;
    }

    private Material Tinted(Material src, Color tint, float glow)
    {
        if (src == null) return null;
        var key = (src, tint, glow);
        if (mats.TryGetValue(key, out var m)) return m;
        m = new Material(src);
        owned.Add(m);
        Color baseCol = src.HasProperty("_BaseColor") ? src.GetColor("_BaseColor") : Color.white;
        m.SetColor("_BaseColor", baseCol * tint);
        m.SetFloat("_Smoothness", 0.35f);
        var tex = src.HasProperty("_BaseMap") ? src.GetTexture("_BaseMap") : null;
        m.EnableKeyword("_EMISSION");
        m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
        if (tex != null) m.SetTexture("_EmissionMap", tex);
        m.SetColor("_EmissionColor", (tex != null ? Color.white : baseCol) * tint * glow);
        mats[key] = m;
        return m;
    }
}
