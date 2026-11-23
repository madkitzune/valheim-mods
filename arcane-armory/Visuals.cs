using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.Rendering;

namespace ArcaneArmory
{
    // Procedural ornaments (feathers, gems, rings, runes) built onto the cloned weapon models.
    // The base meshes are not readable, so vertex positions are read back from the GPU once and the ornaments are added to the prefab afterwards.
    internal static class Visuals
    {
        public static Mesh FeatherMesh;
        public static Material FeatherMat;
        private static readonly Dictionary<Mesh, Vector3[]> vertCache = new Dictionary<Mesh, Vector3[]>();
        private static readonly Dictionary<Mesh, List<Action<Vector3[]>>> waiting = new Dictionary<Mesh, List<Action<Vector3[]>>>();

        public static void Decorate(GameObject prefab, WeaponDef d)
        {
            if (string.IsNullOrEmpty(d.Look)) return;
            foreach (var mf in prefab.GetComponentsInChildren<MeshFilter>(true))
            {
                if (mf.sharedMesh == null || mf.GetComponent<Renderer>() == null) continue;
                string n = mf.name.ToLowerInvariant();
                if (n.Contains("upgrader") || n.Contains("effect") || mf.name == "AAOrnament") continue;
                var mesh = mf.sharedMesh;
                var holder = mf;
                WithVerts(mesh, v => { try { Build(holder, d, v); } catch (Exception e) { Plugin.Log.LogWarning($"[look {d.Id}] {e.Message}"); } });
            }
        }

        private static void WithVerts(Mesh m, Action<Vector3[]> cb)
        {
            if (vertCache.TryGetValue(m, out var v)) { cb(v); return; }
            if (m.isReadable) { vertCache[m] = m.vertices; cb(vertCache[m]); return; }
            if (waiting.TryGetValue(m, out var list)) { list.Add(cb); return; }
            waiting[m] = new List<Action<Vector3[]>> { cb };
            try
            {
                int stream = m.GetVertexAttributeStream(VertexAttribute.Position);
                int offset = m.GetVertexAttributeOffset(VertexAttribute.Position);
                int stride = m.GetVertexBufferStride(stream);
                int count = m.vertexCount;
                if (m.GetVertexAttributeFormat(VertexAttribute.Position) != VertexAttributeFormat.Float32) { Plugin.Log.LogWarning("[look] unsupported vertex format on " + m.name); return; }
                var gb = m.GetVertexBuffer(stream);
                AsyncGPUReadback.Request(gb, req =>
                {
                    try
                    {
                        if (req.hasError) { Plugin.Log.LogWarning("[look] readback failed " + m.name + ", using bounds"); Fallback(m); return; }
                        var bytes = req.GetData<byte>().ToArray();
                        var arr = new Vector3[count];
                        for (int i = 0; i < count; i++)
                        {
                            int o = i * stride + offset;
                            arr[i] = new Vector3(BitConverter.ToSingle(bytes, o), BitConverter.ToSingle(bytes, o + 4), BitConverter.ToSingle(bytes, o + 8));
                        }
                        vertCache[m] = arr;
                        foreach (var a in waiting[m]) a(arr);
                    }
                    catch (Exception e) { Plugin.Log.LogWarning("[look] readback: " + e.Message); }
                    finally { try { gb.Dispose(); } catch { } }
                });
            }
            catch (Exception e) { Plugin.Log.LogWarning("[look] " + e.Message); }
        }

        private static void Fallback(Mesh m)
        {
            var b = m.bounds;
            var arr = new Vector3[8];
            for (int i = 0; i < 8; i++)
                arr[i] = b.center + new Vector3((i & 1) == 0 ? -b.extents.x : b.extents.x, (i & 2) == 0 ? -b.extents.y : b.extents.y, (i & 4) == 0 ? -b.extents.z : b.extents.z);
            vertCache[m] = arr;
            if (waiting.TryGetValue(m, out var l)) foreach (var a in l) a(arr);
        }

