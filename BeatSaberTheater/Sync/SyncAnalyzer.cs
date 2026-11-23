using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace BeatSaberTheater.Sync;

internal static class SyncAnalyzer
{
    public static Task<SyncResult> AnalyzeAsync(
        string ffmpegPath, string songPath, string videoPath, float playbackSpeed, CancellationToken token)
    {
        return Task.Run(async () =>
        {
            if (playbackSpeed <= 0 || float.IsNaN(playbackSpeed) || float.IsInfinity(playbackSpeed))
                throw new SyncException("Playback speed must be a finite, positive number.");

            var total = Stopwatch.StartNew();
            var stage = Stopwatch.StartNew();
            var audio = await Task.WhenAll(
                AudioDecoder.DecodeAsync(ffmpegPath, songPath, token),
                AudioDecoder.DecodeAsync(ffmpegPath, videoPath, token)).ConfigureAwait(false);
            var result = new SyncResult
            {
                SongSeconds = (double)audio[0].Length / SyncSettings.SampleRate,
                VideoSeconds = (double)audio[1].Length / SyncSettings.SampleRate,
                PlaybackSpeed = playbackSpeed,
                DecodeMs = stage.ElapsedMilliseconds
            };

            token.ThrowIfCancellationRequested();
            stage.Restart();
            var envelopes = await Task.WhenAll(
                Task.Run(() => OnsetEnvelope.Compute(audio[0], token), token),
                Task.Run(() => OnsetEnvelope.Compute(audio[1], token), token)).ConfigureAwait(false);
            var song = envelopes[0];
            var video = ResampleVideo(envelopes[1], playbackSpeed, token);
            result.EnvelopeMs = stage.ElapsedMilliseconds;

            token.ThrowIfCancellationRequested();
            stage.Restart();
            var songSquares = PrefixSquares(song, token);
            var correlation = new Correlation(song.Length, video, token);
            var minOverlap = Math.Min(SyncSettings.MinOverlapSeconds * SyncSettings.FramesPerSecond,
                SyncSettings.MinOverlapFraction * Math.Min(song.Length, video.Length));
            var scores = correlation.Score(song, songSquares, 0, song.Length, minOverlap, token);
            var bestIndex = AddCandidates(result, scores, song.Length - 1, token);
            result.CorrelationMs = stage.ElapsedMilliseconds;

            token.ThrowIfCancellationRequested();
            stage.Restart();
            var bestLag = bestIndex - (song.Length - 1);
            AnalyzeSlices(result, song, songSquares, correlation, bestIndex >= 0, bestLag, token);
            result.SliceMs = stage.ElapsedMilliseconds;

            result.Confidence = GetConfidence(result);
            result.TotalMs = total.ElapsedMilliseconds;
            token.ThrowIfCancellationRequested();
            return result;
        }, token);
    }

    private static float[] ResampleVideo(float[] video, float speed, CancellationToken token)
    {
        if (speed == 1f)
            return video;

        // The last sample is included only if its interpolated source position fits.
        var count = Math.Floor((video.Length - 1) / (double)speed) + 1;
        if (count >= int.MaxValue)
            throw new SyncException("Playback speed is too low for this video's length.");

        var resampled = new float[(int)count];
        for (var k = 0; k < resampled.Length; k++)
        {
            if ((k & 4095) == 0)
                token.ThrowIfCancellationRequested();
            var position = (double)speed * k;
            var left = (int)position;
            var right = Math.Min(left + 1, video.Length - 1);
            resampled[k] = (float)(video[left] + (video[right] - video[left]) * (position - left));
        }
        return resampled;
    }

    private static double[] PrefixSquares(float[] values, CancellationToken token)
    {
        var squares = new double[values.Length + 1];
        for (var i = 0; i < values.Length; i++)
        {
            if ((i & 4095) == 0)
                token.ThrowIfCancellationRequested();
            squares[i + 1] = squares[i] + (double)values[i] * values[i];
        }
        return squares;
    }

    private static int AddCandidates(SyncResult result, float[] scores, int zeroLagIndex, CancellationToken token)
    {
        var bestIndex = -1;
        var suppressed = new bool[scores.Length];
        for (var i = 0; i < SyncSettings.MaxCandidates; i++)
        {
            var peak = FindBest(scores, suppressed, token);
            if (peak < 0)
                break;
            if (i == 0)
                bestIndex = peak;

            result.Candidates.Add(new SyncCandidate
            {
                OffsetMs = ToOffsetMs(Refine(scores, peak) - zeroLagIndex, result.PlaybackSpeed),
                Score = scores[peak]
            });

            // Keep the original scores for interpolation at the edge of a suppressed region.
            var start = Math.Max(0, peak - SyncSettings.CandidateSuppressionFrames);
            var end = Math.Min(scores.Length - 1, peak + SyncSettings.CandidateSuppressionFrames);
            for (var j = start; j <= end; j++)
                suppressed[j] = true;
        }

        if (bestIndex >= 0)
        {
            result.OffsetMs = result.Candidates[0].OffsetMs;
            result.Score = result.Candidates[0].Score;
            var runnerUp = NearRunnerUp(scores, bestIndex);
            result.Margin = result.Score == 0 && runnerUp == 0 ? 0 : result.Score / runnerUp;
        }
        return bestIndex;
    }

