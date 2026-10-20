using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.FileProviders;
using StemForge.Core.Contracts;
using StemForge.Core.Dag;
using StemForge.Export;
using StemForge.Inference;
using StemForge.Web;
using StemForge.Web.Hubs;

var builder = WebApplication.CreateBuilder(args);

// 計算專案根目錄與 workspace_data 路徑 (指向 D:\工作專案\GoogleAntigravity\AI音樂分軌)
string contentRoot = builder.Environment.ContentRootPath;
string solutionRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));
if (!File.Exists(Path.Combine(solutionRoot, "Directory.Build.props")))
{
    solutionRoot = Path.GetFullPath(Path.Combine(contentRoot, "..", ".."));
    if (!File.Exists(Path.Combine(solutionRoot, "Directory.Build.props")))
    {
        solutionRoot = contentRoot;
    }
}
string workspaceDataDir = Path.Combine(solutionRoot, "workspace_data");
string projectsDir = Path.Combine(workspaceDataDir, "projects");
Directory.CreateDirectory(projectsDir);

// 自動定位 wwwroot 靜態網頁資源，相容任何執行路徑 (Visual Studio、根目錄 CLI、Release_Portable)
string wwwrootDir = Path.Combine(AppContext.BaseDirectory, "wwwroot");
if (!Directory.Exists(wwwrootDir))
{
    wwwrootDir = Path.Combine(contentRoot, "wwwroot");
}
if (!Directory.Exists(wwwrootDir))
{
    wwwrootDir = Path.Combine(solutionRoot, "src", "StemForge.Web", "wwwroot");
}
if (!Directory.Exists(wwwrootDir))
{
    wwwrootDir = Path.Combine(contentRoot, "src", "StemForge.Web", "wwwroot");
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

var app = builder.Build();

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
            return Results.BadRequest(new { error = "請上傳 MP3/WAV/FLAC 檔案或輸入有效的本地檔案路徑。" });
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

// 4. 一鍵生成內建錄音室多樂器測試曲並執行全套 19 軌拆解
app.MapPost("/api/jobs/demo", async (
    DagOrchestrator orchestrator,
    IHubContext<SeparationHub> hub,
    CancellationToken ct) =>
{
    string jobId = "demo_" + Guid.NewGuid().ToString("N")[..6];
    string jobDir = Path.Combine(projectsDir, jobId);
    Directory.CreateDirectory(jobDir);

    string demoWavPath = Path.Combine(jobDir, "Studio_Multitrack_Demo.wav");
    DemoSongSynthesizer.GenerateStudioDemoMixWav(demoWavPath);

    var req = new SeparationJobRequest
    {
        JobId = jobId,
        InputFilePath = demoWavPath,
        OriginalFileName = "Studio_Multitrack_Demo.wav",
        SelectedNodeIds = StemDagCatalog.SelectableLeafNodes.Select(n => n.NodeId).ToList(),
        AudioSepPrompts = ["violin", "brass"],
        Mode = SeparationMode.BitAccurateResidual,
        UseFp16 = true,
        Overlap = 4,
        ExportReaperProject = true,
        AutoGenerateMidi = false
    };

    var result = await orchestrator.ExecuteSeparationDagAsync(
        req,
        async progress => await hub.Clients.All.SendAsync("ProgressUpdated", progress, ct),
        ct);

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

// 7. 一鍵打包下載整包分軌 ZIP (含 32-bit WAV + MIDI + Reaper .rpp)
app.MapPost("/api/jobs/{jobId}/export-zip", (
    string jobId,
    DagOrchestrator orchestrator) =>
{
    var job = orchestrator.GetJobResult(jobId);
    if (job == null) return Results.NotFound();

    string jobDir = Path.Combine(projectsDir, jobId);
    string rppFileName = $"{Path.GetFileNameWithoutExtension(job.OriginalFileName)}_StemForge.rpp";
    string rppPath = Path.Combine(jobDir, rppFileName);

    string zipName = $"{Path.GetFileNameWithoutExtension(job.OriginalFileName)}_StemForge_Stems.zip";
    string zipPath = Path.Combine(jobDir, zipName);

    StemZipArchiver.BuildJobZipArchive(zipPath, job, rppPath);
    job.ZipArchiveUrl = $"/projects/{jobId}/{Uri.EscapeDataString(zipName)}";

    return Results.Ok(new { zipUrl = job.ZipArchiveUrl, fileName = zipName });
});

// 8. 針對已分離專案動態新增任意自訂樂器音軌 (如 flute / 長笛 / saxophone / 大提琴)
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

app.Run();
