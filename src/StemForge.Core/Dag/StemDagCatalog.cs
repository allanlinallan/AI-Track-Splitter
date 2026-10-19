using StemForge.Core.Contracts;

namespace StemForge.Core.Dag;

public static class StemNodeIds
{
    public const string MixOriginal = "MIX_ORIGINAL";

    // Intermediate Buses (可快取母軌，亦可直接試聽)
    public const string BusVocalsAll = "V0_VOCALS_ALL";
    public const string BusInstAll = "I0_INST_ALL";
    public const string BusVocalLeadWet = "V1_LEAD_WET";
    public const string BusVocalBackingWet = "V2_BACKING_WET";
    public const string BusDrumsAll = "D0_DRUMS_ALL";
    public const string BusMelodicResidual = "I1_MELODIC_RESIDUAL";
    public const string BusGuitarAll = "G0_GUITAR_ALL";
    public const string BusOtherAll = "O0_OTHER_ALL";

    // 19 軌精細分軌葉節點 (Leaf Stems)
    // Group 1: Vocal Micro-Stems (5 軌)
    public const string VocalLeadDry = "STEM_01_VOCAL_LEAD_DRY";
    public const string VocalLeadReverb = "STEM_02_VOCAL_LEAD_REVERB";
    public const string VocalBackingDry = "STEM_03_VOCAL_BACKING_DRY";
    public const string VocalBackingReverb = "STEM_04_VOCAL_BACKING_REVERB";
    public const string VocalBreathNoise = "STEM_05_VOCAL_BREATH_NOISE";

    // Group 2: DrumSep 6-Stem Micro-Decomposition (6 軌)
    public const string DrumKick = "STEM_06_DRUM_KICK";
    public const string DrumSnare = "STEM_07_DRUM_SNARE";
    public const string DrumToms = "STEM_08_DRUM_TOMS";
    public const string DrumHiHat = "STEM_09_DRUM_HIHAT";
    public const string DrumRide = "STEM_10_DRUM_RIDE";
    public const string DrumCrash = "STEM_11_DRUM_CRASH";

    // Group 3: Core Instruments & Guitar Spatial Split (4 軌)
    public const string Bass = "STEM_12_BASS";
    public const string PianoKeys = "STEM_13_PIANO_KEYS";
    public const string GuitarLeadCenter = "STEM_14_GUITAR_LEAD_CENTER";
    public const string GuitarRhythmSide = "STEM_15_GUITAR_RHYTHM_SIDE";

    // Group 4: Orchestral & Synth Texture Split (3+ 軌)
    public const string OtherHarmonicStringsPad = "STEM_16_OTHER_HARMONIC_STRINGSPAD";
    public const string OtherPercussiveBrassPluck = "STEM_17_OTHER_PERCUSSIVE_BRASSPLUCK";
    public const string CustomAudioSepPrefix = "STEM_18_AUDIOSEP_";

    // Group 5: Bit-Accurate Energy Conservation Residual (1 軌)
    public const string ResidualBitAccurate = "STEM_19_RESIDUAL_BITACCURATE";
}

