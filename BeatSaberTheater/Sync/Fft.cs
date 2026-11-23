using System;
using System.Threading;

namespace BeatSaberTheater.Sync;

internal sealed class Fft
{
    private readonly int _size;
    private readonly int[] _reversed;
    private readonly float[] _twiddleRe;
    private readonly float[] _twiddleIm;

    public Fft(int size, CancellationToken token)
    {
        if (size <= 0 || (size & (size - 1)) != 0)
            throw new ArgumentOutOfRangeException(nameof(size), "FFT size must be a power of two.");

        _size = size;
        _reversed = new int[size];
        for (var i = 1; i < size; i++)
        {
            if ((i & 4095) == 0)
                token.ThrowIfCancellationRequested();
            _reversed[i] = (_reversed[i >> 1] >> 1) | ((i & 1) * (size >> 1));
        }

        // Pack each stage's twiddles contiguously so the butterfly loop can stream them.
        _twiddleRe = new float[size - 1];
        _twiddleIm = new float[size - 1];
        for (var half = 1; half < size; half <<= 1)
        {
            for (var j = 0; j < half; j++)
            {
                if ((j & 4095) == 0)
                    token.ThrowIfCancellationRequested();
                var angle = -Math.PI * j / half;
                _twiddleRe[half - 1 + j] = (float)Math.Cos(angle);
                _twiddleIm[half - 1 + j] = (float)Math.Sin(angle);
            }
        }
    }

    public void Transform(float[] re, float[] im, bool inverse, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        for (var i = 0; i < _size; i++)
        {
            if ((i & 4095) == 0)
                token.ThrowIfCancellationRequested();
            var j = _reversed[i];
            if (j <= i)
                continue;

            var real = re[i];
            var imaginary = im[i];
            re[i] = re[j];
            im[i] = im[j];
            re[j] = real;
            im[j] = imaginary;
        }

        var sign = inverse ? -1f : 1f;
        for (var half = 1; half < _size; half <<= 1)
        {
            token.ThrowIfCancellationRequested();
            var width = half << 1;
            for (var start = 0; start < _size; start += width)
            {
                if ((start & 4095) == 0)
                    token.ThrowIfCancellationRequested();

                for (var chunk = 0; chunk < half; chunk += 4096)
                {
                    if (chunk != 0)
                        token.ThrowIfCancellationRequested();
                    var end = Math.Min(chunk + 4096, half);
                    for (var j = chunk; j < end; j++)
                    {
                        var left = start + j;
                        var right = left + half;
                        var wr = _twiddleRe[half - 1 + j];
                        var wi = _twiddleIm[half - 1 + j] * sign;
                        var real = wr * re[right] - wi * im[right];
                        var imaginary = wr * im[right] + wi * re[right];
                        re[right] = re[left] - real;
                        im[right] = im[left] - imaginary;
                        re[left] += real;
                        im[left] += imaginary;
                    }
                }
            }
        }

        if (inverse)
        {
            var scale = 1f / _size;
            for (var i = 0; i < _size; i++)
            {
                if ((i & 4095) == 0)
                    token.ThrowIfCancellationRequested();
                re[i] *= scale;
                im[i] *= scale;
            }
        }
    }
}
