namespace BeatSaberTheater.Sync;

internal static class SyncSettings
{
    public const int SampleRate = 12000;
    public const int FrameSize = 512;
    public const int HopSize = 120;
    public const int FramesPerSecond = 100;
    public const float LogCompression = 100f;
    public const int LocalMeanRadius = 25;
    public const float MinOverlapSeconds = 20f;
    public const float MinOverlapFraction = 0.5f;
    public const int CandidateSuppressionFrames = 15;
    public const int MaxCandidates = 3;
    public const float SliceSeconds = 20f;
    public const float SliceMinStd = 0.3f;
    public const int SliceAgreementFrames = 3;
    // A slice whose own best spot is elsewhere still agrees if the global offset scores at least this share of its
    // best: songs repeat sections almost exactly (TUYU live tests), so a chorus slice fits every chorus equally well.
    public const float SliceConsistentRatio = 0.85f;
    // Calibrated 2026-11-22 on synthetic cases from two songs and 6 real map/video pairs. Real matches scored
    // 0.79-0.99 with 80-100 % slice agreement; unrelated songs scored 0.17-0.31 and half-spliced ones 0.48-0.72
    // with 43 % agreement.
    // Margin is only a light check: songs with a repeated section (Stay Alive) have a runner-up far away at 1.23x
    // while every slice still agrees, so the slices are the real tie-breaker.
    public const float StrongMinScore = 0.50f;
    public const float StrongMinMargin = 1.15f;
    public const float StrongMinAgreement = 0.75f;
    public const float NoMatchMaxScore = 0.40f;
    // ...unless this many slices land exactly (+-SliceAgreementFrames) on the best offset: live sets that cut or
    // extend sections (Porter Robinson concert, Something Comforting) score low overall but still line up in parts.
    // Unrelated songs never had more than one such slice.
    public const int NoMatchMaxExactSlices = 2;
    // The margin only compares against rivals this close to the best offset. Beat- and bar-level rivals are the
    // confusing kind; far-away rivals are repeated sections, which the slices already sort out (TUYU tests).
    public const float MarginWindowSeconds = 10f;
    public const int DecodeTimeoutSeconds = 120;
}
