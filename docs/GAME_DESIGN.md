# Game Design Overview

The reference document for what this project is. Read this before designing or implementing any gameplay system.

## Concept

An ocean survival/exploration game set on an infinite procedurally generated sea dotted with procedurally generated islands. Inspired by *Raft*, but **intentionally divergent** in how the player navigates and progresses.

The player begins on a small island/raft with nothing, and works outward from there.

## Core Loop

1. Arrive at an island
2. Explore it
3. Discover a clue
4. Interpret the clue to figure out where the next island is
5. Sail there
6. Repeat

Everything else (crafting, farming, fishing, gathering) exists to sustain and enable this loop, not to replace it.

## Island Types

| Tier | Contents |
|------|----------|
| Common | Trees, rocks, plants |
| Uncommon | Animals, ores |
| Rare | Shipwrecks, old houses |
| Special / progression | Clues that advance the main journey toward a final destination |

Common and uncommon islands supply the resource economy. Rare islands are the reward for exploring off the critical path. Special islands are the spine of the story and the only places the main journey advances.

## Navigation & Progression

This is the system that most defines the game, and the one that most deliberately departs from *Raft*'s receiver-and-coordinates loop.

**There is no complete world map and no GPS marker.** The player finds the next island by reading clues and reading the environment.

Three methods, layered:

- **Journal entries** — found on islands, describing the next location in evocative, non-numeric language: direction, distance-band ("many days east"), biome, weather. Never coordinates.
- **Migratory guide creatures** — birds and dolphins that, once triggered by a clue, travel toward the next point of interest. The player follows them.
- **Constellations** — celestial navigation at night as a way to hold a heading.

### Design intent

- Do not feel like a reskin of *Raft*'s receiver/code system.
- Finding the next island should feel like **genuine discovery**, not like following a waypoint.
- The player should feel like they are **figuring out the world**, not being told about it.

When a feature would make navigation more convenient at the cost of that feeling, the feeling wins. Any UI that resolves a clue into an exact marker is a violation of this intent.

## Other Core Systems

- Crafting
- Farming
- Fishing
- Resource gathering

These are conventional survival-game systems and are expected to behave conventionally. They are the support structure for exploration, so they should be readable and low-friction rather than novel.

## Open Questions

- What the final destination is, and what reaching it means
- How many special/progression islands make up the main journey
- How the three navigation methods hand off to each other (does a journal entry always trigger a guide creature, or are they alternates?)
- How the world seeds and persists islands the player has already visited