public static class StemDagCatalog
{
    private static readonly List<StemNodeDefinition> _nodes =
    [
        // ==================== 中繼母軌 (Intermediate Buses) ====================
        new(
            StemNodeIds.BusVocalsAll, "V0_Vocals_All", "總人聲母軌 (All Vocals)", "Vocal Bus", "#F43F5E",
            StemNodeIds.MixOriginal, "STAGE_1_ROFORMER_MASTER", ProcessingEngineType.AudioSeparatorCli,
            "model_bs_roformer_ep_317_sdr_12.9755.ckpt", IsIntermediateBus: true, DefaultSelected: false,
            "Stage 1 BS-RoFormer (SDR 12.97) 分離出的總人聲母軌（含主唱、和聲與空間殘響）"
        ),
        new(
            StemNodeIds.BusInstAll, "I0_Inst_All", "總伴奏母軌 (Full Instrumental)", "Core Instrument Bus", "#64748B",
            StemNodeIds.MixOriginal, "STAGE_1_ROFORMER_MASTER", ProcessingEngineType.AudioSeparatorCli,
            "model_bs_roformer_ep_317_sdr_12.9755.ckpt", IsIntermediateBus: true, DefaultSelected: false,
            "Stage 1 BS-RoFormer 無人聲純伴奏母軌（以此作為樂器拆解起點可避免人聲泛音殘留於吉他/合成器）"
        ),
        new(
            StemNodeIds.BusVocalLeadWet, "V1_Lead_Wet", "主唱含殘響 (Lead Vocal Wet)", "Vocal Bus", "#FB7185",
            StemNodeIds.BusVocalsAll, "STAGE_2A_KARAOKE_SPLIT", ProcessingEngineType.AudioSeparatorCli,
            "mel_band_roformer_karaoke_aufr33_viperx_sdr_10.1956.ckpt", IsIntermediateBus: true, DefaultSelected: false,
            "Mel-Band RoFormer Karaoke 分離出的主唱（尚未去殘響）"
        ),
        new(
            StemNodeIds.BusVocalBackingWet, "V2_Backing_Wet", "和聲含殘響 (Backing Vocals Wet)", "Vocal Bus", "#FDA4AF",
            StemNodeIds.BusVocalsAll, "STAGE_2A_KARAOKE_SPLIT", ProcessingEngineType.AudioSeparatorCli,
            "mel_band_roformer_karaoke_aufr33_viperx_sdr_10.1956.ckpt", IsIntermediateBus: true, DefaultSelected: false,
            "Mel-Band RoFormer Karaoke 分離出的和聲與合音群（尚未去殘響）"
        ),
        new(
            StemNodeIds.BusDrumsAll, "D0_Drums_All", "總鼓組母軌 (All Drums)", "Drum Bus (DrumSep)", "#F59E0B",
            StemNodeIds.BusInstAll, "STAGE_2B_DEMUCS_FT_CORE", ProcessingEngineType.DemucsCli,
            "htdemucs_ft.yaml", IsIntermediateBus: true, DefaultSelected: false,
            "HTDemucs_ft 拆出的高瞬態總鼓組母軌（供 Stage 3 DrumSep 6 軌細分使用）"
        ),
        new(
            StemNodeIds.BusMelodicResidual, "I1_Melodic_Residual", "純旋律樂器母軌 (Melodic Bus)", "Core Instrument Bus", "#38BDF8",
            StemNodeIds.BusInstAll, "STAGE_2B_DEMUCS_FT_CORE", ProcessingEngineType.DemucsCli,
            "htdemucs_ft.yaml", IsIntermediateBus: true, DefaultSelected: false,
            "從純伴奏扣除 Drums 與 Bass 後的無鼓無貝斯旋律母軌"
        ),
        new(
            StemNodeIds.BusGuitarAll, "G0_Guitar_All", "總吉他母軌 (All Guitars)", "Core Instrument Bus", "#10B981",
            StemNodeIds.BusMelodicResidual, "STAGE_2C_DEMUCS_6S_MELODIC", ProcessingEngineType.DemucsCli,
            "htdemucs_6s.yaml", IsIntermediateBus: true, DefaultSelected: false,
            "從純旋律母軌分離出的總吉他軌（供 Mid/Side 主奏與節奏吉他拆解）"
        ),
        new(
            StemNodeIds.BusOtherAll, "O0_Other_All", "其他管弦與合成器母軌 (Other/Synths Bus)", "Orchestral & Synth Bus", "#A855F7",
            StemNodeIds.BusMelodicResidual, "STAGE_2C_DEMUCS_6S_MELODIC", ProcessingEngineType.DemucsCli,
            "htdemucs_6s.yaml", IsIntermediateBus: true, DefaultSelected: false,
            "扣除人聲、鼓、貝斯、吉他、鋼琴後的管弦樂與合成器母軌"
        ),

        // ==================== Group 1: 人聲極致細分 (5 軌) ====================
        new(
            StemNodeIds.VocalLeadDry, "01_Vocal_Lead_Dry", "01. 主唱 - 純乾音 (Lead Vocal Dry)", "Vocal Bus", "#FF2E63",
            StemNodeIds.BusVocalLeadWet, "STAGE_3V_LEAD_DEREVERB", ProcessingEngineType.AudioSeparatorCli,
            "deverb_bs_roformer_8_384dim_10depth.ckpt", IsIntermediateBus: false, DefaultSelected: true,
            "去除空間殘響與回音的純乾主唱，適合混音重上效果器、扒主旋律或 RVC/GPT-SoVITS 訓練"
        ),
        new(
            StemNodeIds.VocalLeadReverb, "02_Vocal_Lead_Reverb", "02. 主唱 - 空間殘響 (Lead Reverb FX)", "Vocal Bus", "#FF7597",
            StemNodeIds.BusVocalLeadWet, "STAGE_3V_LEAD_DEREVERB", ProcessingEngineType.AudioSeparatorCli,
            "deverb_bs_roformer_8_384dim_10depth.ckpt", IsIntermediateBus: false, DefaultSelected: true,
            "透過 32-bit Float 反相相消 (Lead_Wet - Lead_Dry) 獨立抽出的原曲主唱空間殘響軌"
        ),
        new(
            StemNodeIds.VocalBackingDry, "03_Vocal_Backing_Dry", "03. 和聲 - 純乾音 (Backing Vocals Dry)", "Vocal Bus", "#EC4899",
            StemNodeIds.BusVocalBackingWet, "STAGE_3V_BACKING_DEREVERB", ProcessingEngineType.AudioSeparatorCli,
            "deverb_bs_roformer_8_384dim_10depth.ckpt", IsIntermediateBus: false, DefaultSelected: true,
            "獨立拆出的純乾和聲/合音部，抓和聲編曲必備"
        ),
        new(
            StemNodeIds.VocalBackingReverb, "04_Vocal_Backing_Reverb", "04. 和聲 - 空間殘響 (Backing Reverb FX)", "Vocal Bus", "#F472B6",
            StemNodeIds.BusVocalBackingWet, "STAGE_3V_BACKING_DEREVERB", ProcessingEngineType.AudioSeparatorCli,
            "deverb_bs_roformer_8_384dim_10depth.ckpt", IsIntermediateBus: false, DefaultSelected: true,
            "和聲部的獨立空間殘響與延遲尾音"
        ),
        new(
            StemNodeIds.VocalBreathNoise, "05_Vocal_Breath_Noise", "05. 人聲氣音與唇齒音 (Breath & Air)", "Vocal Bus", "#FB923C",
            StemNodeIds.VocalLeadDry, "STAGE_4V_VOCAL_DENOISE", ProcessingEngineType.AudioSeparatorCli,
            "denoise_mel_band_roformer_aufr33_sdr_27.9959.ckpt", IsIntermediateBus: false, DefaultSelected: true,
            "從乾主唱抽離出的換氣聲、高頻空氣感與微細節底噪"
        ),

        // ==================== Group 2: 鼓組微結構 DrumSep (6 軌) ====================
        new(
            StemNodeIds.DrumKick, "06_Drum_Kick", "06. 大鼓 / 底鼓 (Kick)", "Drum Bus (DrumSep)", "#EAB308",
            StemNodeIds.BusDrumsAll, "STAGE_3D_MDX23C_DRUMSEP", ProcessingEngineType.AudioSeparatorCli,
            "MDX23C-DrumSep-aufr33-jarredou.ckpt", IsIntermediateBus: false, DefaultSelected: true,
            "MDX23C-DrumSep 獨立拆出的大鼓 (Kick) 低頻瞬態軌"
        ),
        new(
            StemNodeIds.DrumSnare, "07_Drum_Snare", "07. 小鼓 / 軍鼓 (Snare)", "Drum Bus (DrumSep)", "#FACC15",
            StemNodeIds.BusDrumsAll, "STAGE_3D_MDX23C_DRUMSEP", ProcessingEngineType.AudioSeparatorCli,
            "MDX23C-DrumSep-aufr33-jarredou.ckpt", IsIntermediateBus: false, DefaultSelected: true,
            "MDX23C-DrumSep 獨立拆出的小鼓與 Rimshot 軌"
        ),
        new(
            StemNodeIds.DrumToms, "08_Drum_Toms", "08. 通鼓 / 落地鼓 (Toms)", "Drum Bus (DrumSep)", "#FDE047",
            StemNodeIds.BusDrumsAll, "STAGE_3D_MDX23C_DRUMSEP", ProcessingEngineType.AudioSeparatorCli,
            "MDX23C-DrumSep-aufr33-jarredou.ckpt", IsIntermediateBus: false, DefaultSelected: true,
            "MDX23C-DrumSep 獨立拆出的過門通鼓 (Hi/Mid/Floor Toms)"
        ),
        new(
            StemNodeIds.DrumHiHat, "09_Drum_HiHat", "09. 踩鈸 (Hi-Hat Closed/Open)", "Drum Bus (DrumSep)", "#FEF08A",
            StemNodeIds.BusDrumsAll, "STAGE_3D_MDX23C_DRUMSEP", ProcessingEngineType.AudioSeparatorCli,
            "MDX23C-DrumSep-aufr33-jarredou.ckpt", IsIntermediateBus: false, DefaultSelected: true,
            "MDX23C-DrumSep 獨立拆出的閉合與開合踩鈸律動軌"
        ),
        new(
            StemNodeIds.DrumRide, "10_Drum_Ride", "10. 疊音鈸 (Ride Cymbal)", "Drum Bus (DrumSep)", "#F59E0B",
            StemNodeIds.BusDrumsAll, "STAGE_3D_MDX23C_DRUMSEP", ProcessingEngineType.AudioSeparatorCli,
            "MDX23C-DrumSep-aufr33-jarredou.ckpt", IsIntermediateBus: false, DefaultSelected: true,
            "MDX23C-DrumSep 獨立拆出的 Ride 鈸叮叮聲與 Bell"
        ),
        new(
            StemNodeIds.DrumCrash, "11_Drum_Crash", "11. 碎音鈸 / 吊鈸 (Crash & Overhead)", "Drum Bus (DrumSep)", "#D97706",
            StemNodeIds.BusDrumsAll, "STAGE_3D_MDX23C_DRUMSEP", ProcessingEngineType.AudioSeparatorCli,
            "MDX23C-DrumSep-aufr33-jarredou.ckpt", IsIntermediateBus: false, DefaultSelected: true,
            "MDX23C-DrumSep 獨立拆出的重拍碎音鈸 (Crash)"
        ),

        // ==================== Group 3: 核心樂器與吉他細分 (4 軌) ====================
        new(
            StemNodeIds.Bass, "12_Bass", "12. 貝斯 (Electric / Synth Bass)", "Core Instrument Bus", "#3B82F6",
            StemNodeIds.BusInstAll, "STAGE_2B_DEMUCS_FT_CORE", ProcessingEngineType.DemucsCli,
            "htdemucs_ft.yaml", IsIntermediateBus: false, DefaultSelected: true,
            "HTDemucs_ft 專精微調模型分離出的低頻貝斯線"
        ),
        new(
            StemNodeIds.PianoKeys, "13_Piano_Keys", "13. 鋼琴與鍵盤 (Piano / Rhodes / Keys)", "Core Instrument Bus", "#06B6D4",
            StemNodeIds.BusMelodicResidual, "STAGE_2C_DEMUCS_6S_MELODIC", ProcessingEngineType.DemucsCli,
            "htdemucs_6s.yaml", IsIntermediateBus: false, DefaultSelected: true,
            "從無鼓無貝斯旋律母軌中拆出的鋼琴與電鋼琴和聲軌"
        ),
        new(
            StemNodeIds.GuitarLeadCenter, "14_Guitar_Lead_Center", "14. 吉他 - 中置主奏/Solo (Guitar Lead/Mid)", "Core Instrument Bus", "#10B981",
            StemNodeIds.BusGuitarAll, "STAGE_3G_CSHARP_MIDSIDE", ProcessingEngineType.CSharpNativeMidSide,
            "CSharp_STFT_PanningMask_v1", IsIntermediateBus: false, DefaultSelected: true,
            "C# 原生 STFT 頻域相位遮罩拆出的中置吉他（Solo、主奏旋律、單軌木吉他指彈）"
        ),
        new(
            StemNodeIds.GuitarRhythmSide, "15_Guitar_Rhythm_Side", "15. 吉他 - 雙軌節奏刷弦 (Guitar Rhythm/Side)", "Core Instrument Bus", "#34D399",
            StemNodeIds.BusGuitarAll, "STAGE_3G_CSHARP_MIDSIDE", ProcessingEngineType.CSharpNativeMidSide,
            "CSharp_STFT_PanningMask_v1", IsIntermediateBus: false, DefaultSelected: true,
            "C# 原生 STFT 頻域相位遮罩拆出的極左/極右 Double-Tracking 節奏吉他與寬立體聲吉他"
        ),

        // ==================== Group 4: 管弦與合成器織體細分 (2+ 軌) ====================
        new(
            StemNodeIds.OtherHarmonicStringsPad, "16_Strings_Pad_Harmonic", "16. 弦樂群與鋪底合成器 (Strings / Sustained Pad)", "Orchestral & Synth Bus", "#A855F7",
            StemNodeIds.BusOtherAll, "STAGE_3O_CSHARP_HPSS", ProcessingEngineType.CSharpNativeHpss,
            "CSharp_Fitzgerald_HPSS_Wiener", IsIntermediateBus: false, DefaultSelected: true,
            "C# 原生二維頻譜中值濾波 (HPSS) 分離出的橫向連續諧波：小提琴/大提琴弦樂群、長音管風琴、Warm Pad"
        ),
        new(
            StemNodeIds.OtherPercussiveBrassPluck, "17_Brass_Pluck_Transient", "17. 銅管齊奏與撥奏合成器 (Brass Stabs / Synth Pluck)", "Orchestral & Synth Bus", "#C084FC",
            StemNodeIds.BusOtherAll, "STAGE_3O_CSHARP_HPSS", ProcessingEngineType.CSharpNativeHpss,
            "CSharp_Fitzgerald_HPSS_Wiener", IsIntermediateBus: false, DefaultSelected: true,
            "C# 原生二維頻譜中值濾波 (HPSS) 分離出的縱向瞬態：銅管重音 (Brass Section)、Arp 琶音、撥弦與打擊特效"
        ),

        // ==================== Group 5: 100% 能量守恆殘差軌 (1 軌) ====================
        new(
            StemNodeIds.ResidualBitAccurate, "19_Residual_BitAccurate", "19. 能量守恆微細節殘差軌 (100% Bit-Accurate Residual)", "Bit-Accurate Conservation Bus", "#94A3B8",
            StemNodeIds.MixOriginal, "STAGE_99_CSHARP_RESIDUAL", ProcessingEngineType.CSharpNativeResidual,
            "CSharp_SIMD_ExactResidual", IsIntermediateBus: false, DefaultSelected: true,
            "Mix - ∑(All Active Leaf Stems)：保證所有音軌在 DAW 推子歸零疊加時 100% 無損還原原曲 (Null Test 抵消至 -140 dBFS)"
        )
    ];

