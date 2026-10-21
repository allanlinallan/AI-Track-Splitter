using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.FileProviders;
using StemForge.Core.Contracts;
using StemForge.Core.Dag;
using StemForge.Dsp;
using StemForge.Export;
using StemForge.Inference;
using StemForge.Web;
using StemForge.Web.Hubs;

var builder = WebApplication.CreateBuilder(args);

// 計算專案根目錄與 workspace_data 路徑 (指向 D:\工作專案\GoogleAntigravity\AI音樂分軌)
string solutionRoot = FindProjectRoot();
string contentRoot = builder.Environment.ContentRootPath;
string workspaceDataDir = Path.Combine(solutionRoot, "workspace_data");
string projectsDir = Path.Combine(workspaceDataDir, "projects");
Directory.CreateDirectory(projectsDir);

// 自動定位 wwwroot 靜態網頁資源，相容任何執行路徑 (Visual Studio、根目錄 CLI、Release_Portable)
string wwwrootDir = Path.Combine(solutionRoot, "src", "StemForge.Web", "wwwroot");
if (!Directory.Exists(wwwrootDir))
{
    wwwrootDir = Path.Combine(solutionRoot, "wwwroot");
}
if (!Directory.Exists(wwwrootDir))
{
    wwwrootDir = Path.Combine(AppContext.BaseDirectory, "wwwroot");
}
if (Directory.Exists(wwwrootDir))
{
    builder.Environment.WebRootPath = wwwrootDir;
    builder.Environment.WebRootFileProvider = new PhysicalFileProvider(wwwrootDir);
}
if (Directory.Exists(wwwrootDir))
{
    builder.Environment.WebRootPath = wwwrootDir;
    builder.Environment.WebRootFileProvider = new PhysicalFileProvider(wwwrootDir);
}

// 允許上傳大型無損 WAV/FLAC 檔案 (最高 600 MB)
builder.Services.Configure<FormOptions>(options =>
{
    options.MultipartBodyLengthLimit = 600L * 1024 * 1024;
});
builder.WebHost.ConfigureKestrel(options =>
{
    options.Limits.MaxRequestBodySize = 600L * 1024 * 1024;
});

builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter());
});

builder.Services.AddSignalR();
builder.Services.AddSingleton(_ => new DagOrchestrator(solutionRoot, workspaceDataDir));

// 預設綁定 5188 埠，並自動開啟預設瀏覽器（若外部未指定 --urls）
string defaultUrl = "http://localhost:5188";
if (!args.Any(a => a.StartsWith("--urls", StringComparison.OrdinalIgnoreCase)))
{
    builder.WebHost.UseUrls(defaultUrl);
}

var app = builder.Build();

app.Lifetime.ApplicationStarted.Register(() =>
{
    try
    {
        string openUrl = defaultUrl;
        var urls = app.Urls;
        if (urls.Count > 0) openUrl = urls.First();
        Process.Start(new ProcessStartInfo
        {
            FileName = openUrl,
            UseShellExecute = true
        });
    }
    catch { /* 忽略自動開啟瀏覽器失敗 */ }
});

app.UseDefaultFiles();
app.UseStaticFiles();

// 掛載 /projects 靜態音訊串流目錄，啟用 Range Processing 支援 WebAudio 時間軸即時跳轉
app.UseStaticFiles(new StaticFileOptions
{
    FileProvider = new PhysicalFileProvider(projectsDir),
    RequestPath = "/projects",
    ServeUnknownFileTypes = true,
    DefaultContentType = "application/octet-stream",
    OnPrepareResponse = ctx =>
    {
        ctx.Context.Response.Headers.AcceptRanges = "bytes";
        ctx.Context.Response.Headers.CacheControl = "no-cache";
    }
});

app.MapHub<SeparationHub>("/hubs/separation");

app.MapGet("/favicon.ico", () => Results.NoContent());

// 1. 取得硬體 (RTX 4050 6GB VRAM) 與環境狀態
app.MapGet("/api/system/status", async (DagOrchestrator orchestrator) =>
{
    var status = await orchestrator.CliRunner.GetSystemEnvironmentStatusAsync();
    return Results.Ok(status);
});

