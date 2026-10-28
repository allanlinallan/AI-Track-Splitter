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

// 掛載 /docs 靜態手冊與圖片目錄
string docsDir = Path.Combine(AppContext.BaseDirectory, "docs");
if (!Directory.Exists(docsDir))
{
    string fallback = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "docs");
    if (Directory.Exists(fallback)) docsDir = Path.GetFullPath(fallback);
}

if (Directory.Exists(docsDir))
{
    app.UseStaticFiles(new StaticFileOptions
    {
        FileProvider = new PhysicalFileProvider(docsDir),
        RequestPath = "/docs",
        ServeUnknownFileTypes = true
    });
}

app.MapGet("/docs", () => Results.Redirect("/docs/USER_GUIDE.html"));
app.MapGet("/manual", () => Results.Redirect("/docs/USER_GUIDE.html"));

// 0. 本機音源即時預覽串流 (支援 Range Requests，即時預聽任意格式)
app.MapGet("/api/preview/local-file", (string path) =>
{
    string clean = path.Trim('"').Trim();
    if (!File.Exists(clean)) return Results.NotFound(new { error = "找不到該本機音源檔案" });
    string ext = Path.GetExtension(clean).ToLowerInvariant();
    string contentType = ext switch
    {
        ".mp3" => "audio/mpeg",
        ".wav" => "audio/wav",
        ".flac" => "audio/flac",
        ".m4a" => "audio/mp4",
        ".ogg" => "audio/ogg",
        ".mp4" => "video/mp4",
        ".mov" => "video/quicktime",
        ".webm" => "video/webm",
        _ => "application/octet-stream"
    };
    return Results.File(clean, contentType, enableRangeProcessing: true);
});

app.MapHub<SeparationHub>("/hubs/separation");

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



// 6.0. 單軌一鍵 AI 頻譜自適應降噪 (Spectral Gating Denoise / A-B Toggle)
app.MapPost("/api/jobs/{jobId}/denoise/{nodeId}", async (
    string jobId,
    string nodeId,
    DagOrchestrator orchestrator,
    CancellationToken ct) =>
{
    string jobDir = Path.Combine(projectsDir, jobId);
    string stemsDir = Path.Combine(jobDir, "stems");
    if (!Directory.Exists(stemsDir))
        return Results.NotFound(new { error = "找不到專案音軌目錄" });

    string? stemWavPath = null;
    var job = orchestrator.GetJobResult(jobId);
    if (job != null)
    {
        var stem = job.Stems.FirstOrDefault(s => string.Equals(s.NodeId, nodeId, StringComparison.OrdinalIgnoreCase));
        if (stem != null && File.Exists(stem.FilePath))
            stemWavPath = stem.FilePath;
    }

    if (stemWavPath == null)
    {
        var files = Directory.GetFiles(stemsDir, "*.wav");
        stemWavPath = files.FirstOrDefault(f => !f.EndsWith(".orig.wav", StringComparison.OrdinalIgnoreCase) && 
            (Path.GetFileNameWithoutExtension(f).Equals(nodeId, StringComparison.OrdinalIgnoreCase) ||
             f.Contains(nodeId, StringComparison.OrdinalIgnoreCase)));
    }

    if (stemWavPath == null || !File.Exists(stemWavPath))
        return Results.NotFound(new { error = "找不到該音軌 WAV 檔" });

    string backupPath = Path.Combine(Path.GetDirectoryName(stemWavPath)!, $"{Path.GetFileNameWithoutExtension(stemWavPath)}.orig.wav");

    // 若備份檔已存在 -> 代表目前已是降噪狀態，使用者再次點擊表示要【還原原音】
    if (File.Exists(backupPath))
    {
        File.Copy(backupPath, stemWavPath, overwrite: true);
        File.Delete(backupPath);
        return Results.Ok(new
        {
            nodeId,
            isDenoised = false,
            message = "已還原為原始未降噪音軌"
        });
    }

    // 備份原始音訊，執行自適應頻譜降噪
    File.Copy(stemWavPath, backupPath, overwrite: true);
    var (success, reducedDb, errorMsg) = await orchestrator.CliRunner.DenoiseWavFileAsync(
        backupPath,
        stemWavPath,
        propDecrease: 0.85,
        ct: ct);

    if (!success)
    {
        if (File.Exists(backupPath))
        {
            File.Copy(backupPath, stemWavPath, overwrite: true);
            File.Delete(backupPath);
        }
        return Results.BadRequest(new { error = $"AI 降噪處理失敗: {errorMsg}" });
    }

    return Results.Ok(new
    {
        nodeId,
        isDenoised = true,
        noiseReducedDb = reducedDb,
        message = $"AI 降噪完成！已自適應濾除 {reducedDb} dB 雜訊"
    });
});


