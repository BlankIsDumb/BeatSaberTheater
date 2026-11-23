using System;
using System.Threading;

namespace BeatSaberTheater.Sync;

internal static class OnsetEnvelope
{
    public static float[] Compute(float[] samples, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        const int frameSize = SyncSettings.FrameSize;
        if (samples.Length < frameSize)
            return Array.Empty<float>();

        var frameCount = (samples.Length - frameSize) / SyncSettings.HopSize + 1;
        var flux = new float[frameCount];
        var fft = new Fft(frameSize, token);
        var window = new float[frameSize];
        var real = new float[frameSize];
        var imaginary = new float[frameSize];
        var previous = new float[frameSize / 2 + 1];

        for (var j = 0; j < frameSize; j++)
            window[j] = (float)(0.5 - 0.5 * Math.Cos(2 * Math.PI * j / (frameSize - 1)));

        for (var i = 0; i < frameCount; i++)
        {
            var start = i * SyncSettings.HopSize;
            for (var j = 0; j < frameSize; j++)
            {
                real[j] = samples[start + j] * window[j];
                imaginary[j] = 0;
            }

            fft.Transform(real, imaginary, false, token);
            var total = 0f;
            for (var k = 1; k <= frameSize / 2; k++)
            {
                var magnitude = Math.Sqrt(real[k] * real[k] + imaginary[k] * imaginary[k]);
                var compressed = (float)Math.Log(1 + SyncSettings.LogCompression * magnitude);
                total += Math.Max(0f, compressed - previous[k]);
                previous[k] = compressed;
            }
            flux[i] = i == 0 ? 0 : total;
        }

        // Keep the raw flux intact while a running sum advances the centered window.
        const int radius = SyncSettings.LocalMeanRadius;
        var envelope = new float[frameCount];
        var left = 0;
        var right = 0;
        double localSum = 0;
        double sum = 0;
        double sumSquared = 0;
        for (var i = 0; i < frameCount; i++)
        {
            if ((i & 4095) == 0)
                token.ThrowIfCancellationRequested();
            var nextLeft = Math.Max(0, i - radius);
            var nextRight = Math.Min(frameCount, i + radius + 1);
            while (left < nextLeft)
                localSum -= flux[left++];
            while (right < nextRight)
                localSum += flux[right++];

            var value = (float)Math.Max(0, flux[i] - localSum / (right - left));
            envelope[i] = value;
            sum += value;
            sumSquared += (double)value * value;
        }

        var mean = sum / frameCount;
        var variance = sumSquared / frameCount - mean * mean;
        // Avoid amplifying numerical residue in effectively silent audio.
        if (variance <= 1e-16)
            return new float[frameCount];

        var inverseStd = 1 / Math.Sqrt(variance);
        for (var i = 0; i < frameCount; i++)
        {
            if ((i & 4095) == 0)
                token.ThrowIfCancellationRequested();
            envelope[i] = (float)((envelope[i] - mean) * inverseStd);
        }
        return envelope;
    }
}