// 2. 取得 19+ 軌階層式 DAG 分離樹定義
app.MapGet("/api/dag/catalog", () =>
{
    return Results.Ok(new
    {
        allNodes = StemDagCatalog.AllNodes,
        selectableLeaves = StemDagCatalog.SelectableLeafNodes
    });
});

// 3. 上傳音訊檔案（或指定本地路徑）並執行精細分軌 DAG
app.MapPost("/api/jobs/separate", async (
    HttpRequest httpReq,
    DagOrchestrator orchestrator,
    IHubContext<SeparationHub> hub,
    CancellationToken ct) =>
{
    string jobId = Guid.NewGuid().ToString("N")[..10];
    string jobDir = Path.Combine(projectsDir, jobId);
    Directory.CreateDirectory(jobDir);

    string inputFilePath = "";
    string originalFileName = "audio.wav";
    var selectedNodeIds = new List<string>();
    var audioSepPrompts = new List<string>();
    var mode = SeparationMode.BitAccurateResidual;
    bool useFp16 = true;
    int overlap = 4;
    bool exportReaper = true;
    bool autoMidi = false;

    if (httpReq.HasFormContentType)
    {
        var form = await httpReq.ReadFormAsync(ct);
        var file = form.Files.GetFile("file");
        string? localPath = form["localFilePath"].FirstOrDefault();

        if (file is { Length: > 0 })
        {
            originalFileName = file.FileName;
            inputFilePath = Path.Combine(jobDir, $"upload_{Path.GetFileName(file.FileName)}");
            await using var fs = new FileStream(inputFilePath, FileMode.Create, FileAccess.Write);
            await file.CopyToAsync(fs, ct);
        }
        else if (!string.IsNullOrWhiteSpace(localPath) && File.Exists(localPath.Trim('"')))
        {
            string cleanLocal = localPath.Trim('"');
            originalFileName = Path.GetFileName(cleanLocal);
            inputFilePath = cleanLocal;
        }
        else
        {
            return Results.BadRequest(new { error = "請上傳 MP3/WAV/FLAC/MP4 檔案或輸入有效的本地檔案路徑。" });
        }

        if (form.TryGetValue("selectedNodes", out var nodesVal) && !string.IsNullOrWhiteSpace(nodesVal))
        {
            selectedNodeIds = JsonSerializer.Deserialize<List<string>>(nodesVal!) ?? [];
        }
        if (form.TryGetValue("prompts", out var promptsVal) && !string.IsNullOrWhiteSpace(promptsVal))
        {
            audioSepPrompts = JsonSerializer.Deserialize<List<string>>(promptsVal!) ?? [];
        }
        if (form.TryGetValue("mode", out var modeVal) && Enum.TryParse<SeparationMode>(modeVal, true, out var parsedMode))
        {
            mode = parsedMode;
        }
        if (form.TryGetValue("useFp16", out var fp16Val) && bool.TryParse(fp16Val, out var parsedFp16))
        {
            useFp16 = parsedFp16;
        }
        if (form.TryGetValue("overlap", out var ovVal) && int.TryParse(ovVal, out var parsedOv))
        {
            overlap = Math.Clamp(parsedOv, 1, 16);
        }
        if (form.TryGetValue("autoMidi", out var midiVal) && bool.TryParse(midiVal, out var parsedMidi))
        {
            autoMidi = parsedMidi;
        }
    }

    var req = new SeparationJobRequest
    {
        JobId = jobId,
        InputFilePath = inputFilePath,
        OriginalFileName = originalFileName,
        SelectedNodeIds = selectedNodeIds,
        AudioSepPrompts = audioSepPrompts,
        Mode = mode,
        UseFp16 = useFp16,
        Overlap = overlap,
        ExportReaperProject = exportReaper,
        AutoGenerateMidi = autoMidi
    };

    var result = await orchestrator.ExecuteSeparationDagAsync(
        req,
        async progress => await hub.Clients.All.SendAsync("ProgressUpdated", progress, ct),
        ct);

    try
    {
        var jsonOpts = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, WriteIndented = true };
        jsonOpts.Converters.Add(new JsonStringEnumConverter());
        await File.WriteAllTextAsync(Path.Combine(wwwrootDir, "latest_gpu_job.json"), JsonSerializer.Serialize(result, jsonOpts), ct);
    }
    catch { /* ignore snapshot write error */ }

    return Results.Ok(result);
});

