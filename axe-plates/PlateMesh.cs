using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using HarmonyLib;
using UnityEngine;

namespace AxePlates
{
    // Procedural curved plate with a row of nails, built along the cutting edge of an axe head mesh.
    internal static class PlateMesh
    {
        private class Builder
        {
            public List<Vector3> V = new List<Vector3>();
            public List<Vector2> UV = new List<Vector2>();
            public List<int> T = new List<int>();
            private Vector2 uv = new Vector2(0.5f, 0.5f);
            public int Add(Vector3 p) { V.Add(p); UV.Add(uv); return V.Count - 1; }
            public void Quad(int a, int b, int c, int d)
            {
                T.Add(a); T.Add(b); T.Add(c); T.Add(a); T.Add(c); T.Add(d);
                T.Add(a); T.Add(c); T.Add(b); T.Add(a); T.Add(d); T.Add(c);
            }
            public void Box(Vector3 c, Vector3 e)
            {
                int[] i = new int[8];
                for (int k = 0; k < 8; k++)
                    i[k] = Add(c + new Vector3((k & 1) == 0 ? -e.x : e.x, (k & 2) == 0 ? -e.y : e.y, (k & 4) == 0 ? -e.z : e.z));
                Quad(i[0], i[1], i[3], i[2]); Quad(i[4], i[5], i[7], i[6]);
                Quad(i[0], i[1], i[5], i[4]); Quad(i[2], i[3], i[7], i[6]);
                Quad(i[0], i[2], i[6], i[4]); Quad(i[1], i[3], i[7], i[5]);
            }
            public Mesh ToMesh(string name)
            {
                var m = new Mesh { name = name };
                m.SetVertices(V); m.SetUVs(0, UV); m.SetTriangles(T, 0);
                m.RecalculateNormals(); m.RecalculateBounds();
                return m;
            }
        }

        // One point of the plate: outer edge position, direction towards the blade, thin axis, half thickness of the blade.
        private struct Pt { public Vector3 Outer, Inward, Thin; public float Half; }

        private class Prof
        {
            public List<Pt> Pts = new List<Pt>();
            public float Depth, Nail, Margin;
            public string Why;
            public bool Pending;
            public float Since;
            public bool Ok => !Pending && Pts.Count >= 3 && Why == null;
        }

        private static readonly Dictionary<Mesh, Prof> profCache = new Dictionary<Mesh, Prof>();
        private static readonly HashSet<string> warned = new HashSet<string>();

        private static float Ax(Vector3 p, int a) => a == 0 ? p.x : a == 1 ? p.y : p.z;
        private static Vector3 AxV(int a) => a == 0 ? Vector3.right : a == 1 ? Vector3.up : Vector3.forward;

        public static bool LastPending;

        private static Prof Analyze(Mesh m)
        {
            if (profCache.TryGetValue(m, out var cached)) return cached;
            var P = new Prof();
            profCache[m] = P;
            try
            {
                if (m.isReadable) Compute(P, m, m.vertices);
                else StartReadback(P, m);
            }
            catch (Exception ex) { P.Why = ex.Message; P.Pending = false; }
            return P;
        }

