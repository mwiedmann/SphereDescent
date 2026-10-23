using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace SphereDescent
{
    public static partial class LevelBuilder
    {
        /// <summary>
        /// The core's boss battle (see <see cref="CoreLayout"/>): a ball of boiling lava, the ground standing up
        /// out of it as hilly paths and two round plazas (floored like the shells), the energy ball trapped in
        /// rings of glowing walls on one plaza, a key pad per wall with its hidden cable, energy cubes and mines,
        /// and the lava rain.
        /// </summary>
        static void CreateCore(LevelInstance level, MazeLayout layout, LevelSettings s, Transform root, LevelMaterials mats, VisualStyle style)
        {
            var core = layout.Core;
            int layer = layout.ShellCount;
            int scoreIndex = layout.Sections.Count; // the core scores after the last section

            // The lava: the acid's boiling surface, in reds and oranges.
            var lavaMat = level.Own(new Material(mats.acid));
            lavaMat.SetColor("_Deep", new Color(0.3f, 0.03f, 0f));
            lavaMat.SetColor("_Glow", new Color(1f, 0.36f, 0.04f));
            lavaMat.SetFloat("_Intensity", 1.7f);
            lavaMat.SetFloat("_Scale", 3.5f);
            lavaMat.SetFloat("_BubbleSize", 1.7f);
            lavaMat.SetFloat("_BubbleAmount", 0.4f);
            var lavaMesh = level.Own(MeshBuilders.BuildSphere(IcoSphere.Get(s.coreMeshSubdivisions), core.LavaRadius));
            CreateMeshObject(root, "Lava", lavaMesh, lavaMat, false, ShadowCastingMode.Off);

            // The ground, leaving out what lies wholly under the lava.
            var ico = IcoSphere.Get(6);
            var verts = new List<Vector3>(ico.Vertices.Length);
            var sunk = new bool[ico.Vertices.Length];
            float bottom = core.LavaRadius - CoreLayout.LavaDepth;
            for (int i = 0; i < ico.Vertices.Length; i++)
            {
                float r = core.SurfaceRadius(ico.Vertices[i]);
                sunk[i] = r <= bottom + 1e-3f;
                verts.Add(ico.Vertices[i] * r);
            }
            var tris = new List<int>();
            for (int t = 0; t < ico.Triangles.Length; t += 3)
            {
                int a = ico.Triangles[t], b = ico.Triangles[t + 1], c = ico.Triangles[t + 2];
                if (sunk[a] && sunk[b] && sunk[c]) continue;
                tris.Add(a); tris.Add(b); tris.Add(c);
            }
            var ground = level.Own(new Mesh { name = "Core Ground", indexFormat = IndexFormat.UInt32 });
            ground.SetVertices(verts);
            ground.SetTriangles(tris, 0);
            ground.RecalculateNormals();
            ground.RecalculateBounds();
            var groundMat = level.Own(ZoneMaterial(mats.ground, GroundColor(layer), 3f, style.groundZoneMix, style));
            CreateMeshObject(root, "Core Ground", ground, groundMat, true, ShadowCastingMode.Off);

            // The walls round the energy ball: see-through glowing energy with a bright rim along the top,
            // magenta on the outside through violet to blue at the middle, rising taller toward the middle so
            // every ring shows.
            float G = core.GroundRadius;
            Vector3 cageUp = core.Cage;
            var glowTemplate = mats.beam;
            var sparkMat = level.Own(LevelMaterials.MakeSpark());
            int rings = core.RingRadii.Length;
            var barriers = new CoreBarrier[rings];
            for (int i = 0; i < rings; i++)
            {
                float height = 2.6f + (rings - 1 - i) * 0.45f;
                Color color = Color.HSVToRGB(Mathf.Lerp(0.62f, 0.88f, rings > 1 ? i / (rings - 1f) : 1f), 0.85f, 1f);
                var mat = level.Own(LevelMaterials.MakeEmissive(color, 2.4f));
                var field = level.Own(LevelMaterials.MakeGlass(color, 0.3f, 0.9f));
                var go = new GameObject($"Core Wall {i + 1}");
                go.transform.SetParent(root, false);
                go.transform.SetPositionAndRotation(cageUp * G, Quaternion.FromToRotation(Vector3.up, cageUp));
                var wall = CreateMeshObject(go.transform, "Wall", level.Own(RingWallMesh(cageUp, core.RingRadii[i], 0.32f, G - 0.4f, G + height, G)),
                    mat, true, ShadowCastingMode.On);
                wall.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
                wall.GetComponent<MeshRenderer>().sharedMaterials = new[] { field, mat };
                var barrier = go.AddComponent<CoreBarrier>();
                barrier.wall = wall;
                barrier.material = mat;
                barrier.color = color;
                barrier.phase = i * 0.7f;
                barrier.ringRadius = core.RingRadii[i];
                barrier.height = height;
                barrier.glow = glowTemplate;
                barrier.sparkMaterial = sparkMat;
                barriers[i] = barrier;
            }

            // The energy ball in the middle.
            var orbColor = new Color(0.45f, 0.95f, 1f);
            var orbGo = new GameObject("Energy Ball");
            orbGo.transform.SetParent(root, false);
            orbGo.transform.SetPositionAndRotation(cageUp * G, Quaternion.FromToRotation(Vector3.up, cageUp));
            var orbMat = level.Own(LevelMaterials.MakeEmissive(Color.Lerp(orbColor, Color.white, 0.5f), 5f));
            var body = Part(orbGo.transform, PrimitiveType.Sphere, "Body", orbMat);
            // High enough that it never dips into the ground as it bobs.
            body.localPosition = new Vector3(0f, 2.6f, 0f);
            var haloMat = level.Own(new Material(glowTemplate));
            haloMat.SetFloat("_Soft", 1f);
            haloMat.SetColor("_Color", orbColor);
            haloMat.SetFloat("_Intensity", 2.2f);
            var halo = CreateMeshObject(orbGo.transform, "Glow", level.Own(MeshBuilders.BuildDisc(0.5f, 32)), haloMat, false, ShadowCastingMode.Off);
            var orb = orbGo.AddComponent<EnergyOrb>();
            orb.body = body;
            orb.halo = halo.transform;
            orb.material = orbMat;
            orb.color = Color.Lerp(orbColor, Color.white, 0.5f);
            orb.trailMaterial = sparkMat;

            // Key pads, each with its cable to its wall.
            var pads = new SwitchPadKit(level);
            var pressable = new List<LaserSwitch>();
            for (int p = 0; p < core.Pads.Length; p++)
            {
                Vector3 dir = core.Pads[p];
                var sw = pads.Create(level, root, "Key Pad", dir * core.SurfaceRadius(dir), dir);
                sw.points = s.keyPadPoints;
                sw.section = scoreIndex;
                // The cable creeps back to the walls, a little faster than a laser switch's (9).
                sw.cableSpeed = 9.8f;
                var cableMesh = level.Own(RevealedStrip(core, core.Cables[p], 0.26f, out int[] cableTriangles, out float[] cableDistances,
                    out float[] wallDistances));
                var cable = CreateMeshObject(root, "Cable", cableMesh, pads.cable, false, ShadowCastingMode.Off);
                sw.cable = cable.GetComponent<MeshFilter>().sharedMesh;
                sw.cableTriangles = cableTriangles;
                sw.cableDistances = cableDistances;
                sw.wallDistances = wallDistances;
                pressable.Add(sw);
            }

            var cubes = new CubeKit(level, glowTemplate);
            foreach (var d in core.Cubes) cubes.Create(level, s, root, d * core.SurfaceRadius(d), d, false, scoreIndex);
            var mines = new MineKit(level, s, glowTemplate);
            foreach (var d in core.Mines) mines.Create(level, s, root, d * core.SurfaceRadius(d), core.Normal(d));

            var arena = root.gameObject.AddComponent<CoreArena>();
            arena.barriers = barriers;
            arena.orb = orb;
            arena.lavaRadius = core.LavaRadius;
            arena.layer = layer;

            // Lava raining down on the ball until the energy ball is freed.
            var rain = new GameObject("Lava Rain").AddComponent<LavaRain>();
            rain.transform.SetParent(root, false);
            rain.arena = arena;
            rain.core = core;
            rain.layer = layer;
            rain.interval = new Vector2(s.lavaRainIntervalMin, s.lavaRainIntervalMax);
            rain.fallTime = s.lavaBlobFallTime;
            rain.splashRadius = s.lavaBlobSplashRadius;
            var sphere = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            rain.blobMesh = sphere.GetComponent<MeshFilter>().sharedMesh;
            Object.DestroyImmediate(sphere);
            rain.blobMaterial = level.Own(LevelMaterials.MakeEmissive(new Color(1f, 0.32f, 0.03f), 3.5f));
            rain.shadowMaterial = level.Own(LevelMaterials.MakeGlass(Color.black, 0.6f, 0f));
            var rimMat = level.Own(new Material(mats.spot));
            rimMat.SetColor("_Color", new Color(1f, 0.12f, 0.02f));
            rimMat.SetFloat("_Intensity", 1.4f);
            rain.rimMaterial = rimMat;
            rain.glowMaterial = glowTemplate;
            arena.cage = core.Cage;
            arena.cageRadius = core.CageRadius;
            arena.groundRadius = G;
            // Where the ball comes back once it has reached the plaza: just inside its edge, where the path from
            // the landing comes in.
            arena.plazaCheckpoint = core.Paths[0][0] * (G + 0.5f);
            orb.arena = arena;
            foreach (var sw in pressable) sw.arena = arena;
        }

        /// <summary>
        /// A round wall standing on the sphere around <paramref name="center"/>: <paramref name="ringRadius"/>
        /// units out along the ground of radius <paramref name="ground"/>, from <paramref name="baseRadius"/> up to
        /// <paramref name="topRadius"/> (distances from the world's center), with a flat top. The sides are the
        /// first submesh and the top the second (so the rim can glow). Built in world space.
        /// </summary>
        static Mesh RingWallMesh(Vector3 center, float ringRadius, float thickness, float baseRadius, float topRadius, float ground)
        {
            const int segments = 120;
            Vector3 t1 = Vector3.Cross(center, Mathf.Abs(center.y) < 0.9f ? Vector3.up : Vector3.right).normalized;
            Vector3 t2 = Vector3.Cross(center, t1);
            Vector3 Dir(float angle, float phi) =>
                center * Mathf.Cos(angle) + (t1 * Mathf.Cos(phi) + t2 * Mathf.Sin(phi)) * Mathf.Sin(angle);
            float inner = (ringRadius - thickness * 0.5f) / ground, outer = (ringRadius + thickness * 0.5f) / ground;
            var verts = new List<Vector3>();
            var normals = new List<Vector3>();
            var tris = new List<int>();
            var topTris = new List<int>();
            // Each face separately (outside, inside, top), so the edges stay crisp.
            void Face(List<int> into, System.Func<float, (Vector3 a, Vector3 b)> edge, System.Func<float, Vector3> normal)
            {
                int start = verts.Count;
                for (int k = 0; k <= segments; k++)
                {
                    float phi = Mathf.PI * 2f * k / segments;
                    var (a, b) = edge(phi);
                    verts.Add(a); verts.Add(b);
                    normals.Add(normal(phi)); normals.Add(normal(phi));
                }
                for (int k = 0; k < segments; k++)
                {
                    int v = start + k * 2;
                    // Wound to face along the normal.
                    Vector3 n = normals[v];
                    bool flip = Vector3.Dot(Vector3.Cross(verts[v + 1] - verts[v], verts[v + 2] - verts[v]), n) < 0f;
                    if (!flip) into.AddRange(new[] { v, v + 1, v + 2, v + 1, v + 3, v + 2 });
                    else into.AddRange(new[] { v, v + 2, v + 1, v + 1, v + 2, v + 3 });
                }
            }
            Vector3 Out(float phi) => (t1 * Mathf.Cos(phi) + t2 * Mathf.Sin(phi));
            Face(tris, phi => (Dir(outer, phi) * baseRadius, Dir(outer, phi) * topRadius), Out);
            Face(tris, phi => (Dir(inner, phi) * baseRadius, Dir(inner, phi) * topRadius), phi => -Out(phi));
            Face(topTris, phi => (Dir(inner, phi) * topRadius, Dir(outer, phi) * topRadius), phi => Dir((inner + outer) * 0.5f, phi));
            var mesh = new Mesh { name = "Core Wall", subMeshCount = 2 };
            mesh.SetVertices(verts);
            mesh.SetNormals(normals);
            mesh.SetTriangles(tris, 0);
            mesh.SetTriangles(topTris, 1);
            mesh.RecalculateBounds();
            return mesh;
        }

        /// <summary>
        /// A flat ribbon lying on the core's ground along a line of directions, to be revealed a piece at a time
        /// like a laser switch's cable: its triangles in order along the line (six per piece, returned
        /// separately with how far along each piece ends), and the mesh starts with none of them drawn. Also
        /// how far along it reaches the outside face of each wall round the energy ball.
        /// </summary>
        static Mesh RevealedStrip(CoreLayout core, List<Vector3> line, float width, out int[] triangles, out float[] distances,
            out float[] wallDistances)
        {
            // Every half unit or so, on the ground.
            var points = new List<Vector3>();
            for (int k = 0; k + 1 < line.Count; k++)
            {
                float length = Vector3.Angle(line[k], line[k + 1]) * Mathf.Deg2Rad * core.GroundRadius;
                int steps = Mathf.Max(1, Mathf.CeilToInt(length / 0.5f));
                for (int n = 0; n < steps; n++) points.Add(Vector3.Slerp(line[k], line[k + 1], n / (float)steps));
            }
            points.Add(line[line.Count - 1]);
            for (int k = 0; k < points.Count; k++)
            {
                Vector3 dir = points[k].normalized;
                points[k] = dir * (core.SurfaceRadius(dir) + 0.035f);
            }

            var verts = new List<Vector3>();
            var normals = new List<Vector3>();
            var tris = new List<int>();
            var dist = new List<float>();
            float travelled = 0f;
            for (int k = 0; k < points.Count; k++)
            {
                Vector3 up = points[k].normalized;
                Vector3 along = points[Mathf.Min(k + 1, points.Count - 1)] - points[Mathf.Max(k - 1, 0)];
                Vector3 side = Vector3.Cross(up, along).normalized * (width * 0.5f);
                verts.Add(points[k] - side); verts.Add(points[k] + side);
                normals.Add(up); normals.Add(up);
                if (k == 0) continue;
                int v = verts.Count - 4;
                bool flip = Vector3.Dot(Vector3.Cross(verts[v + 1] - verts[v], verts[v + 2] - verts[v]), up) < 0f;
                tris.AddRange(!flip ? new[] { v, v + 1, v + 2, v + 1, v + 3, v + 2 } : new[] { v, v + 2, v + 1, v + 1, v + 2, v + 3 });
                travelled += Vector3.Distance(points[k - 1], points[k]);
                dist.Add(travelled);
            }
            triangles = tris.ToArray();
            distances = dist.ToArray();
            wallDistances = new float[core.RingRadii.Length];
            for (int i = 0; i < wallDistances.Length; i++)
            {
                // The first piece ending inside the wall's outside face.
                float face = core.RingRadii[i] + 0.2f;
                wallDistances[i] = travelled;
                for (int k = 1; k < points.Count; k++)
                    if (Vector3.Angle(points[k], core.Cage) * Mathf.Deg2Rad * core.GroundRadius <= face) { wallDistances[i] = dist[k - 1]; break; }
            }
            var mesh = new Mesh { name = "Cable" };
            mesh.SetVertices(verts);
            mesh.SetNormals(normals);
            mesh.SetTriangles(new int[0], 0);
            mesh.bounds = new Bounds(Vector3.zero, Vector3.one * core.GroundRadius * 3f);
            return mesh;
        }
    }
}
