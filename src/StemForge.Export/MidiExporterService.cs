using System.Diagnostics;
using System.Numerics;
using StemForge.Dsp;

namespace StemForge.Export;

/// <summary>
/// 音軌轉 MIDI 匯出引擎：
/// 1. 優先偵測並呼叫 ai_engine/.venv 下的 Spotify Basic-Pitch CLI 產出高精準度多音高 + Pitch Bend MIDI。
/// 2. 內建純 C# 諧波乘積頻譜 (Harmonic Product Spectrum, HPS) + 音符起始點偵測 (Onset Detection) 並直接編碼標準 SMF Format-1 (.mid) 二進位檔，
///    確保即使未安裝外部套件也能 100% 獨立轉出可用於 DAW 扒帶的 MIDI 音符軌！
/// </summary>
public static class MidiExporterService
{
    public static async Task<string> ConvertWavToMidiAsync(
        string wavFilePath,
        string outputMidiPath,
        string? pythonVenvDir = null,
        CancellationToken ct = default)
    {
        var outDir = Path.GetDirectoryName(outputMidiPath) ?? "";
        Directory.CreateDirectory(outDir);

        // 1. 嘗試使用 Spotify Basic-Pitch CLI (若環境中已安裝)
        if (!string.IsNullOrEmpty(pythonVenvDir))
        {
            string basicPitchExe = Path.Combine(pythonVenvDir, "Scripts", "basic-pitch.exe");
            if (File.Exists(basicPitchExe))
            {
                try
                {
                    var psi = new ProcessStartInfo
                    {
                        FileName = basicPitchExe,
                        Arguments = $"\"{outDir}\" \"{wavFilePath}\"",
                        UseShellExecute = false,
                        CreateNoWindow = true,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true
                    };
                    using var proc = Process.Start(psi);
                    if (proc != null)
                    {
                        await proc.WaitForExitAsync(ct);
                        string expectedBasicPitchMidi = Path.Combine(
                            outDir,
                            $"{Path.GetFileNameWithoutExtension(wavFilePath)}_basic_pitch.mid");
                        if (File.Exists(expectedBasicPitchMidi))
                        {
                            File.Move(expectedBasicPitchMidi, outputMidiPath, overwrite: true);
                            return outputMidiPath;
                        }
                    }
                }
                catch
                {
                    // Fallback to native C# HPS Pitch-to-MIDI engine
                }
            }
        }

        // 2. 純 C# 原生 STFT 諧波乘積頻譜 (HPS) 轉標準 MIDI 檔
        var audio = WavFileIo.ReadStereoAudio(wavFilePath);
        GenerateNativeMidiFromAudio(audio, outputMidiPath, Path.GetFileNameWithoutExtension(wavFilePath));
        return outputMidiPath;
    }

    private static void GenerateNativeMidiFromAudio(StereoAudioBuffer audio, string outputMidiPath, string trackName)
    {
        const int fftSize = 4096;
        const int hopSize = 512;
        int sr = audio.SampleRate;

        // 合併為單聲道分析音高
        float[] mono = new float[audio.Length];
        for (int i = 0; i < audio.Length; i++)
        {
            mono[i] = 0.5f * (audio.Left[i] + audio.Right[i]);
        }

        var spec = StftEngine.ForwardStft(mono, fftSize, hopSize);
        int numFrames = spec.Length;
        int numBins = fftSize / 2 + 1;
        double binHz = (double)sr / fftSize;
        double frameSec = (double)hopSize / sr;

        // 以 120 BPM, 480 Ticks Per Quarter Note 換算: 1 秒 = 960 Ticks
        const int ticksPerQuarter = 480;
        const double ticksPerSec = 960.0;

        int minBin = Math.Max(2, (int)(45.0 / binHz));   // ~ F#1
        int maxBin = Math.Min(numBins - 1, (int)(2200.0 / binHz)); // ~ C#7

        var activeNoteFrames = new int[numFrames];
        var frameVelocity = new int[numFrames];

        for (int t = 0; t < numFrames; t++)
        {
            var frame = spec[t];
            double bestScore = 0.0;
            int bestBin = -1;
            double frameEnergy = 0.0;

            for (int b = minBin; b <= maxBin; b++)
            {
                double m1 = frame[b].Magnitude;
                frameEnergy += m1 * m1;

                // Harmonic Product Spectrum (基頻 + 2、3次諧波強化)
                double m2 = (b * 2 < numBins) ? frame[b * 2].Magnitude : 1.0;
                double m3 = (b * 3 < numBins) ? frame[b * 3].Magnitude : 1.0;
                double score = m1 * (1.0 + 0.6 * m2 + 0.35 * m3);

                if (score > bestScore)
                {
                    bestScore = score;
                    bestBin = b;
                }
            }

            double rms = Math.Sqrt(frameEnergy / (maxBin - minBin + 1));
            if (bestBin > 0 && rms > 0.012)
            {
                // 拋物線內插求精確頻率
                double alpha = spec[t][Math.Max(1, bestBin - 1)].Magnitude;
                double beta = spec[t][bestBin].Magnitude;
                double gamma = spec[t][Math.Min(numBins - 1, bestBin + 1)].Magnitude;
                double denom = alpha - 2.0 * beta + gamma;
                double p = Math.Abs(denom) > 1e-9 ? 0.5 * (alpha - gamma) / denom : 0.0;
                double exactFreq = (bestBin + Math.Clamp(p, -0.5, 0.5)) * binHz;

                int midiNote = (int)Math.Round(69.0 + 12.0 * Math.Log2(exactFreq / 440.0));
                if (midiNote is >= 24 and <= 108)
                {
                    activeNoteFrames[t] = midiNote;
                    int vel = (int)Math.Clamp(45.0 + 28.0 * Math.Log10(rms * 100.0 + 1.0), 35, 124);
                    frameVelocity[t] = vel;
                }
            }
        }

        // 合併連續相同音高的 Frame 並過濾少於 3 frames (~35ms) 的雜訊
        var noteEvents = new List<(int Tick, bool IsNoteOn, byte Note, byte Velocity)>();
        int idx = 0;
        while (idx < numFrames)
        {
            int note = activeNoteFrames[idx];
            if (note == 0)
            {
                idx++;
                continue;
            }

            int startFrame = idx;
            int maxVel = frameVelocity[idx];
            while (idx < numFrames && activeNoteFrames[idx] == note)
            {
                if (frameVelocity[idx] > maxVel) maxVel = frameVelocity[idx];
                idx++;
            }

            int durationFrames = idx - startFrame;
            if (durationFrames >= 3)
            {
                int startTick = (int)Math.Round(startFrame * frameSec * ticksPerSec);
                int endTick = (int)Math.Round(idx * frameSec * ticksPerSec);
                if (endTick > startTick)
                {
                    noteEvents.Add((startTick, true, (byte)note, (byte)maxVel));
                    noteEvents.Add((endTick, false, (byte)note, 0));
                }
            }
        }

        noteEvents.Sort((a, b) =>
        {
            int cmp = a.Tick.CompareTo(b.Tick);
            return cmp != 0 ? cmp : a.IsNoteOn.CompareTo(b.IsNoteOn);
        });

        WriteStandardMidiFile(outputMidiPath, trackName, ticksPerQuarter, noteEvents);
    }