        // ---------- geometry helpers ----------
        private class MB
        {
            public List<Vector3> V = new List<Vector3>(); public List<int> T = new List<int>(); public List<Vector2> UV = new List<Vector2>();
            public int Add(Vector3 p) { V.Add(p); UV.Add(Vector2.zero); return V.Count - 1; }
            public void Quad(int a, int b, int c, int d) { T.AddRange(new[] { a, b, c, a, c, d, a, c, b, a, d, c }); }
            public void Tri(int a, int b, int c) { T.AddRange(new[] { a, b, c, a, c, b }); }
            public Mesh Mesh(string name)
            {
                var m = new Mesh { name = name };
                m.SetVertices(V); m.SetUVs(0, UV); m.SetTriangles(T, 0); m.RecalculateNormals(); m.RecalculateBounds();
                return m;
            }
        }

        private static void Seg(MB b, Vector3 a, Vector3 c, Vector3 side, float w)
        {
            // thin double-sided strip from a to c, width w along side
            int i0 = b.Add(a - side * w), i1 = b.Add(a + side * w), i2 = b.Add(c + side * w), i3 = b.Add(c - side * w);
            b.Quad(i0, i1, i2, i3);
        }

        private static void Feather(MB b, Vector3 root, Vector3 dir, Vector3 side, float len, float wid)
        {
            // diamond-shaped feather with a slight curve
            Vector3 mid = root + dir * (len * 0.45f);
            int r = b.Add(root), l = b.Add(mid - side * wid), rr = b.Add(mid + side * wid), t = b.Add(root + dir * len + side * (wid * 0.2f));
            b.Quad(r, l, t, rr);
        }

        private static void Box(MB b, Vector3 c, Vector3 e)
        {
            int[] i = new int[8];
            for (int k = 0; k < 8; k++) i[k] = b.Add(c + new Vector3((k & 1) == 0 ? -e.x : e.x, (k & 2) == 0 ? -e.y : e.y, (k & 4) == 0 ? -e.z : e.z));
            b.Quad(i[0], i[1], i[3], i[2]); b.Quad(i[4], i[5], i[7], i[6]); b.Quad(i[0], i[1], i[5], i[4]);
            b.Quad(i[2], i[3], i[7], i[6]); b.Quad(i[0], i[2], i[6], i[4]); b.Quad(i[1], i[3], i[7], i[5]);
        }

        private static void Gem(MB b, Vector3 c, float s)
        {
            var p = new[] { c + Vector3.right * s, c - Vector3.right * s, c + Vector3.up * s * 1.4f, c - Vector3.up * s * 1.4f, c + Vector3.forward * s, c - Vector3.forward * s };
            int[] id = p.Select(x => b.Add(x)).ToArray();
            b.Tri(id[2], id[0], id[4]); b.Tri(id[2], id[4], id[1]); b.Tri(id[2], id[1], id[5]); b.Tri(id[2], id[5], id[0]);
            b.Tri(id[3], id[4], id[0]); b.Tri(id[3], id[1], id[4]); b.Tri(id[3], id[5], id[1]); b.Tri(id[3], id[0], id[5]);
        }

        private static void Ring(MB b, Vector3 c, Vector3 axis, float radius, float tube, int seg = 18)
        {
            Vector3 u = Vector3.Cross(axis, Mathf.Abs(Vector3.Dot(axis, Vector3.up)) > 0.9f ? Vector3.right : Vector3.up).normalized;
            Vector3 v = Vector3.Cross(axis, u).normalized;
            for (int i = 0; i < seg; i++)
            {
                float a0 = 2 * Mathf.PI * i / seg, a1 = 2 * Mathf.PI * (i + 1) / seg;
                Vector3 p0 = c + (u * Mathf.Cos(a0) + v * Mathf.Sin(a0)) * radius, p1 = c + (u * Mathf.Cos(a1) + v * Mathf.Sin(a1)) * radius;
                Seg(b, p0 - axis * tube, p1 - axis * tube, axis, tube * 0.1f);
                int q0 = b.Add(p0 - axis * tube), q1 = b.Add(p0 + axis * tube), q2 = b.Add(p1 + axis * tube), q3 = b.Add(p1 - axis * tube);
                b.Quad(q0, q1, q2, q3);
            }
        }

