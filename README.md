# PeekABoo — Concealment Icon Exploit Fix

**Version:** 3.0.1 — *"Freeze mode, throttled"*
**Author:** YandrosTheSane

## What It Does

PeekABoo fixes a UI information leak that lets the player detect hidden enemies through the concealment icon. 

Without this mod, the concealment icon disappears whenever any enemy gains line of sight to your unit — even if that enemy is deep in the fog of war and completely invisible to you. 

The disappearing icon gives you free intel you shouldn't have.

## The Problem

When an enemy spots your unit, the game removes the concealment icon by setting `Actor.m_VisibilityToAI` to Visible. It never checks whether the player can actually see that enemy.

The result: a hidden enemy 9 tiles away (outside your vision range of 7) spots you, and your concealment icon disappears — telling you "someone is watching from that direction."

### Meta information abuse

```
Enemy (hidden, 9 tiles away)
|
|  enemy vision range covers you
|
Your Unit (vision range 7)

You can't see the enemy. But your concealment icon disappears,
revealing their presence.
```

If you move closer (within your own vision range), the enemy becomes visible and the icon legitimately isn't there — that's normal behavior.

### Root Cause

The concealment icon is driven by `Actor.m_VisibilityToAI`:

| Value | Meaning |
|-------|---------|
| Visible (1) | Icon removed — "enemies can see you" |
| Hidden (2) | Icon shown — "you are concealed" |

The game sets this to Visible whenever any enemy gains line of sight to your unit — it doesn't check whether you can actually see that enemy.

## How It Currently Works

### Core Mechanism: Freeze Mode

PeekABoo takes full control of `m_VisibilityToAI` and forces it to the correct value:

1. For each player unit, check all enemies on the map
2. Does any enemy have line of sight to this unit? (`CanActorSee`)
3. Is that enemy visible or detected by the player? (`GetVisibilityState`)
4. If a visible/detected enemy sees you -> allow icon removal (you know they're there)
5. If only hidden enemies see you -> force concealment icon to stay (no free intel)

### Performance

The expensive LOS scan (iterating all player x enemy pairs) no longer runs every frame. Instead the mod separates the work into two parts:

- **Recompute** (every 5 frames): full LOS scan, rebuilds a cache of per-unit visibility results
- **Apply** (every frame, dual-pass): sets `m_VisibilityToAI` from cache — no LOS calls, essentially free

The dual-pass apply (OnUpdate + coroutine) is kept to prevent flicker from game overwrites between Unity update phases.

Measured with 1 player unit and 38 enemies:

| | Average | Max |
|---|---------|-----|
| Recompute (every 5 frames) | ~150us | ~300us |
| Apply (every frame) | ~1us | ~18us |

At 60fps a frame budget is 16.6ms. The mod uses ~0.15ms every 5th frame and ~0.001ms on the others.

### Gameplay Effect

As a side effect of the throttled scan, enemy visibility during their turn now behaves in a more natural way. When an enemy outside your vision takes an offensive action against your unit (e.g. shooting), the game briefly flips their visibility state — the mod picks this up and you see the action play out. Once the action ends their position is lost and reverts to a "last known position" marker. You get to see what happened to your unit but you don't get to keep tracking the enemy afterwards.

This feels like the right tactical experience: you know you got shot at and from where, but the enemy fades back into the fog of war.

### Is AI Behavior Impacted?

As far as I can tell `m_VisibilityToAI` is UI-only — it does not affect AI firing behavior. I verified this by forcing it to Hidden while a visible enemy had line of sight, then ending the turn. The enemy still fired. The AI uses `CanActorSee()` directly for targeting, not this field.

## Complementary Mods

- [BooAPeek ~ By YandrosTheSane](https://www.nexusmods.com/menace/mods/73) — Fixes the mirror-image problem: the AI's illegitimate knowledge of concealed *player* positions. Without it, enemies flee from and take cover against units they've never seen.
- [Wake Up ~ By Pylkij](https://www.nexusmods.com/menace/mods/36)

With those 3 mods (AI is active, You don't get free intel, They don't get free intel) you can get into situation like this in one turn.

Later down the road there will be a need for rebalance.

## Installation

Use the https://github.com/p0ss/MenaceAssetPacker/releases to deploy (build the sources) and activate the mod.

## Current State & Known Limitations

### What v3.0.1 Does Well

- Eliminates the player's illegitimate knowledge of hidden enemy positions via the concealment icon
- Concealment icon only changes when a visible/detected enemy has LOS — no more free intel from fog-of-war enemies
- Enemy offensive actions are briefly revealed then fade back to last-known-position markers
- Negligible performance impact (~0.15ms every 5th frame)
- Stable across all tested scenarios — no flicker, no crashes

### What v3.0.1 Does NOT Do

- **Not a full fog-of-war system:** PeekABoo only controls the concealment icon (`m_VisibilityToAI`). Other UI elements that might leak information are not addressed.
- **5-frame scan delay:** The LOS cache can be up to 5 frames stale. In practice this is imperceptible (~83ms at 60fps).

## Settings

Configurable via the in-game Modkit settings panel:

| Setting | Default | Description |
|---------|---------|-------------|
| **Debug Logging** | Off | Logs periodic perf summaries with recompute/apply timings and actor counts |

## Investigation & Testing

Five approaches were tested before arriving at the current freeze mode. Full investigation details, ruled-out fixes, SDK methods reference, visibility states, and performance benchmarks in [docs/CONCEALMENT_ANALYSIS.md](docs/CONCEALMENT_ANALYSIS.md).

## File Structure

```
PeekABoo-modpack/
├── modpack.json              # Mod metadata and load order
├── src/
│   └── PeekABooPlugin.cs    # Plugin source (IModpackPlugin)
├── docs/
│   └── CONCEALMENT_ANALYSIS.md  # Full investigation with ruled-out fixes and SDK reference
├── CHANGELOG.md              # Version history
└── README.md                 # This file
```

## Credits

- **MenaceAssetPacker** — https://github.com/p0ss/MenaceAssetPacker — without that I wouldn't have dreamed to do this fix
- **"Emo Used HM01"** — https://www.nexusmods.com/menace/mods/16 — comprehensive modding structure for modders

## Requirements

- Menace with MelonLoader
- Menace ModpackLoader
