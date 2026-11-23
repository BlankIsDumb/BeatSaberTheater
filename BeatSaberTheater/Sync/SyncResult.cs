using System;
using System.Collections.Generic;

namespace BeatSaberTheater.Sync;

internal enum SyncConfidence
{
    Strong,
    Unsure,
    None
}

internal sealed class SyncResult
{
    public double OffsetMs { get; set; }
    public float Score { get; set; }
    public float Margin { get; set; }
    public float Agreement { get; set; }
    // Slices whose own best offset is the global one (not just consistent with it)
    public int ExactSlices { get; set; }
    public SyncConfidence Confidence { get; set; }
    public List<SyncCandidate> Candidates { get; } = new List<SyncCandidate>();
    public List<SyncRange> MatchedRanges { get; } = new List<SyncRange>();
    public List<SyncSlice> Slices { get; } = new List<SyncSlice>();
    public double SongSeconds { get; set; }
    public double VideoSeconds { get; set; }
    public float PlaybackSpeed { get; set; }
    public long DecodeMs { get; set; }
    public long EnvelopeMs { get; set; }
    public long CorrelationMs { get; set; }
    public long SliceMs { get; set; }
    public long TotalMs { get; set; }
}

internal sealed class SyncCandidate
{
    public double OffsetMs { get; set; }
    public float Score { get; set; }
}

internal sealed class SyncRange
{
    public double StartSeconds { get; set; }
    public double EndSeconds { get; set; }
}

internal sealed class SyncSlice
{
    public double StartSeconds { get; set; }
    public double EndSeconds { get; set; }
    public bool Valid { get; set; }
    public bool Agrees { get; set; }
    public double OffsetMs { get; set; }
    public float Score { get; set; }
    // The slice's score at the global offset (NaN if that offset leaves too little overlap)
    public float GlobalScore { get; set; } = float.NaN;
}

internal sealed class SyncException : Exception
{
    public SyncException(string message) : base(message)
    {
    }
}