    public static IReadOnlyList<StemNodeDefinition> AllNodes => _nodes;

    public static IReadOnlyList<StemNodeDefinition> SelectableLeafNodes =>
        _nodes.Where(n => !n.IsIntermediateBus).ToList();

    public static StemNodeDefinition? GetById(string nodeId) =>
        _nodes.FirstOrDefault(n => string.Equals(n.NodeId, nodeId, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// 根據使用者勾選的終端音軌節點，自動向上追溯所有必要的父節點（母軌），並按拓撲順序排列出所需執行的 Stage 清單。
    /// </summary>
    public static List<string> BuildStageExecutionPlan(IEnumerable<string> requestedNodeIds, bool hasAudioSepPrompts)
    {
        var requiredNodes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var queue = new Queue<string>(requestedNodeIds);

        if (hasAudioSepPrompts)
        {
            queue.Enqueue(StemNodeIds.BusOtherAll);
        }

        while (queue.Count > 0)
        {
            var currentId = queue.Dequeue();
            if (!requiredNodes.Add(currentId))
                continue;

            var def = GetById(currentId);
            if (def?.ParentNodeId is { } parentId && parentId != StemNodeIds.MixOriginal)
            {
                queue.Enqueue(parentId);
            }
        }

        // 定義嚴格的拓撲執行順序 (確保母軌一定先於子軌產生)
        string[] orderedStages =
        [
            "STAGE_1_ROFORMER_MASTER",
            "STAGE_2A_KARAOKE_SPLIT",
            "STAGE_3V_LEAD_DEREVERB",
            "STAGE_3V_BACKING_DEREVERB",
            "STAGE_4V_VOCAL_DENOISE",
            "STAGE_2B_DEMUCS_FT_CORE",
            "STAGE_2C_DEMUCS_6S_MELODIC",
            "STAGE_3D_MDX23C_DRUMSEP",
            "STAGE_3G_CSHARP_MIDSIDE",
            "STAGE_3O_CSHARP_HPSS",
            "STAGE_3P_AUDIOSEP_PROMPT",
            "STAGE_99_CSHARP_RESIDUAL"
        ];

        var activeStages = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var nodeId in requiredNodes)
        {
            var def = GetById(nodeId);
            if (def != null)
            {
                activeStages.Add(def.StageId);
            }
        }

        if (hasAudioSepPrompts)
        {
            activeStages.Add("STAGE_3P_AUDIOSEP_PROMPT");
        }

        // 總是最後執行 Residual 能量守恆合成
        activeStages.Add("STAGE_99_CSHARP_RESIDUAL");

        return orderedStages.Where(activeStages.Contains).ToList();
    }
}