        // Non-readable meshes: copy the position stream back from the GPU vertex buffer.
        private static void StartReadback(Prof P, Mesh m)
        {
            int stream = m.GetVertexAttributeStream(UnityEngine.Rendering.VertexAttribute.Position);
            int offset = m.GetVertexAttributeOffset(UnityEngine.Rendering.VertexAttribute.Position);
            var fmt = m.GetVertexAttributeFormat(UnityEngine.Rendering.VertexAttribute.Position);
            int dim = m.GetVertexAttributeDimension(UnityEngine.Rendering.VertexAttribute.Position);
            int stride = m.GetVertexBufferStride(stream);
            int count = m.vertexCount;
            if (fmt != UnityEngine.Rendering.VertexAttributeFormat.Float32 || dim < 3) { P.Why = "unsupported position format " + fmt + "x" + dim; return; }
            var gb = m.GetVertexBuffer(stream);
            P.Pending = true; P.Since = Time.realtimeSinceStartup;
            Plugin.Log.LogInfo($"[plate] readback start {m.name} verts={count} stride={stride} offset={offset}");
            UnityEngine.Rendering.AsyncGPUReadback.Request(gb, req =>
            {
                try
                {
                    if (req.hasError) { P.Why = "gpu readback failed"; return; }
                    var data = req.GetData<byte>();
                    var bytes = data.ToArray();
                    var v = new Vector3[count];
                    for (int i = 0; i < count; i++)
                    {
                        int o = i * stride + offset;
                        v[i] = new Vector3(BitConverter.ToSingle(bytes, o), BitConverter.ToSingle(bytes, o + 4), BitConverter.ToSingle(bytes, o + 8));
                    }
                    P.Pending = false;
                    Compute(P, m, v);
                }
                catch (Exception ex) { P.Why = "readback: " + ex.Message; }
                finally { P.Pending = false; try { gb.Dispose(); } catch { } }
            });
        }

        // ---- skinned (equipped) model: plates are parented to the hand bones ----
        private static readonly Dictionary<Mesh, Vector3[]> vertCache = new Dictionary<Mesh, Vector3[]>();
        private static readonly HashSet<Mesh> reading = new HashSet<Mesh>();
        private static readonly HashSet<Mesh> readFailed = new HashSet<Mesh>();
        private static readonly Dictionary<(Mesh, int), Mesh> boneMeshes = new Dictionary<(Mesh, int), Mesh>();

        private static void ReadVerts(Mesh m)
        {
            if (!reading.Add(m)) return;
            try
            {
                int stream = m.GetVertexAttributeStream(UnityEngine.Rendering.VertexAttribute.Position);
                int offset = m.GetVertexAttributeOffset(UnityEngine.Rendering.VertexAttribute.Position);
                var fmt = m.GetVertexAttributeFormat(UnityEngine.Rendering.VertexAttribute.Position);
                int stride = m.GetVertexBufferStride(stream);
                int count = m.vertexCount;
                if (fmt != UnityEngine.Rendering.VertexAttributeFormat.Float32) { readFailed.Add(m); Plugin.Log.LogWarning("[plate] unsupported position format " + fmt); return; }
                var gb = m.GetVertexBuffer(stream);
                Plugin.Log.LogInfo($"[plate] skinned readback start {m.name} verts={count} stride={stride} offset={offset}");
                UnityEngine.Rendering.AsyncGPUReadback.Request(gb, req =>
                {
                    try
                    {
                        if (req.hasError) { readFailed.Add(m); Plugin.Log.LogWarning("[plate] skinned readback failed"); return; }
                        var bytes = req.GetData<byte>().ToArray();
                        var v = new Vector3[count];
                        for (int i = 0; i < count; i++)
                        {
                            int o = i * stride + offset;
                            v[i] = new Vector3(BitConverter.ToSingle(bytes, o), BitConverter.ToSingle(bytes, o + 4), BitConverter.ToSingle(bytes, o + 8));
                        }
                        vertCache[m] = v;
                    }
                    catch (Exception ex) { readFailed.Add(m); Plugin.Log.LogWarning("[plate] skinned readback: " + ex.Message); }
                    finally { try { gb.Dispose(); } catch { } }
                });
            }
            catch (Exception ex) { readFailed.Add(m); Plugin.Log.LogWarning("[plate] skinned readback start: " + ex.Message); }
        }

