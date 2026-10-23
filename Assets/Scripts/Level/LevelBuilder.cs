using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace SphereDescent
{
    /// <summary>Turns a <see cref="MazeLayout"/> into GameObjects: shells, walls, hole rings, boost pads and the core (the exit).</summary>
    public static class LevelBuilder
    {
        public static LevelInstance Build(MazeLayout layout, LevelSettings s, LevelMaterials mats, int world, VisualStyle style = null)
        {
            style ??= VisualStyle.Synthwave();
            var root = new GameObject($"World {world} (seed {layout.Seed})");
            var level = root.AddComponent<LevelInstance>();
            var shellIco = IcoSphere.Get(s.shellMeshSubdivisions);
            Vector3 startDir = layout.Grids[0].CellCenters[layout.StartCell];
            var zones = new ZoneMap(layout.Seed, style.zones);
            zones.ApplyToShaders();
            if (style.caustics > 0f) Shader.SetGlobalTexture("_CausticTex", CausticTexture.Get());
            level.Init(s, layout, world, startDir * (s.shellRadii[0] + 1.5f), zones);

            // A light beam marks where each section ends, colored like the rings: orange over the hole to drop
            // through, cyan over the boost pad to climb from. Only the current section's beam is shown. The
            // shells above are hidden from inner layers, so every beam rises the full Beam Height.
            var beamDrop = level.Own(BeamMaterial(mats.beam, mats.ringDrop.color, 0.3f));
            var beamPad = level.Own(BeamMaterial(mats.beam, mats.ringUp.color, 0.3f));
            var holeFloorMat = level.Own(LevelMaterials.MakeBlack());
            // Used holes and pads get sealed with a black disc marked with a red X.
            var sealMat = level.Own(LevelMaterials.MakeMetal(new Color(0.02f, 0.02f, 0.025f), 0.6f, 0.4f));
            var crossMat = level.Own(LevelMaterials.MakeEmissive(new Color(1f, 0.03f, 0.02f), 2.2f));

            // One root per layer (shells, then the core). Everything is parented under the layer it sits on,
            // so whole layers can be hidden at once.
            var layerRoots = new Transform[layout.ShellCount + 1];
            for (int layer = 0; layer <= layout.ShellCount; layer++)
            {
                layerRoots[layer] = new GameObject(level.LayerName(layer)).transform;
                layerRoots[layer].SetParent(root.transform, false);
            }

            var terrains = BuildTerrains(layout, s);
            // Each shell is drawn in chunks; only those near the camera are switched on.
            var chunkGrids = new SphereGrid[layout.ShellCount];
            var chunkRoots = new Transform[layout.ShellCount][];
            for (int shell = 0; shell < layout.ShellCount; shell++)
            {
                chunkGrids[shell] = SphereGrid.ForRadius(s.shellRadii[shell], s.chunkSize);
                chunkRoots[shell] = new Transform[chunkGrids[shell].CellCount];
                for (int c = 0; c < chunkRoots[shell].Length; c++)
                {
                    chunkRoots[shell][c] = new GameObject($"Chunk {c}").transform;
                    chunkRoots[shell][c].SetParent(layerRoots[shell], false);
                }
            }
            for (int shell = 0; shell < layout.ShellCount; shell++)
            {
                var grid = layout.Grids[shell];
                var terrain = terrains[shell];
                float radius = s.shellRadii[shell];
                float holeRadius = s.HoleRadius(grid, shell);
                var layerRoot = layerRoots[shell];

                var cuts = new List<HoleCut>();
                foreach (var h in layout.Holes)
                    if (h.Shell == shell)
                        cuts.Add(new HoleCut { Direction = h.Direction, AngularRadius = holeRadius / radius });
                // Acid pools: a pit the exact shape of their cell (its edges run under the walls).
                foreach (var p in layout.AcidPools)
                {
                    if (p.Shell != shell) continue;
                    Vector3 c = grid.CellCenters[p.Cell];
                    var corners = CellCorners(grid, p.Cell);
                    // Each side's plane, facing into the cell.
                    var sides = new Vector3[corners.Count];
                    for (int k = 0; k < corners.Count; k++)
                    {
                        Vector3 nrm = Vector3.Cross(corners[k], corners[(k + 1) % corners.Count]).normalized;
                        sides[k] = Vector3.Dot(nrm, c) < 0f ? -nrm : nrm;
                    }
                    cuts.Add(new HoleCut
                    {
                        Direction = c,
                        Inside = d => System.Array.TrueForAll(sides, n => Vector3.Dot(d, n) > 0f),
                        SnapToEdge = d =>
                        {
                            // Onto the side this point is farthest beyond.
                            Vector3 n = sides[0];
                            foreach (var o in sides) if (Vector3.Dot(d, o) < Vector3.Dot(d, n)) n = o;
                            return d - n * Vector3.Dot(d, n);
                        },
                    });
                }

                var groundMat = level.Own(ZoneMaterial(mats.ground, GroundColor(shell), 3f, style.groundZoneMix, style));
                var shellMesh = level.Own(MeshBuilders.BuildShell(shellIco, radius, s.shellThickness, cuts, terrain));
                // Physics uses the whole shell (no seams to catch the ball); it's drawn in chunks. Shells don't cast
                // shadows, otherwise the outer shell would plunge every inner layer into darkness.
                AddCollider(layerRoot, "Shell", shellMesh);
                var shellPieces = MeshBuilders.SplitByDirection(shellMesh, chunkGrids[shell].CellCenters, "Shell Chunk");
                for (int c = 0; c < shellPieces.Length; c++)
                    if (shellPieces[c] != null)
                        CreateMeshObject(chunkRoots[shell][c], "Shell", level.Own(shellPieces[c]), groundMat, false, ShadowCastingMode.Off);

                var walls = new List<(Vector3, Vector3)>();
                for (int e = 0; e < grid.Edges.Length; e++)
                    if (!layout.Open[shell][e]) walls.Add((grid.Edges[e].P0, grid.Edges[e].P1));
                var wallMat = level.Own(ZoneMaterial(mats.wall, WallColor(shell), 2f, style.wallZoneMix, style));
                // Neon strip along the top of the walls: the shader measures height above this shell.
                wallMat.SetFloat("_WallMode", 1f);
                wallMat.SetFloat("_SurfaceRadius", radius);
                wallMat.SetFloat("_WallHeight", s.wallHeight);
                wallMat.SetFloat("_LineWidth", 0.025f); // thinner panel lines than the floor's
                var wallMesh = level.Own(MeshBuilders.BuildWalls(walls, radius, s.wallHeight, s.wallThickness, terrain));
                AddCollider(layerRoot, "Walls", wallMesh);
                // Cut from the finished walls, so the joints between pieces stay seamless.
                var wallPieces = MeshBuilders.SplitByDirection(wallMesh, chunkGrids[shell].CellCenters, "Walls Chunk");
                for (int c = 0; c < wallPieces.Length; c++)
                {
                    if (wallPieces[c] == null) continue;
                    var piece = CreateMeshObject(chunkRoots[shell][c], "Walls", level.Own(wallPieces[c]), wallMat, false, ShadowCastingMode.On);
                    level.RegisterShadowCaster(shell, piece.GetComponent<MeshRenderer>());
                }

                // Glowing rings mark holes: cyan = you can come back up through it, orange = one-way drop.
                var upRing = level.Own(MeshBuilders.BuildTorus(holeRadius + 0.3f, 0.18f));
                var dropRing = level.Own(MeshBuilders.BuildTorus(holeRadius + 0.3f, 0.18f));
                var holeFloor = level.Own(MeshBuilders.BuildDisc(holeRadius + 0.4f));
                var holeCover = level.Own(MeshBuilders.BuildDisc(holeRadius + 0.55f, 40));
                var padRadiusHere = LevelSettings.PadRadius(holeRadius);
                var padCover = level.Own(MeshBuilders.BuildDisc(padRadiusHere + 0.3f, 40));
                for (int i = 0; i < layout.Holes.Count; i++)
                {
                    var h = layout.Holes[i];
                    if (h.Shell != shell) continue;
                    Vector3 dir = h.Direction;
                    var ring = CreateMeshObject(layerRoot, h.TwoWay ? "Hole (two-way)" : "Hole (drop)",
                        h.TwoWay ? upRing : dropRing, h.TwoWay ? mats.ringUp : mats.ringDrop, false, ShadowCastingMode.Off);
                    ring.transform.SetPositionAndRotation(dir * radius, Quaternion.FromToRotation(Vector3.up, dir));

                    // Only the current layer is drawn, so without this you'd see into the sphere through the hole.
                    // No collider: the ball falls straight through, and the layer (disc included) is hidden once it has.
                    var floor = CreateMeshObject(layerRoot, "Hole Floor", holeFloor, holeFloorMat, false, ShadowCastingMode.Off);
                    floor.transform.SetPositionAndRotation(dir * (radius - s.shellThickness - 0.1f), Quaternion.FromToRotation(Vector3.up, dir));

                    // Once used, the hole is sealed over, so there's no going back through it.
                    var holeSeal = CreateSeal(layerRoot, "Used Hole", holeCover, holeRadius, dir * (radius + 0.03f), dir, sealMat, crossMat);
                    level.RegisterUsable(false, i, new[] { ring, floor }, holeSeal);

                    // Hole i ends section i. A two-way hole is where a climb comes out, so its section ends at
                    // the pad below it instead.
                    if (h.TwoWay)
                    {
                        var pad = CreateBoostPad(level, layerRoots[shell + 1], layout, s, h, holeRadius, mats.pad);
                        var padSeal = CreateSeal(layerRoots[shell + 1], "Used Pad", padCover, padRadiusHere,
                            pad.transform.position + dir * 0.06f, dir, sealMat, crossMat);
                        level.RegisterUsable(true, i, new[] { pad }, padSeal);
                        float padRadius = s.SurfaceRadius(shell + 1);
                        CreateBeam(level, i, shell + 1, layerRoots[shell + 1], dir, padRadius, padRadius + s.beamHeight,
                            LevelSettings.PadRadius(holeRadius) * 0.75f, beamPad);
                    }
                    else
                    {
                        // From inside the hole up into the sky.
                        CreateBeam(level, i, shell, layerRoot, dir, radius - s.shellThickness, radius + s.beamHeight,
                            holeRadius * 0.75f, beamDrop);
                    }
                }
            }

            CreateMines(level, layout, s, terrains, layerRoots, mats.beam);
            CreateLaserGates(level, layout, s, terrains, layerRoots, zones, mats.beam);
            CreateCameras(level, layout, s, terrains, layerRoots, mats);
            CreateCubes(level, layout, s, terrains, layerRoots, mats.spot);
            CreateCheckpoints(level, layout, s, layerRoots);
            CreateArrows(level, layout, s, terrains, layerRoots);
            CreateAcidPools(level, layout, s, layerRoots, mats);
            CreateMortars(level, layout, s, terrains, layerRoots, mats);

            // The whole core is the exit: it glows gold, and landing on it completes the world.
            var coreRoot = layerRoots[layout.ShellCount];
            var coreMat = level.Own(LevelMaterials.MakeEmissive(mats.exit.color, 0.8f));
            var coreMesh = level.Own(MeshBuilders.BuildSphere(IcoSphere.Get(s.coreMeshSubdivisions), s.coreRadius));
            CreateMeshObject(coreRoot, "Core (Exit)", coreMesh, coreMat, true, ShadowCastingMode.Off);

            // Everything else on a shell (hazards, pickups, pads, rings) goes into the chunk it sits in, so it's
            // switched off with it. Exit beams and cables stay out: they're meant to be seen from afar.
            var chunks = new List<(GameObject chunk, Vector3 center, int layer)>();
            for (int shell = 0; shell < layout.ShellCount; shell++)
            {
                var loose = new List<Transform>();
                foreach (Transform child in layerRoots[shell])
                    if (!child.name.StartsWith("Chunk ") && child.name != "Shell" && child.name != "Walls" &&
                        child.name != "Light Beam" && child.name != "Cable")
                        loose.Add(child);
                foreach (var child in loose)
                    child.SetParent(chunkRoots[shell][chunkGrids[shell].NearestCell(child.position.normalized)], true);
                for (int c = 0; c < chunkRoots[shell].Length; c++)
                    chunks.Add((chunkRoots[shell][c].gameObject, chunkGrids[shell].CellCenters[c] * s.shellRadii[shell], shell));
            }
            // Other layers: about a chunk around the viewer, reaching the next shell down (or up) from the camera.
            float layerGap = s.ShellCount > 1 ? s.shellRadii[0] - s.shellRadii[1] : 14f;
            level.RegisterChunks(chunks, s.chunkViewDistance + s.chunkSize * 0.75f, layerGap + 6f + s.chunkSize * 0.75f);
            level.RegisterLayers(layerRoots);
            return level;
        }

        /// <summary>
        /// Hills for every shell. The ground stays flat around each hole, under each boost pad (beneath the
        /// hole on the next shell down, which is also where you land after a drop) and at the start.
        /// </summary>
        static ShellTerrain[] BuildTerrains(MazeLayout layout, LevelSettings s)
        {
            var terrains = new ShellTerrain[layout.ShellCount];
            for (int shell = 0; shell < layout.ShellCount; shell++)
                terrains[shell] = ShellTerrain.Generate(s.shellRadii[shell], s.HillHeight(shell), s.HillSize(shell), s.hillCoverage,
                    layout.Seed * 31 + shell);
            // Ease back into the hills gently (slope under about 37 degrees), so you never land beside a cliff.
            float Fade(int shell) => Mathf.Max(s.HillSize(shell), 2f * s.HillHeight(shell));
            terrains[0].AddFlatSpot(layout.Grids[0].CellCenters[layout.StartCell], s.hillFlatMargin, Fade(0));
            foreach (var h in layout.Holes)
            {
                float flat = s.HoleRadius(layout.Grids[h.Shell], h.Shell) + s.hillFlatMargin;
                // After a boost pad climb you land beside the hole still rolling; extra room to stop keeps
                // the ball from running up a hill and rolling back into the hole.
                terrains[h.Shell].AddFlatSpot(h.Direction, h.TwoWay ? flat + 4f : flat, Fade(h.Shell));
                if (h.Shell + 1 < layout.ShellCount) terrains[h.Shell + 1].AddFlatSpot(h.Direction, flat, Fade(h.Shell + 1));
            }
            // Laser gates stand on flat ground, so no hill lifts the ball over (or tucks it under) the lasers,
            // and so do their switch pads.
            foreach (var g in layout.LaserGates)
            {
                var edge = layout.Grids[g.Shell].Edges[g.Edge];
                float halfWidth = Vector3.Angle(edge.P0, edge.P1) * Mathf.Deg2Rad * s.shellRadii[g.Shell] * 0.5f;
                terrains[g.Shell].AddFlatSpot(edge.P0 + edge.P1, halfWidth + 1.5f, Fade(g.Shell));
                terrains[g.Shell].AddFlatSpot(layout.Grids[g.Shell].CellCenters[g.SwitchCell], 2.5f, Fade(g.Shell));
            }
            foreach (var c in layout.Checkpoints)
                terrains[c.Shell].AddFlatSpot(c.Direction, s.checkpointRadius + 0.8f, Fade(c.Shell));
            // Acid pools: the whole cell and its doorways, so the pit's edge and the bridge are level.
            foreach (var p in layout.AcidPools)
                terrains[p.Shell].AddFlatSpot(layout.Grids[p.Shell].CellCenters[p.Cell], s.cellSpacing * 0.75f, Fade(p.Shell));
            // Arrows lie flat on the floor.
            foreach (var a in layout.Arrows)
                terrains[a.Shell].AddFlatSpot(a.Direction, 1.8f, Fade(a.Shell));
            // So does the ground a camera's spotlight sweeps: a hill would hide the ball from the light, or
            // put the light pool in the air.
            foreach (var c in layout.Cameras)
            {
                var edge = layout.Grids[c.Shell].Edges[c.Edge];
                float halfWidth = Vector3.Angle(edge.P0, edge.P1) * Mathf.Deg2Rad * s.shellRadii[c.Shell] * 0.5f;
                terrains[c.Shell].AddFlatSpot(edge.P0 + edge.P1, Mathf.Max(halfWidth, s.spotReach) + s.spotRadius + 0.5f, Fade(c.Shell));
            }
            return terrains;
        }

        /// <summary>
        /// Mines: a dark metal disc sunk into the ground (deep enough to sit flush on a slope) with a ring of
        /// teeth around its rim, a dim red ring inside them, and a red light in the middle that blinks. Each
        /// blink also lights a red glow around the mine. A trigger around it catches the ball.
        /// </summary>
        static void CreateMines(LevelInstance level, MazeLayout layout, LevelSettings s, ShellTerrain[] terrains,
            Transform[] layerRoots, Material glowTemplate)
        {
            if (layout.Mines.Count == 0) return;
            var bodyMat = level.Own(LevelMaterials.MakeMetal(new Color(0.06f, 0.06f, 0.07f), 0.85f, 0.55f));
            var red = new Color(1f, 0.03f, 0.01f);
            var ringMat = level.Own(LevelMaterials.MakeEmissive(red, 0.5f));
            var lightOn = level.Own(LevelMaterials.MakeEmissive(red, 2.5f));
            var lightOff = level.Own(LevelMaterials.MakeEmissive(new Color(0.25f, 0.01f, 0.01f), 0.2f));
            var glowOn = level.Own(BeamMaterial(glowTemplate, red, 0f));
            glowOn.SetFloat("_Intensity", 1.2f);
            var glowOff = level.Own(BeamMaterial(glowTemplate, red, 0f));
            glowOff.SetFloat("_Intensity", 0.15f);
            float r = s.mineRadius;
            float bodyTop = s.mineHeight * 0.6f, sink = 0.35f;
            var bodyMesh = level.Own(MineBodyMesh(r, bodyTop, sink, s.mineHeight));
            var ringMesh = level.Own(MeshBuilders.BuildTorus(r * 0.72f, 0.04f, 32, 6));
            var glowMesh = level.Own(MeshBuilders.BuildBeam(r * 1.6f, 0.4f));

            foreach (var m in layout.Mines)
            {
                Vector3 dir = m.Direction;
                var go = new GameObject("Mine");
                go.transform.SetParent(layerRoots[m.Shell], false);
                // Tilted to lie flush with the ground, on a hillside too.
                go.transform.SetPositionAndRotation(dir * terrains[m.Shell].SurfaceRadius(dir),
                    Quaternion.FromToRotation(Vector3.up, terrains[m.Shell].Normal(dir)));

                CreateMeshObject(go.transform, "Body", bodyMesh, bodyMat, false, ShadowCastingMode.Off);
                var ring = CreateMeshObject(go.transform, "Ring", ringMesh, ringMat, false, ShadowCastingMode.Off);
                ring.transform.localPosition = new Vector3(0f, bodyTop, 0f);
                var glow = CreateMeshObject(go.transform, "Glow", glowMesh, glowOff, false, ShadowCastingMode.Off);
                glow.transform.localPosition = new Vector3(0f, -0.1f, 0f);

                var bulb = Part(go.transform, PrimitiveType.Sphere, "Light", lightOff);
                bulb.localPosition = new Vector3(0f, bodyTop, 0f);
                bulb.localScale = new Vector3(r * 0.5f, (s.mineHeight - bodyTop) * 2f, r * 0.5f);

                var trigger = go.AddComponent<SphereCollider>();
                trigger.isTrigger = true;
                trigger.radius = r * 0.9f;

                var mine = go.AddComponent<Mine>();
                mine.lightRenderer = bulb.GetComponent<Renderer>();
                mine.lightOn = lightOn;
                mine.lightOff = lightOff;
                mine.glowRenderer = glow.GetComponent<Renderer>();
                mine.glowOn = glowOn;
                mine.glowOff = glowOff;
            }
        }

        /// <summary>
        /// A flat strip on the floor along the way from a laser gate's switch pad to its doorway, through the
        /// middle of each cell on the way, hugging the ground over hills. At the doorway it branches left and
        /// right along the gate to the two walls the lasers come out of. Its triangles run in order from the pad
        /// to the gate, then both branches at once, piece by piece (returned separately, with how far along the
        /// cable each piece ends), and the mesh starts with none of them drawn, so it can be revealed a piece at
        /// a time.
        /// </summary>
        static Mesh CableMesh(MazeLayout layout, LevelSettings s, ShellTerrain[] terrains, MazeLayout.LaserGate g, float width,
            out int[] triangles, out float[] distances)
        {
            var grid = layout.Grids[g.Shell];
            var terrain = terrains[g.Shell];
            float radius = s.shellRadii[g.Shell];
            var door = grid.Edges[g.Edge];
            var cells = layout.PathBetween(g.Section, g.SwitchCell, g.EntrySideCell, g.Edge);
            var corners = new List<Vector3>();
            foreach (int c in cells) corners.Add(grid.CellCenters[c] * radius);
            // From the edge of the pad, to the middle of the doorway (under the lasers).
            if (corners.Count > 1)
                corners[0] += (corners[1] - corners[0]).normalized * 1.4f;
            Vector3 middle = (door.P0 + door.P1).normalized * radius;
            corners.Add(middle);
            // The branches stop at the face of each wall.
            Vector3 end0 = door.P0 * radius, end1 = door.P1 * radius;
            float stop = s.wallThickness * 0.5f + 0.05f;
            end0 += (middle - end0).normalized * stop;
            end1 += (middle - end1).normalized * stop;

            var verts = new List<Vector3>();
            var normals = new List<Vector3>();
            var main = Strip(Along(corners), 0f);
            float mainLength = main.Count > 0 ? main[main.Count - 1].distance : 0f;
            var left = Strip(Along(new List<Vector3> { middle, end0 }), mainLength);
            var right = Strip(Along(new List<Vector3> { middle, end1 }), mainLength);
            var tris = new List<int>();
            var dist = new List<float>();
            foreach (var (quad, d) in main) { tris.AddRange(quad); dist.Add(d); }
            // Both branches grow together: their pieces interleaved.
            for (int k = 0; k < Mathf.Max(left.Count, right.Count); k++)
            {
                if (k < left.Count) { tris.AddRange(left[k].quad); dist.Add(left[k].distance); }
                if (k < right.Count) { tris.AddRange(right[k].quad); dist.Add(right[k].distance); }
            }

            // Points every half unit along a line of corners, set on the ground.
            List<Vector3> Along(List<Vector3> line)
            {
                var points = new List<Vector3>();
                for (int k = 0; k + 1 < line.Count; k++)
                {
                    float length = Vector3.Distance(line[k], line[k + 1]);
                    int steps = Mathf.Max(1, Mathf.CeilToInt(length / 0.5f));
                    for (int n = 0; n < steps; n++) points.Add(line[k] + (line[k + 1] - line[k]) * (n / (float)steps));
                }
                points.Add(line[line.Count - 1]);
                for (int k = 0; k < points.Count; k++)
                {
                    Vector3 dir = points[k].normalized;
                    points[k] = dir * (terrain.SurfaceRadius(dir) + 0.035f);
                }
                return points;
            }

            // A flat ribbon along the points, facing up; one quad (six indices) per step, in order, each with how
            // far along the cable it ends (counting from <paramref name="startDistance"/>).
            List<(int[] quad, float distance)> Strip(List<Vector3> points, float startDistance)
            {
                var quads = new List<(int[], float)>();
                float travelled = startDistance;
                for (int k = 0; k < points.Count; k++)
                {
                    Vector3 up = points[k].normalized;
                    Vector3 along = points[Mathf.Min(k + 1, points.Count - 1)] - points[Mathf.Max(k - 1, 0)];
                    Vector3 side = Vector3.Cross(up, along).normalized * (width * 0.5f);
                    verts.Add(points[k] - side); verts.Add(points[k] + side);
                    normals.Add(up); normals.Add(up);
                    if (k == 0) continue;
                    int v = verts.Count - 4;
                    // Facing up: wound so the normal agrees with up.
                    bool flip = Vector3.Dot(Vector3.Cross(verts[v + 1] - verts[v], verts[v + 2] - verts[v]), up) < 0f;
                    travelled += Vector3.Distance(points[k - 1], points[k]);
                    quads.Add((!flip ? new[] { v, v + 1, v + 2, v + 1, v + 3, v + 2 } : new[] { v, v + 2, v + 1, v + 1, v + 2, v + 3 }, travelled));
                }
                return quads;
            }

            triangles = tris.ToArray();
            distances = dist.ToArray();
            var mesh = new Mesh { name = "Cable" };
            mesh.SetVertices(verts);
            mesh.SetNormals(normals);
            mesh.SetTriangles(new int[0], 0);
            mesh.bounds = new Bounds(middle, Vector3.one * 200f);
            return mesh;
        }

        /// <summary>
        /// A flat key symbol in the local XZ plane, about 1.5 long along X: a ring for the bow, a shaft, and two
        /// teeth hanging off the far end.
        /// </summary>
        static Mesh KeyMesh()
        {
            var temp = GameObject.CreatePrimitive(PrimitiveType.Cube);
            var box = temp.GetComponent<MeshFilter>().sharedMesh;
            var bow = MeshBuilders.BuildTorus(0.24f, 0.075f, 28, 8);
            const float h = 0.05f;
            var parts = new[]
            {
                new CombineInstance { mesh = bow, transform = Matrix4x4.TRS(new Vector3(-0.48f, 0f, 0f), Quaternion.identity, new Vector3(1f, h / 0.15f, 1f)) },
                new CombineInstance { mesh = box, transform = Matrix4x4.TRS(new Vector3(0.18f, 0f, 0f), Quaternion.identity, new Vector3(0.9f, h, 0.14f)) },
                new CombineInstance { mesh = box, transform = Matrix4x4.TRS(new Vector3(0.44f, 0f, -0.15f), Quaternion.identity, new Vector3(0.11f, h, 0.24f)) },
                new CombineInstance { mesh = box, transform = Matrix4x4.TRS(new Vector3(0.6f, 0f, -0.12f), Quaternion.identity, new Vector3(0.11f, h, 0.18f)) },
            };
            var mesh = new Mesh { name = "Key" };
            mesh.CombineMeshes(parts, true, true);
            Object.DestroyImmediate(temp);
            Object.DestroyImmediate(bow);
            return mesh;
        }

        /// <summary>The mine's metal body as one mesh: a squat cylinder plus eight teeth standing around its rim.</summary>
        static Mesh MineBodyMesh(float r, float bodyTop, float sink, float height)
        {
            var parts = new List<CombineInstance>();
            var temps = new List<GameObject>();
            Mesh Builtin(PrimitiveType type)
            {
                var t = GameObject.CreatePrimitive(type);
                temps.Add(t);
                return t.GetComponent<MeshFilter>().sharedMesh;
            }
            parts.Add(new CombineInstance
            {
                mesh = Builtin(PrimitiveType.Cylinder),
                transform = Matrix4x4.TRS(new Vector3(0f, (bodyTop - sink) * 0.5f, 0f), Quaternion.identity,
                    new Vector3(r * 2f, (bodyTop + sink) * 0.5f, r * 2f)),
            });
            var cube = Builtin(PrimitiveType.Cube);
            for (int k = 0; k < 8; k++)
            {
                float a = k * 45f;
                // Diamond-turned cubes poking up around the rim, like the teeth of a trap.
                Vector3 at = Quaternion.Euler(0f, a, 0f) * new Vector3(r * 0.9f, bodyTop, 0f);
                parts.Add(new CombineInstance
                {
                    mesh = cube,
                    transform = Matrix4x4.TRS(at, Quaternion.Euler(0f, a, 45f), new Vector3(0.11f, 0.11f, 0.16f) * (height / 0.125f)),
                });
            }
            var mesh = new Mesh { name = "Mine Body" };
            mesh.CombineMeshes(parts.ToArray(), true, true);
            foreach (var t in temps) Object.DestroyImmediate(t);
            return mesh;
        }

        /// <summary>
        /// Laser gates: horizontal lasers stacked from near the ground to just under the wall tops (too close
        /// together to slip between, too high to jump over), filling the doorway between its two walls. Each is
        /// a white-hot core inside a thin glow in the color opposite the local zone's, so it stands out from the
        /// neon floor and walls. A trigger across the doorway catches the ball. Their switch pads: a metal disc
        /// with a ring and a key symbol pulsing warning red, turning green once rolled over.
        /// </summary>
        static void CreateLaserGates(LevelInstance level, MazeLayout layout, LevelSettings s, ShellTerrain[] terrains,
            Transform[] layerRoots, ZoneMap zones, Material glowTemplate)
        {
            if (layout.LaserGates.Count == 0) return;
            var offColor = new Color(0.05f, 1f, 0.2f);
            var warning = new Color(1f, 0.04f, 0.02f);
            var disarmedMat = level.Own(LevelMaterials.MakeEmissive(offColor, 1.4f));
            var cableMat = level.Own(LevelMaterials.MakeEmissive(offColor, 2.4f));
            var metalMat = level.Own(LevelMaterials.MakeMetal(new Color(0.06f, 0.06f, 0.07f), 0.85f, 0.55f));
            var ringMesh = level.Own(MeshBuilders.BuildTorus(1.05f, 0.07f, 40, 8));
            var keyMesh = level.Own(KeyMesh());
            var glowMesh = level.Own(MeshBuilders.BuildBeam(0.5f, 1f, 12));
            var holeMesh = level.Own(MeshBuilders.BuildDisc(0.1f, 16));
            var emberMesh = level.Own(MeshBuilders.BuildDisc(0.05f, 12));
            var holeMat = level.Own(LevelMaterials.MakeBlack());
            var emberMat = level.Own(LevelMaterials.MakeEmissive(new Color(1f, 0.02f, 0.01f), 1.8f));
            // The walls were just built; make their colliders findable for the raycasts that place the holes.
            Physics.SyncTransforms();
            const float coreWidth = 0.05f, glowWidth = 0.1f;

            foreach (var g in layout.LaserGates)
            {
                float radius = s.shellRadii[g.Shell];
                var grid = layout.Grids[g.Shell];
                var edge = grid.Edges[g.Edge];
                Vector3 a = edge.P0 * radius, b = edge.P1 * radius;
                Vector3 up = (edge.P0 + edge.P1).normalized;
                Vector3 along = (b - a).normalized;
                float length = Vector3.Distance(a, b);

                var go = new GameObject("Laser Gate");
                go.transform.SetParent(layerRoots[g.Shell], false);
                go.transform.SetPositionAndRotation(up * radius, Quaternion.LookRotation(along, up));
                var lasers = new GameObject("Lasers").transform;
                lasers.SetParent(go.transform, false);

                // The opposite hue to the zone it stands in, but never green: green means a switched-off gate.
                Color.RGBToHSV(zones.Colors[zones.ZoneAt(up * radius)], out float hue, out _, out _);
                float laserHue = Mathf.Repeat(hue + 0.5f, 1f);
                if (Mathf.Abs(laserHue - 0.33f) < 0.12f) laserHue = 0.5f;
                Color laserColor = Color.HSVToRGB(laserHue, 1f, 1f);
                Color coreColor = Color.Lerp(laserColor, Color.white, 0.55f);
                var coreMat = level.Own(LevelMaterials.MakeEmissive(coreColor, 4f));
                var glowMat = level.Own(BeamMaterial(glowTemplate, laserColor, 1f));
                glowMat.SetFloat("_BandStrength", 0f);

                void Beam(Vector3 from, Vector3 to)
                {
                    var core = Part(lasers, PrimitiveType.Cylinder, "Laser", coreMat);
                    core.SetPositionAndRotation((from + to) * 0.5f, Quaternion.FromToRotation(Vector3.up, to - from));
                    core.localScale = new Vector3(coreWidth, Vector3.Distance(from, to) * 0.5f, coreWidth);
                    var glow = CreateMeshObject(lasers, "Laser Glow", glowMesh, glowMat, false, ShadowCastingMode.Off).transform;
                    glow.SetPositionAndRotation(from, Quaternion.FromToRotation(Vector3.up, to - from));
                    glow.localScale = new Vector3(glowWidth, Vector3.Distance(from, to), glowWidth);
                }
                // The holes each laser shoots out of, where it meets the wall at either end of the doorway: a
                // dark hole with a faint red glow inside, which stays when the lasers are switched off.
                void Hole(Vector3 from, Vector3 toward)
                {
                    foreach (var hit in Physics.RaycastAll(from, toward - from, Vector3.Distance(from, toward) + 0.5f))
                    {
                        if (hit.collider.name != "Walls") continue;
                        Quaternion facing = Quaternion.FromToRotation(Vector3.up, hit.normal);
                        var hole = CreateMeshObject(go.transform, "Laser Hole", holeMesh, holeMat, false, ShadowCastingMode.Off);
                        hole.transform.SetPositionAndRotation(hit.point + hit.normal * 0.01f, facing);
                        var ember = CreateMeshObject(go.transform, "Laser Hole Glow", emberMesh, emberMat, false, ShadowCastingMode.Off);
                        ember.transform.SetPositionAndRotation(hit.point + hit.normal * 0.015f, facing);
                        return;
                    }
                }
                for (int row = 0; row < s.laserRows; row++)
                {
                    float h = Mathf.Lerp(0.3f, s.wallHeight - 0.3f, row / (float)Mathf.Max(1, s.laserRows - 1));
                    Vector3 from = a + edge.P0 * h, to = b + edge.P1 * h, mid = (from + to) * 0.5f;
                    Beam(from, to);
                    Hole(mid, from);
                    Hole(mid, to);
                }

                var field = go.AddComponent<BoxCollider>();
                field.isTrigger = true;
                field.center = new Vector3(0f, s.wallHeight * 0.5f, 0f);
                field.size = new Vector3(0.35f, s.wallHeight, length);

                var gate = go.AddComponent<LaserGate>();
                gate.lasers = lasers.gameObject;
                gate.field = field;
                gate.coreMaterial = coreMat;
                gate.glowMaterial = glowMat;
                gate.coreColor = coreColor;

                // The switch pad, with its own warning material so it can pulse.
                var armedMat = level.Own(LevelMaterials.MakeEmissive(warning, 1.5f));
                Vector3 dir = grid.CellCenters[g.SwitchCell];
                var pad = new GameObject("Laser Switch");
                pad.transform.SetParent(layerRoots[g.Shell], false);
                pad.transform.SetPositionAndRotation(dir * radius, Quaternion.FromToRotation(Vector3.up, dir));
                var disc = Part(pad.transform, PrimitiveType.Cylinder, "Base", metalMat);
                disc.localPosition = new Vector3(0f, -0.12f, 0f);
                disc.localScale = new Vector3(2.6f, 0.2f, 2.6f);
                var ring = CreateMeshObject(pad.transform, "Ring", ringMesh, armedMat, false, ShadowCastingMode.Off);
                ring.transform.localPosition = new Vector3(0f, 0.08f, 0f);
                // A key symbol inside the ring, lying flat, so it reads as "unlock".
                var key = CreateMeshObject(pad.transform, "Key", keyMesh, armedMat, false, ShadowCastingMode.Off);
                key.transform.localPosition = new Vector3(0f, 0.06f, 0f);
                key.transform.localRotation = Quaternion.Euler(0f, 35f, 0f);

                var trigger = pad.AddComponent<SphereCollider>();
                trigger.isTrigger = true;
                trigger.radius = 1.2f;
                trigger.center = new Vector3(0f, 0.3f, 0f);

                var sw = pad.AddComponent<LaserSwitch>();
                sw.gate = gate;
                sw.indicators = new[] { ring.GetComponent<Renderer>(), key.GetComponent<Renderer>() };
                sw.armed = armedMat;
                sw.disarmed = disarmedMat;
                sw.warningColor = warning;

                // The cable from the pad to the gate, hidden until the pad is used.
                var cableMesh = level.Own(CableMesh(layout, s, terrains, g, 0.26f, out int[] cableTriangles, out float[] cableDistances));
                var cable = CreateMeshObject(layerRoots[g.Shell], "Cable", cableMesh, cableMat, false, ShadowCastingMode.Off);
                sw.cable = cable.GetComponent<MeshFilter>().sharedMesh;
                sw.cableTriangles = cableTriangles;
                sw.cableDistances = cableDistances;
            }
        }

        /// <summary>
        /// Security cameras: a thin pole standing on a wall beside the doorway, topped by a dark camera body
        /// with a glowing lens that turns to follow its spotlight. The light is faked: a glowing pool on the
        /// ground and a faint cone from the lens, cheaper than a real light and visible on any floor. The
        /// laser it fires starts hidden.
        /// </summary>
        static void CreateCameras(LevelInstance level, MazeLayout layout, LevelSettings s, ShellTerrain[] terrains,
            Transform[] layerRoots, LevelMaterials mats)
        {
            if (layout.Cameras.Count == 0) return;
            var metal = level.Own(LevelMaterials.MakeMetal(new Color(0.07f, 0.07f, 0.08f), 0.8f, 0.6f));
            var lensMat = level.Own(LevelMaterials.MakeEmissive(new Color(1f, 0.9f, 0.6f), 2.5f));
            var laserMat = level.Own(LevelMaterials.MakeEmissive(new Color(1f, 0.05f, 0.02f), 6f));
            var discMesh = level.Own(MeshBuilders.BuildDisc(1f, 40));
            var coneMesh = level.Own(MeshBuilders.BuildCone());

            foreach (var c in layout.Cameras)
            {
                var root = layerRoots[c.Shell];
                float radius = s.shellRadii[c.Shell];
                var door = layout.Grids[c.Shell].Edges[c.Edge];
                Vector3 center = (door.P0 + door.P1).normalized;
                Vector3 across = Vector3.ProjectOnPlane(door.P1 - door.P0, center).normalized;
                float halfWidth = Vector3.Angle(door.P0, door.P1) * Mathf.Deg2Rad * radius * 0.5f;

                // Pole on top of the wall.
                var go = new GameObject("Security Camera");
                go.transform.SetParent(root, false);
                float wallTop = terrains[c.Shell].SurfaceRadius(c.Mount) + s.wallHeight;
                go.transform.SetPositionAndRotation(c.Mount * wallTop, Quaternion.FromToRotation(Vector3.up, c.Mount));
                var pole = Part(go.transform, PrimitiveType.Cylinder, "Pole", metal);
                pole.localPosition = new Vector3(0f, s.cameraPoleHeight * 0.5f - 0.1f, 0f);
                pole.localScale = new Vector3(0.12f, s.cameraPoleHeight * 0.5f, 0.12f);

                // The camera itself, which turns to look at its spot: a body, a hood over the front, a lens.
                var head = new GameObject("Head").transform;
                head.SetParent(go.transform, false);
                head.localPosition = new Vector3(0f, s.cameraPoleHeight + 0.2f, 0f);
                var body = Part(head, PrimitiveType.Cube, "Body", metal);
                body.localScale = new Vector3(0.45f, 0.4f, 0.85f);
                var hood = Part(head, PrimitiveType.Cube, "Hood", metal);
                hood.localPosition = new Vector3(0f, 0.24f, 0.2f);
                hood.localScale = new Vector3(0.55f, 0.06f, 0.75f);
                var lensRing = Part(head, PrimitiveType.Cylinder, "Lens", lensMat);
                lensRing.localPosition = new Vector3(0f, 0f, 0.43f);
                lensRing.localRotation = Quaternion.Euler(90f, 0f, 0f);
                lensRing.localScale = new Vector3(0.28f, 0.03f, 0.28f);
                var lens = new GameObject("Lens Point").transform;
                lens.SetParent(head, false);
                lens.localPosition = new Vector3(0f, 0f, 0.47f);

                // Its light: a pool on the ground and a faint cone, each with its own material so it can turn red.
                var spotMat = level.Own(new Material(mats.spot));
                // Its spot, cone and laser belong to the camera, so they're switched on and off with it.
                var spot = CreateMeshObject(go.transform, "Spotlight", discMesh, spotMat, false, ShadowCastingMode.Off).transform;
                spot.localScale = Vector3.one * s.spotRadius;
                var coneMat = level.Own(BeamMaterial(mats.beam, Color.white, 1f));
                coneMat.SetFloat("_BandStrength", 0f);
                var cone = CreateMeshObject(go.transform, "Light Cone", coneMesh, coneMat, false, ShadowCastingMode.Off).transform;

                var laser = Part(go.transform, PrimitiveType.Cylinder, "Camera Laser", laserMat);

                var cam = go.AddComponent<SecurityCamera>();
                cam.head = head;
                cam.lens = lens;
                cam.spot = spot;
                cam.cone = cone;
                cam.laser = laser;
                cam.spotMaterial = spotMat;
                cam.coneMaterial = coneMat;
                cam.center = center;
                cam.across = across;
                cam.along = Vector3.Cross(center, across);
                cam.groundRadius = radius;
                cam.layer = c.Shell;
                // Swing nearly wall to wall across the doorway, and into the cells either side.
                cam.sweepAcross = Mathf.Max(0f, halfWidth - 1f);
                cam.sweepAlong = s.spotReach;
                cam.period = s.spotPeriod;
                cam.phase = c.Phase;
                cam.pattern = c.Pattern;
                cam.spotRadius = s.spotRadius;
            }
        }

        /// <summary>
        /// Energy cubes: a crisp glowing outline of a cube (its twelve edges) around faint glass faces, with a
        /// tiny, intensely bright point at its heart, like a singularity, in a tight soft glow. Floats a little
        /// above the ground. Gold for ordinary cubes; the super cube is larger and magenta.
        /// </summary>
        static void CreateCubes(LevelInstance level, MazeLayout layout, LevelSettings s, ShellTerrain[] terrains, Transform[] layerRoots,
            Material glowTemplate)
        {
            if (layout.Cubes.Count == 0) return;
            var gold = new Color(1f, 0.78f, 0.15f);
            var magenta = new Color(1f, 0.2f, 0.85f);
            var edgeGold = level.Own(LevelMaterials.MakeEmissive(gold, 2.2f));
            var edgeMagenta = level.Own(LevelMaterials.MakeEmissive(magenta, 2.6f));
            var faceGold = level.Own(LevelMaterials.MakeGlass(gold, 0.08f, 0.15f));
            var faceMagenta = level.Own(LevelMaterials.MakeGlass(magenta, 0.1f, 0.2f));
            // The point inside: white-hot, tinted just slightly.
            var pointGold = level.Own(LevelMaterials.MakeEmissive(Color.Lerp(gold, Color.white, 0.7f), 12f));
            var pointMagenta = level.Own(LevelMaterials.MakeEmissive(Color.Lerp(magenta, Color.white, 0.6f), 12f));
            Material Glow(Color c, float intensity)
            {
                var m = level.Own(new Material(glowTemplate));
                m.SetFloat("_Soft", 1f);
                m.SetColor("_Color", c);
                m.SetFloat("_Intensity", intensity);
                return m;
            }
            var glowGold = Glow(gold, 1.6f);
            var glowMagenta = Glow(magenta, 2f);
            var glowMesh = level.Own(MeshBuilders.BuildDisc(1f, 24));
            var frameMesh = level.Own(CubeFrameMesh(0.04f));
            foreach (var c in layout.Cubes)
            {
                float size = s.cubeSize * (c.Super ? 1.4f : 1f);
                float lift = 0.45f + size * 0.6f;
                var go = new GameObject(c.Super ? "Super Cube" : "Energy Cube");
                go.transform.SetParent(layerRoots[c.Shell], false);
                go.transform.SetPositionAndRotation(c.Direction * terrains[c.Shell].SurfaceRadius(c.Direction),
                    Quaternion.FromToRotation(Vector3.up, c.Direction));
                var visual = new GameObject("Visual").transform;
                visual.SetParent(go.transform, false);
                visual.localPosition = new Vector3(0f, lift, 0f);
                var frame = CreateMeshObject(visual, "Outline", frameMesh, c.Super ? edgeMagenta : edgeGold, false, ShadowCastingMode.Off);
                frame.transform.localScale = Vector3.one * size;
                var faces = Part(visual, PrimitiveType.Cube, "Faces", c.Super ? faceMagenta : faceGold);
                faces.localScale = Vector3.one * size * 0.98f;
                var point = Part(visual, PrimitiveType.Sphere, "Singularity", c.Super ? pointMagenta : pointGold);
                point.localScale = Vector3.one * s.cubeCoreSize * (c.Super ? 1.3f : 1f);
                var glow = CreateMeshObject(go.transform, "Glow", glowMesh, c.Super ? glowMagenta : glowGold, false, ShadowCastingMode.Off);
                glow.transform.localScale = Vector3.one * s.cubeCoreSize * 5f;

                var trigger = go.AddComponent<SphereCollider>();
                trigger.isTrigger = true;
                trigger.center = new Vector3(0f, lift, 0f);
                // A little generous, so cubes are easy to grab.
                trigger.radius = Mathf.Max(0.45f, size * 0.75f) * 1.25f;

                var cube = go.AddComponent<EnergyCube>();
                cube.visual = visual;
                cube.halo = glow.transform;
                cube.isSuper = c.Super;
                cube.points = c.Super ? s.superCubePoints : s.cubePoints;
                cube.section = c.Section;
                if (c.Super) cube.spinSpeed = 80f;
            }
        }

        /// <summary>The twelve edges of a unit cube (centered on the origin) as thin bars, in one mesh.</summary>
        static Mesh CubeFrameMesh(float thickness)
        {
            var temp = GameObject.CreatePrimitive(PrimitiveType.Cube);
            var box = temp.GetComponent<MeshFilter>().sharedMesh;
            var parts = new List<CombineInstance>();
            float len = 1f + thickness;
            for (int axis = 0; axis < 3; axis++)
                for (int a = -1; a <= 1; a += 2)
                    for (int b = -1; b <= 1; b += 2)
                    {
                        var pos = new Vector3[] { new Vector3(0f, a, b), new Vector3(a, 0f, b), new Vector3(a, b, 0f) }[axis] * 0.5f;
                        var scale = new Vector3[] { new Vector3(len, thickness, thickness), new Vector3(thickness, len, thickness),
                            new Vector3(thickness, thickness, len) }[axis];
                        parts.Add(new CombineInstance { mesh = box, transform = Matrix4x4.TRS(pos, Quaternion.identity, scale) });
                    }
            var mesh = new Mesh { name = "Cube Frame" };
            mesh.CombineMeshes(parts.ToArray(), true, true);
            Object.DestroyImmediate(temp);
            return mesh;
        }

        /// <summary>
        /// Checkpoint pads: a wide metal disc with an outer ring, an inner ring and a flag symbol in the middle,
        /// all dim white until reached, then bright green.
        /// </summary>
        static void CreateCheckpoints(LevelInstance level, MazeLayout layout, LevelSettings s, Transform[] layerRoots)
        {
            if (layout.Checkpoints.Count == 0) return;
            var metal = level.Own(LevelMaterials.MakeMetal(new Color(0.08f, 0.08f, 0.09f), 0.8f, 0.5f));
            var inactive = level.Own(LevelMaterials.MakeEmissive(new Color(0.75f, 0.8f, 0.9f), 0.7f));
            var active = level.Own(LevelMaterials.MakeEmissive(new Color(0.25f, 1f, 0.05f), 1.5f));
            float r = s.checkpointRadius;
            var outer = level.Own(MeshBuilders.BuildTorus(r * 0.92f, 0.1f, 56, 8));
            var inner = level.Own(MeshBuilders.BuildTorus(r * 0.62f, 0.06f, 48, 8));
            var flag = level.Own(FlagMesh(r * 0.5f));
            foreach (var c in layout.Checkpoints)
            {
                var go = new GameObject("Checkpoint");
                go.transform.SetParent(layerRoots[c.Shell], false);
                go.transform.SetPositionAndRotation(c.Direction * s.shellRadii[c.Shell], Quaternion.FromToRotation(Vector3.up, c.Direction));
                var disc = Part(go.transform, PrimitiveType.Cylinder, "Base", metal);
                disc.localPosition = new Vector3(0f, -0.12f, 0f);
                disc.localScale = new Vector3(r * 2f, 0.2f, r * 2f);
                var ring = CreateMeshObject(go.transform, "Ring", outer, inactive, false, ShadowCastingMode.Off);
                ring.transform.localPosition = new Vector3(0f, 0.08f, 0f);
                var ring2 = CreateMeshObject(go.transform, "Inner Ring", inner, inactive, false, ShadowCastingMode.Off);
                ring2.transform.localPosition = new Vector3(0f, 0.06f, 0f);
                var mark = CreateMeshObject(go.transform, "Flag", flag, inactive, false, ShadowCastingMode.Off);
                mark.transform.localPosition = new Vector3(0f, 0.06f, 0f);

                var trigger = go.AddComponent<SphereCollider>();
                trigger.isTrigger = true;
                trigger.radius = r * 0.8f;
                trigger.center = new Vector3(0f, 0.4f, 0f);

                var pad = go.AddComponent<CheckpointPad>();
                pad.indicators = new[] { ring.GetComponent<Renderer>(), ring2.GetComponent<Renderer>(), mark.GetComponent<Renderer>() };
                pad.inactive = inactive;
                pad.active = active;
                pad.pulseRing = ring.transform;
                // Later along the world is higher, so reaching a skipped pad never moves you back.
                pad.order = c.Section * 1000 + c.RouteIndex;
                pad.section = c.Section;
                pad.points = s.checkpointPoints;
            }
        }

        /// <summary>Floor arrows: a broad, softly glowing arrow lying flat, pointing the way.</summary>
        static void CreateArrows(LevelInstance level, MazeLayout layout, LevelSettings s, ShellTerrain[] terrains, Transform[] layerRoots)
        {
            if (layout.Arrows.Count == 0) return;
            var mat = level.Own(LevelMaterials.MakeEmissive(new Color(0.85f, 0.9f, 1f), 0.55f));
            var mesh = level.Own(ArrowMesh());
            foreach (var a in layout.Arrows)
            {
                var arrow = CreateMeshObject(layerRoots[a.Shell], "Arrow", mesh, mat, false, ShadowCastingMode.Off);
                arrow.transform.SetPositionAndRotation(a.Direction * (terrains[a.Shell].SurfaceRadius(a.Direction) + 0.03f),
                    Quaternion.LookRotation(a.Pointing, a.Direction));
            }
        }

        /// <summary>
        /// A flat arrow in the local XZ plane pointing along +Z, about 2.6 long: one solid shape (shaft and head
        /// sharing their corners, so there are no seams), with a thin rim down to the floor.
        /// </summary>
        static Mesh ArrowMesh()
        {
            const float shaft = 0.24f, head = 0.75f, tail = -1.3f, neck = 0.15f, tip = 1.3f, thick = 0.04f;
            // Outline, counterclockwise seen from above.
            var outline = new[]
            {
                new Vector3(-shaft, 0f, tail), new Vector3(shaft, 0f, tail), new Vector3(shaft, 0f, neck),
                new Vector3(head, 0f, neck), new Vector3(0f, 0f, tip), new Vector3(-head, 0f, neck), new Vector3(-shaft, 0f, neck),
            };
            var verts = new List<Vector3>();
            var normals = new List<Vector3>();
            var indices = new List<int>();
            foreach (var p in outline) { verts.Add(p + Vector3.up * thick); normals.Add(Vector3.up); }
            // Top: the shaft as two triangles, the head as three, all on the outline's own corners.
            int[] top = { 0, 2, 1, 0, 6, 2, 6, 4, 2, 2, 4, 3, 6, 5, 4 };
            indices.AddRange(top);
            // Rim: a thin wall around the edge, so it reads as one raised piece from low angles too.
            for (int k = 0; k < outline.Length; k++)
            {
                Vector3 a = outline[k], b = outline[(k + 1) % outline.Length];
                Vector3 n = Vector3.Cross(Vector3.up, b - a).normalized * -1f;
                int v = verts.Count;
                verts.Add(a); verts.Add(b); verts.Add(b + Vector3.up * thick); verts.Add(a + Vector3.up * thick);
                normals.Add(n); normals.Add(n); normals.Add(n); normals.Add(n);
                indices.Add(v); indices.Add(v + 2); indices.Add(v + 1);
                indices.Add(v); indices.Add(v + 3); indices.Add(v + 2);
            }
            var mesh = new Mesh { name = "Arrow" };
            mesh.SetVertices(verts);
            mesh.SetNormals(normals);
            mesh.SetTriangles(indices, 0);
            mesh.RecalculateBounds();
            return mesh;
        }

        /// <summary>A flat flag symbol in the local XZ plane, about <paramref name="size"/> tall along Z: a pole and a pennant.</summary>
        static Mesh FlagMesh(float size)
        {
            var temp = GameObject.CreatePrimitive(PrimitiveType.Cube);
            var box = temp.GetComponent<MeshFilter>().sharedMesh;
            const float h = 0.05f;
            float pole = size * 1.6f;
            var parts = new System.Collections.Generic.List<CombineInstance>
            {
                new CombineInstance { mesh = box, transform = Matrix4x4.TRS(new Vector3(-size * 0.45f, 0f, 0f), Quaternion.identity, new Vector3(0.12f, h, pole)) },
            };
            // The pennant: a stack of bars narrowing toward its tip, so it reads as a triangle.
            const int bars = 7;
            float top = pole * 0.5f, depth = size * 0.75f;
            for (int i = 0; i < bars; i++)
            {
                float f = (i + 0.5f) / bars;
                float length = size * 1.05f * (1f - Mathf.Abs(f - 0.5f) * 2f) + 0.08f;
                float z = top - f * depth;
                parts.Add(new CombineInstance
                {
                    mesh = box,
                    transform = Matrix4x4.TRS(new Vector3(-size * 0.45f + length * 0.5f, 0f, z), Quaternion.identity, new Vector3(length, h, depth / bars * 1.02f)),
                });
            }
            var mesh = new Mesh { name = "Flag" };
            mesh.CombineMeshes(parts.ToArray(), true, true);
            Object.DestroyImmediate(temp);
            return mesh;
        }

        static Transform Part(Transform parent, PrimitiveType type, string name, Material mat)
        {
            var go = GameObject.CreatePrimitive(type);
            Object.DestroyImmediate(go.GetComponent<Collider>());
            go.name = name;
            go.transform.SetParent(parent, false);
            var mr = go.GetComponent<MeshRenderer>();
            mr.sharedMaterial = mat;
            mr.shadowCastingMode = ShadowCastingMode.Off;
            return go.transform;
        }

        /// <summary>
        /// The cover that seals a used hole or pad: a solid black disc with a red X on it. Starts hidden;
        /// LevelInstance.MarkUsed shows it.
        /// </summary>
        static GameObject CreateSeal(Transform parent, string name, Mesh disc, float markSize, Vector3 position, Vector3 up,
            Material discMat, Material crossMat)
        {
            var seal = CreateMeshObject(parent, name, disc, discMat, true, ShadowCastingMode.Off);
            seal.transform.SetPositionAndRotation(position, Quaternion.FromToRotation(Vector3.up, up));
            foreach (float angle in new[] { 45f, -45f })
            {
                var bar = Part(seal.transform, PrimitiveType.Cube, "X", crossMat);
                bar.localPosition = new Vector3(0f, 0.02f, 0f);
                bar.localRotation = Quaternion.Euler(0f, angle, 0f);
                bar.localScale = new Vector3(markSize * 1.5f, 0.03f, Mathf.Max(0.18f, markSize * 0.14f));
            }
            seal.SetActive(false);
            return seal;
        }

        static GameObject CreateBoostPad(LevelInstance level, Transform parent, MazeLayout layout, LevelSettings s,
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
            return go;
        }

        static void CreateBeam(LevelInstance level, int section, int layer, Transform parent, Vector3 dir, float fromRadius,
            float toRadius, float radius, Material mat)
        {
            var beam = CreateMeshObject(parent, "Light Beam", level.Own(MeshBuilders.BuildBeam(radius, toRadius - fromRadius)),
                mat, false, ShadowCastingMode.Off);
            beam.transform.SetPositionAndRotation(dir * fromRadius, Quaternion.FromToRotation(Vector3.up, dir));
            beam.GetComponent<MeshRenderer>().receiveShadows = false;
            level.RegisterBeam(section, layer, beam);
        }

        static Material BeamMaterial(Material template, Color color, float fadeStart)
        {
            var m = new Material(template);
            m.SetColor("_Color", color);
            m.SetFloat("_FadeStart", fadeStart);
            return m;
        }

        /// <summary>
        /// Acid pools: the pit cut into the floor (done with the shell) gets a solid bottom, a glowing acid
        /// surface a little below the floor, and a thin metal bridge with bright edges running from the doorway
        /// you come in by, through the middle (a round landing where it bends), to the doorway out.
        /// </summary>
        static void CreateAcidPools(LevelInstance level, MazeLayout layout, LevelSettings s, Transform[] layerRoots, LevelMaterials mats)
        {
            if (layout.AcidPools.Count == 0) return;
            var metal = level.Own(LevelMaterials.MakeMetal(new Color(0.09f, 0.09f, 0.1f), 0.85f, 0.5f));
            var edgeMat = level.Own(LevelMaterials.MakeEmissive(new Color(1f, 0.85f, 0.2f), 1.6f));
            var pitMat = level.Own(LevelMaterials.MakeBlack());
            var steamMat = level.Own(LevelMaterials.MakeSoftPuff());
            float w = s.acidBridgeWidth;
            const float depth = 1.05f;
            foreach (var p in layout.AcidPools)
            {
                var grid = layout.Grids[p.Shell];
                float r = s.shellRadii[p.Shell];
                Vector3 c = grid.CellCenters[p.Cell];
                var go = new GameObject("Acid Pool");
                go.transform.SetParent(layerRoots[p.Shell], false);
                go.transform.position = c * r;

                var corners = CellCorners(grid, p.Cell);

                var bottom = level.Own(CellCapMesh(c, corners, r - 1f, 8));
                // These meshes are built in world space; the pool object itself sits at the cell (so it lands in the
                // right chunk), so they're placed back at the world origin.
                var pitBottom = CreateMeshObject(go.transform, "Pit Bottom", bottom, pitMat, true, ShadowCastingMode.Off);
                pitBottom.transform.position = Vector3.zero;
                var acid = CreateMeshObject(go.transform, "Acid", level.Own(CellCapMesh(c, corners, r - 0.55f, 8)), mats.acid, false, ShadowCastingMode.Off);
                acid.transform.position = Vector3.zero;
                CreateSteam(go.transform, c * (r - 0.5f), c, s.cellSpacing * 0.32f, steamMat);
                // Walls lining the pit all the way round, so you can't see under the floor through the doorways.
                CreateMeshObject(go.transform, "Pit Walls", level.Own(PitWallMesh(c, corners, r, r - 1f, 8)), metal, true, ShadowCastingMode.Off)
                    .transform.position = Vector3.zero;

                // The bridge: from just outside the doorway in, through the middle, to just outside the doorway out.
                Vector3 Doorway(int other, float beyond)
                {
                    var e = grid.Edges[grid.EdgeBetween(p.Cell, other)];
                    Vector3 mid = (e.P0 + e.P1).normalized;
                    Vector3 away = Vector3.ProjectOnPlane(grid.CellCenters[other] - mid, mid).normalized;
                    return (mid * r + away * beyond).normalized * r;
                }
                var path = new List<Vector3> { Doorway(p.From, 1.5f), c * r, Doorway(p.To, 1.5f) };
                var bridge = level.Own(BridgeMesh(path, w, depth, out Mesh edges));
                CreateMeshObject(go.transform, "Bridge", bridge, metal, true, ShadowCastingMode.On).transform.position = Vector3.zero;
                CreateMeshObject(go.transform, "Bridge Edges", level.Own(edges), edgeMat, false, ShadowCastingMode.Off).transform.position = Vector3.zero;
                // A round landing where it bends, so the turn is as wide as the bridge.
                var landing = Part(go.transform, PrimitiveType.Cylinder, "Bridge Landing", metal);
                landing.SetPositionAndRotation(c * (r + 0.02f - depth * 0.5f), Quaternion.FromToRotation(Vector3.up, c));
                landing.localScale = new Vector3(w * 1.15f, depth * 0.5f, w * 1.15f);
                landing.gameObject.AddComponent<MeshCollider>().sharedMesh = landing.GetComponent<MeshFilter>().sharedMesh;

                var pool = go.AddComponent<AcidPool>();
                pool.center = c;
                pool.neighbors = System.Array.ConvertAll(grid.Neighbors[p.Cell], n => grid.CellCenters[n]);
                pool.floorRadius = r;
                pool.layer = p.Shell;
            }
        }

        /// <summary>
        /// Mortars: a dark cannon perched on top of a wall, gripping it with two clawed legs, one down each side,
        /// each ending in two red-tipped claws hooked against the wall. The legs meet at a ball joint, where the
        /// turret (a domed head with a red band and a stubby barrel pitched up, glowing orange at the mouth)
        /// swivels to aim. Bombs and their landing markers go in the layer itself, so they aren't switched off
        /// with the mortar's chunk.
        /// </summary>
        static void CreateMortars(LevelInstance level, MazeLayout layout, LevelSettings s, ShellTerrain[] terrains,
            Transform[] layerRoots, LevelMaterials mats)
        {
            if (layout.Mortars.Count == 0) return;
            var metal = level.Own(LevelMaterials.MakeMetal(new Color(0.08f, 0.08f, 0.09f), 0.8f, 0.55f));
            var band = level.Own(LevelMaterials.MakeEmissive(new Color(1f, 0.08f, 0.04f), 2.2f));
            var mouth = level.Own(LevelMaterials.MakeEmissive(new Color(1f, 0.5f, 0.1f), 3f));
            var bombMat = level.Own(LevelMaterials.MakeMetal(new Color(0.05f, 0.05f, 0.05f), 0.6f, 0.7f));
            var bombLight = level.Own(LevelMaterials.MakeEmissive(new Color(1f, 0.1f, 0.05f), 8f));
            var markerMat = level.Own(new Material(mats.spot));
            markerMat.SetColor("_Color", new Color(1f, 0.08f, 0.04f));
            markerMat.SetFloat("_Intensity", 1.6f);
            var tmp = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            var sphereMesh = tmp.GetComponent<MeshFilter>().sharedMesh;
            Object.DestroyImmediate(tmp);

            foreach (var m in layout.Mortars)
            {
                var go = new GameObject("Mortar");
                go.transform.SetParent(layerRoots[m.Shell], false);
                float wallTop = terrains[m.Shell].SurfaceRadius(m.Mount) + s.wallHeight;
                // Facing along the wall, so its local x runs across it.
                var wall = layout.Grids[m.Shell].Edges[m.Wall];
                Vector3 along = Vector3.ProjectOnPlane(wall.P1 - wall.P0, m.Mount).normalized;
                go.transform.SetPositionAndRotation(m.Mount * wallTop, Quaternion.LookRotation(along, m.Mount));
                const float scale = 1.4f;
                go.transform.localScale = Vector3.one * scale;

                // The legs: from the joint out over each side of the wall, then two claws down its face.
                float face = s.wallThickness * 0.5f / scale;
                Vector3 joint = new Vector3(0f, 0.6f, 0f);
                var ball = Part(go.transform, PrimitiveType.Sphere, "Joint", metal);
                ball.localPosition = joint;
                ball.localScale = Vector3.one * 0.42f;
                foreach (float side in new[] { -1f, 1f })
                {
                    Vector3 hip = new Vector3(side * 0.12f, 0.55f, 0f), knee = new Vector3(side * (face + 0.27f), 0.32f, 0f);
                    Bone(go.transform, hip, knee, 0.12f, metal);
                    var kneeBall = Part(go.transform, PrimitiveType.Sphere, "Knee", metal);
                    kneeBall.localPosition = knee;
                    kneeBall.localScale = Vector3.one * 0.26f;
                    foreach (float z in new[] { -0.17f, 0.17f })
                    {
                        Vector3 ankle = new Vector3(side * (face + 0.15f), -0.12f, z);
                        Vector3 tip = new Vector3(side * (face + 0.03f), -0.36f, z * 1.1f);
                        Bone(go.transform, knee, ankle, 0.09f, metal);
                        Bone(go.transform, ankle, tip, 0.065f, metal);
                        var nail = Part(go.transform, PrimitiveType.Sphere, "Claw Tip", band);
                        nail.localPosition = tip;
                        nail.localScale = Vector3.one * 0.12f;
                    }
                }

                // The turret swivels on the joint to face its target; the barrel is pitched up in it.
                var turret = new GameObject("Turret").transform;
                turret.SetParent(go.transform, false);
                turret.localPosition = joint;
                var dome = Part(turret, PrimitiveType.Sphere, "Dome", metal);
                dome.localPosition = new Vector3(0f, 0.12f, 0f);
                dome.localScale = new Vector3(0.62f, 0.5f, 0.62f);
                var stripe = Part(turret, PrimitiveType.Cylinder, "Band", band);
                stripe.localPosition = new Vector3(0f, 0.1f, 0f);
                stripe.localScale = new Vector3(0.64f, 0.025f, 0.64f);
                var barrel = new GameObject("Barrel").transform;
                barrel.SetParent(turret, false);
                barrel.localPosition = new Vector3(0f, 0.2f, 0f);
                barrel.localRotation = Quaternion.Euler(35f, 0f, 0f);
                var tube = Part(barrel, PrimitiveType.Cylinder, "Tube", metal);
                tube.localPosition = new Vector3(0f, 0.42f, 0f);
                tube.localScale = new Vector3(0.32f, 0.42f, 0.32f);
                var lip = Part(barrel, PrimitiveType.Cylinder, "Mouth", mouth);
                lip.localPosition = new Vector3(0f, 0.84f, 0f);
                lip.localScale = new Vector3(0.38f, 0.04f, 0.38f);
                var muzzle = new GameObject("Muzzle").transform;
                muzzle.SetParent(barrel, false);
                muzzle.localPosition = new Vector3(0f, 0.94f, 0f);

                var mortar = go.AddComponent<Mortar>();
                mortar.turret = turret;
                mortar.barrel = barrel;
                mortar.muzzle = muzzle;
                mortar.bombParent = layerRoots[m.Shell];
                mortar.bombMesh = sphereMesh;
                mortar.bombMaterial = bombMat;
                mortar.bombLightMaterial = bombLight;
                mortar.markerMaterial = markerMat;
                mortar.glowMaterial = mats.beam;
                mortar.layer = m.Shell;
                mortar.range = s.mortarRange;
                mortar.interval = s.mortarInterval;
                mortar.flightTime = s.mortarFlightTime;
                mortar.blastRadius = s.mortarBlastRadius;
            }
        }

        /// <summary>A cylinder from <paramref name="a"/> to <paramref name="b"/> (local to <paramref name="parent"/>): a limb.</summary>
        static Transform Bone(Transform parent, Vector3 a, Vector3 b, float radius, Material mat)
        {
            var t = Part(parent, PrimitiveType.Cylinder, "Leg", mat);
            t.localPosition = (a + b) * 0.5f;
            t.localRotation = Quaternion.FromToRotation(Vector3.up, b - a);
            t.localScale = new Vector3(radius * 2f, (b - a).magnitude * 0.5f, radius * 2f);
            return t;
        }

        /// <summary>
        /// Steam off an acid pool: soft pale-green puffs rising from all over its surface, swelling, swaying and
        /// fading as they go.
        /// </summary>
        static void CreateSteam(Transform parent, Vector3 position, Vector3 up, float radius, Material mat)
        {
            var go = new GameObject("Steam");
            go.transform.SetParent(parent, false);
            go.transform.SetPositionAndRotation(position, Quaternion.FromToRotation(Vector3.up, up));
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main;
            main.loop = true;
            main.prewarm = true;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = 140;
            main.startLifetime = new ParticleSystem.MinMaxCurve(3f, 5f);
            main.startSpeed = 0f;
            main.startSize = new ParticleSystem.MinMaxCurve(2f, 3.6f);
            main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            main.startColor = new Color(0.75f, 1f, 0.7f, 1f);
            var emission = ps.emission;
            emission.rateOverTime = 24f;
            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Circle;
            shape.radius = radius;
            shape.rotation = new Vector3(90f, 0f, 0f); // the circle lies flat on the pool
            var velocity = ps.velocityOverLifetime;
            velocity.enabled = true;
            velocity.space = ParticleSystemSimulationSpace.Local;
            velocity.x = new ParticleSystem.MinMaxCurve(-0.15f, 0.15f);
            velocity.y = new ParticleSystem.MinMaxCurve(0.7f, 1.4f);
            velocity.z = new ParticleSystem.MinMaxCurve(-0.15f, 0.15f);
            var color = ps.colorOverLifetime;
            color.enabled = true;
            var fade = new Gradient();
            fade.SetKeys(
                new[] { new GradientColorKey(new Color(0.8f, 1f, 0.6f), 0f), new GradientColorKey(new Color(0.9f, 1f, 0.9f), 1f) },
                new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(0.38f, 0.15f), new GradientAlphaKey(0.2f, 0.6f), new GradientAlphaKey(0f, 1f) });
            color.color = fade;
            var size = ps.sizeOverLifetime;
            size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 0.6f, 1f, 1.7f));
            var spin = ps.rotationOverLifetime;
            spin.enabled = true;
            spin.z = new ParticleSystem.MinMaxCurve(-0.4f, 0.4f);
            var noise = ps.noise;
            noise.enabled = true;
            noise.strength = 0.35f;
            noise.frequency = 0.35f;
            noise.scrollSpeed = 0.25f;
            var renderer = go.GetComponent<ParticleSystemRenderer>();
            renderer.sharedMaterial = mat;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            ps.Play();
        }

        /// <summary>A cell's outline: the corners of its walls, in order around it.</summary>
        static List<Vector3> CellCorners(SphereGrid grid, int cell)
        {
            Vector3 c = grid.CellCenters[cell];
            var corners = new List<Vector3>();
            foreach (int nb in grid.Neighbors[cell])
            {
                var e = grid.Edges[grid.EdgeBetween(cell, nb)];
                foreach (var q in new[] { e.P0, e.P1 })
                    if (!corners.Exists(k => Vector3.Angle(k, q) < 1e-3f)) corners.Add(q);
            }
            Vector3 ax = Vector3.Cross(c, Mathf.Abs(c.y) < 0.9f ? Vector3.up : Vector3.right).normalized, ay = Vector3.Cross(c, ax);
            corners.Sort((a, b) => Mathf.Atan2(Vector3.Dot(a, ay), Vector3.Dot(a, ax)).CompareTo(Mathf.Atan2(Vector3.Dot(b, ay), Vector3.Dot(b, ax))));
            return corners;
        }

        /// <summary>A spherical patch shaped like a maze cell (fanned out from its center to its corners) at the given radius, facing out.</summary>
        static Mesh CellCapMesh(Vector3 center, List<Vector3> corners, float radius, int steps)
        {
            var verts = new List<Vector3>();
            var tris = new List<int>();
            for (int k = 0; k < corners.Count; k++)
            {
                Vector3 a = corners[k], b = corners[(k + 1) % corners.Count];
                int start = verts.Count;
                // Rows from the center outward, each a line from a-side to b-side.
                for (int i = 0; i <= steps; i++)
                    for (int j = 0; j <= i; j++)
                    {
                        float u = i / (float)steps, v = i == 0 ? 0f : j / (float)i;
                        Vector3 rim = Vector3.Lerp(a, b, v);
                        verts.Add(Vector3.Lerp(center, rim, u).normalized * radius);
                    }
                int Row(int i) => start + i * (i + 1) / 2;
                for (int i = 0; i < steps; i++)
                    for (int j = 0; j <= i; j++)
                    {
                        tris.Add(Row(i) + j); tris.Add(Row(i + 1) + j); tris.Add(Row(i + 1) + j + 1);
                        if (j < i) { tris.Add(Row(i) + j); tris.Add(Row(i + 1) + j + 1); tris.Add(Row(i) + j + 1); }
                    }
            }
            // Wind every triangle to face away from the center of the world.
            for (int t = 0; t < tris.Count; t += 3)
            {
                Vector3 n = Vector3.Cross(verts[tris[t + 1]] - verts[tris[t]], verts[tris[t + 2]] - verts[tris[t]]);
                if (Vector3.Dot(n, verts[tris[t]]) < 0f) (tris[t + 1], tris[t + 2]) = (tris[t + 2], tris[t + 1]);
            }
            var normals = verts.ConvertAll(v => v.normalized);
            var mesh = new Mesh { name = "Cell Cap", indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
            mesh.SetVertices(verts);
            mesh.SetNormals(normals);
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateBounds();
            return mesh;
        }

        /// <summary>The sides of a pit shaped like a maze cell, from <paramref name="top"/> down to <paramref name="bottom"/>, facing in.</summary>
        static Mesh PitWallMesh(Vector3 center, List<Vector3> corners, float top, float bottom, int steps)
        {
            var verts = new List<Vector3>();
            var normals = new List<Vector3>();
            var tris = new List<int>();
            for (int k = 0; k < corners.Count; k++)
            {
                Vector3 a = corners[k], b = corners[(k + 1) % corners.Count];
                int start = verts.Count;
                for (int i = 0; i <= steps; i++)
                {
                    Vector3 d = Vector3.Lerp(a, b, i / (float)steps).normalized;
                    Vector3 inward = Vector3.ProjectOnPlane(center - d, d).normalized;
                    verts.Add(d * top); verts.Add(d * bottom);
                    normals.Add(inward); normals.Add(inward);
                }
                for (int i = 0; i < steps; i++)
                {
                    int t0 = start + i * 2, b0 = t0 + 1, t1 = t0 + 2, b1 = t0 + 3;
                    // Wound to face into the pit.
                    bool flip = Vector3.Dot(Vector3.Cross(verts[b0] - verts[t0], verts[t1] - verts[t0]), normals[t0]) < 0f;
                    if (!flip) { tris.Add(t0); tris.Add(b0); tris.Add(t1); tris.Add(t1); tris.Add(b0); tris.Add(b1); }
                    else { tris.Add(t0); tris.Add(t1); tris.Add(b0); tris.Add(t1); tris.Add(b1); tris.Add(b0); }
                }
            }
            var mesh = new Mesh { name = "Pit Walls" };
            mesh.SetVertices(verts);
            mesh.SetNormals(normals);
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateBounds();
            return mesh;
        }

        /// <summary>
        /// A narrow beam following <paramref name="path"/> (points on the floor), its top just above the floor and
        /// <paramref name="depth"/> deep, with flat top, sides and ends. Also returns two thin bright strips along
        /// its top edges.
        /// </summary>
        static Mesh BridgeMesh(List<Vector3> path, float width, float depth, out Mesh edgeStrips)
        {
            // Points every half unit along the path, lifted just above the floor.
            var pts = new List<Vector3>();
            for (int k = 0; k + 1 < path.Count; k++)
            {
                int steps = Mathf.Max(1, Mathf.CeilToInt(Vector3.Distance(path[k], path[k + 1]) / 0.5f));
                for (int n = 0; n < steps; n++) pts.Add(Vector3.Lerp(path[k], path[k + 1], n / (float)steps));
            }
            pts.Add(path[path.Count - 1]);
            // On the sphere (straight lines between points would dip below the curved floor), just above it.
            float surface = path[0].magnitude;
            for (int k = 0; k < pts.Count; k++) pts[k] = pts[k].normalized * (surface + 0.02f);

            var verts = new List<Vector3>();
            var normals = new List<Vector3>();
            var tris = new List<int>();
            var eVerts = new List<Vector3>();
            var eTris = new List<int>();
            void Quad(List<Vector3> vs, List<int> ts, Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 facing, List<Vector3> ns)
            {
                int i = vs.Count;
                vs.Add(a); vs.Add(b); vs.Add(c); vs.Add(d);
                if (ns != null) { ns.Add(facing); ns.Add(facing); ns.Add(facing); ns.Add(facing); }
                bool flip = Vector3.Dot(Vector3.Cross(b - a, c - a), facing) < 0f;
                if (!flip) { ts.Add(i); ts.Add(i + 1); ts.Add(i + 2); ts.Add(i); ts.Add(i + 2); ts.Add(i + 3); }
                else { ts.Add(i); ts.Add(i + 2); ts.Add(i + 1); ts.Add(i); ts.Add(i + 3); ts.Add(i + 2); }
            }
            Vector3 Side(int k)
            {
                Vector3 up = pts[k].normalized;
                Vector3 along = pts[Mathf.Min(k + 1, pts.Count - 1)] - pts[Mathf.Max(k - 1, 0)];
                return Vector3.Cross(up, along).normalized;
            }
            for (int k = 0; k + 1 < pts.Count; k++)
            {
                Vector3 u0 = pts[k].normalized, u1 = pts[k + 1].normalized;
                Vector3 s0 = Side(k) * (width * 0.5f), s1 = Side(k + 1) * (width * 0.5f);
                Vector3 tl0 = pts[k] + s0, tr0 = pts[k] - s0, tl1 = pts[k + 1] + s1, tr1 = pts[k + 1] - s1;
                Vector3 bl0 = tl0 - u0 * depth, br0 = tr0 - u0 * depth, bl1 = tl1 - u1 * depth, br1 = tr1 - u1 * depth;
                Quad(verts, tris, tl0, tl1, tr1, tr0, u0, normals);
                Quad(verts, tris, tl0, tl1, bl1, bl0, s0.normalized, normals);
                Quad(verts, tris, tr0, tr1, br1, br0, -s0.normalized, normals);
                // Bright strips just inside both top edges.
                Vector3 e0 = s0.normalized * 0.07f, e1 = s1.normalized * 0.07f;
                Vector3 lift0 = u0 * 0.006f, lift1 = u1 * 0.006f;
                Quad(eVerts, eTris, tl0 + lift0, tl1 + lift1, tl1 - e1 + lift1, tl0 - e0 + lift0, u0, null);
                Quad(eVerts, eTris, tr0 + lift0, tr1 + lift1, tr1 + e1 + lift1, tr0 + e0 + lift0, u0, null);
            }
            // End caps.
            foreach (int k in new[] { 0, pts.Count - 1 })
            {
                Vector3 u = pts[k].normalized, sd = Side(k) * (width * 0.5f);
                Vector3 outward = (k == 0 ? pts[0] - pts[1] : pts[k] - pts[k - 1]).normalized;
                Quad(verts, tris, pts[k] + sd, pts[k] - sd, pts[k] - sd - u * depth, pts[k] + sd - u * depth, outward, normals);
            }
            var mesh = new Mesh { name = "Bridge" };
            mesh.SetVertices(verts);
            mesh.SetNormals(normals);
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateBounds();
            edgeStrips = new Mesh { name = "Bridge Edges" };
            edgeStrips.SetVertices(eVerts);
            edgeStrips.SetTriangles(eTris, 0);
            edgeStrips.RecalculateNormals();
            edgeStrips.RecalculateBounds();
            return mesh;
        }

        /// <summary>An invisible object that only collides, with this mesh.</summary>
        static void AddCollider(Transform parent, string name, Mesh mesh)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.AddComponent<MeshCollider>().sharedMesh = mesh;
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

        static Material ZoneMaterial(Material template, Color tint, float gridSize, float zoneMix, VisualStyle style)
        {
            var m = new Material(template) { color = tint };
            if (m.HasProperty("_ZoneMode")) m.SetFloat("_ZoneMode", 1f);
            if (m.HasProperty("_ZoneMix")) m.SetFloat("_ZoneMix", zoneMix);
            if (m.HasProperty("_GridSize")) m.SetFloat("_GridSize", gridSize);
            if (m.HasProperty("_DarkColor")) m.SetColor("_DarkColor", style.darkColor);
            if (m.HasProperty("_GlowIntensity")) m.SetFloat("_GlowIntensity", style.glowIntensity);
            if (m.HasProperty("_PatternStrength")) m.SetFloat("_PatternStrength", style.patternStrength);
            if (m.HasProperty("_Caustics")) m.SetFloat("_Caustics", style.caustics);
            if (m.HasProperty("_CausticScale")) m.SetFloat("_CausticScale", style.causticScale);
            return m;
        }

        // Brightness of each layer's surfaces and glow: deeper layers get a little dimmer.
        static Color GroundColor(int layer) => Color.white * (1f - 0.08f * layer);

        static Color WallColor(int layer) => Color.white * (1f - 0.08f * layer);
    }
}
