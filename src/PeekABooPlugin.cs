using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using Il2CppInterop.Runtime.InteropTypes;
using MelonLoader;
using Menace.ModpackLoader;
using Menace.SDK;

namespace Menace.PeekABoo;

public class PeekABooPlugin : IModpackPlugin
{
    private static MelonLogger.Instance Log;
    private bool _inTactical;
    private int _initDelay;

    private const string MOD_NAME = "PeekABoo";

    // Player faction IDs
    private const int FACTION_PLAYER_1 = 1;
    private const int FACTION_PLAYER_2 = 2;

    // Recompute LOS every N frames; cached results are applied every frame
    private const int TICK_INTERVAL = 5;
    private int _tickCounter;

    // Cached guard results: actor reference + target visibility
    private readonly List<(Il2CppMenace.Tactical.Actor actor, Il2CppMenace.Tactical.Visibility target)> _guardCache = new();

    // Perf tracking (active when DebugLogging is on)
    private const int PERF_LOG_INTERVAL = 60; // log summary every N recomputes (~5s)
    private readonly Stopwatch _sw = new();
    private int _perfCounter;
    private long _recomputeTotalUs;
    private long _recomputeMaxUs;
    private long _applyTotalUs;
    private long _applyMaxUs;
    private int _applyCount;
    private int _lastPlayerCount;
    private int _lastEnemyCount;

    // ═══════════════════════════════════════════════════════════════════
    //  Plugin Lifecycle
    // ═══════════════════════════════════════════════════════════════════

    public void OnInitialize(MelonLogger.Instance logger, HarmonyLib.Harmony harmony)
    {
        Log = logger;
        RegisterSettings();
        Log.Msg("PeekABoo v3.0.1 initialized (freeze mode, throttled)");
    }

    public void OnSceneLoaded(int buildIndex, string sceneName)
    {
        if (sceneName == "Tactical")
        {
            _inTactical = true;
            _initDelay = 60;
            _tickCounter = 0;
            _guardCache.Clear();
            MelonCoroutines.Start(ConcealmentGuardCoroutine());
            Log.Msg("PeekABoo v3.0.1 — Concealment guard armed");
        }
        else
        {
            _inTactical = false;
            _guardCache.Clear();
        }
    }

    // ═══════════════════════════════════════════════════════════════════
    //  Settings
    // ═══════════════════════════════════════════════════════════════════

    private static bool HideConcealmentEnabled => ModSettings.Get<bool>(MOD_NAME, "HideConcealmentFromUndetected");
    private static bool DebugLogging => ModSettings.Get<bool>(MOD_NAME, "DebugLogging");

    private void RegisterSettings()
    {
        ModSettings.Register(MOD_NAME, settings =>
        {
            settings.AddHeader("Concealment Fix");
            settings.AddToggle("HideConcealmentFromUndetected", "Hide Concealment from Undetected", true);

            settings.AddHeader("Debug");
            settings.AddToggle("DebugLogging", "Debug Logging", false);
        });
    }

    // ═══════════════════════════════════════════════════════════════════
    //  Concealment Guard — freeze mode (throttled)
    //  Expensive LOS scan runs every TICK_INTERVAL frames.
    //  Cached results are applied every frame (OnUpdate + coroutine)
    //  to prevent flicker from game overwrites.
    // ═══════════════════════════════════════════════════════════════════

    public void OnUpdate()
    {
        if (!_inTactical || !HideConcealmentEnabled || _initDelay > 0)
        {
            if (_initDelay > 0) _initDelay--;
            return;
        }

        if (++_tickCounter >= TICK_INTERVAL)
        {
            _tickCounter = 0;

            if (DebugLogging)
            {
                _sw.Restart();
                RecomputeConcealment();
                _sw.Stop();
                long us = _sw.Elapsed.Ticks / (TimeSpan.TicksPerMillisecond / 1000);
                _recomputeTotalUs += us;
                if (us > _recomputeMaxUs) _recomputeMaxUs = us;
                _perfCounter++;

                if (_perfCounter >= PERF_LOG_INTERVAL)
                    LogPerfSummary();
            }
            else
            {
                RecomputeConcealment();
            }
        }

        if (DebugLogging)
        {
            _sw.Restart();
            ApplyCachedConcealment();
            _sw.Stop();
            long us = _sw.Elapsed.Ticks / (TimeSpan.TicksPerMillisecond / 1000);
            _applyTotalUs += us;
            if (us > _applyMaxUs) _applyMaxUs = us;
            _applyCount++;
        }
        else
        {
            ApplyCachedConcealment();
        }
    }