        private static void ApplySkinned(SkinnedMeshRenderer smr, PlateDef d, List<GameObject> made)
        {
            var mesh = smr.sharedMesh;
            if (mesh == null) return;
            Vector3[] v;
            if (mesh.isReadable) v = mesh.vertices;
            else if (!vertCache.TryGetValue(mesh, out v))
            {
                if (readFailed.Contains(mesh)) { if (warned.Add("sk" + mesh.name)) Plugin.Log.LogWarning("[plate] no vertex data for skinned mesh " + mesh.name); return; }
                ReadVerts(mesh); LastPending = true; return;
            }
            var bones = smr.bones; var bps = mesh.bindposes;
            var hands = new List<int>();
            for (int i = 0; i < bones.Length && i < bps.Length; i++)
                if (bones[i] != null && (bones[i].name == "LeftHand" || bones[i].name == "RightHand")) hands.Add(i);
            if (hands.Count == 0) { if (warned.Add("hb" + mesh.name)) Plugin.Log.LogWarning("[plate] no hand bones found on " + mesh.name); return; }
            var origins = new List<Vector3>();
            foreach (int hi in hands) origins.Add(bps[hi].inverse.MultiplyPoint3x4(Vector3.zero));
            var clusters = new List<List<Vector3>>();
            foreach (var _ in hands) clusters.Add(new List<Vector3>());
            foreach (var p in v)
            {
                int best = 0; float bd = float.MaxValue;
                for (int k = 0; k < origins.Count; k++) { float dd = (p - origins[k]).sqrMagnitude; if (dd < bd) { bd = dd; best = k; } }
                clusters[best].Add(p);
            }
            // refine: split the mesh into separate axes by k-means seeded at the hand bones
            for (int it = 0; it < 4 && hands.Count > 1; it++)
            {
                var cen = new Vector3[hands.Count];
                for (int k = 0; k < hands.Count; k++)
                {
                    if (clusters[k].Count == 0) { cen[k] = origins[k]; continue; }
                    Vector3 s = Vector3.zero; foreach (var p in clusters[k]) s += p; cen[k] = s / clusters[k].Count;
                }
                foreach (var c in clusters) c.Clear();
                foreach (var p in v)
                {
                    int best = 0; float bd = float.MaxValue;
                    for (int k = 0; k < cen.Length; k++) { float dd = (p - cen[k]).sqrMagnitude; if (dd < bd) { bd = dd; best = k; } }
                    clusters[best].Add(p);
                }
            }
            for (int k = 0; k < hands.Count; k++)
            {
                int hi = hands[k];
                var key = (mesh, hi);
                if (!boneMeshes.TryGetValue(key, out var bm))
                {
                    bm = null;
                    if (clusters[k].Count >= 12)
                    {
                        var P = new Prof();
                        Compute(P, mesh, clusters[k].ToArray());
                        if (P.Ok)
                        {
                            var M = bps[hi];
                            float sc = M.MultiplyVector(Vector3.right).magnitude;
                            for (int i = 0; i < P.Pts.Count; i++)
                            {
                                var q = P.Pts[i];
                                P.Pts[i] = new Pt { Outer = M.MultiplyPoint3x4(q.Outer), Inward = M.MultiplyVector(q.Inward).normalized, Thin = M.MultiplyVector(q.Thin).normalized, Half = q.Half * sc };
                            }
                            P.Depth *= sc; P.Margin *= sc; P.Nail *= sc;
                            bm = BuildMesh(P, 2);
                            Plugin.Log.LogInfo($"[plate] skinned plate built for bone {bones[hi].name} ({clusters[k].Count} verts)");
                        }
                        else Plugin.Log.LogWarning($"[plate] skinned cluster {bones[hi].name}: {P.Why ?? "edge not found"}");
                    }
                    boneMeshes[key] = bm;
                }
                if (bm == null) continue;
                var go = new GameObject(PlateName);
                go.transform.SetParent(bones[hi], false);
                go.AddComponent<MeshFilter>().sharedMesh = bm;
                go.AddComponent<MeshRenderer>().sharedMaterial = MatFor(d, smr);
                made.Add(go);
            }
        }