// 5. 0 秒瞬時切換分離模式 (PureIsolation / BitAccurateResidual / BitAccurateProportional / AiTrainingDry)
app.MapPost("/api/jobs/{jobId}/mode/{mode}", async (
    string jobId,
    SeparationMode mode,
    DagOrchestrator orchestrator,
    IHubContext<SeparationHub> hub,
    CancellationToken ct) =>
{
    var updated = await orchestrator.SwitchSeparationModeAsync(
        jobId,
        mode,
        async progress => await hub.Clients.All.SendAsync("ProgressUpdated", progress, ct),
        ct);

    return updated != null ? Results.Ok(updated) : Results.NotFound(new { error = "找不到指定的分軌任務" });
});

// 6. 單軌一鍵轉 MIDI (.mid)
app.MapPost("/api/jobs/{jobId}/midi/{nodeId}", async (
    string jobId,
    string nodeId,
    DagOrchestrator orchestrator,
    CancellationToken ct) =>
{
    var job = orchestrator.GetJobResult(jobId);
    if (job == null) return Results.NotFound();

    var stem = job.Stems.FirstOrDefault(s => string.Equals(s.NodeId, nodeId, StringComparison.OrdinalIgnoreCase));
    if (stem == null || !File.Exists(stem.FilePath))
        return Results.NotFound(new { error = "找不到該音軌 WAV 檔" });

    string jobDir = Path.Combine(projectsDir, jobId);
    string midiFileName = $"{Path.GetFileNameWithoutExtension(stem.FilePath)}.mid";
    string outputMidiPath = Path.Combine(jobDir, "midi", midiFileName);

    await MidiExporterService.ConvertWavToMidiAsync(
        stem.FilePath,
        outputMidiPath,
        orchestrator.CliRunner.GetVenvDir(),
        ct);

    stem.MidiFilePath = outputMidiPath;
    stem.MidiRelativeUrl = $"/projects/{jobId}/midi/{midiFileName}";

    return Results.Ok(new
    {
        nodeId = stem.NodeId,
        midiUrl = stem.MidiRelativeUrl,
        fileName = midiFileName
    });
});

// 7. 打包下載指定或整包分軌 ZIP (含 32-bit WAV + MIDI + Reaper .rpp)
app.MapPost("/api/jobs/{jobId}/export-zip", async (
    string jobId,
    HttpRequest httpReq,
    DagOrchestrator orchestrator,
    CancellationToken ct) =>
{
    var job = orchestrator.GetJobResult(jobId);
    if (job == null) return Results.NotFound();

    List<string>? selectedNodeIds = null;
    if (httpReq.HasJsonContentType())
    {
        try
        {
            using var reader = new StreamReader(httpReq.Body);
            var body = await reader.ReadToEndAsync(ct);
            if (!string.IsNullOrWhiteSpace(body))
            {
                using var doc = JsonDocument.Parse(body);
                if (doc.RootElement.TryGetProperty("selectedNodes", out var nodesEl) && nodesEl.ValueKind == JsonValueKind.Array)
                {
                    selectedNodeIds = nodesEl.EnumerateArray().Select(x => x.GetString()).Where(x => !string.IsNullOrEmpty(x)).ToList()!;
                }
            }
        }
        catch { /* fallback to all */ }
    }

    string jobDir = Path.Combine(projectsDir, jobId);
    string rppFileName = $"{Path.GetFileNameWithoutExtension(job.OriginalFileName)}_StemForge.rpp";
    string rppPath = Path.Combine(jobDir, rppFileName);

    bool isFiltered = selectedNodeIds != null && selectedNodeIds.Count > 0 && selectedNodeIds.Count < job.Stems.Count;
    int exportTrackCount = isFiltered ? selectedNodeIds!.Count : job.Stems.Count;
    string zipSuffix = isFiltered ? $"_選取{exportTrackCount}軌.zip" : $"_全套{exportTrackCount}軌.zip";
    string zipName = $"{Path.GetFileNameWithoutExtension(job.OriginalFileName)}{zipSuffix}";
    string zipPath = Path.Combine(jobDir, zipName);

    StemZipArchiver.BuildJobZipArchive(zipPath, job, rppPath, selectedNodeIds);
    job.ZipArchiveUrl = $"/projects/{jobId}/{Uri.EscapeDataString(zipName)}";

    return Results.Ok(new { zipUrl = job.ZipArchiveUrl, fileName = zipName });
});

