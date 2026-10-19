using System.Numerics;
using System.Numerics.Tensors;

namespace StemForge.Dsp;

/// <summary>
/// Fitzgerald 二維頻譜中值濾波諧波/瞬態分離器 (Harmonic-Percussive Source Separation, HPSS)：
/// - 橫向（時間軸）中值濾波捕捉連續穩態諧波：小提琴/大提琴弦樂群 (Strings)、合成器鋪底 (Warm Pad)、管風琴
/// - 縱向（頻率軸）中值濾波捕捉寬頻瞬態衝擊：銅管重音齊奏 (Brass Stabs)、合成器撥奏 (Pluck / Arp)、打擊特效
/// 並透過 Soft Wiener Masking 與互補差值，確保 Harmonic + Percussive === Mother Stem 100% 能量守恆。
/// </summary>
public static class HpssDecomposer
{
    public static (StereoAudioBuffer HarmonicStringsPad, StereoAudioBuffer PercussiveBrassPluck) SeparateHarmonicPercussive(
        StereoAudioBuffer input,
        int timeMedianKernel = 15,
        int freqMedianKernel = 15,
        float wienerPower = 2.0f)
    {
        const int fftSize = 2048;
        const int hopSize = 512;

        var specL = StftEngine.ForwardStft(input.Left, fftSize, hopSize);
        var specR = StftEngine.ForwardStft(input.Right, fftSize, hopSize);

        int numFrames = specL.Length;
        int numBins = fftSize / 2 + 1;

        // 計算立體聲合成振幅頻譜 Mag[frame][bin]
        var mag = new float[numFrames][];
        Parallel.For(0, numFrames, t =>
        {
            var m = new float[numBins];
            for (int f = 0; f < numBins; f++)
            {
                double ml = specL[t][f].Magnitude;
                double mr = specR[t][f].Magnitude;
                m[f] = (float)Math.Sqrt((ml * ml + mr * mr) * 0.5);
            }
            mag[t] = m;
        });

        int halfT = timeMedianKernel / 2;
        int halfF = freqMedianKernel / 2;

        var harmSpecL = new Complex[numFrames][];
        var harmSpecR = new Complex[numFrames][];

        Parallel.For(0, numFrames, t =>
        {
            var hL = new Complex[numBins];
            var hR = new Complex[numBins];
            Span<float> timeWindow = stackalloc float[timeMedianKernel];
            Span<float> freqWindow = stackalloc float[freqMedianKernel];

            for (int f = 0; f < numBins; f++)
            {
                // 1. 時間軸中值 (Harmonic 穩態能量)
                int tCount = 0;
                for (int dt = -halfT; dt <= halfT; dt++)
                {
                    int tt = Math.Clamp(t + dt, 0, numFrames - 1);
                    timeWindow[tCount++] = mag[tt][f];
                }
                float hVal = QuickMedian(timeWindow[..tCount]);

                // 2. 頻率軸中值 (Percussive 瞬態能量)
                int fCount = 0;
                for (int df = -halfF; df <= halfF; df++)
                {
                    int ff = Math.Clamp(f + df, 0, numBins - 1);
                    freqWindow[fCount++] = mag[t][ff];
                }
                float pVal = QuickMedian(freqWindow[..fCount]);

                double hPow = Math.Pow(hVal, wienerPower);
                double pPow = Math.Pow(pVal, wienerPower);
                double hMask = hPow / (hPow + pPow + 1e-12);

                hL[f] = specL[t][f] * hMask;
                hR[f] = specR[t][f] * hMask;
            }

            harmSpecL[t] = hL;
            harmSpecR[t] = hR;
        });

        float[] harmLeft = StftEngine.InverseStft(harmSpecL, input.Length, fftSize, hopSize);
        float[] harmRight = StftEngine.InverseStft(harmSpecR, input.Length, fftSize, hopSize);

        float[] percLeft = new float[input.Length];
        float[] percRight = new float[input.Length];

        TensorPrimitives.Subtract(input.Left, harmLeft, percLeft);
        TensorPrimitives.Subtract(input.Right, harmRight, percRight);

        return (
            new StereoAudioBuffer(input.SampleRate, harmLeft, harmRight),
            new StereoAudioBuffer(input.SampleRate, percLeft, percRight)
        );
    }