        // No vertex data: a plate along one side of the mesh bounds (variant 0..3 flips side / swaps axes).
        private static void Fallback(Prof P, Mesh m)
        {
            P.Pts.Clear(); P.Why = null;
            Bounds b = m.bounds; Vector3 s = b.size;
            int at = 0; if (s.y < s[at]) at = 1; if (s.z < s[at]) at = 2;
            int a1 = (at + 1) % 3, a2 = (at + 2) % 3;
            int v = Plugin.FallbackVariant.Value;
            int aw = (v & 2) == 0 ? a1 : a2, al = aw == a1 ? a2 : a1;
            int sign = (v & 1) == 0 ? 1 : -1;
            float wExt = sign > 0 ? b.max[aw] : b.min[aw];
            float longest = Mathf.Max(s.x, Mathf.Max(s.y, s.z));
            P.Depth = Mathf.Clamp(s[aw] * 0.18f, longest * 0.01f, longest * 0.3f);
            P.Margin = longest * 0.012f; P.Nail = longest * 0.016f;
            const int N = 7;
            for (int i = 0; i < N; i++)
            {
                float l = Mathf.Lerp(b.min[al] + s[al] * 0.1f, b.max[al] - s[al] * 0.1f, i / (float)(N - 1));
                Vector3 outer = AxV(al) * l + AxV(aw) * (wExt + sign * P.Margin * 0.5f) + AxV(at) * b.center[at];
                P.Pts.Add(new Pt { Outer = outer, Inward = AxV(aw) * (-sign), Thin = AxV(at), Half = Mathf.Max(s[at] * 0.5f * 0.5f, longest * 0.004f) });
            }
        }

        private static void Compute(Prof P, Mesh m, Vector3[] v)
        {
            try
            {
                if (v.Length < 12) { P.Why = "too few vertices"; return; }
                Bounds b = new Bounds(v[0], Vector3.zero);
                foreach (var q in v) b.Encapsulate(q);
                Vector3 size = b.size;
                int at = 0;
                if (size.y < size[at]) at = 1;
                if (size.z < size[at]) at = 2;
                int a1 = (at + 1) % 3, a2 = (at + 2) % 3;
                // thickness of the 6% slab at each of the four extremes of the two in-plane axes
                int bestAxis = -1, bestSign = 0; float bestThick = float.MaxValue;
                foreach (int a in new[] { a1, a2 })
                    foreach (int sg in new[] { -1, 1 })
                    {
                        float ext = sg > 0 ? b.max[a] : b.min[a];
                        float slab = size[a] * 0.06f;
                        float tmin = float.MaxValue, tmax = float.MinValue; int cnt = 0;
                        foreach (var p in v)
                            if (Mathf.Abs(Ax(p, a) - ext) <= slab) { float t = Ax(p, at); if (t < tmin) tmin = t; if (t > tmax) tmax = t; cnt++; }
                        if (cnt < 3) continue;
                        float th = tmax - tmin;
                        if (th < bestThick) { bestThick = th; bestAxis = a; bestSign = sg; }
                    }
                if (bestAxis < 0) { P.Why = "no edge found"; return; }
                int aw = bestAxis, al = (aw == a1) ? a2 : a1;
                float wExt = bestSign > 0 ? b.max[aw] : b.min[aw];
                float wRange = size[aw], lMin = b.min[al], lMax = b.max[al], lRange = size[al];
                const int N = 18;
                var e = new float[N]; var have = new bool[N]; var hc = new float[N]; var hh = new float[N];
                for (int i = 0; i < N; i++)
                {
                    float lo = lMin + lRange * i / N, hi = lMin + lRange * (i + 1) / N;
                    float best = bestSign > 0 ? float.MinValue : float.MaxValue; bool any = false;
                    foreach (var p in v)
                    {
                        float l = Ax(p, al); if (l < lo || l > hi) continue;
                        float w = Ax(p, aw); any = true;
                        if (bestSign > 0 ? w > best : w < best) best = w;
                    }
                    if (!any) continue;
                    e[i] = best; have[i] = true;
                    float tmin = float.MaxValue, tmax = float.MinValue;
                    foreach (var p in v)
                    {
                        float l = Ax(p, al); if (l < lo || l > hi) continue;
                        if (Mathf.Abs(Ax(p, aw) - best) > wRange * 0.12f) continue;
                        float t = Ax(p, at); if (t < tmin) tmin = t; if (t > tmax) tmax = t;
                    }
                    hc[i] = (tmin + tmax) * 0.5f; hh[i] = (tmax - tmin) * 0.5f;
                }
                // keep the longest run of slices that lie near the global edge line
                float thr = wRange * 0.30f;
                int bs = -1, bl = 0, cs = -1, cl = 0;
                for (int i = 0; i < N; i++)
                {
                    bool near = have[i] && Mathf.Abs(e[i] - wExt) <= thr;
                    if (near) { if (cs < 0) { cs = i; cl = 0; } cl++; if (cl > bl) { bl = cl; bs = cs; } } else cs = -1;
                }
                if (bl < 3) { P.Why = "edge run too short (" + bl + ")"; return; }
                // smooth
                var sm = new float[N];
                for (int i = bs; i < bs + bl; i++)
                {
                    float s = 0; int c = 0;
                    for (int k = i - 1; k <= i + 1; k++) if (k >= bs && k < bs + bl) { s += e[k]; c++; }
                    sm[i] = s / c;
                }
                float longest = Mathf.Max(size.x, Mathf.Max(size.y, size.z));
                P.Depth = Mathf.Clamp(wRange * 0.10f, longest * 0.01f, longest * 0.12f);
                P.Margin = longest * 0.008f;
                P.Nail = longest * 0.012f;
                for (int i = bs; i < bs + bl; i++)
                {
                    float l = lMin + lRange * (i + 0.5f) / N;
                    Vector3 outer = AxV(al) * l + AxV(aw) * (sm[i] + bestSign * P.Margin * 0.5f) + AxV(at) * hc[i];
                    P.Pts.Add(new Pt { Outer = outer, Inward = AxV(aw) * (-bestSign), Thin = AxV(at), Half = Mathf.Max(hh[i], longest * 0.004f) });
                }
                Plugin.Log.LogInfo($"[plate] analysed mesh {m.name}: size={size} thin={at} wide={aw}({bestSign}) long={al} slices={bl} edgeThickness={bestThick:0.0000}");
            }
            catch (Exception ex) { P.Why = ex.Message; }
            return;
        }

