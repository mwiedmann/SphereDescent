# Glossary

The words we use when talking about Sphere Descent, and what each is called in the code where that differs.

## Structure

| Term | Meaning | In the code |
|---|---|---|
| **World** | One full run, from the surface down to the core. Each world has a new maze. | `world` in `GameManager` |
| **Sphere** | One walkable maze layer. There are four, from the **surface** inward. | *shell* (`shellRadii`); *layer* when the core is counted too |
| **Core** | The ball of lava at the center, with hilly wall-less paths over it: the boss battle that ends each world. | *core*, the last layer; `CoreLayout` |
| **Energy ball** | The pulsing ball trapped in the middle of the core's walls. Rolling into it once the walls are down frees it and ends the world. | `EnergyOrb` |
| **Core walls** | The six concentric glowing walls around the energy ball. Each key pad's cable breaks the outermost one still standing. | `CoreBarrier` |
| **Lava rain** | Blobs of lava that drop onto the core every few seconds, aimed ahead of the ball, each with a dark shadow ringed in red where it will land. Stops once the energy ball is freed. | `LavaRain`, `LavaBlob` |
| **Lava** | Everything on the core off the paths. Rolling into it blows the ball up, and it comes back where it landed. | `CoreArena` |
| **Section** | The stretch of maze from where you arrive on a sphere (through a hole or off a pad) to the hole or pad that takes you to the next one. A world is a chain of them. | `MazeLayout.Section` |
| **First half / second half** | The part of a section before or after its main (halfway) checkpoint. Cubes and major hazards are placed per half. | `cubesPerHalfSection`, `majorHazardsPerHalf` |

## Inside a section

| Term | Meaning | In the code |
|---|---|---|
| **Cell** | One room of the maze grid, about 16 units across. Every sphere has its own grid. | `SphereGrid` cells |
| **Doorway** | An opening between two neighbouring cells (a wall that isn't there). | an open edge (`Open[shell][edge]`) |
| **Route** | The shortest way through a section, from where you arrive to its exit. | `MazeLayout.Route` |
| **Side lane** | A branch off the route that rejoins it: another way through. | |
| **Spur** | A short dead-end side path, such as the one leading to a laser gate's key pad. | `SwitchInSpur` |
| **Choke point** | A doorway every way through the section has to cross. Laser gates and camera runs go on these. | `MazeLayout.Chokepoints` |
| **Filler** | The sealed-off maze between sections, there to make every sphere look like a maze. You can't get into it. | `CarveFiller`, region `-1` |

## Between spheres

| Term | Meaning | In the code |
|---|---|---|
| **Drop hole** | An orange-ringed hole you fall through to the sphere below. | a hole with `TwoWay` false |
| **Climb hole** | A cyan-ringed hole you come up through, above a boost pad. | a hole with `TwoWay` true |
| **Boost pad** | Launches you up through the climb hole above it. | `BoostPad` |
| **Landing spot** | Where you arrive on the next sphere. | the next section's `EntryCell` |
| **Sealed** | A used hole or pad, covered with a black disc marked with a red X. | `LevelInstance.MarkUsed` |

## Finding your way and starting over

| Term | Meaning | In the code |
|---|---|---|
| **Spawn** | Where a world starts. | `SpawnPosition` |
| **Checkpoint** | A flag pad along a section that turns green once reached. The **main checkpoint** is halfway along and splits the section into halves; on the first two spheres there are also **extra checkpoints** a quarter and three quarters of the way. | `CheckpointPad`, `Checkpoint.Main` |
| **Respawn point** | Where you come back after being blown up: the landing spot on that sphere, or the furthest checkpoint reached on it. | `checkpoint` in `BallController` |
| **Zone** | One of the 12 named color regions on each sphere. They line up through every layer. | `ZoneMap` |
| **Exit beam** | The column of light over the hole or pad that ends the section you're in. | light beams, `SetBeamSection` |
| **Floor arrows** | Arrows on the floor every few cells pointing along the route. | `MazeLayout.Arrows` |
| **Style** | The look of a world: Synthwave, Deep Sea or Frozen. | `VisualStyle` |

## Hazards

| Term | Meaning | In the code |
|---|---|---|
| **Major hazard** | A laser gate, camera run, acid pool or mortar: the big obstacles, two in each half section on the first two spheres and one on the innermost ones, never the same kind twice in a half, each kind used about equally. Mines are not major hazards; they're spread through every section. | `MazeLayout.MajorHazard`, `majorHazardsPerHalf` |
| **Mine group** | A cluster of mines in one cell, including its **wall mines** (one beside each wall, so you can't hug the wall past). | `MineGroups`, `Mine.NearWall` |
| **Laser gate** | Horizontal lasers filling a choke point. | `LaserGate` |
| **Key pad** | The pad that switches a laser gate off, marked with a key, usually down a spur. The core has six more, one per core wall, worth 10 points each. | `LaserSwitch` |
| **Cable** | The glowing line that lights up along the floor from the key pad to the gate (or core wall); the lasers go off, or the wall breaks, when it arrives. | the switch's cable mesh |
| **Camera run** | Three security cameras in a row, on poles above the walls on alternating sides. | `SecurityCamera`, `camerasPerGroup` |
| **Sweep pattern** | How a camera's **spotlight** moves: **figure-8**, **orbit** or **sweep**. | `SweepPattern` |
| **Acid pool** | A cell on the route (entered through a choke point) sunk into a pit of acid; falling in destroys the ball. | `AcidPool`, `MazeLayout.AcidPools` |
| **Bridge** | The narrow beam across an acid pool, from the doorway in to the doorway out, with a round landing where it bends. | `acidBridgeWidth` |
| **Mortar** | A turret on top of a wall that lobs bombs at the ball while it's in range, aimed where the ball will be if it keeps rolling the same way. | `Mortar`, `MazeLayout.Mortars` |
| **Bomb marker** | The flashing red circle where a mortar bomb will land; it's as wide as the blast. | `MortarBomb` |

## Pickups

| Term | Meaning | In the code |
|---|---|---|
| **Energy cube** | A glowing gold cube worth 10 points. | `EnergyCube` |
| **Super cube** | The larger magenta cube in each half of a section, worth 50. | `Cube.Super` |
| **Route cube / hidden cube** | A cube in the middle of the route, or one tucked away down a side lane. | `Cube.Hidden` |

## Scores

| Term | Meaning | In the code |
|---|---|---|
| **Section score** | Points from the cubes and checkpoints (5 each) of the section you're in, out of everything in it. Counts up live. | `Scoreboard.WorldScore.Earned` |
| **Core score** | The core, scored like one more section after the last: its cubes, key pads and freeing the energy ball (100). Shown as **The Core** on the score screen. | `WorldScore.CoreIndex` |
| **Finishing a section** | Dropping through its hole, launching off its pad, or (for the core) freeing the energy ball: only then do its points count toward the world and game. | `Scoreboard.Finish` |
| **World score** | Points from the world's finished sections, out of what those sections were worth: how well you've done so far. | `WorldScore.Banked`, `PossibleSoFar` |
| **Game score** | The world scores of this run added up, the same way. Picking a world from the M menu starts a new run. | `Scoreboard.GameBanked`, `GamePossible` |
| **Tally** | A finished section's score shown mid-screen, with the world score counting up to include it. | `GameManager.ShowTally` |
| **Score screens** | After a world: its score for each section and the world total, then (from the second world on) each world's score and the game total. | `GameManager.DrawSummary` |