    // Best score within MarginWindowSeconds of the peak, outside its own suppression zone (0.01 if there is none)
    private static float NearRunnerUp(float[] scores, int peak)
    {
        var window = (int)(SyncSettings.MarginWindowSeconds * SyncSettings.FramesPerSecond);
        var runnerUp = 0.01f;
        for (var i = Math.Max(0, peak - window); i <= Math.Min(scores.Length - 1, peak + window); i++)
        {
            if (Math.Abs(i - peak) <= SyncSettings.CandidateSuppressionFrames) continue;
            if (scores[i] > runnerUp) runnerUp = scores[i];
        }

        return runnerUp;
    }

    private static int FindBest(float[] scores, bool[]? suppressed, CancellationToken token)
    {
        var peak = -1;
        var best = float.NegativeInfinity;
        for (var i = 0; i < scores.Length; i++)
        {
            if ((i & 4095) == 0)
                token.ThrowIfCancellationRequested();
            if (scores[i] > best && (suppressed == null || !suppressed[i]))
            {
                best = scores[i];
                peak = i;
            }
        }
        return peak;
    }

    private static double Refine(float[] scores, int peak)
    {
        if (peak == 0 || peak == scores.Length - 1)
            return peak;

        double before = scores[peak - 1];
        double center = scores[peak];
        double after = scores[peak + 1];
        if (double.IsNegativeInfinity(before) || double.IsNegativeInfinity(after) ||
            center < before || center < after || before + after == 2 * center)
            return peak;

        return peak + 0.5 * (before - after) / (before - 2 * center + after);
    }

    private static double ToOffsetMs(double lag, float speed)
    {
        // Lag is measured on the song axis, while the offset is measured on the video axis.
        return speed * lag * (1000.0 / SyncSettings.FramesPerSecond);
    }

    private static void AnalyzeSlices(SyncResult result, float[] song, double[] songSquares,
        Correlation correlation, bool hasGlobalPeak, int bestLag, CancellationToken token)
    {
        var sliceFrames = (int)(SyncSettings.SliceSeconds * SyncSettings.FramesPerSecond);
        var minimumFrames = sliceFrames / 2;
        var valid = 0;
        var agreeing = 0;
        for (var start = 0; start < song.Length; start += sliceFrames)
        {
            token.ThrowIfCancellationRequested();
            var length = Math.Min(sliceFrames, song.Length - start);
            if (length < minimumFrames)
                break;

            var slice = new SyncSlice
            {
                StartSeconds = (double)start / SyncSettings.FramesPerSecond,
                EndSeconds = (double)(start + length) / SyncSettings.FramesPerSecond
            };
            result.Slices.Add(slice);
            double sum = 0;
            for (var i = start; i < start + length; i++)
            {
                if (((i - start) & 4095) == 0)
                    token.ThrowIfCancellationRequested();
                sum += song[i];
            }

            var mean = sum / length;
            var variance = (songSquares[start + length] - songSquares[start]) / length - mean * mean;
            if (variance < SyncSettings.SliceMinStd * SyncSettings.SliceMinStd)
                continue;

            var scores = correlation.Score(song, songSquares, start, length, minimumFrames, token);
            var peak = FindBest(scores, null, token);
            if (peak < 0)
                continue;

            // The FFT used slice-local indices. Account for its start to compare with the full song.
            var zeroLagIndex = length - 1 + start;
            slice.Valid = true;
            slice.OffsetMs = ToOffsetMs(Refine(scores, peak) - zeroLagIndex, result.PlaybackSpeed);
            slice.Score = scores[peak];
            if (hasGlobalPeak)
            {
                slice.GlobalScore = ScoreNear(scores, bestLag + zeroLagIndex, SyncSettings.SliceAgreementFrames);
                // Repeated sections make a slice's own best spot ambiguous, so it also agrees when the global offset
                // explains it nearly as well
                var exact = Math.Abs((long)peak - zeroLagIndex - bestLag) <= SyncSettings.SliceAgreementFrames;
                if (exact) result.ExactSlices++;
                slice.Agrees = exact || slice.GlobalScore >= SyncSettings.SliceConsistentRatio * slice.Score;
            }
            valid++;
            if (slice.Agrees)
            {
                agreeing++;
                AddMatchedRange(result, slice);
            }
        }

        result.Agreement = valid == 0 ? 0 : (float)agreeing / valid;
    }

