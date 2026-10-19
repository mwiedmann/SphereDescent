using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace SphereDescent
{
    /// <summary>Turns a <see cref="MazeLayout"/> into GameObjects: shells, walls, hole rings, boost pads and the core (the exit).</summary>
    public static class LevelBuilder
    {
        public static LevelInstance Build(MazeLayout layout, LevelSettings s, LevelMaterials mats, int world)
        {
            var root = new GameObject($"World {world} (seed {layout.Seed})");
            var level = root.AddComponent<LevelInstance>();
            var shellIco = IcoSphere.Get(s.shellMeshSubdivisions);
            Vector3 startDir = layout.Grids[0].CellCenters[layout.StartCell];
            var zones = new ZoneMap(layout.Seed);
            zones.ApplyToShaders();
            level.Init(s, layout, world, startDir * (s.shellRadii[0] + 1.5f), zones);

            Material[] landmarkMats = null;
            if (s.landmarks)
            {
                landmarkMats = new Material[ZoneMap.Count];
                for (int z = 0; z < ZoneMap.Count; z++)
                    landmarkMats[z] = level.Own(LevelMaterials.MakeEmissive(zones.Colors[z], 1.3f));
            }

            // One root per layer (shells, then the core). Everything is parented under the layer it sits on,
            // so whole layers can be hidden at once.
            var layerRoots = new Transform[layout.ShellCount + 1];
            var shellRenderers = new Renderer[layout.ShellCount + 1];
            for (int layer = 0; layer <= layout.ShellCount; layer++)
            {
                layerRoots[layer] = new GameObject(level.LayerName(layer)).transform;
                layerRoots[layer].SetParent(root.transform, false);
            }

            for (int shell = 0; shell < layout.ShellCount; shell++)
            {
                var grid = layout.Grids[shell];
                float radius = s.shellRadii[shell];
                float holeRadius = s.HoleRadius(grid, shell);
                var layerRoot = layerRoots[shell];

                var cuts = new List<HoleCut>();
                foreach (var h in layout.Holes)
                    if (h.Shell == shell)
                        cuts.Add(new HoleCut { Direction = h.Direction, AngularRadius = holeRadius / radius });

                var groundMat = level.Own(ZoneMaterial(mats.ground, GroundColor(shell), 3f, 0.6f));
                var shellMesh = level.Own(MeshBuilders.BuildShell(shellIco, radius, s.shellThickness, cuts));
                // Shells don't cast shadows, otherwise the outer shell would plunge every inner layer into darkness.
                shellRenderers[shell] = CreateMeshObject(layerRoot, "Shell", shellMesh, groundMat, true, ShadowCastingMode.Off)
                    .GetComponent<Renderer>();

                var walls = new List<(Vector3, Vector3)>();
                for (int e = 0; e < grid.Edges.Length; e++)
                    if (!layout.Open[shell][e]) walls.Add((grid.Edges[e].P0, grid.Edges[e].P1));
                var wallMat = level.Own(ZoneMaterial(mats.wall, WallColor(shell), 2f, 0.9f));
                // Neon strip along the top of the walls: the shader measures height above this shell.
                wallMat.SetFloat("_WallMode", 1f);
                wallMat.SetFloat("_SurfaceRadius", radius);
                wallMat.SetFloat("_WallHeight", s.wallHeight);
                var wallMesh = level.Own(MeshBuilders.BuildWalls(walls, radius, s.wallHeight, s.wallThickness));
                var wallObj = CreateMeshObject(layerRoot, "Walls", wallMesh, wallMat, true, ShadowCastingMode.On);
                level.RegisterShadowCaster(shell, wallObj.GetComponent<MeshRenderer>());

                // Glowing rings mark holes: cyan = you can come back up through it, orange = one-way drop.
                var upRing = level.Own(MeshBuilders.BuildTorus(holeRadius + 0.3f, 0.18f));
                var dropRing = level.Own(MeshBuilders.BuildTorus(holeRadius + 0.3f, 0.18f));
                foreach (var h in layout.Holes)
                {
                    if (h.Shell != shell) continue;
                    Vector3 dir = h.Direction;
                    var ring = CreateMeshObject(layerRoot, h.TwoWay ? "Hole (two-way)" : "Hole (drop)",
                        h.TwoWay ? upRing : dropRing, h.TwoWay ? mats.ringUp : mats.ringDrop, false, ShadowCastingMode.Off);
                    ring.transform.SetPositionAndRotation(dir * radius, Quaternion.FromToRotation(Vector3.up, dir));

                    if (h.TwoWay)
                        CreateBoostPad(level, layerRoots[shell + 1], layout, s, h, holeRadius, mats.pad);
                }

                if (landmarkMats != null) CreateLandmarks(layerRoot, layout, s, zones, shell, landmarkMats);
            }

            // The whole core is the exit: it glows gold, and landing on it completes the world.
            var coreRoot = layerRoots[layout.ShellCount];
            var coreMat = level.Own(LevelMaterials.MakeEmissive(mats.exit.color, 0.8f));
            var coreMesh = level.Own(MeshBuilders.BuildSphere(IcoSphere.Get(s.coreMeshSubdivisions), s.coreRadius));
            shellRenderers[layout.ShellCount] = CreateMeshObject(coreRoot, "Core (Exit)", coreMesh, coreMat, true, ShadowCastingMode.Off)
                .GetComponent<Renderer>();

            level.RegisterLayers(layerRoots, shellRenderers);
            return level;
        }

        static void CreateBoostPad(LevelInstance level, Transform parent, MazeLayout layout, LevelSettings s,
            MazeLayout.Hole hole, float holeRadius, Material mat)
        {
            var grid = layout.Grids[hole.Shell];
            Vector3 dir = hole.Direction;
            float belowRadius = s.SurfaceRadius(hole.Shell + 1);
            float aboveRadius = s.shellRadii[hole.Shell];

            // Land next to the hole, heading out through the nearest open side of the cell above.
            Vector3 exitTangent = Vector3.zero;
            float bestDot = -2f;
            foreach (int nb in grid.Neighbors[hole.Cell])
            {
                if (!layout.Open[hole.Shell][grid.EdgeBetween(hole.Cell, nb)]) continue;
                float d = Vector3.Dot(grid.CellCenters[nb], dir);
                if (d <= bestDot) continue;
                bestDot = d;
                exitTangent = Vector3.ProjectOnPlane(grid.CellCenters[nb], dir).normalized;
            }
            if (exitTangent == Vector3.zero)
                exitTangent = Vector3.ProjectOnPlane(grid.CellCenters[grid.Neighbors[hole.Cell][0]], dir).normalized;

            var go = new GameObject("Boost Pad");
            go.transform.SetParent(parent, false);
            go.transform.SetPositionAndRotation(dir * belowRadius, Quaternion.FromToRotation(Vector3.up, dir));

            float padRadius = LevelSettings.PadRadius(holeRadius);
            var disc = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            Object.DestroyImmediate(disc.GetComponent<Collider>());
            disc.name = "Disc";
            disc.transform.SetParent(go.transform, false);
            disc.transform.localPosition = new Vector3(0f, 0.02f, 0f);
            disc.transform.localScale = new Vector3(padRadius * 2f, 0.04f, padRadius * 2f);
            disc.GetComponent<MeshRenderer>().sharedMaterial = mat;

            var ring = CreateMeshObject(go.transform, "Pulse Ring", level.Own(MeshBuilders.BuildTorus(padRadius, 0.12f)),
                mat, false, ShadowCastingMode.Off);
            ring.transform.localPosition = new Vector3(0f, 0.1f, 0f);

            var trigger = go.AddComponent<SphereCollider>();
            trigger.isTrigger = true;
            trigger.radius = padRadius;
            trigger.center = new Vector3(0f, 0.6f, 0f);

            var pad = go.AddComponent<BoostPad>();
            pad.Axis = dir;
            pad.TargetSurfaceRadius = aboveRadius;
            pad.ExitTangent = exitTangent;
            pad.LandAngle = (holeRadius + 1.6f) / aboveRadius;
            pad.pulseVisual = ring.transform;
        }

        static GameObject CreateMeshObject(Transform parent, string name, Mesh mesh, Material mat, bool collider, ShadowCastingMode shadows)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = mat;
            mr.shadowCastingMode = shadows;
            if (collider) go.AddComponent<MeshCollider>().sharedMesh = mesh;
            return go;
        }

        /// <summary>
        /// One glowing pillar per zone on this shell, at the free cell nearest the zone's center. They're
        /// tall enough to see over the maze walls, and share the zone's color so you can steer by them.
        /// </summary>
        static void CreateLandmarks(Transform parent, MazeLayout layout, LevelSettings s, ZoneMap zones, int shell, Material[] mats)
        {
            var grid = layout.Grids[shell];
            float radius = s.shellRadii[shell];
            float height = shell == 0 ? 12f : Mathf.Min(10f, s.shellRadii[shell - 1] - s.shellThickness - radius - 2.5f);

            // Keep clear of holes on this shell and of landing spots / pads from the shell above.
            var avoid = new HashSet<int>();
            void Avoid(int cell) { avoid.Add(cell); foreach (int nb in grid.Neighbors[cell]) avoid.Add(nb); }
            if (shell == 0) Avoid(layout.StartCell);
            foreach (var h in layout.Holes)
            {
                if (h.Shell == shell) Avoid(h.Cell);
                if (h.Shell == shell - 1) Avoid(h.LandingCell);
            }

            for (int z = 0; z < ZoneMap.Count; z++)
            {
                int bestCell = -1;
                float bestDot = -2f;
                for (int c = 0; c < grid.CellCount; c++)
                {
                    if (avoid.Contains(c)) continue;
                    float d = Vector3.Dot(grid.CellCenters[c], zones.Directions[z]);
                    if (d > bestDot) { bestDot = d; bestCell = c; }
                }
                if (bestCell < 0) continue;

                Vector3 dir = grid.CellCenters[bestCell];
                var pillar = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                pillar.name = $"Landmark ({zones.Names[z]})";
                pillar.transform.SetParent(parent, false);
                pillar.transform.SetPositionAndRotation(dir * (radius + height * 0.5f - 0.3f), Quaternion.FromToRotation(Vector3.up, dir));
                pillar.transform.localScale = new Vector3(1f, height * 0.5f, 1f);
                var mr = pillar.GetComponent<MeshRenderer>();
                mr.sharedMaterial = mats[z];
                mr.shadowCastingMode = ShadowCastingMode.Off;
            }
        }

        static Material ZoneMaterial(Material template, Color tint, float gridSize, float zoneMix)
        {
            var m = new Material(template) { color = tint };
            if (m.HasProperty("_ZoneMode")) m.SetFloat("_ZoneMode", 1f);
            if (m.HasProperty("_ZoneMix")) m.SetFloat("_ZoneMix", zoneMix);
            if (m.HasProperty("_GridSize")) m.SetFloat("_GridSize", gridSize);
            return m;
        }

        // Brightness of each layer's surfaces and glow: deeper layers get a little dimmer.
        static Color GroundColor(int layer) => Color.white * (1f - 0.08f * layer);

        static Color WallColor(int layer) => Color.white * (1f - 0.08f * layer);
    }
}