// 7.5 合併下載已勾選分軌音訊 (WAV 或 MP3)
app.MapPost("/api/jobs/{jobId}/mixdown", async (
    string jobId,
    HttpRequest httpReq,
    DagOrchestrator orchestrator,
    CancellationToken ct) =>
{
    var job = orchestrator.GetJobResult(jobId);
    if (job == null) return Results.NotFound(new { error = "找不到該分軌任務" });

    List<string>? selectedNodeIds = null;
    string format = "wav"; // "wav" or "mp3"

    if (httpReq.HasJsonContentType())
    {
        try
        {
            using var reader = new StreamReader(httpReq.Body);
            var body = await reader.ReadToEndAsync(ct);
            if (!string.IsNullOrWhiteSpace(body))
            {
                using var doc = JsonDocument.Parse(body);
                if (doc.RootElement.TryGetProperty("selectedNodes", out var nodesEl) && nodesEl.ValueKind == JsonValueKind.Array)
                {
                    selectedNodeIds = nodesEl.EnumerateArray().Select(x => x.GetString()).Where(x => !string.IsNullOrEmpty(x)).ToList()!;
                }
                if (doc.RootElement.TryGetProperty("format", out var formatEl))
                {
                    format = formatEl.GetString()?.ToLowerInvariant() ?? "wav";
                }
            }
        }
        catch { }
    }

    var activeStems = (selectedNodeIds != null && selectedNodeIds.Count > 0)
        ? job.Stems.Where(s => selectedNodeIds.Contains(s.NodeId, StringComparer.OrdinalIgnoreCase)).ToList()
        : job.Stems.ToList();

    if (activeStems.Count == 0)
    {
        return Results.BadRequest(new { error = "請至少選擇一個音軌進行合併匯出。" });
    }

    var buffers = new List<StereoAudioBuffer>(activeStems.Count);
    foreach (var stem in activeStems)
    {
        if (File.Exists(stem.FilePath))
        {
            buffers.Add(WavFileIo.ReadStereoAudio(stem.FilePath));
        }
    }

    if (buffers.Count == 0)
    {
        return Results.BadRequest(new { error = "所選音軌實體檔案不存在。" });
    }

    var mixdownBuffer = WavFileIo.Mixdown(buffers);
    string jobDir = Path.Combine(projectsDir, jobId);
    string baseSongName = Path.GetFileNameWithoutExtension(job.OriginalFileName);
    int trackCount = activeStems.Count;

    if (format == "mp3")
    {
        string tempWavPath = Path.Combine(Path.GetTempPath(), $"mixdown_{Guid.NewGuid():N}.wav");
        string mp3Name = $"{baseSongName}_合併{trackCount}軌.mp3";
        string mp3Path = Path.Combine(jobDir, mp3Name);

        try
        {
            WavFileIo.WriteFloat32Wav(tempWavPath, mixdownBuffer);
            WavFileIo.ConvertWavToMp3(tempWavPath, mp3Path, 320);
        }
        finally
        {
            if (File.Exists(tempWavPath))
            {
                try { File.Delete(tempWavPath); } catch { }
            }
        }

        string fileUrl = $"/projects/{jobId}/{Uri.EscapeDataString(mp3Name)}";
        return Results.Ok(new { url = fileUrl, fileName = mp3Name, trackCount, format = "mp3" });
    }
    else
    {
        string wavName = $"{baseSongName}_合併{trackCount}軌.wav";
        string wavPath = Path.Combine(jobDir, wavName);
        WavFileIo.WriteFloat32Wav(wavPath, mixdownBuffer);

        string fileUrl = $"/projects/{jobId}/{Uri.EscapeDataString(wavName)}";
        return Results.Ok(new { url = fileUrl, fileName = wavName, trackCount, format = "wav" });
    }
});

