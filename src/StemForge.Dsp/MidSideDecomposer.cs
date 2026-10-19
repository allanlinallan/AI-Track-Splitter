using System.Numerics;
using System.Numerics.Tensors;

namespace StemForge.Dsp;

/// <summary>
/// 空間相位分解引擎：
/// 1. 頻域相位相似度遮罩 (STFT Panning Similarity Mask)：將吉他/樂器母軌精準拆為
///    - Center (中置主奏 Lead / Solo / 指彈)
///    - Side (極左極右雙軌刷弦 Rhythm Guitar / 寬立體聲伴奏)
///    並嚴格保證 Center + Side === 原始立體聲母軌 (100% 相位能量守恆)。
/// 2. 32-bit Float 反相相消器：用於從 Wet 軌減去 Dry 軌，無損抽出純空間殘響 (Reverb FX)。
/// </summary>
public static class MidSideDecomposer
{
    public static (StereoAudioBuffer CenterLead, StereoAudioBuffer SideRhythm) DecomposeCenterAndSide(
        StereoAudioBuffer stereoInput,
        float centerSharpnessGamma = 2.8f)
    {
        const int fftSize = 2048;
        const int hopSize = 512;

        var specL = StftEngine.ForwardStft(stereoInput.Left, fftSize, hopSize);
        var specR = StftEngine.ForwardStft(stereoInput.Right, fftSize, hopSize);

        int numFrames = specL.Length;
        int numBins = fftSize / 2 + 1;

        var centerSpecL = new Complex[numFrames][];
        var centerSpecR = new Complex[numFrames][];

        Parallel.For(0, numFrames, f =>
        {
            var cL = new Complex[numBins];
            var cR = new Complex[numBins];
            var frameL = specL[f];
            var frameR = specR[f];

            for (int b = 0; b < numBins; b++)
            {
                Complex l = frameL[b];
                Complex r = frameR[b];

                double magSqL = l.Real * l.Real + l.Imaginary * l.Imaginary;
                double magSqR = r.Real * r.Real + r.Imaginary * r.Imaginary;
                double denom = magSqL + magSqR + 1e-12;

                // 計算左右聲道內積實部 (衡量同相中置程度)
                double crossReal = l.Real * r.Real + l.Imaginary * r.Imaginary;
                double similarity = Math.Clamp(2.0 * Math.Max(0.0, crossReal) / denom, 0.0, 1.0);
                double centerMask = Math.Pow(similarity, centerSharpnessGamma);

                cL[b] = l * centerMask;
                cR[b] = r * centerMask;
            }

            centerSpecL[f] = cL;
            centerSpecR[f] = cR;
        });

        float[] centerLeft = StftEngine.InverseStft(centerSpecL, stereoInput.Length, fftSize, hopSize);
        float[] centerRight = StftEngine.InverseStft(centerSpecR, stereoInput.Length, fftSize, hopSize);

        // Side 採用嚴格殘差相減：確保 Center + Side === stereoInput 100% Bit-Accurate
        float[] sideLeft = new float[stereoInput.Length];
        float[] sideRight = new float[stereoInput.Length];

        TensorPrimitives.Subtract(stereoInput.Left, centerLeft, sideLeft);
        TensorPrimitives.Subtract(stereoInput.Right, centerRight, sideRight);

        return (
            new StereoAudioBuffer(stereoInput.SampleRate, centerLeft, centerRight),
            new StereoAudioBuffer(stereoInput.SampleRate, sideLeft, sideRight)
        );
    }

    /// <summary>
    /// 執行 32-bit Float 反相相消：Residual = WetParent - DryChild (例如抽出純 Reverb 殘響軌或純旋律殘差軌)
    /// </summary>
    public static StereoAudioBuffer SubtractExact(StereoAudioBuffer parentWet, StereoAudioBuffer childDry)
    {
        var alignedChild = WavFileIo.AlignLength(childDry, parentWet.Length);
        var diffL = new float[parentWet.Length];
        var diffR = new float[parentWet.Length];

        TensorPrimitives.Subtract(parentWet.Left, alignedChild.Left, diffL);
        TensorPrimitives.Subtract(parentWet.Right, alignedChild.Right, diffR);

        return new StereoAudioBuffer(parentWet.SampleRate, diffL, diffR);
    }
}