    private IEnumerator ConcealmentGuardCoroutine()
    {
        while (_initDelay > 0 && _inTactical)
            yield return null;

        while (_inTactical)
        {
            yield return null;

            if (!_inTactical || !HideConcealmentEnabled)
                continue;

            ApplyCachedConcealment();
        }
    }

    /// <summary>
    /// Full LOS scan — runs every TICK_INTERVAL frames.
    /// Rebuilds the cache of (actor, target visibility) pairs.
    /// </summary>
    private void RecomputeConcealment()
    {
        try
        {
            _guardCache.Clear();
            int playerCount = 0;

            var actors = EntitySpawner.ListEntities(-1);
            if (actors == null || actors.Length == 0)
                return;

            foreach (var playerUnit in actors)
            {
                if (playerUnit.IsNull || !playerUnit.IsAlive)
                    continue;

                int faction = playerUnit.ReadInt("m_FactionID");
                if (faction != FACTION_PLAYER_1 && faction != FACTION_PLAYER_2)
                    continue;

                var managed = playerUnit.As<Il2CppMenace.Tactical.Actor>();
                if (managed == null)
                    continue;

                playerCount++;

                // Determine the correct visibility: only Visible if a
                // visible/detected enemy actually has LOS to this unit
                bool shouldBeVisible = false;

                foreach (var enemy in actors)
                {
                    if (enemy.IsNull || !enemy.IsAlive)
                        continue;

                    int eFaction = enemy.ReadInt("m_FactionID");
                    if (eFaction == FACTION_PLAYER_1 || eFaction == FACTION_PLAYER_2)
                        continue;

                    if (!LineOfSight.CanActorSee(enemy, playerUnit))
                        continue;

                    int visState = LineOfSight.GetVisibilityState(enemy);
                    if (visState == LineOfSight.VISIBILITY_VISIBLE ||
                        visState == LineOfSight.VISIBILITY_DETECTED)
                    {
                        shouldBeVisible = true;
                        break;
                    }
                }

                var target = shouldBeVisible
                    ? Il2CppMenace.Tactical.Visibility.Visible
                    : Il2CppMenace.Tactical.Visibility.Hidden;

                _guardCache.Add((managed, target));

                if (DebugLogging && !shouldBeVisible && managed.m_VisibilityToAI != target)
                {
                    Log?.Msg($"[PeekABoo] Concealment restored for {GetTemplateName(playerUnit)} " +
                             $"(only hidden enemies have LOS)");
                }
            }

            _lastPlayerCount = playerCount;
            _lastEnemyCount = actors.Length - playerCount;
        }
        catch (Exception ex)
        {
            Log?.Error($"[PeekABoo] Recompute error: {ex.Message}");
        }
    }

    private void LogPerfSummary()
    {
        long recomputeAvg = _recomputeTotalUs / _perfCounter;
        long applyAvg = _applyCount > 0 ? _applyTotalUs / _applyCount : 0;

        Log?.Msg($"[PeekABoo] Perf ({_perfCounter} recomputes, {_applyCount} applies) " +
                 $"| recompute avg={recomputeAvg}us max={_recomputeMaxUs}us " +
                 $"| apply avg={applyAvg}us max={_applyMaxUs}us " +
                 $"| actors: {_lastPlayerCount}P x {_lastEnemyCount}E");

        _perfCounter = 0;
        _recomputeTotalUs = 0;
        _recomputeMaxUs = 0;
        _applyTotalUs = 0;
        _applyMaxUs = 0;
        _applyCount = 0;
    }

    /// <summary>
    /// Apply cached results — runs every frame (both passes).
    /// Just sets m_VisibilityToAI on each cached actor, no LOS calls.
    /// </summary>
    private void ApplyCachedConcealment()
    {
        for (int i = 0; i < _guardCache.Count; i++)
        {
            try
            {
                var (actor, target) = _guardCache[i];
                if (actor != null && actor.m_VisibilityToAI != target)
                    actor.m_VisibilityToAI = target;
            }
            catch
            {
                // Actor may have been destroyed between recomputes
            }
        }
    }

    // ═══════════════════════════════════════════════════════════════════
    //  Helpers
    // ═══════════════════════════════════════════════════════════════════

    private static string GetTemplateName(GameObj obj)
    {
        try
        {
            var template = obj.ReadObj("m_Template");
            if (!template.IsNull)
                return template.GetName() ?? "unknown";
        }
        catch { }
        return "unknown";
    }
}
