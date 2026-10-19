using System.Numerics;

namespace StemForge.Dsp;

/// <summary>
/// 純 C# 高速 Radix-2 Cooley-Tukey FFT 與 Hann Window Overlap-Add 短時距傅立葉轉換 (STFT/iSTFT) 引擎
/// </summary>
public static class StftEngine
{
    public static float[] CreatePeriodicHannWindow(int fftSize)
    {
        var window = new float[fftSize];
        for (int i = 0; i < fftSize; i++)
        {
            window[i] = 0.5f * (1.0f - MathF.Cos(2.0f * MathF.PI * i / fftSize));
        }
        return window;
    }

    public static void FftInPlace(Span<Complex> buffer, bool inverse)
    {
        int n = buffer.Length;
        int bits = (int)Math.Log2(n);

        for (int j = 1, i = 0; j < n; j++)
        {
            int bit = n >> 1;
            for (; (i & bit) != 0; bit >>= 1)
                i ^= bit;
            i ^= bit;

            if (j < i)
            {
                (buffer[j], buffer[i]) = (buffer[i], buffer[j]);
            }
        }

        double angleSign = inverse ? 2.0 * Math.PI : -2.0 * Math.PI;
        for (int len = 2; len <= n; len <<= 1)
        {
            double ang = angleSign / len;
            var wlen = new Complex(Math.Cos(ang), Math.Sin(ang));
            int halfLen = len >> 1;

            for (int i = 0; i < n; i += len)
            {
                var w = Complex.One;
                for (int j = 0; j < halfLen; j++)
                {
                    var u = buffer[i + j];
                    var v = buffer[i + j + halfLen] * w;
                    buffer[i + j] = u + v;
                    buffer[i + j + halfLen] = u - v;
                    w *= wlen;
                }
            }
        }

        if (inverse)
        {
            double invN = 1.0 / n;
            for (int i = 0; i < n; i++)
            {
                buffer[i] *= invN;
            }
        }
    }

    /// <summary>
    /// 計算單聲道訊號的複數頻譜矩陣 [numFrames][numBins (fftSize/2 + 1)]
    /// </summary>
    public static Complex[][] ForwardStft(float[] signal, int fftSize = 2048, int hopSize = 512)
    {
        int numBins = fftSize / 2 + 1;
        int paddedLen = signal.Length + fftSize;
        int numFrames = Math.Max(1, (paddedLen - fftSize) / hopSize + 1);
        var window = CreatePeriodicHannWindow(fftSize);

        var spectrogram = new Complex[numFrames][];

        Parallel.For(0, numFrames, frameIdx =>
        {
            var frameBuf = new Complex[fftSize];
            int startSample = frameIdx * hopSize - fftSize / 2;

            for (int i = 0; i < fftSize; i++)
            {
                int srcIdx = startSample + i;
                float sample = (srcIdx >= 0 && srcIdx < signal.Length) ? signal[srcIdx] : 0f;
                frameBuf[i] = new Complex(sample * window[i], 0.0);
            }

            FftInPlace(frameBuf, inverse: false);

            var halfSpec = new Complex[numBins];
            Array.Copy(frameBuf, halfSpec, numBins);
            spectrogram[frameIdx] = halfSpec;
        });

        return spectrogram;
    }

    /// <summary>
    /// 從半頻譜矩陣 [numFrames][numBins] 逆轉換回時域訊號 (COLA 正規化)
    /// </summary>
    public static float[] InverseStft(Complex[][] spectrogram, int originalLength, int fftSize = 2048, int hopSize = 512)
    {
        int numFrames = spectrogram.Length;
        int numBins = fftSize / 2 + 1;
        var window = CreatePeriodicHannWindow(fftSize);

        int totalOutLen = originalLength + fftSize;
        var output = new float[totalOutLen];
        var windowSum = new float[totalOutLen];

        var framesTimeDomain = new float[numFrames][];

        Parallel.For(0, numFrames, frameIdx =>
        {
            var fullSpec = new Complex[fftSize];
            var halfSpec = spectrogram[frameIdx];
            for (int b = 0; b < numBins; b++)
            {
                fullSpec[b] = halfSpec[b];
            }
            for (int b = 1; b < fftSize / 2; b++)
            {
                fullSpec[fftSize - b] = Complex.Conjugate(halfSpec[b]);
            }

            FftInPlace(fullSpec, inverse: true);

            var timeFrame = new float[fftSize];
            for (int i = 0; i < fftSize; i++)
            {
                timeFrame[i] = (float)fullSpec[i].Real * window[i];
            }
            framesTimeDomain[frameIdx] = timeFrame;
        });

        for (int frameIdx = 0; frameIdx < numFrames; frameIdx++)
        {
            int startSample = frameIdx * hopSize;
            var timeFrame = framesTimeDomain[frameIdx];
            for (int i = 0; i < fftSize; i++)
            {
                int dstIdx = startSample + i;
                if (dstIdx >= 0 && dstIdx < totalOutLen)
                {
                    output[dstIdx] += timeFrame[i];
                    windowSum[dstIdx] += window[i] * window[i];
                }
            }
        }

        var trimmed = new float[originalLength];
        int padOffset = fftSize / 2;
        for (int i = 0; i < originalLength; i++)
        {
            int srcIdx = i + padOffset;
            float norm = windowSum[srcIdx];
            trimmed[i] = norm > 1e-6f ? output[srcIdx] / norm : output[srcIdx];
        }

        return trimmed;
    }
}