        // ---------- ornaments ----------
        private static void Build(MeshFilter mf, WeaponDef d, Vector3[] v)
        {
            if (v == null || v.Length < 8) return;
            if (mf == null) return;
            // do not decorate twice
            if (mf.transform.Find("AAOrnament") != null) return;
            Bounds bb = new Bounds(v[0], Vector3.zero);
            foreach (var p in v) bb.Encapsulate(p);
            Vector3 s = bb.size;
            int la = 0; if (s.y > s[la]) la = 1; if (s.z > s[la]) la = 2;
            int ta = 0; if (s.y < s[ta]) ta = 1; if (s.z < s[ta]) ta = 2;
            if (ta == la) ta = (la + 1) % 3;
            int wa = 3 - la - ta;
            float L = s[la];
            Vector3 A = Ax(la), W = Ax(wa), T = Ax(ta);
            // head end of the long axis = the end with the larger cross-section
            float hi = Spread(v, la, wa, ta, bb.max[la] - L * 0.25f, bb.max[la]);
            float lo = Spread(v, la, wa, ta, bb.min[la], bb.min[la] + L * 0.25f);
            int headSign = hi >= lo ? 1 : -1;
            Vector3 head = bb.center + A * (headSign * s[la] * 0.5f);
            Vector3 tail = bb.center - A * (headSign * s[la] * 0.5f);
            float sizeW = s[wa], sizeT = s[ta];

            Vector3 prevFace = Vector3.zero;
            var root = new GameObject("AAOrnament");
            root.transform.SetParent(mf.transform, false);
            var baseMat = mf.GetComponent<Renderer>().sharedMaterial;
            foreach (var tok in d.Look.Split('|'))
            {
                var parts = tok.Split(':');
                string kind = parts[0];
                Color col = parts.Length > 1 ? Hex(parts[1]) : Color.white;
                var b = new MB();
                bool glow = true;
                if (kind == "feathers2" && FeatherMesh == null) { kind = "feathers"; parts = new[] { "feathers", parts[1], "both", parts.Length > 2 ? parts[2] : "6" }; }
                switch (kind)
                {
                    case "feathers":
                        {
                            string where = parts.Length > 2 ? parts[2] : "both";
                            int count = parts.Length > 3 ? int.Parse(parts[3]) : 5;
                            float len = L * 0.16f, wid = L * 0.025f;
                            foreach (int sg in new[] { 1, -1 })
                            {
                                if (where == "head" && sg != headSign) continue;
                                if (where == "tail" && sg == headSign) continue;
                                Vector3 tip = bb.center + A * (sg * s[la] * 0.5f);
                                for (int i = 0; i < count; i++)
                                {
                                    float k = count == 1 ? 0 : (i / (float)(count - 1)) * 2f - 1f;
                                    // feathers fan out in the plane of the long and the wide axis, a little lower than the tip
                                    Vector3 rootP = tip - A * (sg * L * (0.015f + 0.02f * Mathf.Abs(k)));
                                    Vector3 dir = (A * sg * 0.55f + W * k * 0.8f + T * 0.12f).normalized;
                                    Feather(b, rootP, dir, T, len * (1f - 0.18f * Mathf.Abs(k)), wid);
                                }
                            }
                            glow = false;
                            break;
                        }
                    case "gem":
                        {
                            string where = parts.Length > 2 ? parts[2] : "head";
                            Vector3 pos = where == "tail" ? tail : head + A * (headSign * L * 0.03f);
                            Gem(b, pos, L * (where == "small" ? 0.018f : 0.035f));
                            break;
                        }
                    case "ring":
                        {
                            int n = parts.Length > 2 ? int.Parse(parts[2]) : 1;
                            for (int i = 0; i < n; i++)
                            {
                                float pos = 0.82f - i * 0.05f;
                                Vector3 c = bb.center + A * (headSign * s[la] * (pos - 0.5f));
                                Ring(b, c, A, Mathf.Max(sizeW, sizeT) * 0.5f + L * 0.004f, L * 0.006f);
                            }
                            break;
                        }
                    case "runes":
                        {
                            // face = the thin axis; runes on both faces: an outer ring and a few strokes
                            foreach (int sg in new[] { 1, -1 })
                            {
                                Vector3 n = T * sg;
                                Vector3 c = bb.center + n * (sizeT * 0.5f + L * 0.003f);
                                float r = Mathf.Min(s[la], s[wa]) * 0.30f;
                                Ring(b, c, n, r, L * 0.004f, 24);
                                for (int i = 0; i < 6; i++)
                                {
                                    float a = Mathf.PI * 2 * i / 6;
                                    Vector3 dirv = A * Mathf.Cos(a) + W * Mathf.Sin(a);
                                    Seg(b, c + dirv * r * 0.35f, c + dirv * r * 0.8f, n, L * 0.006f);
                                }
                            }
                            break;
                        }
                    case "feathers2":
                        {
                            if (FeatherMesh == null) break;
                            int count = parts.Length > 2 ? int.Parse(parts[2]) : 6;
                            var fb = FeatherMesh.bounds;
                            int fl = 0; if (fb.size.y > fb.size[fl]) fl = 1; if (fb.size.z > fb.size[fl]) fl = 2;
                            float fscale = (L * 0.15f) / fb.size[fl];
                            Vector3 flocal = Ax(fl);
                            var fmat = new Material(FeatherMat != null ? FeatherMat : baseMat);
                            if (fmat.HasProperty("_Color")) fmat.SetColor("_Color", col);
                            foreach (int sg in new[] { 1, -1 })
                            {
                                for (int i = 0; i < count; i++)
                                {
                                    float k = i / (float)Mathf.Max(1, count - 1);
                                    float l = bb.center[la] + sg * s[la] * (0.46f - 0.20f * k);
                                    Vector3 cpos = Centre(v, la, wa, ta, l, L * 0.02f);
                                    float side = (i % 2 == 0 ? 1f : -1f);
                                    Vector3 dir = (A * (-sg) * 0.35f + T * side * 0.9f).normalized;
                                    var fgo = new GameObject("AA_feather");
                                    fgo.layer = mf.gameObject.layer;
                                    fgo.transform.SetParent(root.transform, false);
                                    fgo.transform.localPosition = cpos + T * side * (sizeT * 0.5f);
                                    fgo.transform.localRotation = Quaternion.FromToRotation(flocal, dir);
                                    fgo.transform.localScale = Vector3.one * fscale;
                                    fgo.AddComponent<MeshFilter>().sharedMesh = FeatherMesh;
                                    var fr = fgo.AddComponent<MeshRenderer>(); fr.sharedMaterial = fmat; fr.shadowCastingMode = ShadowCastingMode.Off;
                                }
                            }
                            glow = false;
                            break;
                        }
                    case "vine":
                        {
                            // helix along the shaft with small leaves
                            float r = Mathf.Min(sizeW, sizeT) * 0.5f * 1.5f + L * 0.004f;
                            int steps = 90; float turns = 7f;
                            float l0 = bb.center[la] - s[la] * 0.45f, l1 = bb.center[la] + s[la] * 0.45f;
                            var lb = new MB();
                            Vector3 prev = Vector3.zero; bool has = false;
                            for (int i = 0; i <= steps; i++)
                            {
                                float k = i / (float)steps; float l = Mathf.Lerp(l0, l1, k);
                                Vector3 c0 = Centre(v, la, wa, ta, l, L * 0.02f);
                                float a = k * turns * Mathf.PI * 2f;
                                Vector3 p = c0 + W * Mathf.Cos(a) * r + T * Mathf.Sin(a) * r;
                                if (has) { Vector3 sideV = (W * Mathf.Cos(a) + T * Mathf.Sin(a)).normalized; Seg(b, prev, p, Vector3.Cross(p - prev, sideV).normalized, L * 0.004f); }
                                prev = p; has = true;
                                if (i % 4 == 1) { Vector3 outt = (W * Mathf.Cos(a) + T * Mathf.Sin(a)).normalized; int s0 = b.Add(p + A * (L * 0.006f)), s1 = b.Add(p - A * (L * 0.006f)), s2 = b.Add(p + outt * (L * 0.03f)); b.Tri(s0, s1, s2); }
                                if (i % 6 == 3) { Vector3 outv = (W * Mathf.Cos(a) + T * Mathf.Sin(a)).normalized; Feather(lb, p, (outv * 0.7f + A * 0.5f).normalized, T, L * 0.035f, L * 0.012f); }
                            }
                            // leaves as a second mesh in a lighter green
                            if (lb.V.Count > 0)
                            {
                                var lgo = new GameObject("AA_leaves"); lgo.layer = mf.gameObject.layer; lgo.transform.SetParent(root.transform, false);
                                lgo.AddComponent<MeshFilter>().sharedMesh = lb.Mesh("AA_leaves");
                                var lm = lgo.AddComponent<MeshRenderer>(); lm.sharedMaterial = Mat(baseMat, Color.Lerp(col, Color.white, 0.35f), true); lm.shadowCastingMode = ShadowCastingMode.Off;
                            }
                            break;
                        }
                    case "banner":
                        {
                            Vector3 top = head - A * (headSign * L * 0.16f);
                            float h = L * 0.10f, wd = L * 0.06f;
                            Vector3 side = W;
                            int q0 = b.Add(top), q1 = b.Add(top + side * wd), q2 = b.Add(top + side * wd - A * (headSign * h) ), q3 = b.Add(top - A * (headSign * h) );
                            b.Quad(q0, q1, q2, q3);
                            // swallow-tail
                            int t0 = b.Add(top - A * (headSign * h)), t1 = b.Add(top + side * wd - A * (headSign * h)), t2 = b.Add(top + side * wd * 0.5f - A * (headSign * h * 1.35f));
                            b.Tri(t0, t1, t2);
                            // white emblem: a small cross
                            var eb = new MB();
                            Vector3 ec = top + side * wd * 0.5f - A * (headSign * h * 0.45f);
                            Seg(eb, ec - side * wd * 0.25f, ec + side * wd * 0.25f, T, L * 0.004f);
                            Seg(eb, ec - A * (headSign * h * 0.25f), ec + A * (headSign * h * 0.25f), T, L * 0.004f);
                            var ego = new GameObject("AA_emblem"); ego.layer = mf.gameObject.layer; ego.transform.SetParent(root.transform, false);
                            ego.transform.localPosition = T * (L * 0.002f);
                            ego.AddComponent<MeshFilter>().sharedMesh = eb.Mesh("AA_emblem");
                            var em = ego.AddComponent<MeshRenderer>(); em.sharedMaterial = Mat(baseMat, Color.white, true); em.shadowCastingMode = ShadowCastingMode.Off;
                            break;
                        }
                    case "inlay":
                        {
                            // flat diamond lying on both faces of the blade, near the middle
                            foreach (int sg in new[] { 1, -1 })
                            {
                                Vector3 n = T * sg;
                                Vector3 c = bb.center + A * (headSign * s[la] * 0.12f) + n * (sizeT * 0.5f + L * 0.002f);
                                float hl = L * 0.07f, hw = L * 0.025f;
                                int a0 = b.Add(c + A * hl), a1 = b.Add(c + W * hw), a2 = b.Add(c - A * hl), a3 = b.Add(c - W * hw);
                                b.Quad(a0, a1, a2, a3);
                            }
                            break;
                        }
                    case "edgeline":
                    case "edgewave":
                    case "edgesaw":
                        {
                            // thin strip along the cutting edge: the side of the wide axis whose slab is the thinnest
                            int es = EdgeSign(v, la, wa, ta, bb);
                            float l0 = bb.center[la] - s[la] * 0.3f, l1 = bb.center[la] + s[la] * 0.45f;
                            if (headSign < 0) { l0 = bb.center[la] - s[la] * 0.45f; l1 = bb.center[la] + s[la] * 0.3f; }
                            int steps = kind == "edgeline" ? 20 : 60;
                            Vector3 prev = Vector3.zero; bool has = false;
                            for (int i = 0; i <= steps; i++)
                            {
                                float k = i / (float)steps; float l = Mathf.Lerp(l0, l1, k);
                                float w = EdgeW(v, la, wa, l, L * 0.02f, es, bb.center[wa]);
                                float off = 0f;
                                if (kind == "edgewave") off = Mathf.Sin(k * Mathf.PI * 10f) * sizeW * 0.04f;
                                if (kind == "edgesaw") off = ((i % 2 == 0) ? 0f : -L * 0.018f * es);
                                Vector3 p = Vector3.zero; p[la] = l; p[wa] = w - es * (L * 0.006f) + (kind == "edgewave" ? off : 0f) + (kind == "edgesaw" ? off : 0f); p[ta] = bb.center[ta] + sizeT * 0.5f + L * 0.002f;
                                if (has) Seg(b, prev, p, T, L * (kind == "edgeline" ? 0.003f : 0.004f));
                                prev = p; has = true;
                                // second face
                                Vector3 p2 = p; p2[ta] = bb.center[ta] - sizeT * 0.5f - L * 0.002f;
                                if (i > 0) { Vector3 pp2 = prevFace; Seg(b, pp2, p2, T, L * (kind == "edgeline" ? 0.003f : 0.004f)); }
                                prevFace = p2;
                            }
                            break;
                        }
                    case "stretch":
                        {
                            float f = parts.Length > 1 ? float.Parse(parts[1], System.Globalization.CultureInfo.InvariantCulture) : 1.2f;
                            var ls = mf.transform.localScale; ls[la] *= f; mf.transform.localScale = ls;
                            break;
                        }
                    case "guard":
                        {
                            // cross guard where the grip ends: a flat bar across the blade
                            Vector3 gc = bb.center - A * (headSign * s[la] * 0.5f) + A * (headSign * s[la] * 0.24f);
                            Vector3 e = Vector3.zero; e[la] = L * 0.018f; e[wa] = Mathf.Max(sizeW * 0.5f, L * 0.02f) + L * 0.07f; e[ta] = Mathf.Max(sizeT * 0.6f, L * 0.012f);
                            Box(b, gc, e);
                            // little end caps
                            Gem(b, gc + W * e[wa], L * 0.014f); Gem(b, gc - W * e[wa], L * 0.014f);
                            break;
                        }
                    case "runeblade":
                        {
                            // red runic inscription on the axe head, running along the cutting edge on both faces
                            int es = EdgeSign(v, la, wa, ta, bb);
                            float lh = bb.center[la] + headSign * s[la] * 0.5f - headSign * L * 0.10f;
                            for (int fs = 0; fs < 2; fs++)
                            {
                                float tz = bb.center[ta] + (fs == 0 ? 1 : -1) * (sizeT * 0.5f + L * 0.002f);
                                for (int g = 0; g < 6; g++)
                                {
                                    float w = bb.center[wa] + es * (sizeW * (0.05f + 0.09f * g));
                                    Vector3 c = Vector3.zero; c[la] = lh; c[wa] = w; c[ta] = tz;
                                    float h = L * 0.03f;
                                    // glyph: bar, slash and a hook
                                    Seg(b, c - A * h, c + A * h, T, L * 0.003f);
                                    Seg(b, c - A * h + W * (L * 0.012f), c + A * (h * 0.2f) - W * (L * 0.012f), T, L * 0.003f);
                                    Seg(b, c + A * h, c + A * h + W * (L * 0.016f), T, L * 0.003f);
                                    if (g % 2 == 0) Seg(b, c, c + W * (L * 0.016f), T, L * 0.003f);
                                }
                            }
                            break;
                        }
                    case "bladeclone":
                        {
                            // a copy of the axe, mirrored across the haft, so the head is doubled (like a second head laid over the first)
                            float wc = 0f; int n = 0;
                            foreach (var p in v) if (Get(p, la) < bb.center[la] - headSign * s[la] * 0.1f * 0 && (headSign > 0 ? Get(p, la) < bb.center[la] - s[la] * 0.1f : Get(p, la) > bb.center[la] + s[la] * 0.1f)) { wc += Get(p, wa); n++; }
                            wc = n > 0 ? wc / n : bb.center[wa];
                            var cgo = new GameObject("AA_bladeclone");
                            cgo.layer = mf.gameObject.layer;
                            cgo.transform.SetParent(root.transform, false);
                            var sc = Vector3.one; sc[wa] = -1.04f; sc[ta] = 1.05f; sc[la] = 1.01f;
                            cgo.transform.localScale = sc;
                            Vector3 pos = Vector3.zero; pos[wa] = 2f * wc * 1.04f - wc * 0.04f; pos[ta] = -bb.center[ta] * 0.05f; pos[la] = -bb.center[la] * 0.01f;
                            cgo.transform.localPosition = pos;
                            cgo.AddComponent<MeshFilter>().sharedMesh = mf.sharedMesh;
                            var cr = cgo.AddComponent<MeshRenderer>();
                            var cmat = new Material(baseMat); if (cmat.HasProperty("_Color")) cmat.SetColor("_Color", Color.Lerp(Color.white, col, 0.6f));
                            cr.sharedMaterial = cmat;
                            glow = false;
                            break;
                        }
                    case "claws":
                        {
                            int es = EdgeSign(v, la, wa, ta, bb);
                            for (int c = 0; c < 3; c++)
                            {
                                float l = bb.center[la] + headSign * s[la] * (0.12f - 0.12f * c);
                                float w0 = EdgeW(v, la, wa, l, L * 0.03f, es, bb.center[wa]);
                                Vector3 prev = Vector3.zero;
                                for (int i = 0; i <= 7; i++)
                                {
                                    float k = i / 7f;
                                    Vector3 p = Vector3.zero; p[la] = l + headSign * L * 0.07f * k * k; p[wa] = w0 + es * L * 0.07f * Mathf.Sin(k * 1.4f); p[ta] = bb.center[ta];
                                    if (i > 0) Seg(b, prev, p, T, L * 0.016f * (1f - k));
                                    prev = p;
                                }
                            }
                            break;
                        }
                    case "flamberg":
                        {
                            // a wavy blade laid over the original one (covers it)
                            int es = EdgeSign(v, la, wa, ta, bb);
                            float l0 = bb.center[la] - headSign * s[la] * 0.5f + headSign * L * 0.30f, l1 = bb.center[la] + headSign * s[la] * 0.5f;
                            int steps = 40;
                            float bw = Mathf.Max(sizeW * 0.55f, L * 0.05f);
                            Vector3 pl = Vector3.zero, pr = Vector3.zero; bool has = false;
                            for (int i = 0; i <= steps; i++)
                            {
                                float k = i / (float)steps;
                                float l = Mathf.Lerp(l0, l1, k);
                                float wave = Mathf.Sin(k * Mathf.PI * 6f) * bw * 0.35f * (1f - k * 0.6f);
                                float half = bw * (1f - 0.75f * k * k);
                                Vector3 cc = Vector3.zero; cc[la] = l; cc[wa] = bb.center[wa] + wave; cc[ta] = bb.center[ta];
                                Vector3 a2 = cc + W * half, b2 = cc - W * half;
                                if (has) { int i0 = b.Add(pl), i1 = b.Add(pr), i2 = b.Add(b2), i3 = b.Add(a2); b.Quad(i0, i1, i2, i3); }
                                pl = a2; pr = b2; has = true;
                            }
                            break;
                        }
                    case "curveblade":
                        {
                            // a long curved serrated blade on the head (glaive / sickle)
                            int es = EdgeSign(v, la, wa, ta, bb);
                            Vector3 basep = head - A * (headSign * L * 0.07f);
                            float R = L * 0.20f;
                            Vector3 prev = Vector3.zero, prevIn = Vector3.zero;
                            for (int i = 0; i <= 24; i++)
                            {
                                float k = i / 24f; float th = k * 2.5f;
                                Vector3 p = basep + W * (es * R * Mathf.Sin(th)) + A * (headSign * R * (1f - Mathf.Cos(th)) * 0.9f);
                                Vector3 inward = (basep + W * (es * R * Mathf.Sin(th) * 0.8f) + A * (headSign * R * (1f - Mathf.Cos(th)) * 0.9f)) ;
                                float wd = L * 0.03f * (1f - k * 0.9f);
                                if (i > 0) { int i0 = b.Add(prev), i1 = b.Add(prevIn), i2 = b.Add(inward), i3 = b.Add(p); b.Quad(i0, i1, i2, i3); }
                                if (i % 2 == 1 && i < 23) { int t0 = b.Add(inward), t1 = b.Add(Vector3.Lerp(inward, prevIn, 0.5f)), t2 = b.Add(inward - W * (es * L * 0.018f) - A * (headSign * L * 0.006f)); b.Tri(t0, t1, t2); }
                                prev = p; prevIn = inward;
                            }
                            break;
                        }
                    case "motes":
                        {
                            int n = parts.Length > 2 ? int.Parse(parts[2]) : 3;
                            for (int i = 0; i < n; i++)
                            {
                                float a = Mathf.PI * 2 * i / n;
                                Gem(b, head + A * (headSign * L * 0.02f) + (W * Mathf.Cos(a) + T * Mathf.Sin(a)) * L * 0.06f, L * 0.012f);
                            }
                            break;
                        }
                }
                if (b.V.Count == 0) continue;
                var go = new GameObject("AA_" + kind);
                go.layer = mf.gameObject.layer;
                go.transform.SetParent(root.transform, false);
                go.AddComponent<MeshFilter>().sharedMesh = b.Mesh("AA_" + kind);
                var mr = go.AddComponent<MeshRenderer>();
                mr.sharedMaterial = Mat(baseMat, col, glow);
                mr.shadowCastingMode = ShadowCastingMode.Off;
            }
        }

