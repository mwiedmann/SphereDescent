# Dev tools

Testing and video tools used while building Sphere Descent. They live outside `Assets/`, so Unity doesn't compile them into the game. To use them, copy the project to a scratch folder, then put these scripts into that copy (never into the real project):

- `Editor/` goes in the copy's `Assets/Editor/`.
- `Runtime/` goes in the copy's `Assets/Scripts/ZProbe/`.

Unity binary: `/Applications/Unity/Hub/Editor/6000.6.6f1/Unity.app/Contents/MacOS/Unity`. Batch mode can't open a project the editor already has open, which is why the tools run on a copy. Keep the copy in sync with `rsync -a Assets/ <copy>/Assets/`.

## Play-mode probe

`Editor/PlayProbeRunner.cs` opens `Main.unity`, enters Play mode, adds `PlayProbe` and quits once `PlayProbe.Done` is set. Run it without `-quit`:

```
Unity -batchmode [-nographics] -projectPath <copy> -executeMethod SphereDescent.EditorTools.PlayProbeRunner.Run -logFile probe.log
```

`Runtime/PlayProbe.cs` is currently set to start the video autopilot. Swap its body for any one-off Play-mode test.

## Gameplay video (`Runtime/VideoPilot.cs`)

An autopilot plays world 1's first section the way a person would:

- **Speed:** about 95% of full, 75% near major hazards.
- **Route:** A* over a fine grid of floor points, clear of walls, mines and holes. Over acid, only the bridge counts as floor.
- **Laser gate:** goes down the side path to the pad, then waits for the cable.
- **Camera run:** one doorway at a time, timed by predicting each spotlight's sweep.
- **Mortar:** dodges each bomb by stopping, swerving or speeding up.
- **Pickups:** hits the checkpoints and a few cubes, including one down a side lane.
- **Camera:** turned to face forward after any reversal.

First do a dry run with `-nographics` and no capture. Then record with graphics:

```
VIDEO_CAPTURE=1 PREVIEW_DIR=<frames dir> Unity -batchmode -projectPath <copy> -executeMethod SphereDescent.EditorTools.PlayProbeRunner.Run -logFile video.log
```

It writes JPEG frames at 30 fps. The game's HUD is drawn with OnGUI, so it isn't in the frames.

## Encoder (`encoder/`)

Build the two Swift tools:

```
swiftc -O encode.swift -o encode
swiftc -O sheet.swift -o sheet
```

- `encode <frames dir> <out.mp4> [fps]` turns PNG or JPEG frames into an H.264 MP4.
- `sheet <frames dir> <out.png> <every> <columns> [tile width]` makes a contact sheet.

## Others

- `Editor/CoreShot.cs`: renders a world's core (`WORLD=n PREVIEW_DIR=<dir>`) from above, from the landing, at the walls and at two key pads, with every cable lit.
- `Runtime/StyleCycleProbe.cs.txt`: a `PlayProbe` body that skips through seven worlds and logs each one's style, to check they take turns in order.
- `Runtime/MeteorProbe.cs.txt`: a `PlayProbe` body that captures frames of a world's opening meteor arrival and checks the ball ends up at the start, visible and steerable.
- `Runtime/CoreProbe.cs.txt`: a `PlayProbe` body that plays out the core: lava, all six key pads, the cubes, freeing the energy ball and the score screens, logging each step. Batch mode starts with the keyboard disabled, so it enables it before simulating a press.
- `Editor/StylePatternShot.cs`: the same for the Frozen, Ruins or Nebula style's own patterns, under its sky (`STYLE=Frozen|Ruins|Nebula`).
- `Editor/PatternShot.cs`: renders Deep Sea floors in a zone of each of the four patterns, once with the geometric set and once with the organic set, for comparing.
- `Editor/AcidShot.cs`: renders acid pools from several angles. A magenta background shows up see-through gaps.
- `Editor/StyleShot.cs`: renders the visual styles.
- `Runtime/PerfProbe.cs`: measures frame time in Play mode.
