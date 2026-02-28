# PeekABoo -- Concealment Exploit Fix

Hi there :)

Here are the details in a more human friendly way. English not being my FL I apologise if the reading isn't fluid.

## The Problem

In vanilla Menace, the concealment icon leaks information about enemies you can't see.

When an enemy spots your unit, the game removes the concealment icon -- even if that enemy is outside your vision range and completely hidden in the fog of war. The disappearing icon tells you "someone is watching from that direction," giving you free intel you shouldn't have.

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

If you move closer (within your own vision range), the enemy becomes visible and the icon legitimately isn't there -- that should be (according to the I decide it so rule) normal behavior.

## Root Cause (as far as I can tell for now)

The concealment icon is driven by `Actor.m_VisibilityToAI`:

| Value | Meaning |
|-------|---------|
| Visible (1) | Icon removed -- "enemies can see you" |
| Hidden (2) | Icon shown -- "you are concealed" |

The game sets this to Visible whenever any enemy gains line of sight to your unit -- it doesn't check whether you can actually see that enemy.
This is the "bug".

## The Fix (Freeze Mode)

Trying to set the value during a turn caused horrible flicker of the icon so the solution to save the player eyes was to invert the logic and instead of making the icon reappear we make it dissapear only if you have line of sight on one opfor.

PeekABoo takes full control of `m_VisibilityToAI` and forces it to the correct value:

1. For each player unit, check all enemies on the map
2. Does any enemy have line of sight to this unit? (`CanActorSee`)
3. Is that enemy visible or detected by the player? (`GetVisibilityState`)
4. If a visible/detected enemy sees you -> allow icon removal (you know they're there)
5. If only hidden enemies see you -> force concealment icon to stay (no free intel)

### Performance (v3.0.1)

The expensive LOS scan (iterating all player x enemy pairs) no longer runs every frame. Instead the mod separates the work into two parts:

- **Recompute** (every 5 frames): full LOS scan, rebuilds a cache of per-unit visibility results
- **Apply** (every frame, dual-pass): sets `m_VisibilityToAI` from cache -- no LOS calls, essentially free

The dual-pass apply (OnUpdate + coroutine) is kept to prevent flicker from game overwrites between Unity update phases.

Measured with 1 player unit and 38 enemies:

| | Average | Max |
|---|---------|-----|
| Recompute (every 5 frames) | ~150us | ~300us |
| Apply (every frame) | ~1us | ~18us |

At 60fps a frame budget is 16.6ms. The mod uses ~0.15ms every 5th frame and ~0.001ms on the others.

### Gameplay effect

As a side effect of the throttled scan, enemy visibility during their turn now behaves in a more natural way. When an enemy outside your vision takes an offensive action against your unit (e.g. shooting), the game briefly flips their visibility state -- the mod picks this up and you see the action play out. Once the action ends their position is lost and reverts to a "last known position" marker. You get to see what happened to your unit but you don't get to keep tracking the enemy afterwards.

This feels like the right tactical experience: you know you got shot at and from where, but the enemy fades back into the fog of war.

### Is AI behavior impacted?

As far as I can tell `m_VisibilityToAI` is UI-only it does not affect AI firing behavior. I verified this by forcing it to Hidden while a visible enemy had line of sight, then ending the turn. The enemy still fired The AI uses `CanActorSee()` directly for targeting, not this field.

---

## Credits
https://github.com/p0ss/MenaceAssetPacker without that I wouldn't have dreamed to do this fix.
"Emo Used HM01" for https://www.nexusmods.com/menace/mods/16 with a comprehensive structure for modders.

## Modding knowledge Gained

Beware that all of this has been infered by Claude as there was a lot of poking around. If this help any of you it's a net benefit but don't take it to heart and check for yourself.

### Visibility states

| Value | Name | Meaning |
|-------|------|---------|
| 0 | Unknown | Not yet evaluated |
| 1 | Visible | Player can see this actor |
| 2 | Hidden | In fog of war |
| 3 | Detected | Radar blip, not directly seen |

### SDK methods used

| Method | Purpose |
|--------|---------|
| `EntitySpawner.ListEntities(-1)` | Get all actors across all factions |
| `GameObj.ReadInt("m_FactionID")` | Identify player (1/2) vs enemy factions |
| `GameObj.As<Actor>().m_VisibilityToAI` | Read/write the concealment icon state |
| `LineOfSight.CanActorSee(enemy, player)` | Check if enemy has LOS to player (directional) |
| `LineOfSight.GetVisibilityState(enemy)` | Check if player can see the enemy |

### What did NOT work (investigated dead ends)

| Approach | Why I think It Failed |
|----------|---------------|
| Harmony prefix on `OnDiscovered` | Native IL2CPP dispatch bypasses managed Harmony patches |
| Checking `m_DiscoveredMask` | Tracks discovery history, does not drive the icon |
| `GameObj.GetName()` on actors | Returns null -- must use `ReadObj("m_Template").GetName()` |
| `ReadInt("VisibilityState")` | Resolves to wrong memory offset; must use `LineOfSight.GetVisibilityState()` |
| Reactive polling (set Hidden only when game sets Visible) | Flickers during movement/abilities |
