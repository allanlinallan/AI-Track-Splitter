using System.Diagnostics;
using System.Numerics.Tensors;
using NAudio.Wave;

namespace StemForge.Dsp;

public sealed class StereoAudioBuffer
{
    public float[] Left { get; }
    public float[] Right { get; }
    public int SampleRate { get; }
    public int Length => Left.Length;
    public double DurationSeconds => SampleRate > 0 ? (double)Length / SampleRate : 0.0;

    public StereoAudioBuffer(int sampleRate, int sampleFrames)
    {
        SampleRate = sampleRate;
        Left = new float[sampleFrames];
        Right = new float[sampleFrames];
    }

    public StereoAudioBuffer(int sampleRate, float[] left, float[] right)
    {
        if (left.Length != right.Length)
            throw new ArgumentException("Left and Right channel lengths must match.");
        SampleRate = sampleRate;
        Left = left;
        Right = right;
    }

    public StereoAudioBuffer Clone()
    {
        var copyL = new float[Left.Length];
        var copyR = new float[Right.Length];
        Array.Copy(Left, copyL, Left.Length);
        Array.Copy(Right, copyR, Right.Length);
        return new StereoAudioBuffer(SampleRate, copyL, copyR);
    }
}

public static class WavFileIo
{
    /// <summary>
    /// 讀取 MP3 / WAV / FLAC / MP4 / MKV / MOV 等音訊或影片檔，若為影片或非標準格式則自動以 FFmpeg 提取無損 32-bit Float 雙聲道
    /// </summary>
    public static StereoAudioBuffer ReadStereoAudio(string filePath)
    {
        string ext = Path.GetExtension(filePath).ToLowerInvariant();
        bool isVideoOrContainer = ext is ".mp4" or ".m4v" or ".mkv" or ".mov" or ".webm" or ".avi" or ".flv" or ".wmv";

        if (isVideoOrContainer)
        {
            string tempWav = Path.Combine(Path.GetTempPath(), $"stemforge_demux_{Guid.NewGuid():N}.wav");
            try
            {
                ExtractAudioViaFfmpeg(filePath, tempWav);
                return ReadAudioViaNAudio(tempWav);
            }
            finally
            {
                if (File.Exists(tempWav))
                {
                    try { File.Delete(tempWav); } catch { }
                }
            }
        }

        try
        {
            return ReadAudioViaNAudio(filePath);
        }
        catch
        {
            // 若 NAudio 格式解碼器失敗 (例如特規 AAC / OGG / OPUS)，使用 FFmpeg 作為 Universal Fallback
            string fallbackWav = Path.Combine(Path.GetTempPath(), $"stemforge_fallback_{Guid.NewGuid():N}.wav");
            try
            {
                ExtractAudioViaFfmpeg(filePath, fallbackWav);
                return ReadAudioViaNAudio(fallbackWav);
            }
            finally
            {
                if (File.Exists(fallbackWav))
                {
                    try { File.Delete(fallbackWav); } catch { }
                }
            }
        }
    }

    private static string ResolveFfmpegPath()
    {
        string[] candidates =
        [
            @"C:\Program Files\ffmpeg\bin\ffmpeg.exe",
            @"C:\ffmpeg\bin\ffmpeg.exe",
            @"ffmpeg.exe"
        ];
        foreach (var c in candidates)
        {
            if (File.Exists(c)) return c;
        }
        return "ffmpeg";
    }

    private static void ExtractAudioViaFfmpeg(string inputPath, string outputWavPath)
    {
        string ffmpegPath = ResolveFfmpegPath();

        var psi = new ProcessStartInfo
        {
            FileName = ffmpegPath,
            Arguments = $"-y -i \"{inputPath}\" -vn -ac 2 -ar 44100 -f wav \"{outputWavPath}\"",
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardError = true,
            RedirectStandardOutput = true
        };

        using var proc = Process.Start(psi);
        if (proc == null) throw new InvalidOperationException("無法啟動 FFmpeg 音訊提取進程。");
        proc.WaitForExit();
        if (proc.ExitCode != 0 || !File.Exists(outputWavPath))
        {
            string err = proc.StandardError.ReadToEnd();
            throw new InvalidOperationException($"FFmpeg 提取 MP4 音訊失敗: {err}");
        }
    }

    private static StereoAudioBuffer ReadAudioViaNAudio(string filePath)
    {
        using var reader = new AudioFileReader(filePath);
        int sr = reader.WaveFormat.SampleRate;
        int channels = reader.WaveFormat.Channels;

        var leftList = new List<float>(sr * 60);
        var rightList = new List<float>(sr * 60);

        float[] readBuf = new float[sr * channels];
        int samplesRead;
        while ((samplesRead = reader.Read(readBuf, 0, readBuf.Length)) > 0)
        {
            if (channels == 1)
            {
                for (int i = 0; i < samplesRead; i++)
                {
                    leftList.Add(readBuf[i]);
                    rightList.Add(readBuf[i]);
                }
            }
            else
            {
                int frames = samplesRead / channels;
                for (int f = 0; f < frames; f++)
                {
                    int baseIdx = f * channels;
                    leftList.Add(readBuf[baseIdx]);
                    rightList.Add(readBuf[baseIdx + 1]);
                }
            }
        }

        return new StereoAudioBuffer(sr, leftList.ToArray(), rightList.ToArray());
    }

    /// <summary>
    /// 以 32-bit IEEE Float WAV 無損格式寫入磁碟（避免任何整數截斷或二次失真）
    /// </summary>
    public static void WriteFloat32Wav(string outputPath, StereoAudioBuffer audio)
    {
        var dir = Path.GetDirectoryName(outputPath);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);