        private static Mesh BuildMesh(Prof P, int nailEvery)
        {
            var B = new Builder();
            int n = P.Pts.Count;
            var A = new int[n]; var Bq = new int[n]; var C = new int[n]; var D = new int[n];
            for (int i = 0; i < n; i++)
            {
                var p = P.Pts[i];
                float h = p.Half + P.Margin;
                Vector3 inner = p.Outer + p.Inward * P.Depth;
                A[i] = B.Add(inner + p.Thin * h); Bq[i] = B.Add(p.Outer + p.Thin * h);
                C[i] = B.Add(p.Outer - p.Thin * h); D[i] = B.Add(inner - p.Thin * h);
            }
            for (int i = 0; i + 1 < n; i++)
            {
                B.Quad(A[i], Bq[i], Bq[i + 1], A[i + 1]);
                B.Quad(Bq[i], C[i], C[i + 1], Bq[i + 1]);
                B.Quad(C[i], D[i], D[i + 1], C[i + 1]);
            }
            B.Quad(A[0], Bq[0], C[0], D[0]);
            B.Quad(A[n - 1], Bq[n - 1], C[n - 1], D[n - 1]);
            for (int i = 0; i < n; i += Math.Max(1, nailEvery))
            {
                var p = P.Pts[i];
                float h = p.Half + P.Margin;
                Vector3 mid = p.Outer + p.Inward * (P.Depth * 0.5f);
                Vector3 e = new Vector3(P.Nail, P.Nail, P.Nail) * 0.5f;
                B.Box(mid + p.Thin * (h + P.Nail * 0.35f), e);
                B.Box(mid - p.Thin * (h + P.Nail * 0.35f), e);
            }
            return B.ToMesh("AxePlateEdge");
        }

