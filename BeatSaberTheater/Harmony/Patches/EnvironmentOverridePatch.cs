using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using JetBrains.Annotations;

namespace BeatSaberTheater.Harmony.Patches;

internal static class SceneTransitionInitOverloads
{
    // Game versions differ in how many Init overloads these types have. On 1.40.8 there are two (with and without
    // IBeatmapLevelData), and normal play goes through the one without, so patching only the first match misses it.
    // None of the overloads call each other, so the prefix still runs once per level start.
    internal static IEnumerable<MethodBase> For(Type type)
    {
        var overloads = AccessTools.GetDeclaredMethods(type).Where(m => m.Name == "Init").ToList();
        Plugin._log.Info($"Patching {overloads.Count} overload(s) of {type.Name}.Init");
        return overloads;
    }
}

[HarmonyPatch]
[UsedImplicitly]
// ReSharper disable once InconsistentNaming
internal static class StandardLevelScenesTransitionSetupDataSOInit
{
    internal delegate void SceneTransitionDelegate(BeatmapLevel beatmapLevel, BeatmapKey beatmapKey,
        ref OverrideEnvironmentSettings overrideEnvironmentSettings);

    public static SceneTransitionDelegate? SceneTransitionCalled;

    [UsedImplicitly]
    private static IEnumerable<MethodBase> TargetMethods()
    {
        return SceneTransitionInitOverloads.For(typeof(StandardLevelScenesTransitionSetupDataSO));
    }

    [UsedImplicitly]
    public static void Prefix(BeatmapLevel beatmapLevel, BeatmapKey beatmapKey,
        ref OverrideEnvironmentSettings overrideEnvironmentSettings)
    {
        SceneTransitionCalled?.Invoke(beatmapLevel, beatmapKey, ref overrideEnvironmentSettings);
    }
}

[HarmonyPatch]
[UsedImplicitly]
// ReSharper disable once InconsistentNaming
internal static class MissionLevelScenesTransitionSetupDataSOInit
{
    [UsedImplicitly]
    private static IEnumerable<MethodBase> TargetMethods()
    {
        return SceneTransitionInitOverloads.For(typeof(MissionLevelScenesTransitionSetupDataSO));
    }

    [UsedImplicitly]
    private static void Prefix(BeatmapLevel beatmapLevel, BeatmapKey beatmapKey)
    {
        try
        {
            var overrideSettings = new OverrideEnvironmentSettings();
            StandardLevelScenesTransitionSetupDataSOInit.Prefix(beatmapLevel, beatmapKey, ref overrideSettings);
        }
        catch (Exception e)
        {
            Plugin._log.Warn(e);
        }
    }
}