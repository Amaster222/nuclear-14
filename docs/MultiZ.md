# Multi-Z Levels

Multi-Z lets you stack maps vertically so players can move between floors, roofs, basements, and underground areas. Each level is a separate map file linked together by the game mode.

This system was ported from ColonialMarinesUniverse (AU-14), which adapted it from Crystall Edge. Performance improvements came from TTMC.

## How it works

Each Z-level is just a normal SS14 map file. Three new fields on the `gameMap` prototype tell the server which maps belong together:

```yaml
- type: gameMap
  id: ExampleMap
  mapPath: /Maps/example_ground.yml      # depth 0, main level
  mapsAbove:
    - /Maps/example_roof.yml             # depth +1
  mapsBelow:
    - /Maps/example_basement.yml         # depth -1
```

When the round starts, the server loads all three maps, creates a "Z-network" to link them, and wires their grids into the station automatically. You don't need to run any extra commands.

## Creating map files

Use the normal map editor for every level:

```bash
mapeditor-open /Maps/example_ground.yml
mapeditor-open /Maps/example_roof.yml
mapeditor-open /Maps/example_basement.yml
```

The most important rule: X/Y coordinates must line up across levels. A hole at (15, 42) on the roof will show whatever exists at (15, 42) on the ground level. If the coordinates don't match, openings won't line up with the rooms below.

## Making openings

An "opening" is any place where players can see or fall through to another Z-level. You create one by erasing tiles.

In the map editor, use the eraser tool to remove floor tiles where you want a hole. The system detects empty tiles as openings automatically. A stairwell is just a 3x3 empty square on the upper map, positioned directly above the staircase on the lower map.

Openings also work for skylights. Punch a hole in the roof map and light from the sky (if you set up lighting) will shine down through it.

## Ladders

Ladders move players between levels. Place a `MZLadder` component on a ladder entity:

```yaml
- type: entity
  id: LadderUp
  components:
    - type: MZLadder
      offset: 1       # positive goes up, negative goes down
      delay: 2        # seconds to climb
```

Put the ladder on the ground level with offset 1 to go up. Put a matching ladder on the roof level with offset -1 to come back down. Players interact with the ladder (E key by default), wait through the climb timer, and appear on the other level.

The old Warper/WarpPoint teleporter system still works fine for instant transitions if you prefer that style.

## Ramps and stairs

For walkable slopes, use `MZHighGround`. This component creates a height curve that players can walk up:

```yaml
- type: entity
  id: StairRamp
  components:
    - type: MZHighGround
      heightCurve: [0.0, 0.25, 0.5, 0.75, 1.05]
      stick: true
      previewUpLevel: true
```

The height curve goes from 0 (ground) to just above 1.05 (transition to the next level up). Each number is a sample point along the ramp. Two points of `[1.05, 1.05]` makes a flat elevated surface like a table or low wall.

Set `stick: true` so players don't slide off. Set `previewUpLevel: true` to automatically show a faint preview of the level above when a player walks near the ramp.

## Falling

Add `MZPhysics` to any entity that should be affected by Z-level gravity:

```yaml
- type: entity
  id: MobHuman
  components:
    - type: MZPhysics
      bounciness: 0.3
```

When a player walks over an empty tile (an opening), the system adds a `MZFalling` marker and gravity pulls them down. They transition to the map below and take fall damage based on how fast they were going.

Bounciness controls how much they bounce on impact. 0 means they stop dead. 0.3 means they bounce back up slightly. Higher values make for a trampoline effect.

## Ghost movement

Add `MZGhostMover` to your ghost observer prototype and ghosts get two action buttons: Move Up and Move Down. These appear in the action bar automatically. Clicking one teleports the ghost to the adjacent Z-level instantly.

```yaml
- type: entity
  id: MobObserver
  components:
    - type: MZGhostMover
```

## Cross-level shooting and visibility

Players can look up through openings, and with look-up mode toggled they can see entities and lights on the level above. The look-up action (default keybind) cycles through three states: normal, faint preview (ghosts the level above at low alpha), and full look-up (shifts aim upward).

Lights on adjacent levels project dim colored circles through openings so you can tell where light sources are without actually seeing them.

## Debugging

Check that things are working:

```
multi_z.enabled true
```

Watch the server log when a round starts. You should see lines like:

```
Created map 42 for Station zNetwork at level 1
Created map 43 for Station zNetwork at level -1
```

Use admin commands to inspect the network:

```
> comp MZNetworkComponent
> comp MZMapComponent <mapEntityUid>
```

## Setting up a test map (checklist)

- [ ] Create 2+ map files with matching X/Y grid coordinates
- [ ] Add `mapsAbove` and/or `mapsBelow` to the gameMap prototype
- [ ] Erase tiles to make openings at matching positions on both levels
- [ ] Place `MZLadder` entities on both levels with matching offsets
- [ ] Add `MZHighGround` for any ramps or stairs
- [ ] Add `MZPhysics` to player or mob prototypes that should fall
- [ ] Add `MZGhostMover` to the ghost observer prototype
- [ ] Start a round and check the server log for zNetwork messages

## CVars reference

| CVar | Default | What it does |
|------|---------|--------------|
| `multi_z.enabled` | true | Master toggle for the whole system |
| `multi_z.render_enabled` | true | Client-side rendering of adjacent levels |
| `multi_z.blur_enabled` | true | Dark tint when looking between levels |
| `multi_z.blur_strength` | 1.0 | How strong the blur tint is |
| `multi_z.faint_upper_enabled` | true | Ghost preview of the level above |
| `multi_z.faint_upper_alpha` | 0.14 | Opacity of the ghost preview |
| `multi_z.max_render_depth` | 8 | How many levels apart can still render |
| `multi_z.cross_z_audio` | true | Sound travels between Z-levels |
| `multi_z.visible_entity_indicators` | true | Show dots for entities on other levels |
| `multi_z.projected_lighting` | true | Show light from adjacent levels |
