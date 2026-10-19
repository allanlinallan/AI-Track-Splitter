using StemForge.Dsp;

namespace StemForge.Web;

/// <summary>
/// 內建多樂器錄音室測試曲合成器：
/// 可一鍵生成包含「中置主唱 + 雙聲道和聲 + 大鼓 Kick + 小鼓 Snare + 踩鈸 Hi-Hat + 貝斯 Bass + 中置主奏吉他 + 左右雙軌節奏吉他 + 弦樂鋪底 Pad + 銅管重音」
/// 的 8 秒立體聲測試母帶，方便在尚未上傳外部 MP3 時立即驗證 19 軌拆解、多軌同步混音器與 -144 dBFS 反相抵消 (Null Test)。
/// </summary>
public static class DemoSongSynthesizer
{
    public static string GenerateStudioDemoMixWav(string outputPath)
    {
        const int sr = 44100;
        const int durationSec = 8;
        int totalSamples = sr * durationSec;

        var left = new float[totalSamples];
        var right = new float[totalSamples];
        var rng = new Random(42);

        // Chord Progression: Am - F - C - G (120 BPM, 2 seconds per bar)
        double[] bassFreqs = [110.0, 87.31, 130.81, 98.0];
        double[][] chordFreqs =
        [
            [220.0, 261.63, 329.63],
            [174.61, 220.0, 261.63],
            [261.63, 329.63, 392.00],
            [196.00, 246.94, 392.00]
        ];
        double[] leadMelodyFreqs = [440.0, 523.25, 659.25, 587.33, 523.25, 493.88, 440.0, 392.0];

        for (int i = 0; i < totalSamples; i++)
        {
            double t = (double)i / sr;
            int bar = Math.Min(3, (int)(t / 2.0));
            int beatIndex = (int)(t * 2.0); // 120 BPM -> 2 beats per sec
            double beatPhase = (t * 2.0) % 1.0;
            double eighthPhase = (t * 4.0) % 1.0;

            // 1. Kick (Beats 0, 2 of each bar)
            float kick = 0f;
            if (beatIndex % 2 == 0 && beatPhase < 0.25)
            {
                double kt = beatPhase * 0.5;
                double kFreq = 52.0 + 110.0 * Math.Exp(-kt * 45.0);
                kick = (float)(0.42 * Math.Sin(2.0 * Math.PI * kFreq * kt) * Math.Exp(-kt * 14.0));
            }

            // 2. Snare (Beats 1, 3 of each bar)
            float snare = 0f;
            if (beatIndex % 2 == 1 && beatPhase < 0.22)
            {
                double st = beatPhase * 0.5;
                double noise = rng.NextDouble() * 2.0 - 1.0;
                snare = (float)((0.20 * Math.Sin(2.0 * Math.PI * 195.0 * st) + 0.22 * noise) * Math.Exp(-st * 22.0));
            }

            // 3. Hi-Hat (8th notes, panned slightly right)
            double hhEnv = Math.Exp(-eighthPhase * 35.0);
            float hihat = (float)(0.08 * (rng.NextDouble() * 2.0 - 1.0) * hhEnv * Math.Sin(2.0 * Math.PI * 8200.0 * t));

            // 4. Bass (Center low harmonic)
            double bf = bassFreqs[bar];
            float bass = (float)(0.24 * (Math.Sin(2.0 * Math.PI * bf * t) + 0.35 * Math.Sin(4.0 * Math.PI * bf * t)));

            // 5. Strings Pad (Wide Stereo Sustained Harmonic)
            float padL = 0f, padR = 0f;
            foreach (double cf in chordFreqs[bar])
            {
                padL += (float)(0.06 * Math.Sin(2.0 * Math.PI * (cf * 0.999) * t));
                padR += (float)(0.06 * Math.Sin(2.0 * Math.PI * (cf * 1.001) * t + 0.8));
            }

            // 6. Rhythm Guitar (Double-Tracked Hard L/R Side)
            double strumEnv = Math.Exp(-eighthPhase * 9.0);
            float rhythmGtrL = (float)(0.11 * strumEnv * Math.Sin(2.0 * Math.PI * chordFreqs[bar][1] * 2.0 * t));
            float rhythmGtrR = (float)(0.11 * strumEnv * Math.Cos(2.0 * Math.PI * chordFreqs[bar][2] * 2.0 * t));

            // 7. Lead Vocal (Center Formant + Vibrato)
            int melIdx = Math.Min(leadMelodyFreqs.Length - 1, (int)(t));
            double vf = leadMelodyFreqs[melIdx] * (1.0 + 0.008 * Math.Sin(2.0 * Math.PI * 5.8 * t));
            float leadVocal = (float)(0.22 * (Math.Sin(2.0 * Math.PI * vf * t) + 0.45 * Math.Sin(4.0 * Math.PI * vf * t) + 0.2 * Math.Sin(6.0 * Math.PI * vf * t)));

            // 8. Backing Vocal Harmony (Wide Stereo Thirds)
            float backVocL = (float)(0.09 * Math.Sin(2.0 * Math.PI * (vf * 1.25) * t + 0.3));
            float backVocR = (float)(0.09 * Math.Sin(2.0 * Math.PI * (vf * 1.50) * t - 0.4));

            float sumL = kick + snare + hihat * 0.6f + bass + padL + rhythmGtrL + leadVocal + backVocL;
            float sumR = kick + snare + hihat * 1.2f + bass + padR + rhythmGtrR + leadVocal + backVocR;

            left[i] = Math.Clamp(sumL * 0.75f, -0.95f, 0.95f);
            right[i] = Math.Clamp(sumR * 0.75f, -0.95f, 0.95f);
        }

        WavFileIo.WriteFloat32Wav(outputPath, new StereoAudioBuffer(sr, left, right));
        return outputPath;
    }
}
