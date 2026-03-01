[size=5][b]PeekABoo — Concealment Icon Exploit Fix[/b][/size]

[b]Version:[/b] 3.0.2 — [i]"Freeze mode, throttled"[/i]
[b]Author:[/b] YandrosTheSane

[size=4][b]What It Does[/b][/size]

PeekABoo fixes a UI information leak that lets the player detect hidden enemies through the concealment icon. Without this mod, the concealment icon disappears whenever any enemy gains line of sight to your unit — even if that enemy is deep in the fog of war and completely invisible to you. The disappearing icon gives you free intel you shouldn't have.

[size=4][b]The Problem[/b][/size]

When an enemy spots your unit, the game removes the concealment icon. It never checks whether the player can actually see that enemy.

The result: a hidden enemy 9 tiles away (outside your vision range of 7) spots you, and your concealment icon disappears — telling you "someone is watching from that direction."

[size=3][b]Meta information abuse[/b][/size]

[code]
Enemy (hidden, 9 tiles away)
|
|  enemy vision range covers you
|
Your Unit (vision range 7)

You can't see the enemy. But your concealment icon disappears,
revealing their presence.
[/code]

If you move closer (within your own vision range), the enemy becomes visible and the icon legitimately isn't there — that's normal behavior.

[size=3][b]Root Cause[/b][/size]

The concealment icon is driven by an internal visibility field:

[list]
[*][b]Visible (1)[/b] — Icon removed — "enemies can see you"
[*][b]Hidden (2)[/b] — Icon shown — "you are concealed"
[/list]

The game sets this to Visible whenever any enemy gains line of sight to your unit — it doesn't check whether you can actually see that enemy.

[size=4][b]How It Currently Works[/b][/size]

[size=3][b]Core Mechanism: Freeze Mode[/b][/size]

PeekABoo takes full control of the visibility field and forces it to the correct value:

[list=1]
[*]For each player unit, check all enemies on the map
[*]Does any enemy have line of sight to this unit?
[*]Is that enemy visible or detected by the player?
[*]If a visible/detected enemy sees you -> allow icon removal (you know they're there)
[*]If only hidden enemies see you -> force concealment icon to stay (no free intel)
[/list]

[size=3][b]Performance[/b][/size]

The expensive LOS scan (iterating all player x enemy pairs) no longer runs every frame. Instead the mod separates the work into two parts:

[list]
[*][b]Recompute[/b] (every 5 frames): full LOS scan, rebuilds a cache of per-unit visibility results
[*][b]Apply[/b] (every frame, dual-pass): sets visibility from cache — no LOS calls, essentially free
[/list]

Measured with 1 player unit and 38 enemies:

[list]
[*]Recompute (every 5 frames): avg ~150us, max ~300us
[*]Apply (every frame): avg ~1us, max ~18us
[/list]

At 60fps a frame budget is 16.6ms. The mod uses ~0.15ms every 5th frame and ~0.001ms on the others.

[size=3][b]Gameplay Effect[/b][/size]

As a side effect of the throttled scan, enemy visibility during their turn now behaves in a more natural way. When an enemy outside your vision takes an offensive action against your unit (e.g. shooting), the game briefly flips their visibility state — the mod picks this up and you see the action play out. Once the action ends their position is lost and reverts to a "last known position" marker. You get to see what happened to your unit but you don't get to keep tracking the enemy afterwards.

This feels like the right tactical experience: you know you got shot at and from where, but the enemy fades back into the fog of war.

[size=3][b]Is AI Behavior Impacted?[/b][/size]

As far as I can tell, the field this mod controls is UI-only — it does not affect AI firing behavior. I verified this by forcing it to Hidden while a visible enemy had line of sight, then ending the turn. The enemy still fired. The AI uses its own LOS checks directly for targeting, not this field.

[size=4][b]Complementary Mods[/b][/size]

[list]
[*][url=https://www.nexusmods.com/menace/mods/73]BooAPeek ~ By YandrosTheSane[/url] — Fixes the mirror-image problem: the AI's illegitimate knowledge of concealed [i]player[/i] positions. Without it, enemies flee from and take cover against units they've never seen.
[*][url=https://www.nexusmods.com/menace/mods/36]Wake Up ~ By Pylkij[/url]
[/list]

With those 3 mods (AI is active, You don't get free intel, They don't get free intel) you can get into situation like this in one turn.

Later down the road there will be a need for rebalance.

[size=4][b]Installation[/b][/size]

Use the [url=https://github.com/p0ss/MenaceAssetPacker/releases]MenaceAssetPacker[/url] to deploy (build the sources) and activate the mod.

[size=4][b]Current State & Known Limitations[/b][/size]

[size=3][b]What v3.0.2 Does Well[/b][/size]

[list]
[*]Eliminates the player's illegitimate knowledge of hidden enemy positions via the concealment icon
[*]Concealment icon only changes when a visible/detected enemy has LOS — no more free intel from fog-of-war enemies
[*]Enemy offensive actions are briefly revealed then fade back to last-known-position markers
[*]Negligible performance impact (~0.15ms every 5th frame)
[*]Stable across all tested scenarios — no flicker, no crashes
[/list]

[size=3][b]What v3.0.2 Does NOT Do[/b][/size]

[list]
[*][b]Not a full fog-of-war system:[/b] PeekABoo only controls the concealment icon. Other UI elements that might leak information are not addressed.
[*][b]5-frame scan delay:[/b] The LOS cache can be up to 5 frames stale. In practice this is imperceptible (~83ms at 60fps).
[/list]

[size=4][b]Settings[/b][/size]

Configurable via the in-game Modkit settings panel:

[list]
[*][b]Debug Logging[/b] (Default: Off) — Logs periodic perf summaries with recompute/apply timings and actor counts
[/list]

The settings header shows the mod version (e.g. "PeekABoo v3.0.2") so you can verify which version is deployed.

[size=4][b]Credits[/b][/size]

[list]
[*][b]MenaceAssetPacker[/b] — [url=https://github.com/p0ss/MenaceAssetPacker]GitHub[/url] — without that I wouldn't have dreamed to do this fix
[*][b]"Emo Used HM01"[/b] — [url=https://www.nexusmods.com/menace/mods/16]Nexus[/url] — comprehensive modding structure for modders
[/list]

[size=4][b]Requirements[/b][/size]

[list]
[*]Menace with MelonLoader
[*]Menace ModpackLoader
[/list]