// 6.01. 單軌二次深度提純 (Deep Bleed Cleaner)
app.MapPost("/api/jobs/{jobId}/deep-clean/{nodeId}", async (
    string jobId,
    string nodeId,
    DagOrchestrator orchestrator,
    CancellationToken ct) =>
{
    string jobDir = Path.Combine(projectsDir, jobId);
    string stemsDir = Path.Combine(jobDir, "stems");
    if (!Directory.Exists(stemsDir))
        return Results.NotFound(new { error = "找不到專案音軌目錄" });

    string? stemWavPath = null;
    var job = orchestrator.GetJobResult(jobId);
    if (job != null)
    {
        var stem = job.Stems.FirstOrDefault(s => string.Equals(s.NodeId, nodeId, StringComparison.OrdinalIgnoreCase));
        if (stem != null && File.Exists(stem.FilePath))
            stemWavPath = stem.FilePath;
    }

    if (stemWavPath == null)
    {
        var files = Directory.GetFiles(stemsDir, "*.wav");
        stemWavPath = files.FirstOrDefault(f => !f.EndsWith(".orig.wav", StringComparison.OrdinalIgnoreCase) && 
            (Path.GetFileNameWithoutExtension(f).Equals(nodeId, StringComparison.OrdinalIgnoreCase) ||
             f.Contains(nodeId, StringComparison.OrdinalIgnoreCase)));
    }

    if (stemWavPath == null || !File.Exists(stemWavPath))
        return Results.NotFound(new { error = $"找不到音軌檔案: {nodeId}" });

    string backupPath = Path.Combine(Path.GetDirectoryName(stemWavPath)!,
        Path.GetFileNameWithoutExtension(stemWavPath) + ".orig.wav");

    // 檢查是否已提純過 (A-B Toggle: 若已有備份檔且按此鈕，還原為原始檔)
    if (File.Exists(backupPath))
    {
        File.Copy(backupPath, stemWavPath, overwrite: true);
        File.Delete(backupPath);
        return Results.Ok(new
        {
            nodeId,
            isDeepCleaned = false,
            message = "已還原為原始音軌"
        });
    }

    // 備份原始檔，並執行深度提純
    File.Copy(stemWavPath, backupPath, overwrite: true);
    var (success, reducedDb, errorMsg) = await orchestrator.CliRunner.DeepCleanWavFileAsync(
        backupPath,
        stemWavPath,
        trackHint: nodeId,
        ct: ct);

    if (!success)
    {
        if (File.Exists(backupPath))
        {
            File.Copy(backupPath, stemWavPath, overwrite: true);
            File.Delete(backupPath);
        }
        return Results.BadRequest(new { error = $"AI 深度提純失敗: {errorMsg}" });
    }

    return Results.Ok(new
    {
        nodeId,
        isDeepCleaned = true,
        noiseReducedDb = reducedDb,
        message = $"完成二次深度提純：濾除動態串音與泛音滲漏 (+{reducedDb} dB 純度提升)"
    });
});