// 7.6 PyTorch GPU 神經變調 API (升降半音 -6 ~ +6)
app.MapPost("/api/jobs/{jobId}/pitch-shift", async (
    string jobId,
    HttpRequest httpReq,
    DagOrchestrator orchestrator,
    IHubContext<SeparationHub> hub,
    CancellationToken ct) =>
{
    var job = orchestrator.GetJobResult(jobId);
    if (job == null) return Results.NotFound(new { error = "找不到該分軌任務" });

    int semitones = 0;
    if (httpReq.HasJsonContentType())
    {
        try
        {
            using var reader = new StreamReader(httpReq.Body);
            var body = await reader.ReadToEndAsync(ct);
            if (!string.IsNullOrWhiteSpace(body))
            {
                using var doc = JsonDocument.Parse(body);
                if (doc.RootElement.TryGetProperty("semitones", out var sProp))
                {
                    semitones = Math.Clamp(sProp.GetInt32(), -6, 6);
                }
            }
        }
        catch { }
    }

    string jobDir = Path.Combine(projectsDir, jobId);
    string stemsDir = Path.Combine(jobDir, "stems");

    if (semitones == 0)
    {
        job.CurrentPitchShift = 0;
        foreach (var stem in job.Stems)
        {
            string fn = Path.GetFileName(stem.FilePath);
            stem.RelativeUrl = $"/projects/{jobId}/stems/{fn}";
        }
        return Results.Ok(new
        {
            jobId,
            semitones = 0,
            detectedKey = job.DetectedKey ?? "C Major",
            detectedKeyDisplayName = job.DetectedKeyDisplayName ?? "C 大調 (C Major)",
            targetKey = job.DetectedKeyDisplayName ?? "C 大調 (C Major)",
            stems = job.Stems
        });
    }

    string pitchOutDir = Path.Combine(jobDir, $"pitch_{semitones}");
    Directory.CreateDirectory(pitchOutDir);

    int existingCount = Directory.Exists(pitchOutDir) ? Directory.GetFiles(pitchOutDir, "*.wav").Length : 0;
    if (existingCount < job.Stems.Count)
    {
        await orchestrator.CliRunner.PitchShiftStemsAsync(
            stemsDir,
            pitchOutDir,
            semitones,
            async (pct, msg) =>
            {
                await hub.Clients.All.SendAsync("ProgressUpdated", new ProgressUpdateEvent(
                    jobId, "PITCH_SHIFT", $"GPU 神經變調 ({semitones:+d} 半音)", pct, pct, msg
                ), ct);
            },
            ct);
    }

    job.CurrentPitchShift = semitones;
    foreach (var stem in job.Stems)
    {
        string fn = Path.GetFileName(stem.FilePath);
        string shiftedFile = Path.Combine(pitchOutDir, fn);
        if (File.Exists(shiftedFile))
        {
            stem.RelativeUrl = $"/projects/{jobId}/pitch_{semitones}/{fn}";
        }
    }

    string origKey = job.DetectedKey ?? "C Major";
    string targetKey = CalculateTransposedKey(origKey, semitones);

    return Results.Ok(new
    {
        jobId,
        semitones,
        detectedKey = job.DetectedKey ?? "C Major",
        detectedKeyDisplayName = job.DetectedKeyDisplayName ?? "C 大調 (C Major)",
        targetKey,
        stems = job.Stems
    });
});

