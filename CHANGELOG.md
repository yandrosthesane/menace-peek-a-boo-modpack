# Changelog

## v3.0.2 -- Housekeeping

### Changed

- Removed the Hide Concealment from Undetected toggle from settings (mod is always active when installed). Only Debug Logging remains as a setting.
- Added release script and .gitignore for release artifacts.
- Restructured README with complementary mod links and documentation.

## v3.0.1 -- Performance & Observability

### Changed

- **Throttled LOS scan**: The expensive per-frame LOS check (iterating all player x enemy pairs) now runs every 5 frames instead of every frame. Cached results are applied every frame (both passes) to prevent flicker. With 1 player and 38 enemies, recompute averages ~150us every 5th frame; apply costs ~1us on every other frame.
- **Debug perf logging**: When Debug Logging is enabled, the mod now logs a periodic summary (~every 5s) with average/max timings for both the recompute and apply phases, plus actor counts (e.g. `recompute avg=150us max=300us | apply avg=1us max=18us | actors: 1P x 38E`).

### Gameplay

- Enemy actions during their turn now feel more natural. When a hidden enemy takes an offensive action (e.g. shooting) against your unit, you see the action play out, but their position reverts to "last known position" once the action ends. You know you got shot at and from where, but you don't get to keep tracking them.

### Fixed

- Removed redundant full LOS scan on the coroutine late pass (was duplicating work). The late pass now only applies cached results.

## v3.0.0 -- Freeze Mode

### Changed

- **Freeze mode**: Takes full ownership of `m_VisibilityToAI` and forces the correct value every frame (dual-pass: OnUpdate + coroutine). Replaces the reactive polling approach from v2.0.0.
- Fixes icon flicker that occurred with reactive polling during movement and abilities.

### How it works

1. For each player unit, check all enemies on the map
2. If a visible/detected enemy has LOS to the player -> allow icon removal
3. If only hidden enemies have LOS -> force concealment icon to stay
4. Writes every frame to override game's own writes

## v2.0.0 -- Reactive Polling (Superseded)

### Added

- Watched for the game setting `m_VisibilityToAI` to Visible and corrected it to Hidden.

### Known Issues

- Visible flicker during movement and abilities -- the game continuously re-sets the value during actions, creating a gap between its write and our correction.

## v1.0.0 -- Initial Release

### Added

- Initial attempt using Harmony prefix on `OnDiscovered`.

### Known Issues

- Native IL2CPP vtable dispatch bypasses Harmony patches entirely. Patches registered and worked via REPL but never fired during gameplay.
