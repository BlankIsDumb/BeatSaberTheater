using System;
using System.Linq;
using BeatSaberTheater.Sync;

namespace SyncHarness;

internal static class Report
{
    public static void PrintAnalysis(SyncResult result)
    {
        Console.WriteLine($"Offset {result.OffsetMs:+0.00;-0.00;0.00} ms   Class {result.Confidence}   " +
            $"Score {result.Score:F4}   Margin {result.Margin:F3}   Agreement {result.Agreement:P0}   Exact slices {result.ExactSlices}");
        Console.WriteLine($"Song {result.SongSeconds:F2} s   Video {result.VideoSeconds:F2} s   Speed {result.PlaybackSpeed:G}x");
        PrintDetails(result);
        Console.WriteLine();
        Console.WriteLine($"{"Start (s)",10} {"End (s)",10} {"Valid",7} {"Agrees",7} {"Offset (ms)",12} {"Score",8} {"At global",10}");
        foreach (var slice in result.Slices)
        {
            var offset = slice.Valid ? $"{slice.OffsetMs:F2}" : "-";
            var score = slice.Valid ? $"{slice.Score:F4}" : "-";
            var global = slice.Valid && !float.IsNaN(slice.GlobalScore) ? $"{slice.GlobalScore:F4}" : "-";
            Console.WriteLine($"{slice.StartSeconds,10:F2} {slice.EndSeconds,10:F2} {slice.Valid,7} " +
                $"{slice.Agrees,7} {offset,12} {score,8} {global,10}");
        }
        Console.WriteLine(Timings(result));
    }

    public static void PrintDetails(SyncResult result)
    {
        var ranges = result.MatchedRanges.Select(range => $"{range.StartSeconds:F2}–{range.EndSeconds:F2} s");
        var peaks = result.Candidates.Select(candidate => $"{candidate.OffsetMs:+0.00;-0.00;0.00} ms ({candidate.Score:F4})");
        Console.WriteLine("  Matched ranges  " + (result.MatchedRanges.Count == 0 ? "none" : string.Join(", ", ranges)));
        Console.WriteLine("  Candidates      " + (result.Candidates.Count == 0 ? "none" : string.Join(", ", peaks)));
    }

    public static string Timings(SyncResult result)
    {
        return $"Decode {result.DecodeMs} ms  Envelope {result.EnvelopeMs} ms  " +
            $"Correlation {result.CorrelationMs} ms  Slices {result.SliceMs} ms  Total {result.TotalMs} ms";
    }
}