// 6.1. 單軌神經樂器音色風格遷移 (Audio-to-Audio Timbre Transfer)
app.MapPost("/api/jobs/{jobId}/timbre-transfer", async (
    string jobId,
    HttpRequest request,
    DagOrchestrator orchestrator,
    CancellationToken ct) =>
{
    var job = orchestrator.GetJobResult(jobId);
    if (job == null) return Results.NotFound(new { error = "找不到該任務" });

    using var reader = new StreamReader(request.Body);
    string body = await reader.ReadToEndAsync(ct);
    using var doc = JsonDocument.Parse(string.IsNullOrWhiteSpace(body) ? "{}" : body);
    var root = doc.RootElement;
    string nodeId = root.TryGetProperty("nodeId", out var np) ? np.GetString() ?? "" : "";
    string instrument = root.TryGetProperty("instrument", out var ip) ? ip.GetString() ?? "violin" : "violin";

    if (string.IsNullOrWhiteSpace(nodeId))
        return Results.BadRequest(new { error = "缺少 nodeId 參數" });

    string jobDir = Path.Combine(projectsDir, jobId);
    var stem = job.Stems.FirstOrDefault(s => string.Equals(s.NodeId, nodeId, StringComparison.OrdinalIgnoreCase));
    string? inputWavPath = stem?.FilePath;

    if (string.IsNullOrEmpty(inputWavPath) || !File.Exists(inputWavPath))
    {
        // 若找不到，嘗試從 raw_stems 目錄比對
        string rawDir = Path.Combine(jobDir, "raw_stems");
        if (Directory.Exists(rawDir))
        {
            var matchFile = Directory.GetFiles(rawDir, "*.wav")
                .FirstOrDefault(f => Path.GetFileName(f).Contains(nodeId, StringComparison.OrdinalIgnoreCase));
            if (matchFile != null) inputWavPath = matchFile;
        }
    }

    if (string.IsNullOrEmpty(inputWavPath) || !File.Exists(inputWavPath))
        return Results.NotFound(new { error = $"找不到音軌 WAV 檔案 (nodeId: {nodeId})" });

    string outDir = Path.Combine(jobDir, "timbre_transfers");
    Directory.CreateDirectory(outDir);
    string safeBaseName = Path.GetFileNameWithoutExtension(inputWavPath);
    string outFileName = $"{safeBaseName}_{instrument}.wav";
    string outputWavPath = Path.Combine(outDir, outFileName);

    var (success, finalPath, instrumentName, errorMsg) = await orchestrator.CliRunner.RunTimbreTransferAsync(
        inputWavPath,
        outputWavPath,
        instrument,
        null,
        ct);

    if (!success)
        return Results.BadRequest(new { error = $"神經音色遷移失敗: {errorMsg}" });

    string relativeUrl = $"/projects/{jobId}/timbre_transfers/{outFileName}";
    return Results.Ok(new
    {
        nodeId = stem?.NodeId ?? nodeId,
        instrument = instrument,
        instrumentName = instrumentName,
        audioUrl = relativeUrl,
        fileName = outFileName
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
    Dictionary<string, List<(double StartSec, double EndSec)>>? mutedRangesMap = null;
    Dictionary<string, float>? trackVolumesMap = null;
    double trimStartSec = 0;
    bool hasCountIn = false;
    double bpm = 120.0;
    double pickupBeats = 0.0;
    int semitones = job.CurrentPitchShift;

    if (httpReq.HasJsonContentType())
    {
        try
        {
            using var reader = new StreamReader(httpReq.Body);
            var body = await reader.ReadToEndAsync(ct);
            if (!string.IsNullOrWhiteSpace(body))
            {
                using var doc = JsonDocument.Parse(body);
                if (doc.RootElement.TryGetProperty("trimStartSec", out var trimEl) && trimEl.TryGetDouble(out var ts))
                {
                    trimStartSec = Math.Max(0, ts);
                }
                if (doc.RootElement.TryGetProperty("hasCountIn", out var countInEl))
                {
                    hasCountIn = countInEl.GetBoolean();
                }
                if (doc.RootElement.TryGetProperty("bpm", out var bpmEl) && bpmEl.TryGetDouble(out var bpmVal) && bpmVal > 20)
                {
                    bpm = bpmVal;
                }
                else if (job.TargetBpm.HasValue && job.TargetBpm.Value > 20)
                {
                    bpm = job.TargetBpm.Value;
                }
                else if (job.DetectedBpm.HasValue && job.DetectedBpm.Value > 20)
                {
                    bpm = job.DetectedBpm.Value;
                }
                if (doc.RootElement.TryGetProperty("pickupBeats", out var pickupEl) && pickupEl.TryGetDouble(out var pbVal))
                {
                    pickupBeats = Math.Max(0, pbVal);
                }
                if (doc.RootElement.TryGetProperty("semitones", out var semiEl) && semiEl.TryGetInt32(out var sVal))
                {
                    semitones = sVal;
                }
                if (doc.RootElement.TryGetProperty("selectedNodes", out var nodesEl) && nodesEl.ValueKind == JsonValueKind.Array)
                {
                    selectedNodeIds = nodesEl.EnumerateArray().Select(x => x.GetString()).Where(x => !string.IsNullOrEmpty(x)).ToList()!;
                }
                if (doc.RootElement.TryGetProperty("format", out var formatEl))
                {
                    format = formatEl.GetString()?.ToLowerInvariant() ?? "wav";
                }
                if (doc.RootElement.TryGetProperty("trackVolumes", out var volEl) && volEl.ValueKind == JsonValueKind.Object)
                {
                    trackVolumesMap = new(StringComparer.OrdinalIgnoreCase);
                    foreach (var prop in volEl.EnumerateObject())
                    {
                        if (prop.Value.TryGetSingle(out var vVal))
                        {
                            trackVolumesMap[prop.Name] = vVal;
                        }
                    }
                }
                if (doc.RootElement.TryGetProperty("mutedRanges", out var mutedEl) && mutedEl.ValueKind == JsonValueKind.Object)
                {
                    mutedRangesMap = new(StringComparer.OrdinalIgnoreCase);
                    foreach (var prop in mutedEl.EnumerateObject())
                    {
                        var ranges = new List<(double, double)>();
                        if (prop.Value.ValueKind == JsonValueKind.Array)
                        {
                            foreach (var item in prop.Value.EnumerateArray())
                            {
                                if (item.TryGetProperty("startSec", out var sProp) && item.TryGetProperty("endSec", out var eProp))
                                {
                                    ranges.Add((sProp.GetDouble(), eProp.GetDouble()));
                                }
                            }
                        }
                        mutedRangesMap[prop.Name] = ranges;
                    }
                }
            }
        }
        catch { }
    }

    var activeStems = (selectedNodeIds != null && selectedNodeIds.Count > 0)
        ? job.Stems.Where(s => selectedNodeIds.Contains(s.NodeId, StringComparer.OrdinalIgnoreCase)).ToList()
        : job.Stems.ToList();

    if (selectedNodeIds != null && selectedNodeIds.Contains("TRACK_00_ORIGINAL", StringComparer.OrdinalIgnoreCase) && File.Exists(job.SourceAudioPath))
    {
        activeStems.Insert(0, new StemTrackResult
        {
            NodeId = "TRACK_00_ORIGINAL",
            DisplayName = "原曲母帶",
            GroupCategory = "Master",
            FilePath = job.SourceAudioPath,
            RelativeUrl = job.SourceAudioUrl,
            ColorHex = "#38bdf8",
            PeakDbFs = -0.1f,
            RmsDbFs = -14.0f
        });
    }

    if (activeStems.Count == 0)
    {
        return Results.BadRequest(new { error = "請至少選擇一個音軌進行合併匯出。" });
    }

    string jobDir = Path.Combine(projectsDir, jobId);
    string stemsDir = Path.Combine(jobDir, "stems");

    var buffers = new List<StereoAudioBuffer>(activeStems.Count);
    foreach (var stem in activeStems)
    {
        string actualPath = stem.FilePath;
        string fn = Path.GetFileName(stem.FilePath);

        // 1. 若當前指定了變調半音數 (semitones != 0)，優先讀取 pitch_{semitones} 目錄中的變調音訊
        if (semitones != 0)
        {
            string pitchedCandidate = Path.Combine(jobDir, $"pitch_{semitones}", fn);
            if (File.Exists(pitchedCandidate))
            {
                actualPath = pitchedCandidate;
            }
        }
        else if (job.CurrentPitchShift != 0)
        {
            string pitchedCandidate = Path.Combine(jobDir, $"pitch_{job.CurrentPitchShift}", fn);
            if (File.Exists(pitchedCandidate))
            {
                actualPath = pitchedCandidate;
            }
        }

        // 2. 檢查 RelativeUrl 是否指向特定處理目錄 (如 /pitch_x/ 或 /tempo_x/)
        if (!string.IsNullOrEmpty(stem.RelativeUrl) && stem.RelativeUrl.StartsWith($"/projects/{jobId}/", StringComparison.OrdinalIgnoreCase))
        {
            string relPart = stem.RelativeUrl.Substring($"/projects/{jobId}/".Length).TrimStart('/');
            string diskPath = Path.Combine(jobDir, relPart.Replace('/', Path.DirectorySeparatorChar));
            if (File.Exists(diskPath))
            {
                actualPath = diskPath;
            }
        }

        if (File.Exists(actualPath))
        {
            var buf = WavFileIo.ReadStereoAudio(actualPath);
            if (mutedRangesMap != null && mutedRangesMap.TryGetValue(stem.NodeId, out var mRanges))
            {
                foreach (var (startSec, endSec) in mRanges)
                {
                    int startSample = Math.Clamp((int)(startSec * buf.SampleRate), 0, buf.Length);
                    int endSample = Math.Clamp((int)(endSec * buf.SampleRate), 0, buf.Length);
                    if (endSample > startSample)
                    {
                        Array.Clear(buf.Left, startSample, endSample - startSample);
                        Array.Clear(buf.Right, startSample, endSample - startSample);
                    }
                }
            }
            if (trackVolumesMap != null && trackVolumesMap.TryGetValue(stem.NodeId, out var volFactor))
            {
                if (Math.Abs(volFactor - 1.0f) > 0.01f)
                {
                    for (int i = 0; i < buf.Length; i++)
                    {
                        buf.Left[i] *= volFactor;
                        buf.Right[i] *= volFactor;
                    }
                }
            }
            buffers.Add(buf);
        }
    }

    if (buffers.Count == 0)
    {
        return Results.BadRequest(new { error = "所選音軌實體檔案不存在。" });
    }

    var mixdownBuffer = WavFileIo.Mixdown(buffers);
    if (trimStartSec > 0)
    {
        mixdownBuffer = WavFileIo.Slice(mixdownBuffer, trimStartSec);
    }
    if (hasCountIn)
    {
        mixdownBuffer = WavFileIo.PrependCountInBar(mixdownBuffer, bpm, pickupBeats);
    }
    string baseSongName = Path.GetFileNameWithoutExtension(job.OriginalFileName);
    int trackCount = activeStems.Count;
    string pitchTag = semitones != 0 ? (semitones > 0 ? $"_升{semitones}半音" : $"_降{Math.Abs(semitones)}半音") : "";
    string countInTag = hasCountIn ? "_預拍" : "";

    if (format == "mp3")
    {
        string tempWavPath = Path.Combine(Path.GetTempPath(), $"mixdown_{Guid.NewGuid():N}.wav");
        string mp3Name = $"{baseSongName}_合併{trackCount}軌{pitchTag}{countInTag}.mp3";
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
        string wavName = $"{baseSongName}_合併{trackCount}軌{pitchTag}{countInTag}.wav";
        string wavPath = Path.Combine(jobDir, wavName);
        WavFileIo.WriteFloat32Wav(wavPath, mixdownBuffer);

        string fileUrl = $"/projects/{jobId}/{Uri.EscapeDataString(wavName)}";
        return Results.Ok(new { url = fileUrl, fileName = wavName, trackCount, format = "wav" });
    }
});

// 7.55 手動校正原調 API (以大調為主，支援 1 鍵快速覆寫)
app.MapPost("/api/jobs/{jobId}/override-key", async (
    string jobId,
    HttpRequest httpReq,
    DagOrchestrator orchestrator,
    CancellationToken ct) =>
{
    var job = orchestrator.GetJobResult(jobId);
    if (job == null) return Results.NotFound(new { error = "找不到該分軌任務" });

    using var reader = new StreamReader(httpReq.Body);
    var body = await reader.ReadToEndAsync(ct);
    if (!string.IsNullOrWhiteSpace(body))
    {
        using var doc = JsonDocument.Parse(body);
        string key = doc.RootElement.TryGetProperty("key", out var kProp) ? kProp.GetString() ?? "C Major" : "C Major";
        string displayName = doc.RootElement.TryGetProperty("displayName", out var dProp) ? dProp.GetString() ?? "C 大調 (C Major)" : "C 大調 (C Major)";

        job.DetectedKey = key;
        job.DetectedKeyDisplayName = displayName;

        string jobDir = Path.Combine(projectsDir, jobId);
        string keyJsonPath = Path.Combine(jobDir, "detected_key.json");
        await File.WriteAllTextAsync(keyJsonPath, JsonSerializer.Serialize(new { key, displayName }), ct);

        return Results.Ok(new { key, displayName });
    }
    return Results.BadRequest(new { error = "無效的請求資料" });
});

// 7.6 PyTorch GPU 神經變調 API (升降半音 -6 ~ +6)
app.MapPost("/api/jobs/{jobId}/pitch-shift", async (
    string jobId,
    PitchShiftRequest? req,
    DagOrchestrator orchestrator,
    IHubContext<SeparationHub> hub,
    CancellationToken ct) =>
{
    var job = orchestrator.GetJobResult(jobId);
    if (job == null) return Results.NotFound(new { error = "找不到該分軌任務" });

    int semitones = Math.Clamp(req?.Semitones ?? 0, -6, 6);

    string jobDir = Path.Combine(projectsDir, jobId);
    string stemsDir = Path.Combine(jobDir, "stems");

    if (semitones == 0)
    {
        job.CurrentPitchShift = 0;
        foreach (var stem in job.Stems)
        {
            string fn = Path.GetFileName(stem.FilePath);
            stem.RelativeUrl = $"/projects/{jobId}/stems/{fn}";
            string origStemFile = Path.Combine(stemsDir, fn);
            if (File.Exists(origStemFile))
            {
                stem.FilePath = origStemFile;
            }
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
            stem.FilePath = shiftedFile;
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

// 7.7 Phase-Vocoder 時間軸無損變速不變調 API (0.5x ~ 2.0x, 21 軌平行加速)
app.MapPost("/api/jobs/{jobId}/time-stretch", async (
    string jobId,
    TimeStretchRequest? req,
    DagOrchestrator orchestrator,
    IHubContext<SeparationHub> hub,
    CancellationToken ct) =>
{
    var job = orchestrator.GetJobResult(jobId);
    if (job == null) return Results.NotFound(new { error = "找不到該分軌任務" });

    double origBpm = job.DetectedBpm ?? 120.0;
    double tempoRatio = req?.TempoRatio ?? 1.0;
    double? targetBpm = req?.TargetBpm;

    if (targetBpm == null || targetBpm <= 0)
    {
        targetBpm = Math.Round(origBpm * tempoRatio, 1);
    }
    else if (req?.TempoRatio == null && origBpm > 0)
    {
        tempoRatio = targetBpm.Value / origBpm;
    }
    tempoRatio = Math.Clamp(tempoRatio, 0.5, 2.0);

    string jobDir = Path.Combine(projectsDir, jobId);
    string stemsDir = Path.Combine(jobDir, "stems");

    // 若接近 1.0，表示恢復原始速度
    if (Math.Abs(tempoRatio - 1.0) < 0.005)
    {
        job.CurrentTempoRatio = 1.0;
        job.TargetBpm = origBpm;
        foreach (var stem in job.Stems)
        {
            string fn = Path.GetFileName(stem.FilePath);
            if (job.CurrentPitchShift != 0 && Directory.Exists(Path.Combine(jobDir, $"pitch_{job.CurrentPitchShift}")))
            {
                string pitchedFile = Path.Combine(jobDir, $"pitch_{job.CurrentPitchShift}", fn);
                stem.RelativeUrl = $"/projects/{jobId}/pitch_{job.CurrentPitchShift}/{fn}";
                if (File.Exists(pitchedFile)) stem.FilePath = pitchedFile;
            }
            else
            {
                stem.RelativeUrl = $"/projects/{jobId}/stems/{fn}";
                string origStemFile = Path.Combine(stemsDir, fn);
                if (File.Exists(origStemFile)) stem.FilePath = origStemFile;
            }
        }
        return Results.Ok(new
        {
            jobId,
            tempoRatio = 1.0,
            detectedBpm = origBpm,
            targetBpm = origBpm,
            stems = job.Stems
        });
    }

    int targetBpmInt = (int)Math.Round(targetBpm.Value);
    string tempoOutDir = Path.Combine(jobDir, $"tempo_{targetBpmInt}");
    Directory.CreateDirectory(tempoOutDir);

    int existingCount = Directory.Exists(tempoOutDir) ? Directory.GetFiles(tempoOutDir, "*.wav").Length : 0;
    if (existingCount < job.Stems.Count)
    {
        string sourceStemsDir = stemsDir;
        if (job.CurrentPitchShift != 0)
        {
            string pitchedDir = Path.Combine(jobDir, $"pitch_{job.CurrentPitchShift}");
            if (Directory.Exists(pitchedDir) && Directory.GetFiles(pitchedDir, "*.wav").Length >= job.Stems.Count)
            {
                sourceStemsDir = pitchedDir;
            }
        }

        await orchestrator.CliRunner.TimeStretchStemsAsync(
            sourceStemsDir,
            tempoOutDir,
            tempoRatio,
            async (pct, msg) =>
            {
                await hub.Clients.All.SendAsync("ProgressUpdated", new ProgressUpdateEvent(
                    jobId, "TIME_STRETCH", $"時間軸變速 ({tempoRatio:F2}x, {targetBpmInt} BPM)", pct, pct, msg
                ), ct);
            },
            ct);
    }

    job.CurrentTempoRatio = tempoRatio;
    job.TargetBpm = targetBpm;
    foreach (var stem in job.Stems)
    {
        string fn = Path.GetFileName(stem.FilePath);
        string stretchedFile = Path.Combine(tempoOutDir, fn);
        if (File.Exists(stretchedFile))
        {
            stem.RelativeUrl = $"/projects/{jobId}/tempo_{targetBpmInt}/{fn}";
            stem.FilePath = stretchedFile;
        }
    }

    return Results.Ok(new
    {
        jobId,
        tempoRatio,
        detectedBpm = origBpm,
        targetBpm = targetBpm.Value,
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
app.MapGet("/api/projects/{jobId}/load", async (string jobId, DagOrchestrator orchestrator, CancellationToken ct) =>
{
    var job = orchestrator.GetJobResult(jobId);
    if (job == null) return Results.NotFound(new { error = "找不到該歷史專案" });

    if (job.DetectedBpm == null && File.Exists(job.SourceAudioPath))
    {
        try
        {
            double bpm = await orchestrator.CliRunner.DetectAudioBpmAsync(job.SourceAudioPath, ct);
            job.DetectedBpm = bpm;
            job.TargetBpm = bpm;
            string bpmJsonPath = Path.Combine(projectsDir, jobId, "detected_bpm.json");
            await File.WriteAllTextAsync(bpmJsonPath, JsonSerializer.Serialize(new { bpm }), ct);
        }
        catch { }
    }

    return Results.Ok(job);
});

// 9.5 刪除指定歷史分軌專案
app.MapDelete("/api/projects/{jobId}", (string jobId, DagOrchestrator orchestrator) =>
{
    bool success = orchestrator.DeleteProject(jobId);
    return success ? Results.Ok(new { success = true, jobId }) : Results.NotFound(new { error = "找不到或無法刪除該歷史專案" });
});

// 9.6 另存當前專案為獨立新專案 (自訂名稱 + 保留剪輯消音狀態)
app.MapPost("/api/projects/{jobId}/save-as", async (
    string jobId,
    HttpRequest request,
    DagOrchestrator orchestrator,
    CancellationToken ct) =>
{
    using var reader = new StreamReader(request.Body);
    string body = await reader.ReadToEndAsync(ct);
    string newProjectName = "";
    string? trackStatesJson = null;

    if (!string.IsNullOrWhiteSpace(body))
    {
        try
        {
            using var doc = JsonDocument.Parse(body);
            if (doc.RootElement.TryGetProperty("newProjectName", out var nameProp))
            {
                newProjectName = nameProp.GetString() ?? "";
            }
            if (doc.RootElement.TryGetProperty("trackStates", out var statesProp))
            {
                trackStatesJson = statesProp.GetRawText();
            }
        }
        catch { }
    }

    if (string.IsNullOrWhiteSpace(newProjectName))
    {
        newProjectName = $"{jobId}_Copy";
    }

    var newJob = await orchestrator.SaveAsProjectAsync(jobId, newProjectName, trackStatesJson, ct);
    if (newJob == null)
    {
        return Results.BadRequest(new { error = "無法另存專案，請確認來源專案是否存在" });
    }

    return Results.Ok(new
    {
        success = true,
        jobId = newJob.JobId,
        projectName = newJob.OriginalFileName,
        job = newJob
    });
});

// 9.7 保存當前專案編輯狀態 (剪輯、靜音與各軌參數)
app.MapPost("/api/projects/{jobId}/save-state", async (
    string jobId,
    HttpRequest request,
    DagOrchestrator orchestrator,
    CancellationToken ct) =>
{
    using var reader = new StreamReader(request.Body);
    string body = await reader.ReadToEndAsync(ct);
    if (string.IsNullOrWhiteSpace(body)) return Results.BadRequest();

    bool ok = await orchestrator.SaveProjectStateAsync(jobId, body, ct);
    return ok ? Results.Ok(new { success = true }) : Results.NotFound();
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

// 11. 自訂新增音軌 (可由使用者上傳 WAV/MP3/FLAC/M4A/OGG 等任意音源加入當前專案)
app.MapPost("/api/jobs/{jobId}/tracks/custom", async (
    string jobId,
    HttpRequest httpReq,
    DagOrchestrator orchestrator,
    CancellationToken ct) =>
{
    var job = orchestrator.GetJobResult(jobId);
    if (job == null) return Results.NotFound(new { error = "找不到該分軌任務" });

    if (!httpReq.HasFormContentType)
        return Results.BadRequest(new { error = "請求必須為 FormContentType (multipart/form-data)" });

    var form = await httpReq.ReadFormAsync(ct);
    var file = form.Files.GetFile("file");
    string? localPath = form["localFilePath"].FirstOrDefault();
    string? trackName = form["trackName"].FirstOrDefault();
    string? groupCategory = form["groupCategory"].FirstOrDefault();
    string? colorHex = form["colorHex"].FirstOrDefault();

    string tempSourceFile = "";
    bool isTempFile = false;

    if (file is { Length: > 0 })
    {
        string ext = Path.GetExtension(file.FileName);
        tempSourceFile = Path.Combine(Path.GetTempPath(), $"stemforge_custom_{Guid.NewGuid():N}{ext}");
        await using var fs = new FileStream(tempSourceFile, FileMode.Create, FileAccess.Write);
        await file.CopyToAsync(fs, ct);
        isTempFile = true;

        if (string.IsNullOrWhiteSpace(trackName))
        {
            trackName = Path.GetFileNameWithoutExtension(file.FileName);
        }
    }
    else if (!string.IsNullOrWhiteSpace(localPath) && File.Exists(localPath.Trim('"')))
    {
        tempSourceFile = localPath.Trim('"');
        if (string.IsNullOrWhiteSpace(trackName))
        {
            trackName = Path.GetFileNameWithoutExtension(tempSourceFile);
        }
    }
    else
    {
        return Results.BadRequest(new { error = "請上傳音訊檔案或提供有效的本機檔案路徑。" });
    }

    try
    {
        var stem = await orchestrator.AddCustomTrackAsync(
            jobId,
            tempSourceFile,
            trackName,
            groupCategory,
            colorHex,
            ct);

        if (stem == null)
            return Results.BadRequest(new { error = "新增自訂音軌失敗，請確認檔案格式是否有效。" });

        try
        {
            var jsonOpts = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, WriteIndented = true };
            jsonOpts.Converters.Add(new JsonStringEnumConverter());
            await File.WriteAllTextAsync(Path.Combine(wwwrootDir, "latest_gpu_job.json"), JsonSerializer.Serialize(job, jsonOpts), ct);
        }
        catch { }

        return Results.Ok(new { stem, job });
    }
    finally
    {
        if (isTempFile && File.Exists(tempSourceFile))
        {
            try { File.Delete(tempSourceFile); } catch { }
        }
    }
});

// 12. 刪除自訂音軌
app.MapDelete("/api/jobs/{jobId}/tracks/{nodeId}", async (
    string jobId,
    string nodeId,
    DagOrchestrator orchestrator,
    CancellationToken ct) =>
{
    var job = orchestrator.GetJobResult(jobId);
    if (job == null) return Results.NotFound(new { error = "找不到該分軌任務" });

    bool ok = await orchestrator.DeleteCustomTrackAsync(jobId, nodeId, ct);
    if (!ok) return Results.NotFound(new { error = "找不到該音軌或無法刪除" });

    try
    {
        var jsonOpts = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, WriteIndented = true };
        jsonOpts.Converters.Add(new JsonStringEnumConverter());
        await File.WriteAllTextAsync(Path.Combine(wwwrootDir, "latest_gpu_job.json"), JsonSerializer.Serialize(job, jsonOpts), ct);
    }
    catch { }

    return Results.Ok(new { success = true, nodeId });
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
    // 1. 若當前目錄即為已封裝的免安裝發布包 (例如 Release_Portable)，優先採用本身目錄
    string baseDir = AppContext.BaseDirectory;
    if (Directory.Exists(Path.Combine(baseDir, "ai_engine")) && Directory.Exists(Path.Combine(baseDir, "wwwroot")))
    {
        return baseDir;
    }

    // 2. 開發環境根目錄
    string defaultDevRoot = @"D:\工作專案\GoogleAntigravity\AI音樂分軌";
    if (Directory.Exists(defaultDevRoot) && File.Exists(Path.Combine(defaultDevRoot, "StemForge.sln")))
        return defaultDevRoot;

    string? curr = baseDir;
    while (!string.IsNullOrEmpty(curr))
    {
        if (File.Exists(Path.Combine(curr, "StemForge.sln")) || Directory.Exists(Path.Combine(curr, "ai_engine")))
            return curr;
        curr = Directory.GetParent(curr)?.FullName;
    }

    return baseDir;
}

app.Run();
