# Main Scene edits — 2026-08-21

Hand-edits made directly to `Assets/Personal/Scenes/Main Scene.unity` to finish the player rig and the floating-origin wiring.

## Reverting

The exact original file is at `Backups/Main Scene.unity.bak` (outside `Assets/`, so Unity ignores it).

```bash
cp "Backups/Main Scene.unity.bak" "Assets/Personal/Scenes/Main Scene.unity"
```

Then let Unity reload the scene. If the editor has it open with unsaved changes, close the scene **without saving** first, or the in-memory copy will overwrite whatever is on disk.

## Hierarchy: before and after

```
BEFORE                                AFTER
Player                                Player
  Main Camera                           Main Camera
Directional Light                     Directional Light
Global Volume                         Global Volume
Manager                               Manager            <- World_Origin moved here
  World          <- World_Origin      World              <- now a scene root
    Ocean                               Ocean
Plane                                 Plane
```

## Changes

### Player (`&149330786`)

| Change | Why |
|---|---|
| Removed `CapsuleCollider` (`&149330792`) | **This was the important one.** `Player_Movement`'s ground and headroom checks filter out the `CharacterController`'s own collider, but nothing else. A second collider on the same object is seen as environment, so `CheckGrounded()` was always true (infinite jumping) and `HasHeadroom()` always false (permanently stuck crouched). |
| Removed `MeshRenderer` (`&149330793`) and `MeshFilter` (`&149330794`) | Leftover capsule primitive. Invisible from inside in first person, but it still cast a capsule shadow around the camera. For a third-person debug visual, add it as a child object instead. |
| `CharacterController` center `(0,0,0)` → `(0,1,0)` | Puts the capsule's feet at the transform pivot. `Player_Movement` derives `feetLocalY` from center and height, and keeps the feet planted while the capsule resizes for crouch — this makes the numbers line up with what you see. |
| `CharacterController` radius `0.5` → `0.35` | 1 m wide was too fat to fit through normal gaps. |
| `CharacterController` skin width `0.08` → `0.035` | Skin width should be roughly 10% of radius. At 0.08 against a 0.35 radius the capsule floats visibly off surfaces. |
| `CharacterController` min move distance `0.001` → `0` | Non-zero values discard small movements, which shows up as stutter at low speed. |
| Transform y `1.84` → `0.1` | With feet now at the pivot, `1.84` started the player nearly two metres in the air. `0.1` drops him onto the Plane immediately. |

### Main Camera (`&330585543`)

| Change | Why |
|---|---|
| Local position y `0.86` → `1.8` | Matches `eyeHeightRatio` 0.9 × capsule height 2. Cosmetic only — `Player_Movement` overwrites local Y every frame — but it makes the scene view match play mode. |

This object *is* the camera pivot; `Player_Movement.cameraPivot` already points at it. Its local rotation is also driven every frame, so authored rotation is ignored.

### World_Origin — moved from `World` to `Manager`

It was on `World`, which was a child of `Manager`, with its `anchor` pointing at its own transform. Two bugs from that:

1. **It could never rebase.** Anchor was itself, so measured drift was always ~0.
2. **Even if it had, the world would not have moved.** `World_Origin` excludes its own `transform.root` from the shift. Its root was `Manager`, so the whole `Manager` subtree — including `World` and the `Ocean` under it — was exempt. The player would have slid back to origin while the ocean stayed behind.

| Change | Why |
|---|---|
| `World_Origin` component (`&1350002294`) moved from `World` to `Manager` | `Manager` is a root object holding only logic, which is what the exclusion rule wants. |
| `World` unparented from `Manager` to scene root, and added to `SceneRoots` | So `World` and everything under it is no longer inside the excluded hierarchy and shifts normally. |
| `anchor` → Player's transform (`&149330795`) | What the world rebases around. `Player_Controller.actAsWorldAnchor` also sets this at `Start`, so it is now correct both in the inspector and at runtime. |

## Not touched

- The `Ocean` object's `MeshFilter` has no mesh assigned and `World_InfiniteOcean` will not render anything until one is. That is mid-setup work with the LowPolyWater package, left alone deliberately.
- No `World_OriginIgnore` was added anywhere — there is no UI canvas or origin-parked object in the scene yet that needs it.
