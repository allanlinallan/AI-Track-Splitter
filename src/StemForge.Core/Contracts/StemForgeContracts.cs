namespace StemForge.Core.Contracts;

public enum SeparationMode
{
    /// <summary>
    /// 扒帶 / 抓譜 / 混音研究模式：套用頻譜軟閘門消除微弱串音
    /// </summary>
    PureIsolation = 0,

    /// <summary>
    /// 100% 能量守恆（獨立殘差軌模式）：產出第 19 軌 Residual，全軌相加與原曲 Null Test 誤差 = 0 (-140 dBFS)
    /// </summary>
    BitAccurateResidual = 1,

    /// <summary>
    /// 100% 能量守恆（能量比例回填模式）：將殘差依瞬時能量比例無縫分配回各子軌，不額外增加軌道但加總 100% 還原原曲
    /// </summary>
    BitAccurateProportional = 2,

    /// <summary>
    /// AI 訓練 / 採樣極致乾音模式：強制去殘響、去氣音底噪、切除首尾靜音並正規化至 -1.0 dBFS
    /// </summary>
    AiTrainingDry = 3
}

public enum ProcessingEngineType
{
    AudioSeparatorCli,
    DemucsCli,
    AudioSepCli,
    CSharpNativeMidSide,
    CSharpNativeHpss,
    CSharpNativeResidual
}

public sealed record StemNodeDefinition(
    string NodeId,
    string FilePrefix,
    string DisplayName,
    string GroupCategory,
    string ColorHex,
    string? ParentNodeId,
    string StageId,
    ProcessingEngineType EngineType,
    string ModelFilename,
    bool IsIntermediateBus,
    bool DefaultSelected,
    string Description
);

public sealed record SeparationJobRequest
{
    public string JobId { get; init; } = Guid.NewGuid().ToString("N")[..12];
    public required string InputFilePath { get; init; }
    public required string OriginalFileName { get; init; }
    public List<string> SelectedNodeIds { get; init; } = [];
    public List<string> AudioSepPrompts { get; init; } = [];
    public SeparationMode Mode { get; init; } = SeparationMode.BitAccurateResidual;
    public bool UseFp16 { get; init; } = true;
    public int Overlap { get; init; } = 4;
    public bool ExportReaperProject { get; init; } = true;
    public bool AutoGenerateMidi { get; init; } = false;
}

public sealed record StemTrackResult
{
    public required string NodeId { get; init; }
    public required string DisplayName { get; init; }
    public required string GroupCategory { get; init; }
    public required string ColorHex { get; init; }
    public required string FilePath { get; init; }
    public required string RelativeUrl { get; init; }
    public string? MidiRelativeUrl { get; set; }
    public string? MidiFilePath { get; set; }
    public double DurationSeconds { get; init; }
    public int SampleRate { get; init; }
    public float PeakDbFs { get; init; }
    public float RmsDbFs { get; init; }
    public float[] WaveformPeaks { get; init; } = [];
    public bool FromCache { get; init; }
}

public sealed record SeparationJobResult
{
    public required string JobId { get; init; }
    public required string OriginalFileName { get; init; }
    public required string SourceAudioUrl { get; init; }
    public required string SourceAudioPath { get; init; }
    public required SeparationMode ActiveMode { get; set; }
    public double DurationSeconds { get; init; }
    public int SampleRate { get; init; }
    public float NullTestCancellationDbFs { get; set; }
    public string? ReaperRppUrl { get; set; }
    public string? ZipArchiveUrl { get; set; }
    public List<StemTrackResult> Stems { get; set; } = [];
}

public sealed record ProgressUpdateEvent(
    string JobId,
    string StageId,
    string StageTitle,
    double OverallPercent,
    double StagePercent,
    string Message,
    int VramUsedMb = 0,
    int VramTotalMb = 6141,
    bool CacheHit = false,
    StemTrackResult? CompletedStem = null
);

public sealed record SystemEnvironmentStatus(
    string GpuName,
    int VramTotalMb,
    int VramUsedMb,
    string DriverVersion,
    string DotNetVersion,
    string PythonVersion,
    bool FfmpegInstalled,
    bool AudioSeparatorInstalled,
    bool DemucsInstalled,
    bool BasicPitchInstalled,
    string WorkspaceRootPath,
    double DiskFreeGb
);

public sealed record HistoryProjectSummary(
    string JobId,
    string OriginalFileName,
    int StemCount,
    DateTime CreatedAt
);
