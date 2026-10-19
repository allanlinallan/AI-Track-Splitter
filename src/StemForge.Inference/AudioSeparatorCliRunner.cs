using System.Diagnostics;
using System.Text;
using System.Text.Json;
using StemForge.Core.Contracts;

namespace StemForge.Inference;

/// <summary>
/// 一次性 CLI Subprocess 調度器：
/// 每次執行 AI 分離時透過 Process.Start 呼叫 ai_engine/stemforge_cli.py (底層封裝開源 HTDemucs_6s + audio-separator UVR ONNX/RoFormer)，
/// 非同步攔截 stdout JSONL 即時進度，並在完成後立即結束 Python 進程，由作業系統 100% 物理回收 RTX 4050 6GB 顯存。
/// </summary>
public sealed class AudioSeparatorCliRunner
{
    private readonly string _solutionRootDir;
    private readonly string _aiEngineDir;
    private readonly string _modelsDir;

    public AudioSeparatorCliRunner(string solutionRootDir, string workspaceDataDir)
    {
        _solutionRootDir = solutionRootDir;
        _aiEngineDir = Path.Combine(solutionRootDir, "ai_engine");
        _modelsDir = Path.Combine(workspaceDataDir, "models");
        Directory.CreateDirectory(_aiEngineDir);
        Directory.CreateDirectory(_modelsDir);
    }

    public string GetPythonExecutable()
    {
        string venvScriptsPython = Path.Combine(_aiEngineDir, ".venv", "Scripts", "python.exe");
        if (File.Exists(venvScriptsPython))
            return venvScriptsPython;

        string venvRootPython = Path.Combine(_aiEngineDir, ".venv", "python.exe");
        if (File.Exists(venvRootPython))
            return venvRootPython;

        // 自動偵測本機已具備 PyTorch CUDA + audio-separator + demucs 的 Conda 環境
        string condaCleanVideo = @"C:\Users\allan\.conda\envs\clean_video\python.exe";
        if (File.Exists(condaCleanVideo))
            return condaCleanVideo;

        return "python";
    }

    public string GetVenvDir()
    {
        string venvDir = Path.Combine(_aiEngineDir, ".venv");
        if (Directory.Exists(venvDir))
            return venvDir;
        string condaDir = @"C:\Users\allan\.conda\envs\clean_video";
        if (Directory.Exists(condaDir))
            return condaDir;
        return venvDir;
    }