// 8. 取得本機歷史分軌專案清單
app.MapGet("/api/projects/history", (DagOrchestrator orchestrator) =>
{
    var history = orchestrator.GetHistoryProjects();
    return Results.Ok(history);
});

// 9. 載入指定歷史分軌專案完整資料進 DAW
app.MapGet("/api/projects/{jobId}/load", (string jobId, DagOrchestrator orchestrator) =>
{
    var job = orchestrator.GetJobResult(jobId);
    return job != null ? Results.Ok(job) : Results.NotFound(new { error = "找不到該歷史專案" });
});

// 9.5 刪除指定歷史分軌專案
app.MapDelete("/api/projects/{jobId}", (string jobId, DagOrchestrator orchestrator) =>
{
    bool success = orchestrator.DeleteProject(jobId);
    return success ? Results.Ok(new { success = true, jobId }) : Results.NotFound(new { error = "找不到或無法刪除該歷史專案" });
});

// 10. 針對已分離專案動態新增任意自訂樂器音軌 (如 flute / 長笛 / saxophone / 大提琴)
app.MapPost("/api/jobs/{jobId}/prompt/{prompt}", async (
    string jobId,
    string prompt,
    DagOrchestrator orchestrator,
    IHubContext<SeparationHub> hub,
    CancellationToken ct) =>
{
    var stem = await orchestrator.ExtractSingleCustomPromptAsync(
        jobId,
        prompt,
        async progress => await hub.Clients.All.SendAsync("ProgressUpdated", progress, ct),
        ct);

    if (stem == null)
        return Results.BadRequest(new { error = $"無法從任務 {jobId} 抽離樂器 [{prompt}]" });

    var job = orchestrator.GetJobResult(jobId);
    if (job != null)
    {
        try
        {
            var jsonOpts = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, WriteIndented = true };
            jsonOpts.Converters.Add(new JsonStringEnumConverter());
            await File.WriteAllTextAsync(Path.Combine(wwwrootDir, "latest_gpu_job.json"), JsonSerializer.Serialize(job, jsonOpts), ct);
        }
        catch { /* ignore */ }
    }

    return Results.Ok(new { stem, job });
});

static string CalculateTransposedKey(string origKey, int semitones)
{
    string[] pitches = ["C", "C#", "D", "D#", "E", "F", "F#", "G", "G#", "A", "A#", "B"];
    string[] zhPitches = ["C", "C# / Db", "D", "D# / Eb", "E", "F", "F# / Gb", "G", "G# / Ab", "A", "A# / Bb", "B"];
    var parts = origKey.Split(' ');
    if (parts.Length < 2) return origKey;
    string root = parts[0];
    string mode = parts[1]; // Major / Minor

    int idx = Array.FindIndex(pitches, p => string.Equals(p, root, StringComparison.OrdinalIgnoreCase));
    if (idx < 0) return origKey;

    int newIdx = (idx + semitones) % 12;
    if (newIdx < 0) newIdx += 12;

    string newRoot = pitches[newIdx];
    string newZh = zhPitches[newIdx];
    string modeZh = mode.Equals("Minor", StringComparison.OrdinalIgnoreCase) ? "小調" : "大調";
    string sign = semitones > 0 ? $"+{semitones}" : $"{semitones}";
    return $"{newZh} {modeZh} ({newRoot} {mode}) [{sign}]";
}

static string FindProjectRoot()
{
    string defaultDevRoot = @"D:\工作專案\GoogleAntigravity\AI音樂分軌";
    if (Directory.Exists(defaultDevRoot) && File.Exists(Path.Combine(defaultDevRoot, "StemForge.sln")))
        return defaultDevRoot;

    string? curr = AppContext.BaseDirectory;
    while (!string.IsNullOrEmpty(curr))
    {
        if (File.Exists(Path.Combine(curr, "StemForge.sln")) || Directory.Exists(Path.Combine(curr, "ai_engine")))
            return curr;
        curr = Directory.GetParent(curr)?.FullName;
    }

    return AppContext.BaseDirectory;
}

app.Run();
