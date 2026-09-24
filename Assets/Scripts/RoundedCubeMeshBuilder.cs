using System.Collections.Generic;
using UnityEngine;

/// Procedurally builds a chamfered ("soft-edged") box mesh — a cube with its 12 edges and 8
/// corners cut flat by a fixed bevel width, instead of Unity's sharp-cornered primitive cube.
/// Flat-shaded (each face gets its own vertices/normal, 26 faces total: 6 main + 12 edge bevels
/// + 8 corner facets) so the bevels read as distinct facets rather than a smoothed blob — a
/// deliberately low-poly "soft cube" look, not a true rounded/spherical corner.
public static class RoundedCubeMeshBuilder
{
    public static Mesh Build(Vector3 size, float chamfer)
    {
        float hx = size.x * 0.5f, hy = size.y * 0.5f, hz = size.z * 0.5f;
        float c = Mathf.Min(chamfer, hx * 0.9f, hy * 0.9f, hz * 0.9f); // stay clear of degenerate zero-area faces

        var vertices = new List<Vector3>();
        var normals = new List<Vector3>();
        var triangles = new List<int>();

        // Emits a triangle wound so that it actually faces the way `normal` claims. The corner
        // lists below were hand-ordered per face and several of them (both Z faces, and every
        // bevel whose octant flipped the sign parity) came out inside-out: the near faces were
        // culled and the camera saw the far side of the box from within. That still looks like
        // a solid box — until a shader uses the normal, at which point dot(N, viewDir) is
        // negative everywhere and any Fresnel/rim term floods the whole surface. Rather than
        // re-derive every winding by hand, the intended normal is the source of truth and the
        // winding is corrected to match it.
        void AddTri(Vector3 a, Vector3 b, Vector3 cPt, Vector3 normal)
        {
            // chamfer == 0 collapses all 20 bevel/corner facets to zero area. They'd rasterize
            // to nothing anyway, but dropping them keeps a plain square-cornered box (which is
            // exactly what the Kick cue asks for) at the 12 triangles it should be.
            Vector3 cross = Vector3.Cross(b - a, cPt - a);
            if (cross.sqrMagnitude < 1e-12f) return;

            int start = vertices.Count;
            bool flip = Vector3.Dot(cross, normal) < 0f;

            vertices.Add(a);
            vertices.Add(flip ? cPt : b);
            vertices.Add(flip ? b : cPt);
            normals.Add(normal); normals.Add(normal); normals.Add(normal);
            triangles.Add(start); triangles.Add(start + 1); triangles.Add(start + 2);
        }

        void AddQuad(Vector3 a, Vector3 b, Vector3 cPt, Vector3 d, Vector3 normal)
        {
            AddTri(a, b, cPt, normal);
            AddTri(a, cPt, d, normal);
        }

        // 6 main faces, each inset by c on its two tangent axes. Corners listed clockwise as
        // seen from outside (Unity's expected front-facing winding).
        AddQuad(new Vector3(hx, hy - c, -(hz - c)), new Vector3(hx, hy - c, hz - c), new Vector3(hx, -(hy - c), hz - c), new Vector3(hx, -(hy - c), -(hz - c)), Vector3.right);
        AddQuad(new Vector3(-hx, hy - c, hz - c), new Vector3(-hx, hy - c, -(hz - c)), new Vector3(-hx, -(hy - c), -(hz - c)), new Vector3(-hx, -(hy - c), hz - c), Vector3.left);
        AddQuad(new Vector3(-(hx - c), hy, hz - c), new Vector3(hx - c, hy, hz - c), new Vector3(hx - c, hy, -(hz - c)), new Vector3(-(hx - c), hy, -(hz - c)), Vector3.up);
        AddQuad(new Vector3(-(hx - c), -hy, -(hz - c)), new Vector3(hx - c, -hy, -(hz - c)), new Vector3(hx - c, -hy, hz - c), new Vector3(-(hx - c), -hy, hz - c), Vector3.down);
        AddQuad(new Vector3(-(hx - c), hy - c, hz), new Vector3(hx - c, hy - c, hz), new Vector3(hx - c, -(hy - c), hz), new Vector3(-(hx - c), -(hy - c), hz), Vector3.forward);
        AddQuad(new Vector3(hx - c, hy - c, -hz), new Vector3(-(hx - c), hy - c, -hz), new Vector3(-(hx - c), -(hy - c), -hz), new Vector3(hx - c, -(hy - c), -hz), Vector3.back);

        // 12 edge bevels — one per edge, a thin quad connecting the two adjacent main faces'
        // matching corner points. Winding flips with sign parity to stay consistently
        // outward-facing regardless of which octant the edge sits in.
        foreach (float sx in new[] { -1f, 1f })
            foreach (float sy in new[] { -1f, 1f })
            {
                Vector3 n = new Vector3(sx, sy, 0f).normalized;
                Vector3 p1 = new Vector3(sx * hx, sy * (hy - c), hz - c);
                Vector3 p2 = new Vector3(sx * hx, sy * (hy - c), -(hz - c));
                Vector3 p3 = new Vector3(sx * (hx - c), sy * hy, -(hz - c));
                Vector3 p4 = new Vector3(sx * (hx - c), sy * hy, hz - c);
                if (sx * sy > 0f) AddQuad(p2, p1, p4, p3, n); else AddQuad(p1, p2, p3, p4, n);
            }
        foreach (float sx in new[] { -1f, 1f })
            foreach (float sz in new[] { -1f, 1f })
            {
                Vector3 n = new Vector3(sx, 0f, sz).normalized;
                Vector3 p1 = new Vector3(sx * hx, hy - c, sz * (hz - c));
                Vector3 p2 = new Vector3(sx * hx, -(hy - c), sz * (hz - c));
                Vector3 p3 = new Vector3(sx * (hx - c), -(hy - c), sz * hz);
                Vector3 p4 = new Vector3(sx * (hx - c), hy - c, sz * hz);
                if (sx * sz > 0f) AddQuad(p1, p2, p3, p4, n); else AddQuad(p2, p1, p4, p3, n);
            }
        foreach (float sy in new[] { -1f, 1f })
            foreach (float sz in new[] { -1f, 1f })
            {
                Vector3 n = new Vector3(0f, sy, sz).normalized;
                Vector3 p1 = new Vector3(hx - c, sy * hy, sz * (hz - c));
                Vector3 p2 = new Vector3(-(hx - c), sy * hy, sz * (hz - c));
                Vector3 p3 = new Vector3(-(hx - c), sy * (hy - c), sz * hz);
                Vector3 p4 = new Vector3(hx - c, sy * (hy - c), sz * hz);
                if (sy * sz > 0f) AddQuad(p2, p1, p4, p3, n); else AddQuad(p1, p2, p3, p4, n);
            }

        // 8 corner facets — a small triangle connecting the 3 nearest bevel-point corners.
        foreach (float sx in new[] { -1f, 1f })
            foreach (float sy in new[] { -1f, 1f })
                foreach (float sz in new[] { -1f, 1f })
                {
                    Vector3 n = new Vector3(sx, sy, sz).normalized;
                    Vector3 px = new Vector3(sx * hx, sy * (hy - c), sz * (hz - c));
                    Vector3 py = new Vector3(sx * (hx - c), sy * hy, sz * (hz - c));
                    Vector3 pz = new Vector3(sx * (hx - c), sy * (hy - c), sz * hz);
                    if (sx * sy * sz > 0f) AddTri(px, pz, py, n); else AddTri(px, py, pz, n);
                }

        var mesh = new Mesh();
        mesh.indexFormat = (vertices.Count > 65000) ? UnityEngine.Rendering.IndexFormat.UInt32 : UnityEngine.Rendering.IndexFormat.UInt16;
        mesh.SetVertices(vertices);
        mesh.SetNormals(normals);
        mesh.SetTriangles(triangles, 0);
        mesh.RecalculateBounds();
        return mesh;
    }
}