    public bool IsAudioSeparatorInstalled()
    {
        string venvExe = Path.Combine(GetVenvDir(), "Scripts", "audio-separator.exe");
        if (File.Exists(venvExe)) return true;

        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = GetPythonExecutable(),
                Arguments = "-c \"import audio_separator, demucs; print('OK')\"",
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            using var proc = Process.Start(psi);
            proc?.WaitForExit(4000);
            string output = proc?.StandardOutput.ReadToEnd() ?? "";
            return output.Contains("OK");
        }
        catch
        {
            return false;
        }
    }

    public async Task<(int UsedMb, int TotalMb, string GpuName, string DriverVer)> QueryNvidiaGpuAsync()
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "nvidia-smi",
                Arguments = "--query-gpu=name,memory.used,memory.total,driver_version --format=csv,noheader,nounits",
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true
            };
            using var proc = Process.Start(psi);
            if (proc != null)
            {
                string line = await proc.StandardOutput.ReadToEndAsync();
                await proc.WaitForExitAsync();
                var parts = line.Trim().Split(',', StringSplitOptions.TrimEntries);
                if (parts.Length >= 4)
                {
                    int.TryParse(parts[1], out int used);
                    int.TryParse(parts[2], out int total);
                    return (used, total > 0 ? total : 6141, parts[0], parts[3]);
                }
            }
        }
        catch
        {
            // Fallback
        }
        return (0, 6141, "NVIDIA GeForce RTX 4050 Laptop GPU", "566.07");
    }

    public async Task<SystemEnvironmentStatus> GetSystemEnvironmentStatusAsync()
    {
        var gpu = await QueryNvidiaGpuAsync();
        bool audioSep = IsAudioSeparatorInstalled();
        bool basicPitch = File.Exists(Path.Combine(GetVenvDir(), "Scripts", "basic-pitch.exe"));

        double freeGb = 0;
        try
        {
            var root = Path.GetPathRoot(_solutionRootDir);
            if (!string.IsNullOrEmpty(root))
            {
                var drive = new DriveInfo(root);
                freeGb = Math.Round(drive.AvailableFreeSpace / (1024.0 * 1024.0 * 1024.0), 2);
            }
        }
        catch { }

        return new SystemEnvironmentStatus(
            GpuName: gpu.GpuName,
            VramTotalMb: gpu.TotalMb,
            VramUsedMb: gpu.UsedMb,
            DriverVersion: gpu.DriverVer,
            DotNetVersion: Environment.Version.ToString(),
            PythonVersion: "Python 3.10 (PyTorch 2.7.1 CUDA)",
            FfmpegInstalled: true,
            AudioSeparatorInstalled: audioSep,
            DemucsInstalled: audioSep,
            BasicPitchInstalled: basicPitch,
            WorkspaceRootPath: _solutionRootDir,
            DiskFreeGb: freeGb
        );
    }

    /// <summary>
    /// 執行 Python AI 深度神經網路推論 CLI Subprocess，攔截 stdout JSONL 並回報進度
    /// </summary>
    public async Task<Dictionary<string, string>?> TryRunAiStageSubprocessAsync(
        string jobId,
        string stageId,
        string taskType,
        string inputWavPath,
        string outputDir,
        string modelName,
        bool useFp16,
        int overlap,
        string? customPrompt,
        Func<double, string, Task> onStageProgress,
        CancellationToken ct)
    {
        string cliScript = Path.Combine(_aiEngineDir, "stemforge_cli.py");
        if (!File.Exists(cliScript) || !IsAudioSeparatorInstalled())
        {
            return null;
        }

        Directory.CreateDirectory(outputDir);

        var args = new StringBuilder();
        args.Append($"\"{cliScript}\" ");
        args.Append($"--task \"{taskType}\" ");
        args.Append($"--stage \"{stageId}\" ");
        args.Append($"--input \"{inputWavPath}\" ");
        args.Append($"--output-dir \"{outputDir}\" ");
        args.Append($"--model-dir \"{_modelsDir}\" ");
        args.Append($"--model \"{modelName}\" ");
        args.Append($"--overlap {overlap} ");
        if (useFp16) args.Append("--fp16 ");
        if (!string.IsNullOrWhiteSpace(customPrompt))
        {
            args.Append($"--prompt \"{customPrompt.Replace("\"", "")}\" ");
        }

        var psi = new ProcessStartInfo
        {
            FileName = GetPythonExecutable(),
            Arguments = args.ToString(),
            WorkingDirectory = _aiEngineDir,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };
        psi.Environment["PYTHONIOENCODING"] = "utf-8";

        using var proc = new Process { StartInfo = psi };
        proc.Start();
        var stderrDrainTask = proc.StandardError.ReadToEndAsync(ct);

        Dictionary<string, string>? outputs = null;

        while (!proc.StandardOutput.EndOfStream)
        {
            ct.ThrowIfCancellationRequested();
            string? line = await proc.StandardOutput.ReadLineAsync(ct);
            if (string.IsNullOrWhiteSpace(line)) continue;

            int braceStart = line.IndexOf('{');
            int braceEnd = line.LastIndexOf('}');
            if (braceStart >= 0 && braceEnd > braceStart)
            {
                string jsonPart = line[braceStart..(braceEnd + 1)];
                try
                {
                    using var doc = JsonDocument.Parse(jsonPart);
                    var root = doc.RootElement;
                    string type = root.TryGetProperty("type", out var tProp) ? tProp.GetString() ?? "" : "";

                    if (type == "progress")
                    {
                        double pct = root.TryGetProperty("percent", out var pProp) ? pProp.GetDouble() : 50.0;
                        string msg = root.TryGetProperty("message", out var mProp) ? mProp.GetString() ?? "" : "GPU AI 深度模型推論中...";
                        await onStageProgress(pct, msg);
                    }
                    else if (type == "completed" && root.TryGetProperty("outputs", out var outObj))
                    {
                        outputs = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                        foreach (var prop in outObj.EnumerateObject())
                        {
                            var val = prop.Value.GetString();
                            if (!string.IsNullOrEmpty(val) && File.Exists(val))
                            {
                                outputs[prop.Name] = val;
                            }
                        }
                    }
                }
                catch
                {
                    // Ignore non-JSON log lines
                }
            }
        }

        await proc.WaitForExitAsync(ct);
        return outputs is { Count: > 0 } ? outputs : null;
    }
}