    // Best score within +-radius of an index, or NaN if none of those lags has enough overlap
    private static float ScoreNear(float[] scores, long index, int radius)
    {
        var best = float.NaN;
        for (var i = Math.Max(0, index - radius); i <= Math.Min(scores.Length - 1, index + radius); i++)
        {
            var score = scores[i];
            if (float.IsNegativeInfinity(score)) continue;
            if (float.IsNaN(best) || score > best) best = score;
        }

        return best;
    }

    private static void AddMatchedRange(SyncResult result, SyncSlice slice)
    {
        var ranges = result.MatchedRanges;
        if (ranges.Count > 0 && ranges[ranges.Count - 1].EndSeconds == slice.StartSeconds)
        {
            ranges[ranges.Count - 1].EndSeconds = slice.EndSeconds;
        }
        else
        {
            ranges.Add(new SyncRange { StartSeconds = slice.StartSeconds, EndSeconds = slice.EndSeconds });
        }
    }

    private static SyncConfidence GetConfidence(SyncResult result)
    {
        if (result.Score < SyncSettings.NoMatchMaxScore && result.ExactSlices <= SyncSettings.NoMatchMaxExactSlices)
            return SyncConfidence.None;
        if (result.Score >= SyncSettings.StrongMinScore && result.Margin >= SyncSettings.StrongMinMargin &&
            result.Agreement >= SyncSettings.StrongMinAgreement)
            return SyncConfidence.Strong;
        return SyncConfidence.Unsure;
    }

    private sealed class Correlation
    {
        private readonly Fft _fft;
        private readonly int _videoLength;
        private readonly double[] _videoSquares;
        private readonly float[] _videoRe;
        private readonly float[] _videoIm;
        private readonly float[] _workRe;
        private readonly float[] _workIm;

        public Correlation(int songLength, float[] video, CancellationToken token)
        {
            var size = 1;
            while (size < (long)songLength + video.Length)
            {
                if (size == 1 << 30)
                    throw new SyncException("The audio is too long for correlation.");
                size <<= 1;
            }

            _fft = new Fft(size, token);
            _videoLength = video.Length;
            _videoSquares = PrefixSquares(video, token);
            _videoRe = new float[size];
            _videoIm = new float[size];
            _workRe = new float[size];
            _workIm = new float[size];

            // The full song and every slice correlate against the same video spectrum.
            Array.Copy(video, _videoRe, video.Length);
            _fft.Transform(_videoRe, _videoIm, false, token);
        }

        public float[] Score(float[] song, double[] songSquares, int start, int length,
            float minOverlap, CancellationToken token)
        {
            Array.Copy(song, start, _workRe, 0, length);
            Array.Clear(_workRe, length, _workRe.Length - length);
            Array.Clear(_workIm, 0, _workIm.Length);
            _fft.Transform(_workRe, _workIm, false, token);

            for (var i = 0; i < _workRe.Length; i++)
            {
                if ((i & 4095) == 0)
                    token.ThrowIfCancellationRequested();

                // Inverse FFT of conj(song) * video gives sum song[t] * video[t + lag].
                var real = _workRe[i];
                var imaginary = _workIm[i];
                _workRe[i] = real * _videoRe[i] + imaginary * _videoIm[i];
                _workIm[i] = real * _videoIm[i] - imaginary * _videoRe[i];
            }
            _fft.Transform(_workRe, _workIm, true, token);

            var scores = new float[length + _videoLength - 1];
            for (var i = 0; i < scores.Length; i++)
            {
                if ((i & 4095) == 0)
                    token.ThrowIfCancellationRequested();
                var lag = i - (length - 1);
                var first = Math.Max(0, -lag);
                var end = Math.Min(length, _videoLength - lag);
                scores[i] = float.NegativeInfinity;
                if (end - first < minOverlap)
                    continue;

                var songEnergy = songSquares[start + end] - songSquares[start + first];
                var videoEnergy = _videoSquares[end + lag] - _videoSquares[first + lag];
                if (songEnergy <= 0 || videoEnergy <= 0)
                    continue;

                // Negative lags wrap to the high indices of the zero-padded inverse FFT.
                var index = lag < 0 ? lag + _workRe.Length : lag;
                var score = _workRe[index] / Math.Sqrt(songEnergy * videoEnergy);
                scores[i] = (float)Math.Max(-1, Math.Min(1, score));
            }

            return scores;
        }
    }
}
