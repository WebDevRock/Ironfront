using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Ironfront.EditorTools.Art
{
    /// <summary>
    /// Small procedural hard-surface modelling kit used to build Ironfront's vehicles, structures and props
    /// from code. Geometry is flat-shaded (every face has its own vertices and normal), which gives the
    /// faceted, chamfered "stylised military hardware" look described in Docs/Milestone-006-Art-Direction.md.
    ///
    /// Each primitive takes a material; the kit groups triangles into one sub-mesh per distinct material so a
    /// whole rigid part renders from a single MeshRenderer. UVs are box-projected in model space at
    /// <see cref="UvScale"/> tiles per metre, so the shared surface-detail texture has a consistent density.
    /// </summary>
    public sealed class MeshKit
    {
        private readonly List<Vector3> _vertices = new List<Vector3>();
        private readonly List<Vector3> _normals = new List<Vector3>();
        private readonly List<Vector2> _uvs = new List<Vector2>();
        private readonly List<Material> _materials = new List<Material>();
        private readonly List<List<int>> _triangles = new List<List<int>>();
        private readonly Stack<Matrix4x4> _stack = new Stack<Matrix4x4>();
        private Matrix4x4 _matrix = Matrix4x4.identity;
        /// <summary>Interior reference point (local space) used to orient faces outwards; null = keep winding.</summary>
        private Vector3? _inside;

        /// <summary>Texture tiles per metre for box-projected UVs.</summary>
        public float UvScale = 0.5f;

        public IReadOnlyList<Material> Materials => _materials;
        public int VertexCount => _vertices.Count;

        // ---------- Transform stack ----------

        public MeshKit Push(Vector3 translation, Quaternion rotation, Vector3? scale = null)
        {
            _stack.Push(_matrix);
            _matrix = _matrix * Matrix4x4.TRS(translation, rotation, scale ?? Vector3.one);
            return this;
        }

        public MeshKit Push(Vector3 translation) => Push(translation, Quaternion.identity);

        public MeshKit Pop()
        {
            _matrix = _stack.Pop();
            return this;
        }

        // ---------- Low level ----------

        private List<int> TrianglesFor(Material m)
        {
            int i = _materials.IndexOf(m);
            if (i < 0)
            {
                _materials.Add(m);
                _triangles.Add(new List<int>());
                i = _materials.Count - 1;
            }
            return _triangles[i];
        }

        /// <summary>A flat polygon (convex, counter-clockwise when viewed from the front), triangulated as a fan.</summary>
        public void Polygon(Material m, params Vector3[] points)
        {
            if (points.Length < 3) return;
            var world = new Vector3[points.Length];
            for (int i = 0; i < points.Length; i++) world[i] = _matrix.MultiplyPoint3x4(points[i]);
            Vector3 normal = Vector3.zero;
            for (int i = 1; i < world.Length - 1; i++)
                normal += Vector3.Cross(world[i] - world[0], world[i + 1] - world[0]);
            if (normal.sqrMagnitude < 1e-12f) return; // degenerate
            normal.Normalize();
            if (_inside.HasValue)
            {
                // Orient outwards: flip if the normal points towards the primitive's interior.
                Vector3 inside = _matrix.MultiplyPoint3x4(_inside.Value);
                Vector3 centroid = Vector3.zero;
                for (int i = 0; i < world.Length; i++) centroid += world[i];
                centroid /= world.Length;
                if (Vector3.Dot(normal, centroid - inside) < 0f)
                {
                    System.Array.Reverse(world);
                    normal = -normal;
                }
            }

            var tris = TrianglesFor(m);
            int start = _vertices.Count;
            for (int i = 0; i < world.Length; i++)
            {
                _vertices.Add(world[i]);
                _normals.Add(normal);
                _uvs.Add(BoxUv(world[i], normal));
            }
            for (int i = 1; i < world.Length - 1; i++)
            {
                tris.Add(start);
                tris.Add(start + i);
                tris.Add(start + i + 1);
            }
        }

        public void Quad(Material m, Vector3 a, Vector3 b, Vector3 c, Vector3 d) => Polygon(m, a, b, c, d);

        private Vector2 BoxUv(Vector3 p, Vector3 n)
        {
            Vector3 an = new Vector3(Mathf.Abs(n.x), Mathf.Abs(n.y), Mathf.Abs(n.z));
            Vector2 uv = an.y >= an.x && an.y >= an.z ? new Vector2(p.x, p.z)
                : an.x >= an.z ? new Vector2(p.z, p.y)
                : new Vector2(p.x, p.y);
            return uv * UvScale;
        }

        // ---------- Primitives ----------

        /// <summary>
        /// General hexahedron from 8 corners: bottom face b0..b3 then top face t0..t3, both listed
        /// counter-clockwise when viewed from above, starting at the (-x,-z) corner.
        /// </summary>
        public void Hexahedron(Material m, Vector3[] c, bool bottom = true)
        {
            Vector3 b0 = c[0], b1 = c[1], b2 = c[2], b3 = c[3], t0 = c[4], t1 = c[5], t2 = c[6], t3 = c[7];
            Vector3 mid = Vector3.zero;
            foreach (var p in c) mid += p;
            var previous = _inside;
            _inside = mid / 8f;
            Polygon(m, t0, t3, t2, t1); // top (viewed from above: clockwise in xz = CCW from +y)
            if (bottom) Polygon(m, b0, b1, b2, b3);
            Polygon(m, b0, t0, t1, b1);
            Polygon(m, b1, t1, t2, b2);
            Polygon(m, b2, t2, t3, b3);
            Polygon(m, b3, t3, t0, b0);
            _inside = previous;
        }

        /// <summary>
        /// Box (or tapered block) standing on <paramref name="baseCenter"/>: bottom footprint <paramref name="bottom"/>
        /// (x,z), top footprint <paramref name="top"/>, top centre shifted by <paramref name="topOffset"/> (x,z).
        /// Covers boxes, wedges, sloped armour plates and roofs.
        /// </summary>
        public void Block(Material m, Vector3 baseCenter, Vector2 bottom, Vector2 top, float height, Vector2 topOffset = default,
            bool bottomFace = false)
        {
            Vector3 bc = baseCenter, tc = baseCenter + new Vector3(topOffset.x, height, topOffset.y);
            float bx = bottom.x * 0.5f, bz = bottom.y * 0.5f, tx = top.x * 0.5f, tz = top.y * 0.5f;
            Hexahedron(m, new[]
            {
                bc + new Vector3(-bx, 0f, -bz), bc + new Vector3(-bx, 0f, bz), bc + new Vector3(bx, 0f, bz), bc + new Vector3(bx, 0f, -bz),
                tc + new Vector3(-tx, 0f, -tz), tc + new Vector3(-tx, 0f, tz), tc + new Vector3(tx, 0f, tz), tc + new Vector3(tx, 0f, -tz),
            }, bottomFace);
        }

        /// <summary>Axis-aligned box centred on <paramref name="center"/>, optionally chamfered on every edge.</summary>
        public void Box(Material m, Vector3 center, Vector3 size, float bevel = 0f, bool bottomFace = false)
        {
            Vector3 h = size * 0.5f;
            bevel = Mathf.Min(bevel, Mathf.Min(h.x, Mathf.Min(h.y, h.z)) * 0.9f);
            if (bevel <= 0.0001f)
            {
                Block(m, center - new Vector3(0f, h.y, 0f), new Vector2(size.x, size.z), new Vector2(size.x, size.z), size.y, default, bottomFace);
                return;
            }

            // Chamfered box: 6 inset faces, 12 edge bevels, 8 corner triangles.
            float b = bevel;
            var previousInside = _inside;
            _inside = center;
            Vector3 P(float sx, float sy, float sz, int insetAxis)
            {
                // Corner (sx,sy,sz) in {-1,1}, pulled in by the bevel on the two axes other than insetAxis.
                float x = sx * (insetAxis == 0 ? h.x : h.x - b);
                float y = sy * (insetAxis == 1 ? h.y : h.y - b);
                float z = sz * (insetAxis == 2 ? h.z : h.z - b);
                return center + new Vector3(x, y, z);
            }

            // Faces (vertices on the face plane, inset).
            Polygon(m, P(-1, 1, -1, 1), P(-1, 1, 1, 1), P(1, 1, 1, 1), P(1, 1, -1, 1)); // +y
            if (bottomFace) Polygon(m, P(-1, -1, -1, 1), P(1, -1, -1, 1), P(1, -1, 1, 1), P(-1, -1, 1, 1)); // -y
            Polygon(m, P(1, -1, -1, 0), P(1, 1, -1, 0), P(1, 1, 1, 0), P(1, -1, 1, 0)); // +x
            Polygon(m, P(-1, -1, 1, 0), P(-1, 1, 1, 0), P(-1, 1, -1, 0), P(-1, -1, -1, 0)); // -x
            Polygon(m, P(1, -1, 1, 2), P(1, 1, 1, 2), P(-1, 1, 1, 2), P(-1, -1, 1, 2)); // +z
            Polygon(m, P(-1, -1, -1, 2), P(-1, 1, -1, 2), P(1, 1, -1, 2), P(1, -1, -1, 2)); // -z

            // Edge bevels: between each pair of adjacent faces.
            for (int sx = -1; sx <= 1; sx += 2)
            for (int sz = -1; sz <= 1; sz += 2) // vertical edges (x,z faces)
            {
                var a0 = P(sx, -1, sz, 0); var a1 = P(sx, 1, sz, 0);
                var c0 = P(sx, -1, sz, 2); var c1 = P(sx, 1, sz, 2);
                if (sx * sz > 0) Polygon(m, a0, a1, c1, c0); else Polygon(m, c0, c1, a1, a0);
            }
            for (int sy = -1; sy <= 1; sy += 2)
            {
                if (sy < 0 && !bottomFace) continue;
                for (int sx = -1; sx <= 1; sx += 2) // edges along z (x,y faces)
                {
                    var a0 = P(sx, sy, -1, 0); var a1 = P(sx, sy, 1, 0);
                    var c0 = P(sx, sy, -1, 1); var c1 = P(sx, sy, 1, 1);
                    if (sx * sy > 0) Polygon(m, a0, c0, c1, a1); else Polygon(m, a1, c1, c0, a0);
                }
                for (int sz = -1; sz <= 1; sz += 2) // edges along x (y,z faces)
                {
                    var a0 = P(-1, sy, sz, 2); var a1 = P(1, sy, sz, 2);
                    var c0 = P(-1, sy, sz, 1); var c1 = P(1, sy, sz, 1);
                    if (sy * sz > 0) Polygon(m, a0, c0, c1, a1); else Polygon(m, a1, c1, c0, a0);
                }
            }
            // Corner triangles.
            for (int sx = -1; sx <= 1; sx += 2)
            for (int sy = -1; sy <= 1; sy += 2)
            for (int sz = -1; sz <= 1; sz += 2)
            {
                if (sy < 0 && !bottomFace) continue;
                var px = P(sx, sy, sz, 0); var py = P(sx, sy, sz, 1); var pz = P(sx, sy, sz, 2);
                if (sx * sy * sz > 0) Polygon(m, px, py, pz); else Polygon(m, px, pz, py);
            }
            if (!bottomFace)
            {
                // No lower chamfer: extend the sides straight down to the base plane so the box sits flush.
                SkirtRing(m, center, h, b);
            }
            _inside = previousInside;
        }

        private void SkirtRing(Material m, Vector3 center, Vector3 h, float b)
        {
            // Vertical strip below the lower bevel line on each side (from -h.y to -h.y+b).
            float y0 = center.y - h.y, y1 = center.y - h.y + b;
            Vector3 c = center;
            Polygon(m, new Vector3(c.x + h.x, y0, c.z - h.z + b), new Vector3(c.x + h.x, y1, c.z - h.z + b),
                new Vector3(c.x + h.x, y1, c.z + h.z - b), new Vector3(c.x + h.x, y0, c.z + h.z - b));
            Polygon(m, new Vector3(c.x - h.x, y0, c.z + h.z - b), new Vector3(c.x - h.x, y1, c.z + h.z - b),
                new Vector3(c.x - h.x, y1, c.z - h.z + b), new Vector3(c.x - h.x, y0, c.z - h.z + b));
            Polygon(m, new Vector3(c.x + h.x - b, y0, c.z + h.z), new Vector3(c.x + h.x - b, y1, c.z + h.z),
                new Vector3(c.x - h.x + b, y1, c.z + h.z), new Vector3(c.x - h.x + b, y0, c.z + h.z));
            Polygon(m, new Vector3(c.x - h.x + b, y0, c.z - h.z), new Vector3(c.x - h.x + b, y1, c.z - h.z),
                new Vector3(c.x + h.x - b, y1, c.z - h.z), new Vector3(c.x + h.x - b, y0, c.z - h.z));
            for (int sx = -1; sx <= 1; sx += 2)
            for (int sz = -1; sz <= 1; sz += 2)
            {
                var a0 = new Vector3(c.x + sx * h.x, y0, c.z + sz * (h.z - b));
                var a1 = new Vector3(c.x + sx * h.x, y1, c.z + sz * (h.z - b));
                var c0 = new Vector3(c.x + sx * (h.x - b), y0, c.z + sz * h.z);
                var c1 = new Vector3(c.x + sx * (h.x - b), y1, c.z + sz * h.z);
                if (sx * sz > 0) Polygon(m, a0, a1, c1, c0); else Polygon(m, c0, c1, a1, a0);
            }
        }

        /// <summary>
        /// Prism from a convex side profile in the (z, y) plane, extruded symmetrically along x by
        /// <paramref name="width"/>. Profile points counter-clockwise when viewed from +x. Ideal for hulls.
        /// </summary>
        public void ExtrudeX(Material m, Vector2[] profileZY, float width, float xCenter = 0f, Material capMaterial = null)
        {
            float hx = width * 0.5f;
            int n = profileZY.Length;
            var right = new Vector3[n];
            var left = new Vector3[n];
            for (int i = 0; i < n; i++)
            {
                right[i] = new Vector3(xCenter + hx, profileZY[i].y, profileZY[i].x);
                left[n - 1 - i] = new Vector3(xCenter - hx, profileZY[i].y, profileZY[i].x);
            }
            var cap = capMaterial ?? m;
            Vector2 mid = Vector2.zero;
            foreach (var p in profileZY) mid += p;
            mid /= n;
            var previous = _inside;
            _inside = new Vector3(xCenter, mid.y, mid.x);
            Polygon(cap, Reverse(right));
            Polygon(cap, Reverse(left));
            for (int i = 0; i < n; i++)
            {
                int j = (i + 1) % n;
                var a = new Vector3(xCenter + hx, profileZY[i].y, profileZY[i].x);
                var b = new Vector3(xCenter + hx, profileZY[j].y, profileZY[j].x);
                var c = new Vector3(xCenter - hx, profileZY[j].y, profileZY[j].x);
                var d = new Vector3(xCenter - hx, profileZY[i].y, profileZY[i].x);
                Polygon(m, a, d, c, b);
            }
            _inside = previous;
        }

        /// <summary>Prism from a convex footprint polygon in (x, z), counter-clockwise from above, extruded up.</summary>
        public void ExtrudeY(Material m, Vector2[] footprintXZ, float y0, float y1, Material topMaterial = null, bool bottomFace = false)
        {
            int n = footprintXZ.Length;
            var top = new Vector3[n];
            var bottom = new Vector3[n];
            for (int i = 0; i < n; i++)
            {
                top[n - 1 - i] = new Vector3(footprintXZ[i].x, y1, footprintXZ[i].y);
                bottom[i] = new Vector3(footprintXZ[i].x, y0, footprintXZ[i].y);
            }
            Vector2 mid = Vector2.zero;
            foreach (var p in footprintXZ) mid += p;
            mid /= n;
            var previous = _inside;
            _inside = new Vector3(mid.x, (y0 + y1) * 0.5f, mid.y);
            Polygon(topMaterial ?? m, top);
            if (bottomFace) Polygon(m, bottom);
            for (int i = 0; i < n; i++)
            {
                int j = (i + 1) % n;
                Polygon(m,
                    new Vector3(footprintXZ[i].x, y0, footprintXZ[i].y), new Vector3(footprintXZ[i].x, y1, footprintXZ[i].y),
                    new Vector3(footprintXZ[j].x, y1, footprintXZ[j].y), new Vector3(footprintXZ[j].x, y0, footprintXZ[j].y));
            }
            _inside = previous;
        }

        /// <summary>Surface of revolution around the local y axis from a (radius, height) profile, bottom to top.</summary>
        public void Lathe(Material m, Vector2[] profile, int segments, Vector3 center, bool capTop = true, bool capBottom = false,
            float startAngle = 0f, Material capMaterial = null)
        {
            var ring = new Vector3[segments];
            var previous = _inside;
            for (int s = 0; s < segments; s++)
            {
                float a0 = startAngle + s * Mathf.PI * 2f / segments;
                float a1 = startAngle + (s + 1) * Mathf.PI * 2f / segments;
                Vector3 d0 = new Vector3(Mathf.Cos(a0), 0f, Mathf.Sin(a0));
                Vector3 d1 = new Vector3(Mathf.Cos(a1), 0f, Mathf.Sin(a1));
                for (int i = 0; i < profile.Length - 1; i++)
                {
                    Vector2 p = profile[i], q = profile[i + 1];
                    var a = center + d0 * p.x + Vector3.up * p.y;
                    var b = center + d1 * p.x + Vector3.up * p.y;
                    var c = center + d1 * q.x + Vector3.up * q.y;
                    var d = center + d0 * q.x + Vector3.up * q.y;
                    // Outward = away from the axis; for flat (horizontal) rings use the direction of travel.
                    float midY = (p.y + q.y) * 0.5f;
                    // Rising segments face away from the axis, falling ones (inner walls) towards it; flat rings
                    // face up when the profile moves inwards and down when it moves outwards.
                    Vector3 radial = (d0 + d1).normalized;
                    if (q.y - p.y > 1e-4f) _inside = center + Vector3.up * midY;
                    else if (p.y - q.y > 1e-4f) _inside = center + Vector3.up * midY + radial * (Mathf.Max(p.x, q.x) * 2f + 1f);
                    else _inside = center + radial * ((p.x + q.x) * 0.5f) + Vector3.up * (midY + (q.x < p.x ? -1f : 1f));
                    if (p.x < 1e-4f) Polygon(m, a, c, d);
                    else if (q.x < 1e-4f) Polygon(m, a, d, b);
                    else Polygon(m, a, d, c, b);
                }
            }
            var cap = capMaterial ?? m;
            if (capTop && profile[profile.Length - 1].x > 1e-4f)
            {
                var top = profile[profile.Length - 1];
                for (int s = 0; s < segments; s++)
                {
                    float a = startAngle - s * Mathf.PI * 2f / segments;
                    ring[s] = center + new Vector3(Mathf.Cos(a) * top.x, top.y, Mathf.Sin(a) * top.x);
                }
                _inside = center + Vector3.up * (top.y - 1f);
                Polygon(cap, ring);
            }
            if (capBottom && profile[0].x > 1e-4f)
            {
                var bottom = profile[0];
                for (int s = 0; s < segments; s++)
                {
                    float a = startAngle + s * Mathf.PI * 2f / segments;
                    ring[s] = center + new Vector3(Mathf.Cos(a) * bottom.x, bottom.y, Mathf.Sin(a) * bottom.x);
                }
                _inside = center + Vector3.up * (bottom.y + 1f);
                Polygon(cap, (Vector3[])ring.Clone());
            }
            _inside = previous;
        }

        /// <summary>Vertical cylinder (optionally tapered) standing on <paramref name="baseCenter"/>.</summary>
        public void Cylinder(Material m, Vector3 baseCenter, float radius, float height, int segments = 12, float topRadius = -1f,
            Material capMaterial = null, float startAngle = 0f)
        {
            float tr = topRadius < 0f ? radius : topRadius;
            Lathe(m, new[] { new Vector2(radius, 0f), new Vector2(tr, height) }, segments, baseCenter, true, false, startAngle, capMaterial);
        }

        /// <summary>Cylinder along local +z from <paramref name="start"/> (barrels, pipes, axles).</summary>
        public void Tube(Material m, Vector3 start, float radius, float length, int segments = 8, float endRadius = -1f, bool caps = true)
        {
            Push(start, Quaternion.Euler(90f, 0f, 0f));
            float er = endRadius < 0f ? radius : endRadius;
            Lathe(m, new[] { new Vector2(radius, 0f), new Vector2(er, length) }, segments, Vector3.zero, caps, caps);
            Pop();
        }

        /// <summary>Cylinder along local +x centred on <paramref name="center"/> (wheels, road wheels, rollers).</summary>
        public void Wheel(Material m, Vector3 center, float radius, float width, int segments = 10, Material hubMaterial = null)
        {
            Push(center + new Vector3(-width * 0.5f, 0f, 0f), Quaternion.Euler(0f, 0f, -90f));
            Lathe(m, new[] { new Vector2(radius, 0f), new Vector2(radius, width) }, segments, Vector3.zero, true, true, 0f, hubMaterial);
            Pop();
        }

        // ---------- Output ----------

        /// <summary>
        /// Milestone 008: false while building skirmish-map environment meshes. Their materials have no normal maps, so
        /// tangents would only add a third to the (large) saved mesh assets.
        /// </summary>
        public static bool Tangents = true;

        public Mesh Build(string name)
        {
            var mesh = new Mesh { name = name };
            if (_vertices.Count > 65000) mesh.indexFormat = IndexFormat.UInt32;
            mesh.SetVertices(_vertices);
            mesh.SetNormals(_normals);
            mesh.SetUVs(0, _uvs);
            mesh.subMeshCount = _triangles.Count;
            for (int i = 0; i < _triangles.Count; i++) mesh.SetTriangles(_triangles[i], i, false);
            mesh.RecalculateBounds();
            if (Tangents) mesh.RecalculateTangents();
            return mesh;
        }

        private static Vector3[] Reverse(Vector3[] a)
        {
            var r = (Vector3[])a.Clone();
            System.Array.Reverse(r);
            return r;
        }
    }
}