        private static readonly Dictionary<(Mesh, int), Mesh> meshCache = new Dictionary<(Mesh, int), Mesh>();

        private static Material MatFor(PlateDef d, Renderer sample)
        {
            if (d.Inst != null) return d.Inst;
            Material src = d.Mat;
            if (src == null && sample != null) src = sample.sharedMaterial;
            if (src != null) d.Inst = d.TintMaterial ? Icons.TintedMaterial(src, d.Tint) : new Material(src);
            else
            {
                var sh = Shader.Find("Standard");
                d.Inst = new Material(sh) { color = d.Tint };
            }
            if (d.Inst != null && d.Emit.a > 0f)
            {
                d.Inst.EnableKeyword("_EMISSION");
                foreach (var pn in new[] { "_EmissionColor", "_EmissiveColor" })
                    if (d.Inst.HasProperty(pn)) d.Inst.SetColor(pn, d.Emit * 0.6f);
            }
            return d.Inst;
        }

        public const string PlateName = "AxePlatesMesh";

        // Attach plates to all head meshes below root. Returns created objects.
        public static List<GameObject> Apply(Transform root, PlateDef d)
        {
            var made = new List<GameObject>();
            LastPending = false;
            if (root == null || d == null) return made;
            var skinned = root.GetComponentsInChildren<SkinnedMeshRenderer>(true);
            if (skinned.Length > 0)
            {
                foreach (var smr in skinned) ApplySkinned(smr, d, made);
                return made;
            }
            var all = new List<MeshFilter>();
            var heads = new List<MeshFilter>();
            foreach (var mf in root.GetComponentsInChildren<MeshFilter>(true))
            {
                if (mf.sharedMesh == null || mf.name == PlateName) continue;
                if (mf.GetComponent<Renderer>() == null) continue;
                all.Add(mf);
                string nm = (mf.name + " " + mf.sharedMesh.name).ToLowerInvariant();
                if (nm.Contains("head") || nm.Contains("blade")) heads.Add(mf);
            }
            var targets = heads.Count > 0 ? heads : all;
            foreach (var mf in targets)
            {
                var prof = Analyze(mf.sharedMesh);
                if (prof.Pending && Time.realtimeSinceStartup - prof.Since > 2f)
                {
                    prof.Pending = false;
                    Plugin.Log.LogWarning("[plate] gpu readback timed out, using bounds-based placement for " + mf.sharedMesh.name);
                    Fallback(prof, mf.sharedMesh);
                }
                if (prof.Pending) { LastPending = true; continue; }
                if (!prof.Ok && prof.Why != null && prof.Why.StartsWith("readback") || prof.Why == "gpu readback failed") { Plugin.Log.LogWarning("[plate] " + prof.Why + ", using bounds-based placement"); prof.Why = null; Fallback(prof, mf.sharedMesh); }
                if (!prof.Ok)
                {
                    if (warned.Add(mf.sharedMesh.name)) Plugin.Log.LogWarning($"[plate] cannot attach to mesh '{mf.sharedMesh.name}' ({mf.name}): {prof.Why}");
                    continue;
                }
                var key = (mf.sharedMesh, 2);
                if (!meshCache.TryGetValue(key, out var mesh) || mesh == null) { mesh = BuildMesh(prof, 2); meshCache[key] = mesh; }
                var go = new GameObject(PlateName);
                go.layer = mf.gameObject.layer;
                go.transform.SetParent(mf.transform, false);
                go.AddComponent<MeshFilter>().sharedMesh = mesh;
                var r = go.AddComponent<MeshRenderer>();
                r.sharedMaterial = MatFor(d, mf.GetComponent<Renderer>());
                made.Add(go);
            }
            return made;
        }