        // centre of the model's cross-section at position l along the long axis
        private static Vector3 Centre(Vector3[] v, int la, int wa, int ta, float l, float band)
        {
            float ws = 0, ts = 0; int n = 0;
            foreach (var p in v) { if (Mathf.Abs(Get(p, la) - l) > band) continue; ws += Get(p, wa); ts += Get(p, ta); n++; }
            if (n == 0) return Vector3.zero;
            Vector3 r = Vector3.zero; r[la] = l; r[wa] = ws / n; r[ta] = ts / n;
            return r;
        }

        // extreme w at position l (towards sign)
        private static float EdgeW(Vector3[] v, int la, int wa, float l, float band, int sign, float fallback)
        {
            float best = sign > 0 ? float.MinValue : float.MaxValue; bool any = false;
            foreach (var p in v)
            {
                if (Mathf.Abs(Get(p, la) - l) > band) continue;
                float w = Get(p, wa); any = true;
                if (sign > 0 ? w > best : w < best) best = w;
            }
            return any ? best : fallback;
        }

        private static int EdgeSign(Vector3[] v, int la, int wa, int ta, Bounds bb)
        {
            float best = float.MaxValue; int sign = 1;
            foreach (int sg in new[] { -1, 1 })
            {
                float ext = sg > 0 ? bb.max[wa] : bb.min[wa];
                float slab = bb.size[wa] * 0.06f, tmin = float.MaxValue, tmax = float.MinValue; int cnt = 0;
                foreach (var p in v) if (Mathf.Abs(Get(p, wa) - ext) <= slab) { float t = Get(p, ta); if (t < tmin) tmin = t; if (t > tmax) tmax = t; cnt++; }
                if (cnt < 3) continue;
                if (tmax - tmin < best) { best = tmax - tmin; sign = sg; }
            }
            return sign;
        }