    /// <summary>
    /// 高純度頻譜諧波峰突顯與瞬態/空間遮罩分頻器：
    /// 結合：
    /// 1. 頻帶高階平滑濾波 (Butterworth-like STFT Roll-off)
    /// 2. 窄頻諧波尖峰突顯遮罩 (Harmonic Peak Salience Mask)：抑制寬頻背景和弦/Pad 串音，只保留長笛/提琴/管樂等明確音高線條
    /// 3. 時間軸延音穩定度 vs 瞬態衝擊遮罩 (Sustained vs Transient Envelope Mask)
    /// 4. 動態頻譜降串音閘門 (Spectral De-bleed Gate)：消除音符間隙的底噪與其他樂器殘留
    /// </summary>
    public static StereoAudioBuffer ExtractFrequencyBand(
        StereoAudioBuffer input,
        float lowCutHz,
        float highCutHz,
        bool preferCenter = false,
        bool preferTransient = false,
        bool isolateNarrowbandMelody = false)
    {
        const int fftSize = 2048;
        const int hopSize = 512;

        var specL = StftEngine.ForwardStft(input.Left, fftSize, hopSize);
        var specR = StftEngine.ForwardStft(input.Right, fftSize, hopSize);

        int numFrames = specL.Length;
        int numBins = fftSize / 2 + 1;
        float binHz = (float)input.SampleRate / fftSize;

        // 預先計算每幀每頻點的立體聲合成振幅與全域峰值，供頻譜降噪閘門與諧波突顯度使用
        var mag = new float[numFrames][];
        float globalMaxMag = 1e-6f;

        for (int t = 0; t < numFrames; t++)
        {
            var m = new float[numBins];
            float frameMax = 0f;
            for (int b = 0; b < numBins; b++)
            {
                float v = (float)Math.Sqrt((specL[t][b].Magnitude * specL[t][b].Magnitude + specR[t][b].Magnitude * specR[t][b].Magnitude) * 0.5);
                m[b] = v;
                if (v > frameMax) frameMax = v;
            }
            mag[t] = m;
            if (frameMax > globalMaxMag) globalMaxMag = frameMax;
        }

        var outSpecL = new Complex[numFrames][];
        var outSpecR = new Complex[numFrames][];

        Parallel.For(0, numFrames, t =>
        {
            var oL = new Complex[numBins];
            var oR = new Complex[numBins];

            // 計算目標頻帶內的當幀最大振幅，作為動態串音閘門基準
            float bandPeak = 1e-6f;
            int minBin = Math.Max(1, (int)(lowCutHz * 0.85f / binHz));
            int maxBin = Math.Min(numBins - 1, (int)(highCutHz * 1.15f / binHz));
            for (int b = minBin; b <= maxBin; b++)
            {
                if (mag[t][b] > bandPeak) bandPeak = mag[t][b];
            }

            for (int b = minBin; b <= maxBin; b++)
            {
                float freq = b * binHz;

                // 1. 陡峭平滑頻帶遮罩 (消除頻帶外串音)
                float gain = 1.0f;
                if (freq < lowCutHz && lowCutHz > 1f)
                {
                    float roll = Math.Clamp((freq - lowCutHz * 0.85f) / (lowCutHz * 0.15f), 0f, 1f);
                    gain *= roll * roll;
                }
                if (freq > highCutHz)
                {
                    float roll = Math.Clamp((highCutHz * 1.15f - freq) / (highCutHz * 0.15f), 0f, 1f);
                    gain *= roll * roll;
                }

                Complex l = specL[t][b];
                Complex r = specR[t][b];

                // 2. 中置 / 兩側空間定位遮罩
                if (preferCenter)
                {
                    double denom = l.Magnitude * l.Magnitude + r.Magnitude * r.Magnitude + 1e-12;
                    double cross = Math.Max(0.0, l.Real * r.Real + l.Imaginary * r.Imaginary);
                    gain *= (float)Math.Pow(Math.Clamp(2.0 * cross / denom, 0.0, 1.0), 2.2);
                }

                // 3. 瞬態 vs 穩態延音遮罩 (真正壓制非目標特性的串音)
                if (t > 1 && t < numFrames - 2)
                {
                    float curr = mag[t][b];
                    float prev = 0.5f * (mag[t - 1][b] + mag[t - 2][b]);
                    float next = 0.5f * (mag[t + 1][b] + mag[t + 2][b]);
                    float neighborTimeAvg = 0.5f * (prev + next);

                    if (preferTransient)
                    {
                        // 只保留瞬間爆發的鼓點/撥弦瞬態，壓制長音鋪底
                        float flux = Math.Clamp((curr - prev * 0.82f) / (curr + 1e-7f), 0f, 1f);
                        gain *= flux * flux;
                    }
                    else
                    {
                        // 只保留時間軸連續穩定的諧波音符，壓制打擊樂與短促雜訊串音
                        float sustainRatio = Math.Clamp(neighborTimeAvg / (curr + 1e-7f), 0f, 1.2f);
                        float sustainMask = Math.Clamp(sustainRatio * 1.1f, 0.02f, 1.0f);
                        gain *= sustainMask * sustainMask;
                    }
                }

                // 4. 窄頻獨奏樂器諧波尖峰突顯 (針對長笛 Flute、小提琴、薩克斯風等單旋律樂器去除寬頻和弦伴奏串音)
                if (isolateNarrowbandMelody)
                {
                    int win = 10;
                    float sumLocal = 0f;
                    int cnt = 0;
                    for (int db = -win; db <= win; db++)
                    {
                        if (Math.Abs(db) <= 1) continue;
                        int bb = Math.Clamp(b + db, 0, numBins - 1);
                        sumLocal += mag[t][bb];
                        cnt++;
                    }
                    float localFloor = (sumLocal / Math.Max(1, cnt)) + 1e-7f;
                    float salience = mag[t][b] / localFloor;
                    // 只有顯著高於周圍頻段背景基底的泛音峰才保留，平坦的寬頻噪音/Pad 直接衰減
                    float salienceMask = Math.Clamp((salience - 1.25f) / 2.2f, 0.0f, 1.0f);
                    gain *= salienceMask * salienceMask;
                }

                // 5. 頻譜微串音軟閘門 (Spectral De-bleed Soft Gate)：濾除低於當幀峰值與全域基底的微弱洩漏音
                float relToFrame = mag[t][b] / bandPeak;
                float relToGlobal = mag[t][b] / globalMaxMag;
                if (relToFrame < 0.12f || relToGlobal < 0.015f)
                {
                    float g1 = Math.Clamp(relToFrame / 0.12f, 0f, 1f);
                    float g2 = Math.Clamp(relToGlobal / 0.015f, 0f, 1f);
                    gain *= g1 * g1 * g2;
                }

                oL[b] = l * gain;
                oR[b] = r * gain;
            }

            outSpecL[t] = oL;
            outSpecR[t] = oR;
        });

        return new StereoAudioBuffer(
            input.SampleRate,
            StftEngine.InverseStft(outSpecL, input.Length, fftSize, hopSize),
            StftEngine.InverseStft(outSpecR, input.Length, fftSize, hopSize)
        );
    }

    private static float QuickMedian(Span<float> span)
    {
        span.Sort();
        int mid = span.Length / 2;
        return (span.Length & 1) == 1 ? span[mid] : 0.5f * (span[mid - 1] + span[mid]);
    }
}