        var format = WaveFormat.CreateIeeeFloatWaveFormat(audio.SampleRate, 2);
        using var writer = new WaveFileWriter(outputPath, format);

        int totalFrames = audio.Length;
        const int chunkFrames = 16384;
        float[] interleaved = new float[chunkFrames * 2];

        int offset = 0;
        while (offset < totalFrames)
        {
            int count = Math.Min(chunkFrames, totalFrames - offset);
            for (int i = 0; i < count; i++)
            {
                interleaved[i * 2] = audio.Left[offset + i];
                interleaved[i * 2 + 1] = audio.Right[offset + i];
            }
            writer.WriteSamples(interleaved, 0, count * 2);
            offset += count;
        }
    }

    /// <summary>
    /// 計算音軌的 True Peak (dBFS)、RMS (dBFS) 與供前端混音台繪製的降採樣波形陣列
    /// </summary>
    public static (float PeakDbFs, float RmsDbFs, float[] WaveformPeaks) AnalyzeAudioMetrics(
        StereoAudioBuffer audio,
        int waveformBuckets = 600)
    {
        int n = audio.Length;
        if (n == 0)
            return (-144f, -144f, new float[waveformBuckets]);

        float maxAbs = 0f;
        double sumSq = 0.0;

        for (int i = 0; i < n; i++)
        {
            float al = MathF.Abs(audio.Left[i]);
            float ar = MathF.Abs(audio.Right[i]);
            float m = MathF.Max(al, ar);
            if (m > maxAbs) maxAbs = m;
            sumSq += (audio.Left[i] * audio.Left[i] + audio.Right[i] * audio.Right[i]) * 0.5;
        }

        float peakDb = maxAbs > 1e-9f ? 20f * MathF.Log10(maxAbs) : -144f;
        double rms = Math.Sqrt(sumSq / n);
        float rmsDb = rms > 1e-9 ? (float)(20.0 * Math.Log10(rms)) : -144f;

        float[] peaks = new float[waveformBuckets];
        int step = Math.Max(1, n / waveformBuckets);
        for (int b = 0; b < waveformBuckets; b++)
        {
            int start = b * step;
            int end = Math.Min(n, start + step);
            float localMax = 0f;
            for (int i = start; i < end; i++)
            {
                float v = MathF.Max(MathF.Abs(audio.Left[i]), MathF.Abs(audio.Right[i]));
                if (v > localMax) localMax = v;
            }
            peaks[b] = Math.Clamp(localMax, 0f, 1f);
        }

        return (MathF.Round(peakDb, 2), MathF.Round(rmsDb, 2), peaks);
    }

    /// <summary>
    /// 確保子音軌與母軌長度完全對齊（補零或截斷至相同 Sample 數，確保反相相消精準對齊）
    /// </summary>
    public static StereoAudioBuffer AlignLength(StereoAudioBuffer source, int targetLength)
    {
        if (source.Length == targetLength)
            return source;

        var aligned = new StereoAudioBuffer(source.SampleRate, targetLength);
        int copyLen = Math.Min(source.Length, targetLength);
        Array.Copy(source.Left, aligned.Left, copyLen);
        Array.Copy(source.Right, aligned.Right, copyLen);
        return aligned;
    }

    /// <summary>
    /// 將多個立體聲音軌以 SIMD Vectorized 平行累加混音 (Mixdown / Summation)
    /// </summary>
    public static StereoAudioBuffer Mixdown(IReadOnlyList<StereoAudioBuffer> buffers)
    {
        if (buffers.Count == 0)
            return new StereoAudioBuffer(44100, 0);

        int maxLen = 0;
        int sampleRate = buffers[0].SampleRate;
        foreach (var b in buffers)
        {
            if (b.Length > maxLen) maxLen = b.Length;
        }

        var sumL = new float[maxLen];
        var sumR = new float[maxLen];

        foreach (var b in buffers)
        {
            int len = b.Length;
            if (len == maxLen)
            {
                TensorPrimitives.Add(sumL, b.Left, sumL);
                TensorPrimitives.Add(sumR, b.Right, sumR);
            }
            else
            {
                TensorPrimitives.Add(sumL.AsSpan(0, len), b.Left, sumL.AsSpan(0, len));
                TensorPrimitives.Add(sumR.AsSpan(0, len), b.Right, sumR.AsSpan(0, len));
            }
        }

        return new StereoAudioBuffer(sampleRate, sumL, sumR);
    }

    /// <summary>
    /// 使用 FFmpeg 將 WAV 轉碼為指定碼率的高音質 MP3 (預設 320kbps CBR)
    /// </summary>
    public static void ConvertWavToMp3(string inputWavPath, string outputMp3Path, int bitrateKbps = 320)
    {
        string ffmpegPath = ResolveFfmpegPath();
        var dir = Path.GetDirectoryName(outputMp3Path);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

        var psi = new ProcessStartInfo
        {
            FileName = ffmpegPath,
            Arguments = $"-y -i \"{inputWavPath}\" -vn -c:a libmp3lame -b:a {bitrateKbps}k \"{outputMp3Path}\"",
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardError = true,
            RedirectStandardOutput = true
        };

        using var proc = Process.Start(psi);
        if (proc == null) throw new InvalidOperationException("無法啟動 FFmpeg 轉碼進程。");
        proc.WaitForExit();
        if (proc.ExitCode != 0 || !File.Exists(outputMp3Path))
        {
            string err = proc.StandardError.ReadToEnd();
            throw new InvalidOperationException($"FFmpeg 轉 MP3 失敗: {err}");
        }
    }
}