        public static void AttachMouldModel(GameObject go, Material baseMat)
        {
            var B = new Builder();
            B.Box(Vector3.zero, new Vector3(0.09f, 0.016f, 0.065f));
            var mesh = B.ToMesh("AxePlateMould");
            var uv = new List<Vector2>();
            foreach (var p in mesh.vertices) uv.Add(new Vector2(p.x / 0.18f + 0.5f, p.z / 0.13f + 0.5f));
            mesh.SetUVs(0, uv);
            var child = new GameObject("AxePlateMouldModel");
            child.transform.SetParent(go.transform, false);
            child.AddComponent<MeshFilter>().sharedMesh = mesh;
            var r = child.AddComponent<MeshRenderer>();
            var mat = baseMat != null ? new Material(baseMat) : new Material(Shader.Find("Standard"));
            mat.mainTexture = Icons.MouldTexture();
            if (mat.HasProperty("_Color")) mat.SetColor("_Color", Color.white);
            r.sharedMaterial = mat;
        }

        // Model for the kit item itself (replaces the borrowed nails model).
        public static void AttachKitModel(GameObject kit, PlateDef d)
        {
            var P = new Prof { Depth = 0.028f, Margin = 0.002f, Nail = 0.0065f };
            const int steps = 9;
            for (int i = 0; i < steps; i++)
            {
                float a = Mathf.Lerp(-55f, 55f, i / (float)(steps - 1)) * Mathf.Deg2Rad;
                const float R = 0.11f;
                Vector3 outer = new Vector3(Mathf.Cos(a) * R - R, Mathf.Sin(a) * R, 0);
                Vector3 inward = new Vector3(-Mathf.Cos(a), -Mathf.Sin(a), 0);
                P.Pts.Add(new Pt { Outer = outer, Inward = inward, Thin = Vector3.forward, Half = 0.0025f });
            }
            var go = new GameObject("AxePlateModel");
            go.transform.SetParent(kit.transform, false);
            go.transform.localPosition = new Vector3(0.05f, 0, 0);
            go.AddComponent<MeshFilter>().sharedMesh = BuildMesh(P, 1);
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterial = MatFor(d, null);
        }
    }

    // ---- visuals in hand (owner publishes material in the ZDO, every client builds the mesh) ----

    [HarmonyPatch(typeof(VisEquipment), "UpdateEquipmentVisuals")]
    internal static class VisPatch
    {
        private const string ZKey = "axeplates.r";

        private class State { public GameObject Inst, LInst; public List<GameObject> LPlates = new List<GameObject>(); public string LMat = ""; public bool LTried; public string Mat = ""; public List<GameObject> Plates = new List<GameObject>(); public bool Tried; }
        private static readonly ConditionalWeakTable<VisEquipment, State> states = new ConditionalWeakTable<VisEquipment, State>();
        private static readonly AccessTools.FieldRef<VisEquipment, ZNetView> nviewRef = AccessTools.FieldRefAccess<VisEquipment, ZNetView>("m_nview");
        private static readonly AccessTools.FieldRef<Humanoid, VisEquipment> visRef = AccessTools.FieldRefAccess<Humanoid, VisEquipment>("m_visEquipment");
        private static readonly AccessTools.FieldRef<VisEquipment, GameObject> leftRef = AccessTools.FieldRefAccess<VisEquipment, GameObject>("m_leftItemInstance");
        private static readonly AccessTools.FieldRef<Humanoid, ItemDrop.ItemData> leftItemRef = AccessTools.FieldRefAccess<Humanoid, ItemDrop.ItemData>("m_leftItem");
        private static readonly AccessTools.FieldRef<VisEquipment, GameObject> rightRef = AccessTools.FieldRefAccess<VisEquipment, GameObject>("m_rightItemInstance");

        private static void Kill(List<GameObject> l)
        {
            foreach (var g in l) if (g != null) UnityEngine.Object.Destroy(g);
            l.Clear();
        }

