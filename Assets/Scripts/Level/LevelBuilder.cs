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

                var groundMat = level.Own(ZoneMaterial(mats.ground, GroundColor(shell), 3f, style.groundZoneMix, style));
                var shellMesh = level.Own(MeshBuilders.BuildShell(shellIco, radius, s.shellThickness, cuts, terrain));
                // Shells don't cast shadows, otherwise the outer shell would plunge every inner layer into darkness.
                CreateMeshObject(layerRoot, "Shell", shellMesh, groundMat, true, ShadowCastingMode.Off);

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
                var wallObj = CreateMeshObject(layerRoot, "Walls", wallMesh, wallMat, true, ShadowCastingMode.On);
                level.RegisterShadowCaster(shell, wallObj.GetComponent<MeshRenderer>());

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
            CreateLaserGates(level, layout, s, layerRoots, zones, mats.beam);
            CreateCameras(level, layout, s, terrains, layerRoots, mats);

            // The whole core is the exit: it glows gold, and landing on it completes the world.
            var coreRoot = layerRoots[layout.ShellCount];
            var coreMat = level.Own(LevelMaterials.MakeEmissive(mats.exit.color, 0.8f));
            var coreMesh = level.Own(MeshBuilders.BuildSphere(IcoSphere.Get(s.coreMeshSubdivisions), s.coreRadius));
            CreateMeshObject(coreRoot, "Core (Exit)", coreMesh, coreMat, true, ShadowCastingMode.Off);

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
                go.transform.SetPositionAndRotation(dir * terrains[m.Shell].SurfaceRadius(dir), Quaternion.FromToRotation(Vector3.up, dir));

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
        /// with a ring and center pulsing warning red, turning green once rolled over.
        /// </summary>
        static void CreateLaserGates(LevelInstance level, MazeLayout layout, LevelSettings s, Transform[] layerRoots,
            ZoneMap zones, Material glowTemplate)
        {
            if (layout.LaserGates.Count == 0) return;
            var offColor = new Color(0.05f, 1f, 0.2f);
            var warning = new Color(1f, 0.04f, 0.02f);
            var disarmedMat = level.Own(LevelMaterials.MakeEmissive(offColor, 1.4f));
            var metalMat = level.Own(LevelMaterials.MakeMetal(new Color(0.06f, 0.06f, 0.07f), 0.85f, 0.55f));
            var ringMesh = level.Own(MeshBuilders.BuildTorus(1.05f, 0.07f, 40, 8));
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
                var light = Part(pad.transform, PrimitiveType.Cylinder, "Light", armedMat);
                light.localPosition = new Vector3(0f, 0.06f, 0f);
                light.localScale = new Vector3(1.1f, 0.03f, 1.1f);

                var trigger = pad.AddComponent<SphereCollider>();
                trigger.isTrigger = true;
                trigger.radius = 1.2f;
                trigger.center = new Vector3(0f, 0.3f, 0f);

                var sw = pad.AddComponent<LaserSwitch>();
                sw.gate = gate;
                sw.indicators = new[] { ring.GetComponent<Renderer>(), light.GetComponent<Renderer>() };
                sw.armed = armedMat;
                sw.disarmed = disarmedMat;
                sw.warningColor = warning;
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
                var spot = CreateMeshObject(root, "Spotlight", discMesh, spotMat, false, ShadowCastingMode.Off).transform;
                spot.localScale = Vector3.one * s.spotRadius;
                var coneMat = level.Own(BeamMaterial(mats.beam, Color.white, 1f));
                coneMat.SetFloat("_BandStrength", 0f);
                var cone = CreateMeshObject(root, "Light Cone", coneMesh, coneMat, false, ShadowCastingMode.Off).transform;

                var laser = Part(root, PrimitiveType.Cylinder, "Camera Laser", laserMat);

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