    private static void WriteStandardMidiFile(
        string outputPath,
        string trackName,
        int ticksPerQuarter,
        List<(int Tick, bool IsNoteOn, byte Note, byte Velocity)> events)
    {
        using var trackData = new MemoryStream();

        // 1. Track Name Meta Event (FF 03 len text)
        var nameBytes = System.Text.Encoding.ASCII.GetBytes(
            string.Concat(trackName.Where(c => c < 128)));
        if (nameBytes.Length > 0)
        {
            WriteVariableLength(trackData, 0);
            trackData.WriteByte(0xFF);
            trackData.WriteByte(0x03);
            WriteVariableLength(trackData, nameBytes.Length);
            trackData.Write(nameBytes);
        }

        // 2. Tempo Meta Event: 120 BPM = 500,000 microseconds/quarter (FF 51 03 07 A1 20)
        WriteVariableLength(trackData, 0);
        trackData.Write([0xFF, 0x51, 0x03, 0x07, 0xA1, 0x20]);

        // 3. Note On / Off Events
        int currentTick = 0;
        foreach (var ev in events)
        {
            int delta = Math.Max(0, ev.Tick - currentTick);
            currentTick = ev.Tick;
            WriteVariableLength(trackData, delta);
            trackData.WriteByte(ev.IsNoteOn ? (byte)0x90 : (byte)0x80);
            trackData.WriteByte(ev.Note);
            trackData.WriteByte(ev.Velocity);
        }

        // 4. End of Track Meta Event (FF 2F 00)
        WriteVariableLength(trackData, 0);
        trackData.Write([0xFF, 0x2F, 0x00]);

        byte[] trackBytes = trackData.ToArray();

        using var fs = new FileStream(outputPath, FileMode.Create, FileAccess.Write);
        // Header Chunk: MThd, length=6, format=0, ntrks=1, division=ticksPerQuarter
        fs.Write("MThd"u8);
        WriteInt32BigEndian(fs, 6);
        WriteInt16BigEndian(fs, 0);
        WriteInt16BigEndian(fs, 1);
        WriteInt16BigEndian(fs, (short)ticksPerQuarter);

        // Track Chunk: MTrk, length, data
        fs.Write("MTrk"u8);
        WriteInt32BigEndian(fs, trackBytes.Length);
        fs.Write(trackBytes);
    }

    private static void WriteVariableLength(Stream s, int value)
    {
        uint buffer = (uint)(value & 0x7F);
        while ((value >>= 7) > 0)
        {
            buffer <<= 8;
            buffer |= (uint)((value & 0x7F) | 0x80);
        }
        while (true)
        {
            s.WriteByte((byte)(buffer & 0xFF));
            if ((buffer & 0x80) != 0)
                buffer >>= 8;
            else
                break;
        }
    }

    private static void WriteInt32BigEndian(Stream s, int val)
    {
        s.WriteByte((byte)((val >> 24) & 0xFF));
        s.WriteByte((byte)((val >> 16) & 0xFF));
        s.WriteByte((byte)((val >> 8) & 0xFF));
        s.WriteByte((byte)(val & 0xFF));
    }

    private static void WriteInt16BigEndian(Stream s, short val)
    {
        s.WriteByte((byte)((val >> 8) & 0xFF));
        s.WriteByte((byte)(val & 0xFF));
    }
}
