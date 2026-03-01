# PeekABoo — Concealment Icon Investigation

Note that this is a summary of a lot of raw output and interactions. If this helps any of you it's a net benefit but don't take it to heart and check for yourself.

## Root Cause

The concealment icon is driven by `Actor.m_VisibilityToAI`:

| Value | Meaning |
|-------|---------|
| Visible (1) | Icon removed — "enemies can see you" |
| Hidden (2) | Icon shown — "you are concealed" |

The game sets this to Visible whenever any enemy gains line of sight to your unit — it doesn't check whether you can actually see that enemy. This is the bug.

## Visibility States

| Value | Name | Meaning |
|-------|------|---------|
| 0 | Unknown | Not yet evaluated |
| 1 | Visible | Player can see this actor |
| 2 | Hidden | In fog of war |
| 3 | Detected | Radar blip, not directly seen |

## SDK Methods Used

| Method | Purpose |
|--------|---------|
| `EntitySpawner.ListEntities(-1)` | Get all actors across all factions |
| `GameObj.ReadInt("m_FactionID")` | Identify player (1/2) vs enemy factions |
| `GameObj.As<Actor>().m_VisibilityToAI` | Read/write the concealment icon state |
| `LineOfSight.CanActorSee(enemy, player)` | Check if enemy has LOS to player (directional) |
| `LineOfSight.GetVisibilityState(enemy)` | Check if player can see the enemy |

## Ruled-Out Fixes

| Approach | Why It Failed |
|----------|---------------|
| Harmony prefix on `OnDiscovered` | Native IL2CPP dispatch bypasses managed Harmony patches |
| Checking `m_DiscoveredMask` | Tracks discovery history, does not drive the icon |
| `GameObj.GetName()` on actors | Returns null — must use `ReadObj("m_Template").GetName()` |
| `ReadInt("VisibilityState")` | Resolves to wrong memory offset; must use `LineOfSight.GetVisibilityState()` |
| Reactive polling (set Hidden only when game sets Visible) | Flickers during movement/abilities — game continuously re-sets the value during actions |

## Fix Evolution

### v1.0.0 — Harmony Prefix on `OnDiscovered` (Failed)

Initial attempt to intercept the concealment state change via Harmony. Patches registered and worked via REPL but never fired during gameplay — native IL2CPP vtable dispatch bypasses managed Harmony patches entirely.

### v2.0.0 — Reactive Polling (Superseded)

Watched for the game setting `m_VisibilityToAI` to Visible and corrected it to Hidden. Worked in principle but produced visible flicker during movement and abilities — the game continuously re-sets the value during actions, creating a gap between its write and our correction.

### v3.0.0 — Freeze Mode (Current)

Takes full ownership of `m_VisibilityToAI` and forces the correct value every frame (dual-pass: OnUpdate + coroutine). Replaced reactive polling — no flicker since we write every frame in two phases.

### v3.0.1 — Performance Throttle (Current)

The expensive LOS scan (iterating all player x enemy pairs) was moved to every 5th frame. Cached results are applied every frame (both passes) to prevent flicker. Eliminated a redundant full LOS scan on the coroutine late pass.

## Technical Details

- **Frame loop:** `OnUpdate` runs every frame; recompute triggers every 5th frame, apply runs every frame
- **Dual-pass apply:** Both `OnUpdate` and a coroutine apply cached results each frame to prevent flicker from game overwrites between Unity update phases
- **LOS checks:** Uses `LineOfSight.CanActorSee(enemy, playerUnit)` — the same function the game uses internally
- **Visibility checks:** Uses `LineOfSight.GetVisibilityState(enemy)` to determine if the player can see the enemy
- **Actor enumeration:** `EntitySpawner.ListEntities(-1)` for all actors across all factions
- **Faction filtering:** Only processes player units (factions 1–2); enemies are everything else
- **Scene lifecycle:** State fully reset on scene transitions; cache cleared when leaving tactical
- **Init delay:** 60-frame delay after scene load for scene initialization
- **Perf tracking:** When Debug Logging is enabled, logs periodic summary (~every 5s) with average/max timings for recompute and apply phases plus actor counts

## AI Behavior Verification

`m_VisibilityToAI` is UI-only — it does not affect AI firing behavior. Verified by forcing it to Hidden while a visible enemy had line of sight, then ending the turn. The enemy still fired. The AI uses `CanActorSee()` directly for targeting, not this field.

## Performance Benchmarks

Measured with 1 player unit and 38 enemies:

| | Average | Max |
|---|---------|-----|
| Recompute (every 5 frames) | ~150us | ~300us |
| Apply (every frame) | ~1us | ~18us |

At 60fps a frame budget is 16.6ms. The mod uses ~0.15ms every 5th frame and ~0.001ms on the others.