        private static float Spread(Vector3[] v, int la, int wa, int ta, float lo, float hi)
        {
            float wmin = float.MaxValue, wmax = float.MinValue, tmin = float.MaxValue, tmax = float.MinValue;
            foreach (var p in v)
            {
                float l = Get(p, la); if (l < lo || l > hi) continue;
                float w = Get(p, wa), t = Get(p, ta);
                if (w < wmin) wmin = w; if (w > wmax) wmax = w; if (t < tmin) tmin = t; if (t > tmax) tmax = t;
            }
            return wmax < wmin ? 0f : (wmax - wmin) + (tmax - tmin);
        }

        private static float Get(Vector3 p, int a) => a == 0 ? p.x : a == 1 ? p.y : p.z;
        private static Vector3 Ax(int a) => a == 0 ? Vector3.right : a == 1 ? Vector3.up : Vector3.forward;

        private static Color Hex(string h)
        {
            if (h.Length == 6 && int.TryParse(h, System.Globalization.NumberStyles.HexNumber, null, out int x))
                return new Color(((x >> 16) & 255) / 255f, ((x >> 8) & 255) / 255f, (x & 255) / 255f, 1f);
            return Color.white;
        }

        private static readonly Dictionary<string, Material> mats = new Dictionary<string, Material>();

        private static Material Mat(Material baseMat, Color c, bool glow)
        {
            string key = ColorUtility.ToHtmlStringRGB(c) + glow;
            if (mats.TryGetValue(key, out var m)) return m;
            m = baseMat != null ? new Material(baseMat) : new Material(Shader.Find("Standard"));
            m.name = "AA_ornament_" + key;
            m.mainTexture = Texture2D.whiteTexture;
            if (m.HasProperty("_Color")) m.SetColor("_Color", c);
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", c);
            if (glow)
            {
                m.EnableKeyword("_EMISSION");
                foreach (var pn in new[] { "_EmissionColor", "_EmissiveColor" }) if (m.HasProperty(pn)) m.SetColor(pn, c * 0.9f);
            }
            mats[key] = m;
            return m;
        }
    }
}