        private static void Postfix(VisEquipment __instance)
        {
            try
            {
                var nv = nviewRef(__instance);
                if (nv == null || !nv.IsValid()) return;
                var zdo = nv.GetZDO();
                if (zdo == null) return;
                var lp = Player.m_localPlayer;
                if (lp != null && visRef(lp) == __instance && nv.IsOwner())
                {
                    var d = Plugin.Enabled.Value ? Plates.Of(lp.GetCurrentWeapon()) : null;
                    string want = d != null ? d.Id : "";
                    if (zdo.GetString(ZKey, "") != want) zdo.Set(ZKey, want);
                    var dl = Plugin.Enabled.Value ? Plates.Of(leftItemRef(lp)) : null;
                    string wantL = dl != null ? dl.Id : "";
                    if (zdo.GetString(ZKey + "l", "") != wantL) zdo.Set(ZKey + "l", wantL);
                }
                string mat = Plugin.Enabled.Value ? zdo.GetString(ZKey, "") : "";
                GameObject inst = rightRef(__instance);
                var st = states.GetOrCreateValue(__instance);
                if (st.Inst != inst) { st.Inst = inst; Kill(st.Plates); st.Mat = ""; st.Tried = false; }
                GameObject linst = leftRef(__instance);
                if (linst != null && !Plates.IsTargetPrefab(Utils.GetPrefabName(linst), null)) linst = null;
                string lmat = Plugin.Enabled.Value ? zdo.GetString(ZKey + "l", "") : "";
                if (lmat == "") lmat = mat;
                if (st.LInst != linst) { st.LInst = linst; Kill(st.LPlates); st.LMat = ""; st.LTried = false; }
                if (!(st.LMat == lmat && (st.LTried || lmat == "")))
                {
                    foreach (var g in st.LPlates) if (g != null) UnityEngine.Object.Destroy(g);
                    st.LPlates.Clear();
                    st.LMat = lmat; st.LTried = true;
                    if (lmat != "" && linst != null && PlateTable.ById.TryGetValue(lmat, out var ldef)) { st.LPlates = PlateMesh.Apply(linst.transform, ldef); if (PlateMesh.LastPending) { st.LMat = ""; st.LTried = false; } }
                }
                if (st.Mat == mat && (st.Tried || mat == "")) return;
                foreach (var g in st.Plates) if (g != null) UnityEngine.Object.Destroy(g);
                st.Plates.Clear();
                st.Mat = mat; st.Tried = true;
                if (mat == "" || inst == null) return;
                Plugin.Log.LogInfo($"[plate] vis: material {mat} on {inst.name}");
                if (PlateTable.ById.TryGetValue(mat, out var def)) { st.Plates = PlateMesh.Apply(inst.transform, def); if (PlateMesh.LastPending) { st.Mat = ""; st.Tried = false; } }
            }
            catch (Exception e) { Plugin.Log.LogWarning("vis patch: " + e.Message); }
        }
    }

    // ---- visuals on dropped items ----

    [HarmonyPatch(typeof(ItemDrop), "Awake")]
    internal static class DropAwake
    {
        private static void Postfix(ItemDrop __instance)
        {
            var d = __instance.m_itemData;
            if (d == null || d.m_shared == null) return;
            if (Plates.IsTargetPrefab(Utils.GetPrefabName(__instance.gameObject), d.m_shared) && __instance.GetComponent<PlateOnDrop>() == null)
                __instance.gameObject.AddComponent<PlateOnDrop>();
        }
    }

    internal class PlateOnDrop : MonoBehaviour
    {
        private ItemDrop drop;
        private string mat = "";
        private List<GameObject> made = new List<GameObject>();
        private float next;

        private void Awake() { drop = GetComponent<ItemDrop>(); }

        private void Update()
        {
            if (Time.time < next) return;
            next = Time.time + 0.7f;
            if (drop == null || drop.m_itemData == null) return;
            string want = "";
            var d = Plates.Of(drop.m_itemData);
            if (d != null) want = d.Id;
            if (want == mat) return;
            foreach (var g in made) if (g != null) Destroy(g);
            made.Clear();
            mat = want;
            if (d != null) { made = PlateMesh.Apply(transform, d); if (PlateMesh.LastPending) mat = ""; }
        }
    }
}
