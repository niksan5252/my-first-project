using UnityEngine;
using UnityEngine.Rendering;

/// Beam and neon-material helpers shared by the Neon Remix rings (RingArchToxicSpike, RingArchIceHex).
public static class RemixGeo
{
    public const float MaxEmission = 2.5f;
    public static readonly int EmissionId = Shader.PropertyToID("_EmissionColor");

    public static MeshRenderer Beam(Transform parent, string name, Mesh mesh, Material mat, Vector3 a, Vector3 b, float thickness)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        Vector3 edge = b - a;
        float len = edge.magnitude;
        go.transform.localPosition = (a + b) * 0.5f;
        if (len > 1e-4f)
        {
            go.transform.localRotation = Quaternion.FromToRotation(Vector3.up, edge / len);
            go.transform.localScale = new Vector3(thickness, len + thickness, thickness);
        }
        else go.transform.localScale = Vector3.one * thickness;
        go.AddComponent<MeshFilter>().sharedMesh = mesh;
        var mr = go.AddComponent<MeshRenderer>();
        mr.sharedMaterial = mat;
        mr.shadowCastingMode = ShadowCastingMode.Off;
        mr.receiveShadows = false;
        return mr;
    }

    public static Mesh Primitive(PrimitiveType type)
    {
        var temp = GameObject.CreatePrimitive(type);
        Mesh mesh = temp.GetComponent<MeshFilter>().sharedMesh;
        Object.Destroy(temp);
        return mesh;
    }

    public static Material Neon(Color c, float intensity)
    {
        var m = new Material(Shader.Find("Universal Render Pipeline/Lit"));
        m.SetColor("_BaseColor", c * 0.1f);
        m.SetFloat("_Smoothness", 0.2f);
        m.SetFloat("_Metallic", 0f);
        SetEmission(m, c, intensity);
        return m;
    }

    public static void SetEmission(Material m, Color c, float intensity)
    {
        m.EnableKeyword("_EMISSION");
        m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
        m.SetColor(EmissionId, c * Mathf.Clamp(intensity, 0f, MaxEmission));
    }
}
