# Project

Unity 6 (6000.3.6f1) ocean survival/exploration game.

**Read [docs/GAME_DESIGN.md](docs/GAME_DESIGN.md) before working on any gameplay system.** It defines the concept, core loop, island tiers, and — most importantly — the clue-based navigation system that deliberately replaces *Raft*'s receiver/coordinates approach. Design decisions should be checked against the design intent section there.

## Layout

- `Assets/Personal/` — our code and content (`Scripts`, `Prefabs`, `Scenes`, `Settings`)
- `Assets/Imported/` — third-party packages (CorePro, EditorAttributes, UGUI Anchor Automatically). Don't edit these.

## Conventions

**Every script uses [EditorAttributes](Assets/Imported/EditorAttributes) for its inspector.** Prefer it over Unity's built-ins: `[Title]` over `[Header]`, plus `[Required]`, `[Suffix]`, `[Clamp]`, `[MinMaxSlider]`, `[HelpBox]`, `[ShowInInspector]`, `[Button]`, `[OnValueChanged]`. Attribute stacking works in Unity 6's UI Toolkit inspector, but the package's own samples only ever stack two — keep it to two property drawers per field and fall back to `[Tooltip]` for the third.

Scripts are prefixed by system and live in a matching folder — `Assets/Personal/Scripts/Player/Player_Motor.cs`. No C# namespaces; the prefix is the namespace.

Input goes through the new Input System via `Assets/Personal/InputSystem_Actions.inputactions`, which generates its wrapper class into `Assets/Personal/InputSystem_Actions.cs` (global namespace, class `InputSystem_Actions`). Don't edit the generated file. Gameplay code reads input through the static `Game_Input` service, never through the wrapper or an `InputActionAsset` directly.

## Infinite world & the floating origin

The world is unbounded, so the scene is kept near origin instead of letting the player sail out into float-precision noise. [World_Origin](Assets/Personal/Scripts/World/World_Origin.cs) slides every root object back once the player passes a threshold, then raises `OriginShifted`.

**This means `transform.position` is scene-space and meaningless as a world location.** Anything that has to know where something really is — generation, island placement, clue targets, save data, distances between islands — uses [World_Coord](Assets/Personal/Scripts/World/World_Coord.cs) (double precision) via `World_Origin.WorldOf(...)` / `LocalOf(...)`.

Two rules for new world systems:
- Parent generated content under a few chunk roots. Rebase cost is per *root* object, not per object.
- Anything caching a world-space position, or holding physics state a teleport would smear, subscribes to `World_Origin.OriginShifted`. Screen/camera-space objects opt out with `World_OriginIgnore`.
