using System;
using HarmonyLib;
using JetBrains.Annotations;
using UnityEngine;
using Zenject;

namespace BeatSaberTheater.Harmony.Patches;

[HarmonyPatch(typeof(GameSongController), nameof(GameSongController.waitUntilIsReadyToStartTheSong), MethodType.Getter)]
internal static class SongStartWaitPatch
{
    // Set by PlaybackManager. Arguments: the gameplay container of this start, seconds held so far. True = start now.
    public static Func<DiContainer?, float, bool>? ReadyToStart;

    [UsedImplicitly]
    private static void Postfix(ref WaitUntil __result)
    {
        if (ReadyToStart == null) return;
        var original = __result;
        var container = Plugin.gameCoreContainer;
        var holdStart = -1f;
        __result = new WaitUntil(() =>
        {
            if (original != null && original.keepWaiting) return false;
            var gate = ReadyToStart;
            if (gate == null) return true;
            if (holdStart < 0) holdStart = Time.realtimeSinceStartup;
            try
            {
                return gate(container, Time.realtimeSinceStartup - holdStart);
            }
            catch (Exception e)
            {
                Plugin._log.Error(e);
                return true;
            }
        });
    }
}
