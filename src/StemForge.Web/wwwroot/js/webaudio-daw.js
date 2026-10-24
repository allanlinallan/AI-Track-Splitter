/**
 * StemForge Studio — WebAudio Sample-Accurate Multitrack DAW Engine & DAG UI Controller
 * 支援 19+ 軌毫秒級同步播放、Solo/Mute 矩陣、單軌相位反轉 (Ø)、Master Null Test 反相抵消驗證、
 * 0 秒即時切換三大分離模式、單軌一鍵轉 MIDI 與 Reaper (.rpp) 工程檔下載。
 */

class StemForgeDawApp {
  constructor() {
    this.catalog = [];
    this.selectedNodes = new Set();
    this.prompts = ["violin", "brass", "flute"];
    this.selectedFile = null;
    this.currentJob = null;

    // WebAudio Multitrack Engine State
    this.audioCtx = null;
    this.masterGainNode = null;
    this.originalMixBuffer = null;
    this.nullTestInvertNode = null;
    this.isNullTestActive = false;

    this.tracks = [];
    this.isPlaying = false;
    this.startTimeCtx = 0;
    this.pauseOffsetSec = 0;
    this.durationSec = 0;
    this.activeSources = [];
    this.rafId = null;

    this.initDom();
    this.initSignalR();
    this.loadSystemStatus();
    this.loadDagCatalog();

    window.addEventListener("resize", () => {
      for (const t of this.tracks) {
        this.drawWaveform(t);
      }
    });
  }

  initDom() {
    this.dropzone = document.getElementById("dropzone");
    this.fileInput = document.getElementById("audioFileInput");
    this.selectedFileLabel = document.getElementById("selectedFileLabel");
    this.localPathInput = document.getElementById("localPathInput");

    // 🎧 音源即時試聽播放器元件
    this.audioPreviewCard = document.getElementById("audioPreviewCard");
    this.previewFileName = document.getElementById("previewFileName");
    this.previewTimeText = document.getElementById("previewTimeText");
    this.btnPreviewPlay = document.getElementById("btnPreviewPlay");
    this.previewScrubber = document.getElementById("previewScrubber");
    this.previewVolume = document.getElementById("previewVolume");
    this.previewMuteIcon = document.getElementById("previewMuteIcon");
    this.previewAudioElement = document.getElementById("previewAudioElement");
    this.currentPreviewUrl = null;
    this.isPreviewScrubbing = false;

    this.btnStartSeparate = document.getElementById("btnStartSeparate");
    this.modeSelect = document.getElementById("separationModeSelect");
    this.dagTreeContainer = document.getElementById("dagTreeContainer");
    this.selectedCountBadge = document.getElementById("selectedCountBadge");

    this.promptInput = document.getElementById("promptInput");
    this.btnAddPrompt = document.getElementById("btnAddPrompt");
    this.promptTagsContainer = document.getElementById("promptTagsContainer");

    this.btnPlayPause = document.getElementById("btnPlayPause");
    this.btnStop = document.getElementById("btnStop");
    this.btnSoloClear = document.getElementById("btnSoloClear");
    this.btnNullTest = document.getElementById("btnNullTest");
    this.masterVolumeSlider = document.getElementById("masterVolumeSlider");
    this.timelineScrubber = document.getElementById("timelineScrubber");
    this.mixerTracksContainer = document.getElementById("mixerTracksContainer");

    this.btnDownloadRpp = document.getElementById("btnDownloadRpp");
    this.btnExportZip = document.getElementById("btnExportZip");

    this.btnSelectAllTracks = document.getElementById("btnSelectAllTracks");
    this.btnClearAllTracks = document.getElementById("btnClearAllTracks");
    this.btnExportSelectedZip = document.getElementById("btnExportSelectedZip");
    this.selectedTracksCountText = document.getElementById("selectedTracksCountText");
    this.btnExportMixdownWav = document.getElementById("btnExportMixdownWav");
    this.btnExportMixdownMp3 = document.getElementById("btnExportMixdownMp3");
    this.mixdownWavCountText = document.getElementById("mixdownWavCountText");
    this.mixdownMp3CountText = document.getElementById("mixdownMp3CountText");
    this.btnOpenHistory = document.getElementById("btnOpenHistory");
    this.historyModal = document.getElementById("historyModal");
    this.btnCloseHistory = document.getElementById("btnCloseHistory");
    this.historyListContainer = document.getElementById("historyListContainer");
    this.checkedNodeIds = new Set();

    // AI 神經變調控制元件 (原調偵測 + ±6半音升降桿 + 點擊開始做 + 一鍵恢復)
    this.detectedKeyBadge = document.getElementById("detectedKeyBadge");
    this.keyOverrideSelect = document.getElementById("keyOverrideSelect");
    this.targetKeyBadge = document.getElementById("targetKeyBadge");
    this.pitchShiftSlider = document.getElementById("pitchShiftSlider");
    this.btnPitchDown = document.getElementById("btnPitchDown");
    this.btnPitchUp = document.getElementById("btnPitchUp");
    this.pitchValueLabel = document.getElementById("pitchValueLabel");
    this.btnApplyPitch = document.getElementById("btnApplyPitch");
    this.btnResetPitch = document.getElementById("btnResetPitch");

    // AI 時間軸拍速控制元件 (原速偵測 + BPM調整滑桿 + 點擊開始做 + 一鍵恢復)
    this.detectedBpmBadge = document.getElementById("detectedBpmBadge");
    this.targetBpmBadge = document.getElementById("targetBpmBadge");
    this.tempoSlider = document.getElementById("tempoSlider");
    this.btnTempoDown = document.getElementById("btnTempoDown");
    this.btnTempoUp = document.getElementById("btnTempoUp");
    this.tempoValueLabel = document.getElementById("tempoValueLabel");
    this.btnApplyTempo = document.getElementById("btnApplyTempo");
    this.btnResetTempo = document.getElementById("btnResetTempo");
    this.originalBpm = 120;
    this.currentBpm = 120;

    // ✂️ 編曲剪輯工具箱 (Pointer, Scissors, Mute)
    this.currentTool = "pointer"; // "pointer" | "scissors" | "mute"
    this.btnToolPointer = document.getElementById("btnToolPointer");
    this.btnToolScissors = document.getElementById("btnToolScissors");
    this.btnToolMute = document.getElementById("btnToolMute");
    this.btnResetClips = document.getElementById("btnResetClips");

    if (this.btnToolPointer) {
      this.btnToolPointer.addEventListener("click", () => this.setToolMode("pointer"));
    }
    if (this.btnToolScissors) {
      this.btnToolScissors.addEventListener("click", () => this.setToolMode("scissors"));
    }
    if (this.btnToolMute) {
      this.btnToolMute.addEventListener("click", () => this.setToolMode("mute"));
    }
    if (this.btnResetClips) {
      this.btnResetClips.addEventListener("click", () => this.resetAllClips());
    }

    // 鍵盤快速鍵：1(選取), 2(剪刀), 3(消音), Ctrl+S(儲存)
    window.addEventListener("keydown", (e) => {
      if ((e.ctrlKey || e.metaKey) && e.key.toLowerCase() === "s") {
        e.preventDefault();
        this.saveCurrentProject();
        return;
      }
      if (e.target.tagName === "INPUT" || e.target.tagName === "TEXTAREA") return;
      if (e.key === "1") this.setToolMode("pointer");
      else if (e.key === "2") this.setToolMode("scissors");
      else if (e.key === "3") this.setToolMode("mute");
    });

    // 💾 儲存與另存專案元件綁定
    this.btnSaveProject = document.getElementById("btnSaveProject");
    if (this.btnSaveProject) {
      this.btnSaveProject.addEventListener("click", () => this.saveCurrentProject());
    }

    this.btnSaveAsProject = document.getElementById("btnSaveAsProject");
    this.saveAsModal = document.getElementById("saveAsModal");
    this.btnCloseSaveAs = document.getElementById("btnCloseSaveAs");
    this.btnCancelSaveAs = document.getElementById("btnCancelSaveAs");
    this.btnConfirmSaveAs = document.getElementById("btnConfirmSaveAs");
    this.saveAsProjectNameInput = document.getElementById("saveAsProjectNameInput");

    if (this.btnSaveAsProject) {
      this.btnSaveAsProject.addEventListener("click", () => this.openSaveAsModal());
    }
    if (this.btnCloseSaveAs) {
      this.btnCloseSaveAs.addEventListener("click", () => this.closeSaveAsModal());
    }
    if (this.btnCancelSaveAs) {
      this.btnCancelSaveAs.addEventListener("click", () => this.closeSaveAsModal());
    }
    if (this.saveAsModal) {
      this.saveAsModal.addEventListener("click", (e) => {
        if (e.target === this.saveAsModal) this.closeSaveAsModal();
      });
    }
    if (this.btnConfirmSaveAs) {
      this.btnConfirmSaveAs.addEventListener("click", () => this.executeSaveAsProject());
    }
    if (this.saveAsProjectNameInput) {
      this.saveAsProjectNameInput.addEventListener("keydown", (e) => {
        if (e.key === "Enter") this.executeSaveAsProject();
        else if (e.key === "Escape") this.closeSaveAsModal();
      });
    }

    // 🔔 節拍器與預拍一小節元件 (Count-in & Metronome)
    this.chkCountIn = document.getElementById("chkCountIn");
    this.selCountInPickup = document.getElementById("selCountInPickup");
    this.chkMetronome = document.getElementById("chkMetronome");
    this.metroBeatIndicator = document.getElementById("metroBeatIndicator");
    this.countInTimeouts = [];
    this.metronomeInterval = null;

    if (this.selCountInPickup) {
      const savedPickup = localStorage.getItem("stemforge_countin_pickup");
      if (savedPickup !== null) {
        this.selCountInPickup.value = savedPickup;
      }
      this.selCountInPickup.addEventListener("change", () => {
        localStorage.setItem("stemforge_countin_pickup", this.selCountInPickup.value);
      });
    }

    const updateCountInUi = () => {
      if (this.selCountInPickup) {
        this.selCountInPickup.style.display = (this.chkCountIn && this.chkCountIn.checked) ? "inline-block" : "none";
      }
      this.updateCheckedTracksCount();
    };

    if (this.chkCountIn) {
      const savedCountIn = localStorage.getItem("stemforge_countin");
      if (savedCountIn !== null) {
        this.chkCountIn.checked = savedCountIn === "true";
      }
      this.chkCountIn.addEventListener("change", () => {
        localStorage.setItem("stemforge_countin", this.chkCountIn.checked);
        updateCountInUi();
      });
      updateCountInUi();
    }

    if (this.chkMetronome) {
      const savedMetronome = localStorage.getItem("stemforge_metronome");
      if (savedMetronome !== null) {
        this.chkMetronome.checked = savedMetronome === "true";
      }
      this.chkMetronome.addEventListener("change", () => {
        localStorage.setItem("stemforge_metronome", this.chkMetronome.checked);
        if (this.isPlaying) {
          if (this.chkMetronome.checked) {
            const bpm = this.getCurrentBpm();
            this.startMetronomeScheduler(this.audioCtx.currentTime + 0.04, this.getCurrentTimeSec(), 60.0 / bpm);
          } else {
            this.clearMetronome();
          }
        }
      });
    }

    // ✂️ 自動切齊開頭靜音元件 (Auto Trim Silence)
    this.leadTrimSec = 0;
    this.btnAutoTrimSilence = document.getElementById("btnAutoTrimSilence");
    this.btnResetTrim = document.getElementById("btnResetTrim");
    this.trimSilenceModal = document.getElementById("trimSilenceModal");
    this.btnCloseTrimSilence = document.getElementById("btnCloseTrimSilence");
    this.btnCancelTrimSilence = document.getElementById("btnCancelTrimSilence");
    this.btnConfirmTrimSilence = document.getElementById("btnConfirmTrimSilence");
    this.trimDetectInfo = document.getElementById("trimDetectInfo");
    this.trimWaveformCanvas = document.getElementById("trimWaveformCanvas");
    this.btnAuditionTrim = document.getElementById("btnAuditionTrim");
    this.trimPresetButtons = document.getElementById("trimPresetButtons");
    this.trimManualInput = document.getElementById("trimManualInput");
    this.btnNudgeTrimM50 = document.getElementById("btnNudgeTrimM50");
    this.btnNudgeTrimM10 = document.getElementById("btnNudgeTrimM10");
    this.btnNudgeTrimP10 = document.getElementById("btnNudgeTrimP10");
    this.btnNudgeTrimP50 = document.getElementById("btnNudgeTrimP50");

    this.detectedOnsetSec = 0;
    this.currentTrimCutSec = 0;
    this.trimMaxViewSec = 6.0;
    this.isAuditioningTrim = false;
    this.trimAuditionSources = [];
    this.trimAuditionTimer = null;
    this.isDraggingTrim = false;

    if (this.btnAutoTrimSilence) {
      this.btnAutoTrimSilence.addEventListener("click", () => this.openTrimSilenceModal());
    }
    if (this.btnResetTrim) {
      this.btnResetTrim.addEventListener("click", () => this.applyLeadTrim(0, true));
    }
    if (this.btnCloseTrimSilence) {
      this.btnCloseTrimSilence.addEventListener("click", () => this.closeTrimSilenceModal());
    }
    if (this.btnCancelTrimSilence) {
      this.btnCancelTrimSilence.addEventListener("click", () => this.closeTrimSilenceModal());
    }
    if (this.trimSilenceModal) {
      this.trimSilenceModal.addEventListener("click", (e) => {
        if (e.target === this.trimSilenceModal) this.closeTrimSilenceModal();
      });
    }
    if (this.btnConfirmTrimSilence) {
      this.btnConfirmTrimSilence.addEventListener("click", () => this.confirmTrimSilence());
    }

    this.initTrimWaveformInteractions();
    this.initTrimPresetControls();

    // 處理中半透明變暗遮罩元件 (Busy Overlay)
    this.busyOverlay = document.getElementById("mixerBusyOverlay");
    this.busyTitle = document.getElementById("busyOverlayTitle");
    this.busySub = document.getElementById("busyOverlaySub");
    this.busyIcon = document.getElementById("busySpinnerIcon");
    this.busyFill = document.getElementById("busyOverlayFill");
    this.busyPct = document.getElementById("busyOverlayPct");

    if (this.pitchShiftSlider) {
      // 拉動時純更新數值與目標調性文字，不觸發耗時神經運算
      this.pitchShiftSlider.addEventListener("input", (e) => this.updatePitchUiOnly(parseInt(e.target.value, 10)));
      this.pitchShiftSlider.addEventListener("change", (e) => this.updatePitchUiOnly(parseInt(e.target.value, 10)));
    }
    if (this.btnPitchDown) {
      this.btnPitchDown.addEventListener("click", () => this.stepPitchUi(-1));
    }
    if (this.btnPitchUp) {
      this.btnPitchUp.addEventListener("click", () => this.stepPitchUi(1));
    }
    if (this.btnApplyPitch) {
      // 點「⚡ 套用變調」才正式向後端送出開始變調！
      this.btnApplyPitch.addEventListener("click", () => {
        const val = parseInt(this.pitchShiftSlider.value, 10) || 0;
        this.applyPitchShift(val);
      });
    }
    if (this.btnResetPitch) {
      this.btnResetPitch.addEventListener("click", () => {
        if (this.pitchShiftSlider) this.pitchShiftSlider.value = 0;
        this.updatePitchUiOnly(0);
        this.applyPitchShift(0);
      });
    }
    if (this.keyOverrideSelect) {
      this.keyOverrideSelect.addEventListener("change", (e) => this.handleManualKeyOverride(e.target.value));
    }

    if (this.tempoSlider) {
      this.tempoSlider.addEventListener("input", (e) => this.updateTempoUiOnly(parseInt(e.target.value, 10)));
      this.tempoSlider.addEventListener("change", (e) => this.updateTempoUiOnly(parseInt(e.target.value, 10)));
    }
    if (this.btnTempoDown) {
      this.btnTempoDown.addEventListener("click", () => this.stepTempoUi(-1));
    }
    if (this.btnTempoUp) {
      this.btnTempoUp.addEventListener("click", () => this.stepTempoUi(1));
    }
    if (this.btnApplyTempo) {
      this.btnApplyTempo.addEventListener("click", () => {
        const val = parseInt(this.tempoSlider.value, 10) || this.originalBpm || 120;
        this.applyTempoShift(val);
      });
    }
    if (this.btnResetTempo) {
      this.btnResetTempo.addEventListener("click", () => {
        if (this.tempoSlider) this.tempoSlider.value = this.originalBpm || 120;
        this.updateTempoUiOnly(this.originalBpm || 120);
        this.applyTempoShift(this.originalBpm || 120);
      });
    }

    if (this.btnExportSelectedZip) {
      this.btnExportSelectedZip.addEventListener("click", () => this.exportSelectedZip());
    }
    if (this.btnExportMixdownWav) {
      this.btnExportMixdownWav.addEventListener("click", () => this.exportMixdown("wav"));
    }
    if (this.btnExportMixdownMp3) {
      this.btnExportMixdownMp3.addEventListener("click", () => this.exportMixdown("mp3"));
    }

    if (this.btnSelectAllTracks) {
      this.btnSelectAllTracks.addEventListener("click", () => this.selectAllTracks(true));
    }
    if (this.btnClearAllTracks) {
      this.btnClearAllTracks.addEventListener("click", () => this.selectAllTracks(false));
    }

    if (this.btnOpenHistory) {
      this.btnOpenHistory.addEventListener("click", () => this.openHistoryModal());
    }
    if (this.btnCloseHistory) {
      this.btnCloseHistory.addEventListener("click", () => this.closeHistoryModal());
    }
    if (this.historyModal) {
      this.historyModal.addEventListener("click", (e) => {
        if (e.target === this.historyModal) this.closeHistoryModal();
      });
    }

    // Drag & Drop File Upload
    this.dropzone.addEventListener("click", () => this.fileInput.click());
    this.fileInput.addEventListener("change", (e) => {
      if (e.target.files.length > 0) {
        this.selectedFile = e.target.files[0];
        this.selectedFileLabel.textContent = `已選擇: ${this.selectedFile.name} (${(this.selectedFile.size / 1048576).toFixed(2)} MB)`;
        const url = URL.createObjectURL(this.selectedFile);
        this.loadSourcePreview(url, this.selectedFile.name);
      }
    });

    this.dropzone.addEventListener("dragover", (e) => {
      e.preventDefault();
      this.dropzone.style.borderColor = "#10b981";
    });
    this.dropzone.addEventListener("dragleave", () => {
      this.dropzone.style.borderColor = "#3b82f6";
    });
    this.dropzone.addEventListener("drop", (e) => {
      e.preventDefault();
      this.dropzone.style.borderColor = "#3b82f6";
      if (e.dataTransfer.files.length > 0) {
        this.selectedFile = e.dataTransfer.files[0];
        this.selectedFileLabel.textContent = `已選擇: ${this.selectedFile.name} (${(this.selectedFile.size / 1048576).toFixed(2)} MB)`;
        const url = URL.createObjectURL(this.selectedFile);
        this.loadSourcePreview(url, this.selectedFile.name);
      }
    });

    // 監聽本機路徑輸入框
    if (this.localPathInput) {
      const handleLocalPath = () => {
        const raw = this.localPathInput.value.trim();
        const p = raw.replace(/^["'](.*)["']$/, "$1").trim();
        if (p) {
          const fileName = p.split(/[\\/]/).pop() || p;
          const url = `/api/preview/local-file?path=${encodeURIComponent(p)}&t=${Date.now()}`;
          this.loadSourcePreview(url, fileName);
        }
      };
      this.localPathInput.addEventListener("change", handleLocalPath);
      this.localPathInput.addEventListener("blur", handleLocalPath);
    }

    this.initSourcePreviewEvents();

    // Presets
    document.querySelectorAll(".preset-btns .btn-xs[data-preset]").forEach((btn) => {
      btn.addEventListener("click", () => this.applyPreset(btn.dataset.preset));
    });
    const savePresetBtn = document.getElementById("btnSaveCustomPreset");
    if (savePresetBtn) {
      savePresetBtn.addEventListener("click", () => {
        const name = prompt("請輸入這個自訂分軌組合的名稱（例如：主唱+木吉他）：");
        if (!name || !name.trim()) return;
        const customBtn = document.createElement("button");
        customBtn.className = "btn-xs";
        customBtn.textContent = `★ ${name.trim()}`;
        const savedIds = Array.from(this.selectedNodes);
        customBtn.addEventListener("click", () => {
          this.selectedNodes = new Set(savedIds);
          this.renderDagTree();
          this.syncLiveTrackSelection();
        });
        savePresetBtn.parentElement.insertBefore(customBtn, savePresetBtn);
      });
    }

    // Prompts
    this.btnAddPrompt.addEventListener("click", () => this.addPromptFromInput());
    this.promptInput.addEventListener("keydown", (e) => {
      if (e.key === "Enter") this.addPromptFromInput();
    });
    this.renderPromptChips();

    // Start Separation
    this.btnStartSeparate.addEventListener("click", () => this.startSeparationJob());

    // Instant Mode Switch
    this.modeSelect.addEventListener("change", () => {
      if (this.currentJob) {
        this.switchJobMode(this.currentJob.jobId, this.modeSelect.value);
      }
    });

    // Transport Controls
    this.btnPlayPause.addEventListener("click", () => this.togglePlayPause());
    this.btnStop.addEventListener("click", () => this.stopPlayback());
    this.btnSoloClear.addEventListener("click", () => this.clearAllSoloMute());
    this.btnNullTest.addEventListener("click", () => this.toggleNullTest());

    this.masterVolumeSlider.addEventListener("input", (e) => {
      if (this.masterGainNode) {
        this.masterGainNode.gain.value = parseFloat(e.target.value);
      }
    });

    this.timelineScrubber.addEventListener("click", (e) => {
      if (!this.durationSec) return;
      const rect = this.timelineScrubber.getBoundingClientRect();
      const ratio = Math.max(0, Math.min(1, (e.clientX - rect.left) / rect.width));
      const globalSec = ratio * this.durationSec;
      const targetSec = Math.max(0, globalSec - (this.leadTrimSec || 0));
      this.seekTo(targetSec);
    });

    this.btnExportZip.addEventListener("click", () => this.exportFullZip());
  }

  initSignalR() {
    this.hubConnection = new signalR.HubConnectionBuilder()
      .withUrl("/hubs/separation")
      .withAutomaticReconnect()
      .build();

    this.hubConnection.on("ProgressUpdated", (ev) => {
      document.getElementById("progressStageTag").textContent = ev.cacheHit ? "CACHE HIT ⚡" : ev.stageId;
      document.getElementById("progressTitleText").textContent = ev.stageTitle;
      document.getElementById("progressPctText").textContent = `${ev.overallPercent}%`;
      document.getElementById("progressBarFill").style.width = `${ev.overallPercent}%`;
      document.getElementById("progressSubText").textContent = ev.message;
      if (ev.vramTotalMb > 0) {
        document.getElementById("vramUsageText").textContent = `${ev.vramUsedMb} / ${ev.vramTotalMb} MB`;
      }
      this.updateBusyOverlayProgress(ev.overallPercent, ev.stageTitle, ev.message);
    });

    this.hubConnection.start().catch((err) => console.warn("SignalR:", err));
  }

  showBusyOverlay(title, sub, pct = 0, icon = "⚡") {
    if (!this.busyOverlay) return;
    if (this.busyTitle && title) this.busyTitle.textContent = title;
    if (this.busySub && sub) this.busySub.textContent = sub;
    if (this.busyIcon && icon) this.busyIcon.textContent = icon;
    this.updateBusyOverlayProgress(pct);
    this.busyOverlay.classList.add("active");
  }

  updateBusyOverlayProgress(pct, title, sub) {
    if (typeof pct === "number") {
      const clamped = Math.max(0, Math.min(100, pct));
      if (this.busyFill) this.busyFill.style.width = `${clamped}%`;
      if (this.busyPct) this.busyPct.textContent = `${clamped.toFixed(clamped % 1 === 0 ? 0 : 1)}%`;
    }
    if (title && this.busyTitle) this.busyTitle.textContent = title;
    if (sub && this.busySub) this.busySub.textContent = sub;
  }

  hideBusyOverlay() {
    if (!this.busyOverlay) return;
    this.busyOverlay.classList.remove("active");
  }

  async loadSystemStatus() {
    try {
      const res = await fetch("/api/system/status");
      const status = await res.json();
      document.getElementById("gpuNameText").textContent = status.gpuName;
      document.getElementById("vramUsageText").textContent = `${status.vramUsedMb} / ${status.vramTotalMb} MB`;
      document.getElementById("diskFreeText").textContent = `${status.diskFreeGb} GB`;
      document.getElementById("engineModePill").textContent = status.audioSeparatorInstalled
        ? "AI GPU + C# SIMD Ready"
        : "C# SIMD Spectral Engine Ready (可選安裝 Python 權重)";
    } catch (e) {
      console.error(e);
    }
  }

  async loadDagCatalog() {
    const res = await fetch("/api/dag/catalog");
    const data = await res.json();
    this.catalog = data.selectableLeaves || [];
    this.selectedNodes = new Set(this.catalog.filter((n) => n.defaultSelected).map((n) => n.nodeId));
    for (const p of this.prompts) {
      this.selectedNodes.add(`STEM_18_AUDIOSEP_${p.toUpperCase()}`);
    }
    this.renderDagTree();
  }

  renderDagTree() {
    this.dagTreeContainer.innerHTML = "";
    const groups = {};
    for (const node of this.catalog) {
      if (!groups[node.groupCategory]) groups[node.groupCategory] = [];
      groups[node.groupCategory].push(node);
    }

    if (this.prompts.length > 0) {
      groups["Custom AudioSep (自訂指定樂器)"] = this.prompts.map((p) => ({
        nodeId: `STEM_18_AUDIOSEP_${p.toUpperCase()}`,
        displayName: `18. 指定樂器抽離 [${p}]`,
        groupCategory: "Custom AudioSep (自訂指定樂器)",
        colorHex: "#E879F9",
        description: `由 AudioSep 文字提示獨立抽出的 [${p}] 音軌`
      }));
    }

    for (const [groupName, nodes] of Object.entries(groups)) {
      const header = document.createElement("div");
      header.className = "dag-group-header";
      header.textContent = groupName;
      this.dagTreeContainer.appendChild(header);

      for (const node of nodes) {
        const row = document.createElement("label");
        row.className = "dag-node-item";
        row.title = node.description;

        const chk = document.createElement("input");
        chk.type = "checkbox";
        chk.checked = this.selectedNodes.has(node.nodeId);
        chk.addEventListener("change", () => {
          if (chk.checked) this.selectedNodes.add(node.nodeId);
          else this.selectedNodes.delete(node.nodeId);
          this.selectedCountBadge.textContent = `${this.selectedNodes.size} 軌`;
          this.syncLiveTrackSelection();
        });

        const dot = document.createElement("span");
        dot.className = "node-color-dot";
        dot.style.backgroundColor = node.colorHex;

        const txt = document.createElement("span");
        txt.textContent = node.displayName;

        row.appendChild(chk);
        row.appendChild(dot);
        row.appendChild(txt);
        this.dagTreeContainer.appendChild(row);
      }
    }
    this.selectedCountBadge.textContent = `${this.selectedNodes.size} 軌`;
  }

  applyPreset(preset) {
    this.selectedNodes.clear();
    for (const node of this.catalog) {
      if (preset === "all") {
        this.selectedNodes.add(node.nodeId);
      } else if (preset === "none") {
        // leave empty
      } else if (preset === "vocals" && node.groupCategory.includes("Vocal")) {
        this.selectedNodes.add(node.nodeId);
      } else if (preset === "drums" && node.groupCategory.includes("Drum")) {
        this.selectedNodes.add(node.nodeId);
      } else if (preset === "rhythm" && (node.groupCategory.includes("Drum") || node.nodeId === "STEM_12_BASS")) {
        this.selectedNodes.add(node.nodeId);
      } else if (preset === "inst" && (node.groupCategory.includes("Instrument") || node.groupCategory.includes("Orchestral"))) {
        this.selectedNodes.add(node.nodeId);
      } else if (preset === "karaoke" && !node.nodeId.includes("VOCAL_LEAD") && !node.nodeId.includes("BREATH")) {
        this.selectedNodes.add(node.nodeId);
      }
    }
    if (preset === "all" || preset === "inst" || preset === "karaoke") {
      for (const p of this.prompts) {
        this.selectedNodes.add(`STEM_18_AUDIOSEP_${p.toUpperCase()}`);
      }
    }
    this.renderDagTree();
    this.syncLiveTrackSelection();
  }

  syncLiveTrackSelection() {
    if (!this.tracks || this.tracks.length === 0) return;
    for (const t of this.tracks) {
      const visible = t.meta?.isOriginalReference || this.selectedNodes.has(t.nodeId);
      t.hiddenByTree = !visible;
      if (t.rowEl) {
        t.rowEl.style.display = visible ? "" : "none";
      }
    }
    this.updateTrackGains();
  }

  async addPromptFromInput() {
    const val = this.promptInput.value.trim().toLowerCase();
    this.promptInput.value = "";
    if (!val) return;

    const customNodeId = `STEM_18_AUDIOSEP_${val.toUpperCase()}`;
    if (!this.prompts.includes(val)) {
      this.prompts.push(val);
    }
    this.selectedNodes.add(customNodeId);
    this.renderPromptChips();
    this.renderDagTree();

    // 若目前已載入歌曲，立即向後端動態抽離該樂器音軌並掛載進右側混音台！
    if (this.currentJob && this.currentJob.jobId) {
      try {
        this.btnAddPrompt.disabled = true;
        this.btnAddPrompt.textContent = "抽離中...";
        const res = await fetch(`/api/jobs/${this.currentJob.jobId}/prompt/${encodeURIComponent(val)}`, {
          method: "POST"
        });
        if (res.ok) {
          const data = await res.json();
          if (data.job) {
            await this.loadJobIntoMixer(data.job);
          }
        }
      } catch (e) {
        console.error(e);
      } finally {
        this.btnAddPrompt.disabled = false;
        this.btnAddPrompt.textContent = "+ 新增";
      }
    }
  }

  renderPromptChips() {
    this.promptTagsContainer.innerHTML = "";
    for (const p of this.prompts) {
      const chip = document.createElement("span");
      chip.className = "prompt-chip";
      chip.innerHTML = `${p} <b class="rm-chip">×</b>`;
      chip.querySelector(".rm-chip").addEventListener("click", () => {
        this.prompts = this.prompts.filter((item) => item !== p);
        this.selectedNodes.delete(`STEM_18_AUDIOSEP_${p.toUpperCase()}`);
        this.renderPromptChips();
        this.renderDagTree();
        this.syncLiveTrackSelection();
      });
      this.promptTagsContainer.appendChild(chip);
    }
  }

  async startSeparationJob() {
    const localPath = this.localPathInput.value.trim();
    if (!this.selectedFile && !localPath) {
      alert("請先選擇 MP3/WAV/FLAC 或 MP4 影片檔案，或貼上本機檔案路徑！");
      return;
    }

    this.stopPlayback();
    this.stopSourcePreview();
    this.btnStartSeparate.disabled = true;
    this.showBusyOverlay("⚡ AI 深度神經分軌中...", "RTX 4050 級聯模型分離運算中...", 0, "🎛️");

    const formData = new FormData();
    if (this.selectedFile) formData.append("file", this.selectedFile);
    if (localPath) formData.append("localFilePath", localPath);
    formData.append("selectedNodes", JSON.stringify(Array.from(this.selectedNodes)));
    formData.append("prompts", JSON.stringify(this.prompts));
    formData.append("mode", this.modeSelect.value);
    formData.append("useFp16", document.getElementById("chkFp16").checked);
    formData.append("autoMidi", document.getElementById("chkAutoMidi").checked);

    try {
      const res = await fetch("/api/jobs/separate", { method: "POST", body: formData });
      const data = await res.json();
      if (!res.ok) throw new Error(data.error || "分軌失敗");
      await this.loadJobIntoMixer(data);
    } catch (err) {
      alert(err.message);
      this.hideBusyOverlay();
    } finally {
      this.btnStartSeparate.disabled = false;
    }
  }

  async switchJobMode(jobId, mode) {
    const wasPlaying = this.isPlaying;
    const currentPos = this.getCurrentTimeSec();
    const currentBpm = this.currentBpm || this.originalBpm;
    const currentLeadTrim = this.leadTrimSec;
    this.stopPlayback();

    const res = await fetch(`/api/jobs/${jobId}/mode/${mode}`, { method: "POST" });
    if (res.ok) {
      const updated = await res.json();
      if (!updated.detectedBpm && currentBpm) {
        updated.detectedBpm = currentBpm;
      }
      await this.loadJobIntoMixer(updated);
      if (currentLeadTrim > 0) {
        this.applyLeadTrim(currentLeadTrim, false);
      }
      if (wasPlaying) {
        this.seekTo(currentPos);
        this.startPlayback();
      }
    }
  }

  ensureAudioContext() {
    if (!this.audioCtx) {
      const AudioContextClass = window.AudioContext || window.webkitAudioContext;
      this.audioCtx = new AudioContextClass();
      this.masterGainNode = this.audioCtx.createGain();
      this.masterGainNode.gain.value = parseFloat(this.masterVolumeSlider.value);

      this.masterLimiter = this.audioCtx.createDynamicsCompressor();
      this.masterLimiter.threshold.value = -0.5; // -0.5 dBFS 類比級母帶限制器防爆音天花板
      this.masterLimiter.knee.value = 6.0; // soft knee 平滑壓縮過渡
      this.masterLimiter.ratio.value = 20.0; // Brickwall 剛性限制
      this.masterLimiter.attack.value = 0.002; // 2ms 極速瞬態捕捉
      this.masterLimiter.release.value = 0.08; // 80ms 快速釋放

      this.masterGainNode.connect(this.masterLimiter);
      this.masterLimiter.connect(this.audioCtx.destination);
    }
    if (this.audioCtx.state === "suspended") {
      this.audioCtx.resume();
    }
  }

  async loadJobIntoMixer(jobResult) {
    this.stopPlayback();
    this.currentJob = jobResult;
    try {
      sessionStorage.setItem("stemforge_last_job", JSON.stringify(jobResult));
    } catch {}
    this.ensureAudioContext();
    this.showBusyOverlay(
      "📂 正在載入音軌至混音台...",
      `正在並行解碼 ${jobResult.stems.length} 條 32-bit Float 音訊緩衝區...`,
      25,
      "🎚️"
    );

    document.getElementById("nullCancellationBadge").textContent =
      `Null 誤差: ${jobResult.nullTestCancellationDbFs} dBFS`;

    const detectedKeyDisplay = jobResult.detectedKeyDisplayName || jobResult.detectedKey || "C 大調 (C Major)";
    if (this.detectedKeyBadge) {
      this.detectedKeyBadge.innerHTML = `原調: ${detectedKeyDisplay} ▾ <span style="font-size: 10px; color: #38bdf8;">[點擊修改]</span>`;
    }
    if (this.keyOverrideSelect && jobResult.detectedKey) {
      const matchOpt = Array.from(this.keyOverrideSelect.options).find(o => o.value.startsWith(jobResult.detectedKey + "|"));
      if (matchOpt) this.keyOverrideSelect.value = matchOpt.value;
    }
    if (this.pitchShiftSlider) {
      this.pitchShiftSlider.value = 0;
    }
    if (this.pitchValueLabel) {
      this.pitchValueLabel.textContent = "0 半音";
    }
    if (this.targetKeyBadge) {
      this.targetKeyBadge.textContent = "目標: 原調 (0)";
    }

    const bpmVal = jobResult.detectedBpm
      ? Math.round(jobResult.detectedBpm)
      : (this.currentBpm && this.currentBpm > 30 ? this.currentBpm : (this.originalBpm || 120));
    this.originalBpm = bpmVal;
    this.currentBpm = bpmVal;

    if (this.detectedBpmBadge) {
      this.detectedBpmBadge.textContent = `原速: ${bpmVal} BPM`;
    }
    if (this.targetBpmBadge) {
      this.targetBpmBadge.textContent = "目標: 原速";
    }
    if (this.tempoSlider) {
      const minBpm = Math.max(40, Math.round(bpmVal * 0.6));
      const maxBpm = Math.min(260, Math.round(bpmVal * 1.5));
      this.tempoSlider.min = minBpm;
      this.tempoSlider.max = maxBpm;
      this.tempoSlider.value = bpmVal;
    }
    if (this.tempoValueLabel) {
      this.tempoValueLabel.textContent = `${bpmVal} BPM`;
    }

    if (jobResult.reaperRppUrl) {
      this.btnDownloadRpp.href = jobResult.reaperRppUrl;
      this.btnDownloadRpp.classList.remove("disabled");
    }
    this.btnExportZip.disabled = false;

    if (this.btnSaveProject) this.btnSaveProject.disabled = false;
    if (this.btnSaveAsProject) this.btnSaveAsProject.disabled = false;
    if (this.btnSelectAllTracks) this.btnSelectAllTracks.disabled = false;
    if (this.btnClearAllTracks) this.btnClearAllTracks.disabled = false;
    this.checkedNodeIds = new Set();

    this.mixerTracksContainer.innerHTML = `<div class="empty-mixer-state">正在將 ${jobResult.stems.length} 條 32-bit Float 音軌解碼載入 WebAudio 同步緩衝區...</div>`;

    // 解碼原曲供 Null Test 反相抵消使用 (加上 timestamp 避免被瀏覽器硬取快取)
    const srcResp = await fetch(`${jobResult.sourceAudioUrl}?t=${Date.now()}`);
    const srcArr = await srcResp.arrayBuffer();
    this.originalMixBuffer = await this.audioCtx.decodeAudioData(srcArr);
    this.durationSec = this.originalMixBuffer.duration;
    document.getElementById("totalTimeText").textContent = this.formatTime(this.durationSec);

    // 計算原曲母帶之 Peak 與 RMS dBFS
    let origMaxVal = 0;
    let origSumSq = 0;
    const ch0 = this.originalMixBuffer.getChannelData(0);
    const chStep = Math.max(1, Math.floor(ch0.length / 50000));
    let origCnt = 0;
    for (let i = 0; i < ch0.length; i += chStep) {
      const v = Math.abs(ch0[i]);
      if (v > origMaxVal) origMaxVal = v;
      origSumSq += v * v;
      origCnt++;
    }
    const origPeakDb = origMaxVal > 0 ? (20 * Math.log10(origMaxVal)).toFixed(1) : "-0.0";
    const origRmsDb = origCnt > 0 ? (20 * Math.log10(Math.sqrt(origSumSq / origCnt))).toFixed(1) : "-14.0";

    const originalTrackStem = {
      nodeId: "TRACK_00_ORIGINAL",
      displayName: "🎵 原曲母帶 (Original Reference)",
      colorHex: "#38bdf8",
      peakDbFs: origPeakDb,
      rmsDbFs: origRmsDb,
      relativeUrl: jobResult.sourceAudioUrl,
      fromCache: false,
      isOriginalReference: true
    };

    // 平行下載並解碼所有分軌
    this.tracks = [];
    const decodedStems = await Promise.all(
      jobResult.stems.map(async (stem) => {
        const r = await fetch(`${stem.relativeUrl}?t=${Date.now()}`);
        const buf = await r.arrayBuffer();
        const audioBuffer = await this.audioCtx.decodeAudioData(buf);
        return { stem, audioBuffer };
      })
    );

    const allStemsToRender = [
      { stem: originalTrackStem, audioBuffer: this.originalMixBuffer },
      ...decodedStems
    ];

    // 解析專案儲存狀態 (project_state.json)
    let projectState = null;
    if (jobResult.projectState) {
      projectState = jobResult.projectState;
    } else if (jobResult.projectStateJson) {
      try {
        projectState = typeof jobResult.projectStateJson === "string"
          ? JSON.parse(jobResult.projectStateJson)
          : jobResult.projectStateJson;
      } catch { }
    }
    this.currentJob.projectState = projectState;

    this.mixerTracksContainer.innerHTML = "";

    for (const { stem, audioBuffer } of allStemsToRender) {
      const gainNode = this.audioCtx.createGain();
      const panNode = this.audioCtx.createStereoPanner();
      panNode.connect(gainNode);
      gainNode.connect(this.masterGainNode);

      const isOriginal = !!stem.isOriginalReference;
      const isResidual = stem.nodeId.startsWith("STEM_19_RESIDUAL");
      const savedState = projectState?.trackStates?.[stem.nodeId];

      let initialRegions = [{
        id: 1,
        startSec: 0,
        endSec: this.durationSec,
        muted: false
      }];
      let nextRegId = 2;

      if (savedState && Array.isArray(savedState.regions) && savedState.regions.length > 0) {
        initialRegions = savedState.regions.map(r => ({
          id: r.id || 1,
          startSec: Number(r.startSec) || 0,
          endSec: Number(r.endSec) || this.durationSec,
          muted: !!r.muted
        }));
        nextRegId = initialRegions.reduce((m, r) => Math.max(m, r.id || 1), 1) + 1;
      }

      const trackObj = {
        nodeId: stem.nodeId,
        meta: stem,
        audioBuffer,
        gainNode,
        panNode,
        solo: savedState ? !!savedState.solo : false,
        mute: savedState ? !!savedState.mute : (isOriginal || isResidual),
        invertPhase: savedState ? !!savedState.invertPhase : false,
        volume: savedState && typeof savedState.volume === "number" ? savedState.volume : 1.0,
        pan: savedState && typeof savedState.pan === "number" ? savedState.pan : 0.0,
        rowEl: null,
        canvas: null,
        playheadEl: null,
        regions: initialRegions,
        nextRegionId: nextRegId,
        regionsOverlayEl: null,
        scissorLineEl: null,
        scissorLabelEl: null
      };

      this.tracks.push(trackObj);
      this.renderTrackStrip(trackObj);
      this.renderTrackRegions(trackObj);
    }

    this.updateTrackGains();

    if (projectState && typeof projectState.leadTrimSec === "number" && projectState.leadTrimSec > 0) {
      this.applyLeadTrim(projectState.leadTrimSec, false);
    } else {
      this.applyLeadTrim(0, false);
    }

    // 確保全部 DOM 掛載完成後繪製高解析度正規化波形
    setTimeout(() => {
      for (const t of this.tracks) {
        this.drawWaveform(t);
      }
      this.renderLeadTrimOverlays();
      this.hideBusyOverlay();
    }, 50);

    this.pauseOffsetSec = 0;
    this.btnPlayPause.disabled = false;
    this.btnStop.disabled = false;
    this.btnSoloClear.disabled = false;
    this.btnNullTest.disabled = false;
  }

  renderTrackStrip(track) {
    const { meta } = track;
    const row = document.createElement("div");
    row.className = "track-row" + (meta.isOriginalReference ? " reference-track" : "");
    track.rowEl = row;

    row.innerHTML = `
      <div class="track-info">
        <div class="track-select-box">
          <input type="checkbox" class="track-select-chk" title="勾選以加入自訂打包下載" ${meta.isOriginalReference ? "" : "checked"} />
        </div>
        <div class="track-color-bar" style="background:${meta.colorHex}"></div>
        <div class="track-title-wrap">
          <div class="track-name" title="${meta.displayName}">${meta.displayName}</div>
          <div class="track-meta">Peak: ${meta.peakDbFs} dB | RMS: ${meta.rmsDbFs} dB ${meta.fromCache ? "⚡Cache" : ""}</div>
        </div>
      </div>
      <div class="track-controls">
        <button class="m-btn solo ${track.solo ? "active" : ""}" title="Solo 獨奏此軌 (對比原曲)">S</button>
        <button class="m-btn mute ${track.mute ? "active" : ""}" title="Mute 靜音此軌">M</button>
        <button class="m-btn invert ${track.invertPhase ? "active" : ""}" title="Phase Invert 180° 反相 (即時反相測試)">Ø</button>
        <div class="fader-wrap">
          <input type="range" class="vol-slider" min="0" max="4.0" step="0.02" value="${track.volume ?? 1.0}" title="音量推子: 0.0 dB (100%)" />
          <input type="range" class="pan-slider" min="-1" max="1" step="0.05" value="${track.pan ?? 0.0}" title="立體聲 Pan (L/R)" />
        </div>
      </div>
      <div class="track-waveform-wrap">
        <canvas class="track-waveform-canvas" height="58"></canvas>
        <div class="track-regions-overlay"></div>
        <div class="scissor-indicator-line">
          <span class="scissor-indicator-label">✂️ 00:00.000</span>
        </div>
        <div class="track-playhead" style="left:0%"></div>
      </div>
      <div class="track-actions">
        ${meta.isOriginalReference ? 
          `<span class="ref-badge">參照軌 REF</span>
           <a class="act-link" href="${meta.relativeUrl}" download title="下載原曲母帶">原曲</a>` :
          `<button class="act-link btn-denoise" title="一鍵 AI 頻譜自適應降噪 (消除環境底噪、電流聲與金屬毛刺)">🧼 降噪</button>
           <button class="act-link btn-deep-clean" title="二次深度提純：一鍵消除動態串音、樂器滲漏與跨軌殘留 (Deep Bleed Cleaner)">✨ 提純</button>
           <a class="act-link" href="${meta.relativeUrl}" download title="下載 32-bit Float WAV">WAV</a>
           <button class="act-link btn-midi" title="轉出此軌 MIDI 音符檔 (.mid)">🎹 MIDI</button>`
        }
      </div>
    `;

    const chkSelect = row.querySelector(".track-select-chk");
    const btnSolo = row.querySelector(".m-btn.solo");
    const btnMute = row.querySelector(".m-btn.mute");
    const btnInvert = row.querySelector(".m-btn.invert");
    const volSlider = row.querySelector(".vol-slider");
    const panSlider = row.querySelector(".pan-slider");
    const wfWrap = row.querySelector(".track-waveform-wrap");
    const canvas = row.querySelector(".track-waveform-canvas");
    const btnMidi = row.querySelector(".btn-midi");

    const regionsOverlay = row.querySelector(".track-regions-overlay");
    const scissorLine = row.querySelector(".scissor-indicator-line");
    const scissorLabel = row.querySelector(".scissor-indicator-label");

    track.canvas = canvas;
    track.playheadEl = row.querySelector(".track-playhead");
    track.regionsOverlayEl = regionsOverlay;
    track.scissorLineEl = scissorLine;
    track.scissorLabelEl = scissorLabel;

    // 預設全勾選 (原曲參照軌除外)
    if (!meta.isOriginalReference) {
      this.checkedNodeIds.add(track.nodeId);
    }
    this.updateCheckedTracksCount();

    chkSelect.addEventListener("change", () => {
      if (chkSelect.checked) {
        this.checkedNodeIds.add(track.nodeId);
      } else {
        this.checkedNodeIds.delete(track.nodeId);
      }
      this.updateCheckedTracksCount();
    });

    btnSolo.addEventListener("click", () => {
      track.solo = !track.solo;
      btnSolo.classList.toggle("active", track.solo);
      this.updateTrackGains();
    });

    btnMute.addEventListener("click", () => {
      track.mute = !track.mute;
      btnMute.classList.toggle("active", track.mute);
      this.updateTrackGains();
    });

    btnInvert.addEventListener("click", () => {
      track.invertPhase = !track.invertPhase;
      btnInvert.classList.toggle("active", track.invertPhase);
      this.updateTrackGains();
    });

    const updateVolTooltip = (vol) => {
      const db = vol > 0.001 ? (20 * Math.log10(vol)).toFixed(1) : "-∞";
      const sign = db > 0 ? "+" : "";
      volSlider.title = `音量推子: ${sign}${db} dB (${(vol * 100).toFixed(0)}%) [雙擊還原 0 dB]`;
    };
    updateVolTooltip(track.volume ?? 1.0);

    volSlider.addEventListener("input", (e) => {
      track.volume = parseFloat(e.target.value);
      updateVolTooltip(track.volume);
      this.updateTrackGains();
    });

    volSlider.addEventListener("dblclick", () => {
      track.volume = 1.0;
      volSlider.value = 1.0;
      updateVolTooltip(1.0);
      this.updateTrackGains();
    });

    panSlider.addEventListener("input", (e) => {
      track.pan = parseFloat(e.target.value);
      track.panNode.pan.value = track.pan;
    });

    wfWrap.addEventListener("mousemove", (e) => {
      if (this.currentTool === "scissors") {
        const rect = wfWrap.getBoundingClientRect();
        const x = Math.max(0, Math.min(rect.width, e.clientX - rect.left));
        const pct = (x / rect.width) * 100;
        const curSec = (x / rect.width) * this.durationSec;
        scissorLine.style.display = "block";
        scissorLine.style.left = `${pct}%`;
        scissorLabel.textContent = `✂️ ${this.formatTime(curSec)}`;
      } else {
        scissorLine.style.display = "none";
      }
    });

    wfWrap.addEventListener("mouseleave", () => {
      scissorLine.style.display = "none";
    });

    wfWrap.addEventListener("click", (e) => {
      if (e.target.closest(".clip-action-pill")) return;

      const rect = wfWrap.getBoundingClientRect();
      const x = Math.max(0, Math.min(rect.width, e.clientX - rect.left));
      const clickRatio = x / rect.width;
      const clickSec = clickRatio * this.durationSec;

      if (this.currentTool === "scissors") {
        this.splitTrackRegion(track, clickSec);
      } else if (this.currentTool === "mute") {
        const reg = track.regions ? track.regions.find((r) => r.startSec <= clickSec && clickSec <= r.endSec) : null;
        if (reg) {
          this.toggleRegionMute(track, reg.id);
        }
      } else {
        const targetSec = Math.max(0, clickSec - (this.leadTrimSec || 0));
        this.seekTo(targetSec);
      }
    });

    this.renderTrackRegions(track);

    const btnDenoise = row.querySelector(".btn-denoise");
    if (btnDenoise) {
      btnDenoise.addEventListener("click", async () => {
      const originalText = btnDenoise.textContent;
      btnDenoise.textContent = "⏳ 降噪中...";
      btnDenoise.disabled = true;

      try {
        const jobId = this.currentJob ? this.currentJob.jobId : (track.meta.jobId || "");
        if (!jobId) throw new Error("尚未載入有效專案");

        const r = await fetch(`/api/jobs/${jobId}/denoise/${encodeURIComponent(track.nodeId)}`, {
          method: "POST"
        });
        const d = await r.json();
        if (!r.ok) {
          throw new Error(d.error || "降噪失敗");
        }

        track.isDenoised = !!d.isDenoised;
        if (track.isDenoised) {
          btnDenoise.classList.add("active");
          btnDenoise.textContent = "🧼 已降噪 (還原)";
          btnDenoise.title = "已自適應濾除底噪與雜音，點擊可還原為未降噪原音";
        } else {
          btnDenoise.classList.remove("active");
          btnDenoise.textContent = "🧼 降噪";
          btnDenoise.title = "一鍵 AI 頻譜自適應降噪 (消除環境雜音、電流聲與金屬毛刺)";
        }

        // 重新拉取並解碼該軌音訊，更新波形與混音緩衝區
        const curPos = this.getCurrentTimeSec();
        const wasPlaying = this.isPlaying;
        const resAudio = await fetch(`${track.meta.relativeUrl}?t=${Date.now()}`);
        const buf = await resAudio.arrayBuffer();
        track.audioBuffer = await this.audioCtx.decodeAudioData(buf);
        this.drawWaveform(track);

        if (wasPlaying) {
          this.pauseOffsetSec = curPos;
          this.startPlayback();
        }

        this.showToast(`✅ [${track.meta.displayName}] ${d.message || "處理成功"}`);
      } catch (err) {
        alert("降噪處理失敗: " + err.message);
        btnDenoise.textContent = originalText;
      } finally {
        btnDenoise.disabled = false;
      }
    });
    }

    const btnDeepClean = row.querySelector(".btn-deep-clean");
    if (btnDeepClean) {
      btnDeepClean.addEventListener("click", async () => {
        const originalText = btnDeepClean.textContent;
        btnDeepClean.textContent = "⏳ 提純中...";
        btnDeepClean.disabled = true;

        try {
          const jobId = this.currentJob ? this.currentJob.jobId : (track.meta.jobId || "");
          if (!jobId) throw new Error("尚未載入有效專案");

          const r = await fetch(`/api/jobs/${jobId}/deep-clean/${encodeURIComponent(track.nodeId)}`, {
            method: "POST"
          });
          const d = await r.json();
          if (!r.ok || d.error) {
            throw new Error(d.error || "深度提純失敗");
          }

          track.isDeepCleaned = !!d.isDeepCleaned;
          if (track.isDeepCleaned) {
            btnDeepClean.classList.add("active");
            btnDeepClean.textContent = "✨ 已提純";
            btnDeepClean.title = `已深度提純 (純度提升 +${d.noiseReducedDb} dB)，點擊可還原為原音軌`;
          } else {
            btnDeepClean.classList.remove("active");
            btnDeepClean.textContent = "✨ 提純";
            btnDeepClean.title = "二次深度提純：一鍵消除動態串音、樂器滲漏與跨軌殘留 (Deep Bleed Cleaner)";
          }

          // 重新拉取並解碼該軌音訊，更新波形與混音緩衝區
          const curPos = this.getCurrentTimeSec();
          const wasPlaying = this.isPlaying;
          const resAudio = await fetch(`${track.meta.relativeUrl}?t=${Date.now()}`);
          const buf = await resAudio.arrayBuffer();
          track.audioBuffer = await this.audioCtx.decodeAudioData(buf);
          this.drawWaveform(track);

          if (wasPlaying) {
            this.pauseOffsetSec = curPos;
            this.startPlayback();
          }

          this.showToast(`✅ [${track.meta.displayName}] ${d.message || "深度提純處理成功"}`);
        } catch (err) {
          alert("深度提純處理失敗: " + err.message);
          btnDeepClean.textContent = originalText;
        } finally {
          btnDeepClean.disabled = false;
        }
      });
    }

    if (btnMidi) {
    btnMidi.addEventListener("click", async () => {
      btnMidi.textContent = "⏳...";
      btnMidi.disabled = true;
      try {
        const r = await fetch(`/api/jobs/${this.currentJob.jobId}/midi/${encodeURIComponent(track.nodeId)}`, {
          method: "POST"
        });
        const d = await r.json();
        if (d.midiUrl) {
          const a = document.createElement("a");
          a.href = d.midiUrl;
          a.download = d.fileName;
          a.click();
          btnMidi.textContent = "✅ .mid";
        }
      } catch (err) {
        alert("轉 MIDI 失敗: " + err.message);
        btnMidi.textContent = "🎹 MIDI";
      } finally {
        btnMidi.disabled = false;
      }
    });
    }

    this.mixerTracksContainer.appendChild(row);
  }

  drawWaveform(track) {
    const canvas = track.canvas;
    if (!canvas || !canvas.parentElement) return;

    const rect = canvas.parentElement.getBoundingClientRect();
    const w = Math.max(320, Math.floor(rect.width || 600));
    const h = 58;
    canvas.width = w;
    canvas.height = h;

    const ctx = canvas.getContext("2d");
    const midY = h / 2;

    ctx.clearRect(0, 0, w, h);

    // 中心基準線
    ctx.strokeStyle = "rgba(148, 163, 184, 0.2)";
    ctx.beginPath();
    ctx.moveTo(0, midY);
    ctx.lineTo(w, midY);
    ctx.stroke();

    // 直接從 WebAudio 解碼後的 AudioBuffer 取樣並做視覺峰值正規化，確保微細節軌道也能清楚看見瞬態波形
    const buf = track.audioBuffer;
    if (!buf || buf.length === 0) return;

    const ch0 = buf.getChannelData(0);
    const ch1 = buf.numberOfChannels > 1 ? buf.getChannelData(1) : ch0;
    const numBuckets = Math.min(w, 450);
    const step = Math.max(1, Math.floor(ch0.length / numBuckets));

    const bucketPeaks = new Float32Array(numBuckets);
    let globalMax = 0.0001;

    for (let b = 0; b < numBuckets; b++) {
      const start = b * step;
      const end = Math.min(ch0.length, start + step);
      // 抽樣加速長音檔繪製
      const stride = Math.max(1, Math.floor((end - start) / 64));
      let localMax = 0;
      for (let i = start; i < end; i += stride) {
        const v = Math.max(Math.abs(ch0[i]), Math.abs(ch1[i]));
        if (v > localMax) localMax = v;
      }
      bucketPeaks[b] = localMax;
      if (localMax > globalMax) globalMax = localMax;
    }

    ctx.fillStyle = track.meta.colorHex;
    const barWidth = w / numBuckets;

    for (let b = 0; b < numBuckets; b++) {
      const normAmp = Math.min(1.0, bucketPeaks[b] / globalMax);
      const barH = Math.max(2, normAmp * (h * 0.84));
      ctx.fillRect(b * barWidth, midY - barH / 2, Math.max(1.2, barWidth - 0.6), barH);
    }
  }

  updateTrackGains() {
    const anySolo = this.tracks.some((t) => t.solo && !t.hiddenByTree);
    const curTime = this.audioCtx ? this.audioCtx.currentTime : 0;
    const curPos = this.getCurrentTimeSec();

    for (const t of this.tracks) {
      const isAudible = !t.hiddenByTree && (anySolo ? t.solo && !t.mute : !t.mute);
      const sign = t.invertPhase ? -1.0 : 1.0;
      t.rowEl.classList.toggle("muted-effective", !isAudible);

      if (this.isPlaying) {
        this.scheduleTrackRegionGains(t, curTime, curPos);
      } else {
        t.gainNode.gain.value = isAudible ? t.volume * sign : 0.0;
      }
    }
  }

  clearAllSoloMute() {
    for (const t of this.tracks) {
      t.solo = false;
      t.mute = t.meta?.isOriginalReference ? true : false;
      t.invertPhase = false;
      t.rowEl?.querySelector(".m-btn.solo")?.classList.remove("active");
      const mBtn = t.rowEl?.querySelector(".m-btn.mute");
      if (mBtn) mBtn.classList.toggle("active", t.mute);
      t.rowEl?.querySelector(".m-btn.invert")?.classList.remove("active");
    }
    this.isNullTestActive = false;
    if (this.btnNullTest) {
      this.btnNullTest.classList.remove("active");
      this.btnNullTest.textContent = "Ø 反相抵消測試 (Null Test: OFF)";
    }
    this.updateTrackGains();
  }

  toggleNullTest() {
    this.isNullTestActive = !this.isNullTestActive;
    this.btnNullTest.classList.toggle("active", this.isNullTestActive);
    this.btnNullTest.textContent = this.isNullTestActive
      ? "Ø 反相抵消測試 (Null Test: ON — 原曲反相疊加中)"
      : "Ø 反相抵消測試 (Null Test: OFF)";

    const refTrack = this.tracks.find((t) => t.meta?.isOriginalReference);
    if (refTrack) {
      refTrack.mute = !this.isNullTestActive;
      refTrack.invertPhase = this.isNullTestActive;
      if (refTrack.rowEl) {
        const mBtn = refTrack.rowEl.querySelector(".m-btn.mute");
        if (mBtn) mBtn.classList.toggle("active", refTrack.mute);
        const invBtn = refTrack.rowEl.querySelector(".m-btn.invert");
        if (invBtn) invBtn.classList.toggle("active", refTrack.invertPhase);
      }
    }

    const resTrack = this.tracks.find((t) => t.nodeId.startsWith("STEM_19_RESIDUAL"));
    if (resTrack) {
      resTrack.mute = !this.isNullTestActive;
      const mBtn = resTrack.rowEl?.querySelector(".m-btn.mute");
      if (mBtn) mBtn.classList.toggle("active", resTrack.mute);
    }

    this.updateTrackGains();
  }

  togglePlayPause() {
    if (this.isPlaying) {
      this.pausePlayback();
    } else {
      this.startPlayback();
    }
  }

  getCurrentBpm() {
    if (this.currentBpm && this.currentBpm > 30) return this.currentBpm;
    if (this.originalBpm && this.originalBpm > 30) return this.originalBpm;
    if (this.tempoSlider && parseInt(this.tempoSlider.value, 10) > 30) {
      return parseInt(this.tempoSlider.value, 10);
    }
    return 120;
  }

  playMetronomeClick(time, isAccent = false) {
    if (!this.audioCtx || this.audioCtx.state === "closed") return;
    try {
      const osc = this.audioCtx.createOscillator();
      const gain = this.audioCtx.createGain();

      // 第一拍重音為高頻清脆 Ping (1760Hz - A6)，其餘拍次為飽滿木質敲擊 Tok (880Hz - A5)
      const baseFreq = isAccent ? 1760 : 880;
      osc.type = "sine";
      osc.frequency.setValueAtTime(baseFreq, time);
      osc.frequency.exponentialRampToValueAtTime(baseFreq * 0.4, time + 0.035);

      const vol = isAccent ? 0.85 : 0.55;
      gain.gain.setValueAtTime(vol, time);
      gain.gain.exponentialRampToValueAtTime(0.0001, time + (isAccent ? 0.045 : 0.03));

      osc.connect(gain);
      gain.connect(this.masterGainNode || this.audioCtx.destination);

      osc.start(time);
      osc.stop(time + 0.05);
      this.activeSources.push(osc);
    } catch (err) {
      console.warn("節拍器發聲失敗:", err);
    }
  }

  flashMetroDot(isAccent, beatNumber = null) {
    if (!this.metroBeatIndicator) return;
    this.metroBeatIndicator.className = isAccent ? "metro-beat-indicator accent" : "metro-beat-indicator beat";
    if (beatNumber !== null) {
      this.metroBeatIndicator.textContent = beatNumber;
    }
    setTimeout(() => {
      if (this.metroBeatIndicator) {
        this.metroBeatIndicator.className = "metro-beat-indicator";
        this.metroBeatIndicator.textContent = "●";
      }
    }, 120);
  }

  startMetronomeScheduler(syncStartAt, startOffset, beatSec) {
    this.clearMetronome();

    let currentBeatIdx = Math.round(startOffset / beatSec);
    let nextBeatTime = syncStartAt;

    this.metronomeInterval = setInterval(() => {
      if (!this.isPlaying || !this.audioCtx) return;
      const lookaheadLimit = this.audioCtx.currentTime + 0.25;

      while (nextBeatTime < lookaheadLimit) {
        if (nextBeatTime >= this.audioCtx.currentTime - 0.03) {
          const measureBeat = ((currentBeatIdx % 4) + 4) % 4;
          const isAccent = (measureBeat === 0);
          this.playMetronomeClick(nextBeatTime, isAccent);

          const delayMs = Math.max(0, (nextBeatTime - this.audioCtx.currentTime) * 1000);
          const beatNum = measureBeat + 1;
          setTimeout(() => {
            if (this.isPlaying) {
              this.flashMetroDot(isAccent, beatNum);
            }
          }, delayMs);
        }
        nextBeatTime += beatSec;
        currentBeatIdx++;
      }
    }, 50);
  }

  clearMetronome() {
    if (this.metronomeInterval) {
      clearInterval(this.metronomeInterval);
      this.metronomeInterval = null;
    }
    if (this.countInTimeouts && this.countInTimeouts.length > 0) {
      for (const tid of this.countInTimeouts) clearTimeout(tid);
      this.countInTimeouts = [];
    }
    if (this.metroBeatIndicator) {
      this.metroBeatIndicator.className = "metro-beat-indicator";
      this.metroBeatIndicator.textContent = "●";
    }
  }

  startPlayback() {
    if (this.tracks.length === 0) return;
    this.ensureAudioContext();
    this.stopActiveSourcesOnly();
    this.clearMetronome();

    const maxPlayable = Math.max(0, this.durationSec - (this.leadTrimSec || 0));
    const offset = this.pauseOffsetSec >= maxPlayable ? 0 : this.pauseOffsetSec;
    this.pauseOffsetSec = offset;

    const bpm = this.getCurrentBpm();
    const beatSec = 60.0 / bpm;
    const hasCountIn = this.chkCountIn && this.chkCountIn.checked;
    const hasMetronome = this.chkMetronome && this.chkMetronome.checked;

    // 弱起拍（Pickup）進歌拍數設定：正拍(0), 搶半拍(0.5), 第4拍(1), 第3拍(2)
    const pickupBeats = (offset < 0.05 && hasCountIn && this.selCountInPickup)
      ? (parseFloat(this.selCountInPickup.value) || 0)
      : 0;

    let syncStartAt = this.audioCtx.currentTime + 0.04;
    let downbeatTime = syncStartAt;

    if (hasCountIn) {
      // ⏳ 前面空一小節 (4 拍預備拍)
      const countInStartAt = syncStartAt;
      const barSec = beatSec * 4;
      downbeatTime = countInStartAt + barSec;

      // 音樂進場發聲時間點：若為弱起 1 拍，則提早在第 4 拍發聲 (countInStartAt + 3 * beatSec)
      syncStartAt = downbeatTime - (pickupBeats * beatSec);

      for (let b = 0; b < 4; b++) {
        const clickTime = countInStartAt + b * beatSec;
        const isAccent = (b === 0);
        this.playMetronomeClick(clickTime, isAccent);

        const delayMs = Math.max(0, (clickTime - this.audioCtx.currentTime) * 1000);
        const beatNum = b + 1;
        const tid = setTimeout(() => {
          if (!this.isPlaying) return;
          this.flashMetroDot(isAccent, beatNum);
          const timecodeEl = document.getElementById("currentTimeText");
          if (timecodeEl) {
            if (pickupBeats > 0 && beatNum > (4 - Math.ceil(pickupBeats))) {
              timecodeEl.textContent = `⏳ 預拍 [${beatNum}/4] 🎵 進歌!`;
            } else {
              timecodeEl.textContent = `⏳ 預拍 [${beatNum}/4]`;
            }
          }
        }, delayMs);
        this.countInTimeouts.push(tid);
      }
    }

    this.startTimeCtx = syncStartAt - offset;
    const effectiveAudioStart = (this.leadTrimSec || 0) + offset;

    for (const t of this.tracks) {
      const src = this.audioCtx.createBufferSource();
      src.buffer = t.audioBuffer;
      src.connect(t.panNode);
      src.start(syncStartAt, effectiveAudioStart);
      this.activeSources.push(src);
      this.scheduleTrackRegionGains(t, syncStartAt, effectiveAudioStart);
    }

    // 原曲母帶已作為 Track 0 納入 WebAudio 混音軌陣列 (由上方迴圈同步播放)

    this.updateTrackGains();
    this.isPlaying = true;
    this.btnPlayPause.textContent = "⏸ 暫停 (Pause)";
    this.btnPlayPause.classList.add("playing");

    if (hasMetronome) {
      const metroStartAt = hasCountIn ? downbeatTime : syncStartAt;
      this.startMetronomeScheduler(metroStartAt, offset, beatSec);
    }

    this.tickPlayhead();
  }

  pausePlayback() {
    if (!this.isPlaying) return;
    this.pauseOffsetSec = this.getCurrentTimeSec();
    this.stopActiveSourcesOnly();
    this.clearMetronome();
    this.isPlaying = false;
    this.btnPlayPause.textContent = "▶ 播放 (Play)";
    this.btnPlayPause.classList.remove("playing");
  }

  stopPlayback() {
    this.stopActiveSourcesOnly();
    this.clearMetronome();
    this.isPlaying = false;
    this.pauseOffsetSec = 0;
    this.btnPlayPause.textContent = "▶ 播放 (Play)";
    this.btnPlayPause.classList.remove("playing");
    this.updatePlayheadUi(0);
  }

  seekTo(targetSec) {
    const maxPlayable = Math.max(0, this.durationSec - (this.leadTrimSec || 0));
    const clamped = Math.max(0, Math.min(maxPlayable, targetSec));
    this.pauseOffsetSec = clamped;
    this.updatePlayheadUi(clamped);
    if (this.isPlaying) {
      this.startPlayback();
    }
  }

  stopActiveSourcesOnly() {
    if (this.rafId) {
      cancelAnimationFrame(this.rafId);
      this.rafId = null;
    }
    for (const s of this.activeSources) {
      try {
        s.stop();
        s.disconnect();
      } catch {}
    }
    this.activeSources = [];
  }

  getCurrentPlaybackTime() {
    return this.getCurrentTimeSec();
  }

  getCurrentTimeSec() {
    if (!this.isPlaying || !this.audioCtx) return this.pauseOffsetSec;
    const t = this.audioCtx.currentTime - this.startTimeCtx;
    if (t < this.pauseOffsetSec) {
      return this.pauseOffsetSec;
    }
    return Math.max(0, t);
  }

  tickPlayhead() {
    if (!this.isPlaying) return;
    const cur = this.getCurrentTimeSec();
    const maxPlayable = Math.max(0, this.durationSec - (this.leadTrimSec || 0));
    if (cur >= maxPlayable) {
      this.stopPlayback();
      return;
    }
    // 若在預拍期間，保留 ⏳ 預拍 [X/4] 視覺，不立即被時間碼數字刷掉
    const isCountingIn = this.audioCtx && (this.audioCtx.currentTime < this.startTimeCtx + this.pauseOffsetSec);
    if (!isCountingIn) {
      this.updatePlayheadUi(cur);
    }
    this.rafId = requestAnimationFrame(() => this.tickPlayhead());
  }

  updatePlayheadUi(sec) {
    const maxPlayable = Math.max(0, this.durationSec - (this.leadTrimSec || 0));
    const curTimeEl = document.getElementById("currentTimeText");
    const totalTimeEl = document.getElementById("totalTimeText");
    if (curTimeEl) curTimeEl.textContent = this.formatTime(sec);
    if (totalTimeEl) totalTimeEl.textContent = this.formatTime(maxPlayable);

    const globalSec = (this.leadTrimSec || 0) + sec;
    const pct = this.durationSec > 0 ? Math.min(100, (globalSec / this.durationSec) * 100) : 0;
    const scrubberProgress = document.getElementById("scrubberProgress");
    const scrubberHead = document.getElementById("scrubberHead");
    if (scrubberProgress) scrubberProgress.style.width = `${pct}%`;
    if (scrubberHead) scrubberHead.style.left = `${pct}%`;

    for (const t of this.tracks) {
      if (t.playheadEl) {
        t.playheadEl.style.left = `${pct}%`;
      }
    }
  }

  updatePlayheadPosition(sec) {
    this.updatePlayheadUi(sec);
  }

  updateCheckedTracksCount() {
    const count = this.checkedNodeIds ? this.checkedNodeIds.size : 0;
    if (this.selectedTracksCountText) {
      this.selectedTracksCountText.textContent = count;
    }
    const hasActiveJob = !!this.currentJob && count > 0;

    if (this.btnExportSelectedZip) {
      this.btnExportSelectedZip.disabled = !hasActiveJob;
      this.btnExportSelectedZip.innerHTML = `📥 下載已勾選分軌 ZIP (<span id="selectedTracksCountText">${count}</span>)`;
      this.selectedTracksCountText = document.getElementById("selectedTracksCountText");
    }
    const hasCountIn = this.chkCountIn && this.chkCountIn.checked;
    const countInLabel = hasCountIn ? " + 預拍" : "";
    if (this.btnExportMixdownWav) {
      this.btnExportMixdownWav.disabled = !hasActiveJob;
      this.btnExportMixdownWav.innerHTML = `🎚️ 合併匯出 WAV (<span id="mixdownWavCountText">${count} 軌${countInLabel}</span>)`;
      this.mixdownWavCountText = document.getElementById("mixdownWavCountText");
    }
    if (this.btnExportMixdownMp3) {
      this.btnExportMixdownMp3.disabled = !hasActiveJob;
      this.btnExportMixdownMp3.innerHTML = `🎵 合併匯出 MP3 (<span id="mixdownMp3CountText">${count} 軌${countInLabel}</span>)`;
      this.mixdownMp3CountText = document.getElementById("mixdownMp3CountText");
    }
  }

  selectAllTracks(isSelected) {
    if (!this.tracks || this.tracks.length === 0) return;
    if (isSelected) {
      for (const t of this.tracks) {
        if (t.meta?.isOriginalReference) continue;
        this.checkedNodeIds.add(t.nodeId);
        if (t.rowEl) {
          const chk = t.rowEl.querySelector(".track-select-chk");
          if (chk) chk.checked = true;
        }
      }
    } else {
      this.checkedNodeIds.clear();
      for (const t of this.tracks) {
        if (t.rowEl) {
          const chk = t.rowEl.querySelector(".track-select-chk");
          if (chk) chk.checked = false;
        }
      }
    }
    this.updateCheckedTracksCount();
  }

  async exportMixdown(format = "wav") {
    if (!this.currentJob || this.checkedNodeIds.size === 0) return;
    const isMp3 = format === "mp3";
    const targetBtn = isMp3 ? this.btnExportMixdownMp3 : this.btnExportMixdownWav;
    const origHtml = targetBtn ? targetBtn.innerHTML : "";
    if (targetBtn) {
      targetBtn.disabled = true;
      targetBtn.textContent = isMp3 ? "⏳ MP3 轉碼中..." : "⏳ WAV 混音中...";
    }

    const hasCountIn = this.chkCountIn && this.chkCountIn.checked;
    const pickupBeats = (hasCountIn && this.selCountInPickup) ? (parseFloat(this.selCountInPickup.value) || 0) : 0;
    const bpm = this.getCurrentBpm() || 120;
    const semitones = parseInt(this.pitchShiftSlider?.value || 0, 10) || (this.currentJob?.currentPitchShift || 0) || 0;
    const signText = semitones > 0 ? `+${semitones} 半音` : (semitones < 0 ? `${semitones} 半音` : "");

    this.showBusyOverlay(
      isMp3 ? "🎵 正在合併匯出 MP3..." : "🎚️ 正在合併匯出 WAV...",
      `正在將已選取的 ${this.checkedNodeIds.size} 條音軌${semitones !== 0 ? ` [${signText}]` : ""}${hasCountIn ? `（含 1 小節節拍器預拍，BPM: ${bpm}）` : ""}進行 32-bit Float 數位總線混音與編碼...`,
      45,
      "🎚️"
    );

    try {
      const mutedRanges = {};
      for (const t of this.tracks) {
        if (t.regions) {
          const mList = t.regions.filter((r) => r.muted).map((r) => ({ startSec: r.startSec, endSec: r.endSec }));
          if (mList.length > 0) {
            mutedRanges[t.nodeId] = mList;
          }
        }
      }
      const trackVolumes = {};
      for (const t of this.tracks) {
        trackVolumes[t.nodeId] = t.volume ?? 1.0;
      }
      const payload = {
        selectedNodes: Array.from(this.checkedNodeIds),
        format: format,
        mutedRanges: mutedRanges,
        trackVolumes: trackVolumes,
        trimStartSec: this.leadTrimSec || 0,
        hasCountIn: hasCountIn,
        pickupBeats: pickupBeats,
        bpm: bpm,
        semitones: semitones
      };
      const r = await fetch(`/api/jobs/${this.currentJob.jobId}/mixdown`, {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify(payload)
      });
      const d = await r.json();
      if (!r.ok) throw new Error(d.error || "合併混音失敗");
      if (d.url) {
        const a = document.createElement("a");
        a.href = d.url;
        a.download = d.fileName;
        document.body.appendChild(a);
        a.click();
        a.remove();
      }
    } catch (err) {
      alert(`合併匯出失敗: ${err.message}`);
    } finally {
      this.hideBusyOverlay();
      if (targetBtn) {
        targetBtn.disabled = false;
        targetBtn.innerHTML = origHtml;
      }
      this.updateCheckedTracksCount();
    }
  }

  async exportFullZip() {
    if (!this.currentJob) return;
    this.btnExportZip.disabled = true;
    this.btnExportZip.textContent = "⏳ 打包中...";
    this.showBusyOverlay("📦 正在打包全套分軌 ZIP...", "正在封裝 32-bit Float WAV + Reaper (.rpp) 工程檔...", 50, "📦");
    try {
      const r = await fetch(`/api/jobs/${this.currentJob.jobId}/export-zip`, { method: "POST" });
      const d = await r.json();
      if (d.zipUrl) {
        const a = document.createElement("a");
        a.href = d.zipUrl;
        a.download = d.fileName;
        a.click();
      }
    } finally {
      this.hideBusyOverlay();
      this.btnExportZip.disabled = false;

    if (this.btnSaveProject) this.btnSaveProject.disabled = false;
    if (this.btnSaveAsProject) this.btnSaveAsProject.disabled = false;
    if (this.btnSelectAllTracks) this.btnSelectAllTracks.disabled = false;
    if (this.btnClearAllTracks) this.btnClearAllTracks.disabled = false;
      this.btnExportZip.textContent = "📦 打包全軌 ZIP";
    }
  }

  async exportSelectedZip() {
    if (!this.currentJob || this.checkedNodeIds.size === 0) return;
    this.btnExportSelectedZip.disabled = true;
    this.btnExportSelectedZip.textContent = `⏳ 正在打包 ${this.checkedNodeIds.size} 軌...`;
    this.showBusyOverlay("📥 正在打包選取分軌 ZIP...", `正在封裝 ${this.checkedNodeIds.size} 條音軌與 Reaper 工程檔...`, 50, "📥");
    try {
      const payload = { selectedNodes: Array.from(this.checkedNodeIds) };
      const r = await fetch(`/api/jobs/${this.currentJob.jobId}/export-zip`, {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify(payload)
      });
      const d = await r.json();
      if (d.zipUrl) {
        const a = document.createElement("a");
        a.href = d.zipUrl;
        a.download = d.fileName;
        a.click();
      }
    } finally {
      this.hideBusyOverlay();
      this.btnExportSelectedZip.disabled = false;
      this.updateCheckedTracksCount();
    }
  }

  async openHistoryModal() {
    if (!this.historyModal) return;
    this.historyModal.classList.add("active");
    await this.renderHistoryList();
  }

  async renderHistoryList() {
    if (!this.historyListContainer) return;
    this.historyListContainer.innerHTML = `<div class="empty-history-text" style="padding: 20px; text-align: center; color: #94a3b8;">正在掃描本機已處理的專案...</div>`;

    try {
      const res = await fetch(`/api/projects/history?t=${Date.now()}`);
      const list = await res.json();
      if (!list || list.length === 0) {
        this.historyListContainer.innerHTML = `<div class="empty-history-text" style="padding: 20px; text-align: center; color: #94a3b8;">目前尚未有任何已分軌的歷史專案</div>`;
        return;
      }

      this.historyListContainer.innerHTML = "";
      for (const item of list) {
        const row = document.createElement("div");
        row.className = "history-item";
        const dateStr = new Date(item.createdAt).toLocaleString();
        row.innerHTML = `
          <div class="history-item-info">
            <div class="history-item-name" title="${item.originalFileName}">🎵 ${item.originalFileName}</div>
            <div class="history-item-meta">時間: ${dateStr} | 共 ${item.stemCount} 條已分離音軌</div>
          </div>
          <div class="history-item-actions">
            <button class="btn-load-item" data-id="${item.jobId}">⚡ 載入混音台</button>
            <button class="btn-delete-item" data-id="${item.jobId}" title="刪除此歷史專案與所有分軌檔案">🗑️ 刪除</button>
          </div>
        `;

        row.querySelector(".btn-load-item").addEventListener("click", async () => {
          this.closeHistoryModal();
          await this.loadHistoryProject(item.jobId);
        });

        row.querySelector(".btn-delete-item").addEventListener("click", async (e) => {
          e.stopPropagation();
          const confirmed = confirm(`確定要徹底刪除專案【${item.originalFileName}】嗎？\n此操作將永久清除該專案所有已分離的音軌檔案。`);
          if (!confirmed) return;

          const btnDel = row.querySelector(".btn-delete-item");
          btnDel.disabled = true;
          btnDel.textContent = "⏳...";

          try {
            const delRes = await fetch(`/api/projects/${item.jobId}`, { method: "DELETE" });
            if (!delRes.ok) {
              const errData = await delRes.json();
              throw new Error(errData.error || "刪除失敗");
            }
            row.style.transition = "all 0.3s ease";
            row.style.opacity = "0";
            row.style.transform = "translateX(30px)";
            setTimeout(() => {
              row.remove();
              if (this.historyListContainer.querySelectorAll(".history-item").length === 0) {
                this.historyListContainer.innerHTML = `<div class="empty-history-text" style="padding: 20px; text-align: center; color: #94a3b8;">目前尚未有任何已分軌的歷史專案</div>`;
              }
            }, 300);
          } catch (err) {
            alert("刪除專案失敗: " + err.message);
            btnDel.disabled = false;
            btnDel.textContent = "🗑️ 刪除";
          }
        });

        this.historyListContainer.appendChild(row);
      }
    } catch (err) {
      this.historyListContainer.innerHTML = `<div class="empty-history-text" style="color: #ef4444; padding: 20px; text-align: center;">載入失敗: ${err.message}</div>`;
    }
  }

  closeHistoryModal() {
    if (this.historyModal) {
      this.historyModal.classList.remove("active");
    }
  }

  async loadHistoryProject(jobId) {
    try {
      this.mixerTracksContainer.innerHTML = `<div class="empty-mixer-state">正在載入歷史專案 [${jobId}]...</div>`;
      const res = await fetch(`/api/projects/${jobId}/load?t=${Date.now()}`);
      if (!res.ok) throw new Error("找不到該歷史專案檔案");
      const job = await res.json();
      await this.loadJobIntoMixer(job);
    } catch (err) {
      alert("載入歷史專案失敗: " + err.message);
    }
  }

  stepPitchUi(delta) {
    if (!this.pitchShiftSlider) return;
    const cur = parseInt(this.pitchShiftSlider.value, 10) || 0;
    const next = Math.max(-6, Math.min(6, cur + delta));
    if (next !== cur) {
      this.pitchShiftSlider.value = next;
      this.updatePitchUiOnly(next);
    }
  }

  async handleManualKeyOverride(compositeVal) {
    if (!compositeVal || !this.currentJob) return;
    const [key, displayName] = compositeVal.split("|");
    if (!key || !displayName) return;

    this.currentJob.detectedKey = key;
    this.currentJob.detectedKeyDisplayName = displayName;

    if (this.detectedKeyBadge) {
      this.detectedKeyBadge.innerHTML = `原調: ${displayName} ▾ <span style="font-size: 10px; color: #38bdf8;">[點擊修改]</span>`;
    }

    const curPitch = parseInt(this.pitchShiftSlider?.value || "0", 10);
    this.updatePitchUiOnly(curPitch);

    this.showToast(`🎵 已手動校正原調為：${displayName}`);

    try {
      await fetch(`/api/jobs/${this.currentJob.jobId}/override-key`, {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({ key, displayName })
      });
    } catch (err) {
      console.warn("Key override sync failed:", err);
    }
  }

  updatePitchUiOnly(val) {
    if (this.pitchValueLabel) {
      this.pitchValueLabel.textContent = val === 0 ? "0 半音" : `${val > 0 ? "+" + val : val} 半音`;
    }
    if (this.targetKeyBadge && this.currentJob) {
      const orig = this.currentJob.detectedKey || "C Major";
      this.targetKeyBadge.textContent = val === 0 ? "目標: 原調 (0)" : `目標: ${this.calcTransposedKey(orig, val)}`;
    }
  }

  calcTransposedKey(origKey, semitones) {
    const pitches = ["C", "C#", "D", "D#", "E", "F", "F#", "G", "G#", "A", "A#", "B"];
    const zhPitches = ["C", "C# / Db", "D", "D# / Eb", "E", "F", "F# / Gb", "G", "G# / Ab", "A", "A# / Bb", "B"];
    const parts = origKey.split(" ");
    if (parts.length < 2) return origKey;
    const root = parts[0];
    const mode = parts[1];
    const idx = pitches.findIndex((p) => p.toLowerCase() === root.toLowerCase());
    if (idx < 0) return origKey;
    let newIdx = (idx + semitones) % 12;
    if (newIdx < 0) newIdx += 12;
    const modeZh = mode.toLowerCase() === "minor" ? "小調" : "大調";
    const sign = semitones > 0 ? `+${semitones}` : `${semitones}`;
    return `${zhPitches[newIdx]} ${modeZh} [${sign}]`;
  }

  async applyPitchShift(semitones) {
    if (!this.currentJob) return;
    semitones = Math.max(-6, Math.min(6, semitones));
    if (this.pitchShiftSlider) this.pitchShiftSlider.value = semitones;
    this.updatePitchUiOnly(semitones);

    const wasPlaying = this.isPlaying;
    const curPos = this.getCurrentTimeSec();
    this.pausePlayback();

    const bannerStage = document.getElementById("progressStageTag");
    const bannerTitle = document.getElementById("progressTitleText");
    const bannerSub = document.getElementById("progressSubText");
    const signText = semitones > 0 ? `+${semitones}` : `${semitones}`;

    if (bannerStage) bannerStage.textContent = "PITCH SHIFT";
    if (bannerTitle) bannerTitle.textContent = `[Phase-Vocoder] 正在執行多軌神經變調 (${signText} 半音)...`;
    if (bannerSub) bannerSub.textContent = "鼓組自動繞過保留衝擊力，人聲鎖定共振峰，伴奏和弦高保真變調";

    if (this.btnApplyPitch) {
      this.btnApplyPitch.disabled = true;
      this.btnApplyPitch.textContent = "⏳ 變調中...";
    }

    const overlayTitle = semitones === 0 ? "⟲ 正在恢復原調..." : `⚡ 正在進行多軌音訊變調 (${signText} 半音)...`;
    const overlaySub = semitones === 0
      ? "正在重新載入與解碼原始音訊軌道..."
      : "鼓組原音保留衝擊力，人聲與樂器 Phase-Vocoder 高保真變調中...";

    this.showBusyOverlay(overlayTitle, overlaySub, 5, "🎵");

    try {
      const res = await fetch(`/api/jobs/${this.currentJob.jobId}/pitch-shift`, {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({ semitones })
      });
      const data = await res.json();
      if (!res.ok) throw new Error(data.error || "變調失敗");

      this.updateBusyOverlayProgress(90, "正在更新混音台音訊...", "載入最新音軌緩衝區並重新繪製波形...");

      if (this.targetKeyBadge) {
        this.targetKeyBadge.textContent = semitones === 0 ? "目標: 原調 (0)" : `目標: ${data.targetKey}`;
      }

      // 平行重新解碼變調後的各軌音訊並更新下載連結
      await Promise.all(
        data.stems.map(async (stem) => {
          const track = this.tracks.find((t) => t.nodeId === stem.nodeId);
          if (track) {
            track.meta.relativeUrl = stem.relativeUrl;
            const link = track.rowEl?.querySelector("a.act-link");
            if (link) link.href = stem.relativeUrl;
            const r = await fetch(`${stem.relativeUrl}?t=${Date.now()}`);
            const buf = await r.arrayBuffer();
            track.audioBuffer = await this.audioCtx.decodeAudioData(buf);
            this.drawWaveform(track);
          }
        })
      );

      if (this.currentJob) {
        this.currentJob.stems = data.stems;
        this.currentJob.currentPitchShift = semitones;
      }

      if (bannerStage) bannerStage.textContent = "READY";
      if (bannerTitle) bannerTitle.textContent = `神經變調完成！當前調性: ${data.targetKey}`;
      if (bannerSub) bannerSub.textContent = "可隨時拉動升降桿或點擊「⟲ 恢復原調」即時切換";

      this.pauseOffsetSec = curPos;
      if (wasPlaying) {
        this.startPlayback();
      }
    } catch (err) {
      alert(`變調失敗: ${err.message}`);
    } finally {
      this.hideBusyOverlay();
      if (this.btnApplyPitch) {
        this.btnApplyPitch.disabled = false;
        this.btnApplyPitch.textContent = "⚡ 套用變調";
      }
    }
  }

  stepTempoUi(delta) {
    if (!this.tempoSlider) return;
    const cur = parseInt(this.tempoSlider.value, 10) || this.originalBpm || 120;
    const minVal = parseInt(this.tempoSlider.min, 10) || 40;
    const maxVal = parseInt(this.tempoSlider.max, 10) || 260;
    const next = Math.max(minVal, Math.min(maxVal, cur + delta));
    if (next !== cur) {
      this.tempoSlider.value = next;
      this.updateTempoUiOnly(next);
    }
  }

  updateTempoUiOnly(targetBpm) {
    const origBpm = this.originalBpm || 120;
    this.currentBpm = targetBpm;
    const ratio = targetBpm / origBpm;
    const pctDiff = ((ratio - 1.0) * 100).toFixed(1);
    const sign = pctDiff > 0 ? "+" : "";

    if (this.tempoValueLabel) {
      this.tempoValueLabel.textContent = `${targetBpm} BPM`;
    }
    if (this.targetBpmBadge) {
      if (Math.abs(ratio - 1.0) < 0.005) {
        this.targetBpmBadge.textContent = "目標: 原速";
      } else {
        this.targetBpmBadge.textContent = `目標: ${targetBpm} BPM (${sign}${pctDiff}%)`;
      }
    }
  }

  async applyTempoShift(targetBpm) {
    if (!this.currentJob) return;
    const origBpm = this.originalBpm || 120;
    targetBpm = Math.max(40, Math.min(260, targetBpm));
    if (this.tempoSlider) this.tempoSlider.value = targetBpm;
    this.updateTempoUiOnly(targetBpm);

    const ratio = targetBpm / origBpm;
    const isReset = Math.abs(ratio - 1.0) < 0.005;

    const wasPlaying = this.isPlaying;
    const curPos = this.getCurrentTimeSec();
    this.pausePlayback();

    const bannerStage = document.getElementById("progressStageTag");
    const bannerTitle = document.getElementById("progressTitleText");
    const bannerSub = document.getElementById("progressSubText");

    const pctDiff = ((ratio - 1.0) * 100).toFixed(1);
    const signText = pctDiff > 0 ? `+${pctDiff}%` : `${pctDiff}%`;

    if (bannerStage) bannerStage.textContent = "TIME STRETCH";
    if (bannerTitle) bannerTitle.textContent = `[Phase-Vocoder] 正在執行 21 軌無損時間軸變速 (${targetBpm} BPM / ${signText})...`;
    if (bannerSub) bannerSub.textContent = "所有分軌（含鼓組）等比例縮放時間軸，音高 100% 鎖定不動，零頻率失真";

    if (this.btnApplyTempo) {
      this.btnApplyTempo.disabled = true;
      this.btnApplyTempo.textContent = "⏳ 變速中...";
    }

    const overlayTitle = isReset
      ? "⟲ 正在恢復原始拍速..."
      : `⚡ 正在進行 21 軌無損時間軸變速 (${targetBpm} BPM / ${signText})...`;
    const overlaySub = isReset
      ? "正在重新載入與解碼原始音訊軌道..."
      : "所有軌道（含鼓組）等比例縮放時間軸，音高 100% 鎖定不動...";

    this.showBusyOverlay(overlayTitle, overlaySub, 5, "⏱️");

    try {
      const res = await fetch(`/api/jobs/${this.currentJob.jobId}/time-stretch`, {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({ targetBpm, tempoRatio: ratio })
      });
      const data = await res.json();
      if (!res.ok) throw new Error(data.error || "變速失敗");

      this.updateBusyOverlayProgress(85, "正在更新混音台音訊...", "載入最新音軌緩衝區並重新計算時間軸長度...");

      if (this.targetBpmBadge) {
        this.targetBpmBadge.textContent = isReset ? "目標: 原速" : `目標: ${targetBpm} BPM (${signText})`;
      }

      // 平行重新解碼變速後的各軌音訊並更新下載連結
      await Promise.all(
        data.stems.map(async (stem) => {
          const track = this.tracks.find((t) => t.nodeId === stem.nodeId);
          if (track) {
            track.meta.relativeUrl = stem.relativeUrl;
            const link = track.rowEl?.querySelector("a.act-link");
            if (link) link.href = stem.relativeUrl;
            const r = await fetch(`${stem.relativeUrl}?t=${Date.now()}`);
            const buf = await r.arrayBuffer();
            track.audioBuffer = await this.audioCtx.decodeAudioData(buf);
          }
        })
      );

      // 重新計算歌曲總時長 (變速後長度改變)
      let maxDur = 0;
      for (const t of this.tracks) {
        if (t.audioBuffer && t.audioBuffer.duration > maxDur) {
          maxDur = t.audioBuffer.duration;
        }
      }
      if (maxDur > 0) {
        this.durationSec = maxDur;
        const totalTimeEl = document.getElementById("totalTimeText");
        if (totalTimeEl) totalTimeEl.textContent = this.formatTime(this.durationSec);
      }

      // 重新繪製 21 軌波形 Canvas (因為時間長度已自適應改變)
      for (const track of this.tracks) {
        this.drawWaveform(track);
      }

      if (this.currentJob) {
        this.currentJob.stems = data.stems;
        this.currentJob.currentTempoRatio = ratio;
        this.currentJob.targetBpm = targetBpm;
      }
      this.currentBpm = targetBpm;

      if (bannerStage) bannerStage.textContent = "READY";
      if (bannerTitle) bannerTitle.textContent = `拍速調整完成！當前拍速: ${targetBpm} BPM`;
      if (bannerSub) bannerSub.textContent = "可隨時拉動滑桿或點擊「⟲ 恢復原速」即時切換";

      // 縮放播放頭位置以保持在歌曲相對位置
      const newPos = isReset ? curPos : curPos / ratio;
      this.pauseOffsetSec = Math.min(newPos, this.durationSec);
      this.updatePlayheadUi(this.pauseOffsetSec);

      if (wasPlaying) {
        this.startPlayback();
      }
    } catch (err) {
      alert(`變速失敗: ${err.message}`);
    } finally {
      this.hideBusyOverlay();
      if (this.btnApplyTempo) {
        this.btnApplyTempo.disabled = false;
        this.btnApplyTempo.textContent = "⚡ 套用新拍速";
      }
    }
  }

  // ============================================================
  // ✂️ 編曲剪輯與片段管理 (Arrangement Clip & Region Management)
  // ============================================================
  setToolMode(mode) {
    this.currentTool = mode;
    document.body.classList.remove("tool-mode-scissors", "tool-mode-mute");
    if (mode === "scissors") document.body.classList.add("tool-mode-scissors");
    if (mode === "mute") document.body.classList.add("tool-mode-mute");

    if (this.btnToolPointer) this.btnToolPointer.classList.toggle("active", mode === "pointer");
    if (this.btnToolScissors) this.btnToolScissors.classList.toggle("active", mode === "scissors");
    if (this.btnToolMute) this.btnToolMute.classList.toggle("active", mode === "mute");
  }

  splitTrackRegion(track, splitSec) {
    if (!track.regions || splitSec <= 0.1 || splitSec >= this.durationSec - 0.1) return;
    const targetIdx = track.regions.findIndex((r) => r.startSec < splitSec && splitSec < r.endSec);
    if (targetIdx < 0) return;

    const r = track.regions[targetIdx];
    if (splitSec - r.startSec < 0.08 || r.endSec - splitSec < 0.08) return;

    const r1 = {
      id: track.nextRegionId++,
      startSec: r.startSec,
      endSec: splitSec,
      muted: r.muted
    };
    const r2 = {
      id: track.nextRegionId++,
      startSec: splitSec,
      endSec: r.endSec,
      muted: r.muted
    };

    track.regions.splice(targetIdx, 1, r1, r2);
    this.renderTrackRegions(track);

    if (this.isPlaying) {
      this.scheduleTrackRegionGains(track, this.audioCtx.currentTime, this.getCurrentTimeSec());
    }
  }

  toggleRegionMute(track, regionId) {
    const reg = track.regions ? track.regions.find((r) => r.id === regionId) : null;
    if (!reg) return;
    reg.muted = !reg.muted;
    this.renderTrackRegions(track);

    if (this.isPlaying) {
      this.scheduleTrackRegionGains(track, this.audioCtx.currentTime, this.getCurrentTimeSec());
    }
  }

  deleteTrackRegion(track, regionId) {
    const reg = track.regions ? track.regions.find((r) => r.id === regionId) : null;
    if (!reg) return;
    reg.muted = true;
    this.renderTrackRegions(track);

    if (this.isPlaying) {
      this.scheduleTrackRegionGains(track, this.audioCtx.currentTime, this.getCurrentTimeSec());
    }
  }

  renderTrackRegions(track) {
    if (!track.regionsOverlayEl) return;
    track.regionsOverlayEl.innerHTML = "";
    const totalDur = this.durationSec > 0 ? this.durationSec : 1.0;

    for (const r of track.regions) {
      const leftPct = (r.startSec / totalDur) * 100;
      const widthPct = ((r.endSec - r.startSec) / totalDur) * 100;

      const el = document.createElement("div");
      el.className = `clip-region ${r.muted ? "clip-muted" : ""}`;
      el.style.left = `${leftPct}%`;
      el.style.width = `${widthPct}%`;

      let inner = "";
      if (r.muted) {
        inner += `<div class="clip-badge-mute">🔇 MUTED</div>`;
      }

      inner += `
        <div class="clip-action-pill">
          <button class="clip-pill-btn btn-clip-toggle-mute" title="${r.muted ? "取消消音" : "消音此段"}">${r.muted ? "🔊" : "🔇"}</button>
          <button class="clip-pill-btn btn-clip-del" title="刪除此段">🗑️</button>
        </div>
      `;

      el.innerHTML = inner;

      const btnToggleMute = el.querySelector(".btn-clip-toggle-mute");
      const btnDel = el.querySelector(".btn-clip-del");

      if (btnToggleMute) {
        btnToggleMute.addEventListener("click", (e) => {
          e.stopPropagation();
          this.toggleRegionMute(track, r.id);
        });
      }
      if (btnDel) {
        btnDel.addEventListener("click", (e) => {
          e.stopPropagation();
          this.deleteTrackRegion(track, r.id);
        });
      }

      track.regionsOverlayEl.appendChild(el);
    }
  }

  resetAllClips() {
    for (const t of this.tracks) {
      t.regions = [{
        id: 1,
        startSec: 0,
        endSec: this.durationSec,
        muted: false
      }];
      t.nextRegionId = 2;
      this.renderTrackRegions(t);
      if (this.isPlaying) {
        this.scheduleTrackRegionGains(t, this.audioCtx.currentTime, this.getCurrentTimeSec());
      }
    }
  }

  scheduleTrackRegionGains(t, syncStartAt, offset) {
    if (!t.regions || t.regions.length === 0 || !t.gainNode) return;
    const anySolo = this.tracks.some((x) => x.solo && !x.hiddenByTree);
    const isAudible = !t.hiddenByTree && (anySolo ? t.solo && !t.mute : !t.mute);
    const sign = t.invertPhase ? -1.0 : 1.0;
    const baseGain = isAudible ? t.volume * sign : 0.0;

    const param = t.gainNode.gain;
    param.cancelScheduledValues(syncStartAt);

    let currentRegionMuted = false;
    for (const r of t.regions) {
      if (r.startSec <= offset && offset < r.endSec) {
        currentRegionMuted = r.muted;
        break;
      }
    }
    const initialGain = currentRegionMuted ? 0.0 : baseGain;
    param.setValueAtTime(initialGain, syncStartAt);

    for (const r of t.regions) {
      if (r.endSec <= offset) continue;

      const regionStartCtx = syncStartAt + Math.max(0, r.startSec - offset);
      const regionEndCtx = syncStartAt + Math.max(0, r.endSec - offset);

      const targetVal = r.muted ? 0.0 : baseGain;

      if (r.startSec > offset) {
        param.setValueAtTime(param.value, regionStartCtx);
        param.linearRampToValueAtTime(targetVal, regionStartCtx + 0.003);
      }
      if (r.muted && r.endSec > offset) {
        param.setValueAtTime(0.0, regionEndCtx);
        param.linearRampToValueAtTime(baseGain, regionEndCtx + 0.003);
      }
    }
  }

  // ============================================================
  // 💾 另存專案 (Save Project As)
  // ============================================================
  openSaveAsModal() {
    if (!this.currentJob) {
      alert("請先載入或上傳一個分軌專案！");
      return;
    }
    const baseName = this.currentJob.originalFileName || this.currentJob.jobId || "專案";
    const defaultName = `${baseName} (修剪副本)`;
    if (this.saveAsProjectNameInput) {
      this.saveAsProjectNameInput.value = defaultName;
    }
    if (this.saveAsModal) {
      this.saveAsModal.classList.add("active");
    }
    setTimeout(() => {
      if (this.saveAsProjectNameInput) {
        this.saveAsProjectNameInput.focus();
        this.saveAsProjectNameInput.select();
      }
    }, 100);
  }

  closeSaveAsModal() {
    if (this.saveAsModal) {
      this.saveAsModal.classList.remove("active");
    }
  }

  collectTrackStates() {
    const trackStates = {};
    for (const t of this.tracks) {
      trackStates[t.nodeId] = {
        regions: (t.regions || []).map((r) => ({
          id: r.id,
          startSec: Number(r.startSec.toFixed(3)),
          endSec: Number(r.endSec.toFixed(3)),
          muted: !!r.muted
        })),
        volume: t.volume,
        pan: t.pan,
        mute: !!t.mute,
        solo: !!t.solo,
        invertPhase: !!t.invertPhase
      };
    }
    return {
      updatedAt: new Date().toISOString(),
      leadTrimSec: this.leadTrimSec || 0,
      trackStates
    };
  }

  async saveCurrentProject() {
    if (!this.currentJob) return;
    const btn = this.btnSaveProject;
    const oldText = btn ? btn.textContent : "💾 儲存專案";
    if (btn) {
      btn.textContent = "⏳ 儲存中...";
      btn.disabled = true;
    }

    try {
      const currentTrackStates = this.collectTrackStates();
      const resp = await fetch(`/api/projects/${this.currentJob.jobId}/save-state`, {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify(currentTrackStates)
      });
      const data = await resp.json();
      if (!resp.ok || !data.success) throw new Error("儲存失敗");

      this.currentJob.projectState = currentTrackStates;
      if (btn) {
        btn.textContent = "✅ 已儲存";
        setTimeout(() => {
          btn.textContent = "💾 儲存專案";
          btn.disabled = false;
        }, 1200);
      }
    } catch (err) {
      alert("儲存專案失敗: " + err.message);
      if (btn) {
        btn.textContent = oldText;
        btn.disabled = false;
      }
    }
  }

  async executeSaveAsProject() {
    if (!this.currentJob) return;
    const inputName = this.saveAsProjectNameInput ? this.saveAsProjectNameInput.value.trim() : "";
    const newProjectName = inputName || `${this.currentJob.originalFileName || "專案"}_副本`;

    this.closeSaveAsModal();
    this.showBusyOverlay(
      "💾 正在另存新專案...",
      `正在建立完整工程副本（含消音與剪輯狀態）：[${newProjectName}]...`,
      40,
      "💾"
    );

    try {
      const currentTrackStates = this.collectTrackStates();
      const resp = await fetch(`/api/projects/${this.currentJob.jobId}/save-as`, {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({
          newProjectName,
          trackStates: currentTrackStates
        })
      });

      const data = await resp.json();
      if (!resp.ok || !data.success || !data.job) {
        throw new Error(data.error || "另存專案失敗");
      }

      this.updateBusyOverlayProgress(85, "切換至新專案...", `已成功建立 [${data.projectName}]`);
      
      // 切換至新專案 (保留剪輯狀態)
      this.currentJob = data.job;
      this.currentJob.projectState = currentTrackStates;

      // 更新進度條與頂部標題
      if (this.progressTitleText) {
        this.progressTitleText.textContent = `當前專案: ${data.projectName} (ID: ${data.jobId})`;
      }

      // 重新整理歷史紀錄列表
      if (typeof this.renderHistoryList === "function") {
        await this.renderHistoryList();
      }

      await new Promise(r => setTimeout(r, 400));
      alert(`✅ 專案已成功另存為：\n【${data.projectName}】\n\n所有剪輯消音片段已 100% 寫入磁碟並持久保存！\n歷史紀錄列表已同步更新。`);
    } catch (err) {
      alert("另存專案失敗: " + err.message);
    } finally {
      this.hideBusyOverlay();
    }
  }

  formatTime(sec) {
    const mins = Math.floor(sec / 60);
    const rem = sec % 60;
    return `${String(mins).padStart(2, "0")}:${rem.toFixed(3).padStart(6, "0")}`;
  }

  // ============================================================
  // 🎧 音源即時試聽播放器 (Pre-listen Player)
  // ============================================================
  initSourcePreviewEvents() {
    if (!this.previewAudioElement) return;

    if (this.btnPreviewPlay) {
      this.btnPreviewPlay.addEventListener("click", () => {
        if (this.previewAudioElement.paused) {
          if (this.isPlaying) this.pausePlayback();
          this.previewAudioElement.play().catch(err => {
            console.warn("試聽播放被瀏覽器限制或格式錯誤:", err);
          });
        } else {
          this.previewAudioElement.pause();
        }
      });
    }

    this.previewAudioElement.addEventListener("play", () => {
      if (this.btnPreviewPlay) {
        this.btnPreviewPlay.textContent = "⏸ 暫停";
        this.btnPreviewPlay.classList.add("playing");
      }
    });

    this.previewAudioElement.addEventListener("pause", () => {
      if (this.btnPreviewPlay) {
        this.btnPreviewPlay.textContent = "▶ 播放試聽";
        this.btnPreviewPlay.classList.remove("playing");
      }
    });

    this.previewAudioElement.addEventListener("ended", () => {
      if (this.btnPreviewPlay) {
        this.btnPreviewPlay.textContent = "▶ 播放試聽";
        this.btnPreviewPlay.classList.remove("playing");
      }
      if (this.previewScrubber) this.previewScrubber.value = 0;
    });

    this.previewAudioElement.addEventListener("timeupdate", () => {
      const cur = this.previewAudioElement.currentTime || 0;
      const dur = this.previewAudioElement.duration || 0;
      if (this.previewTimeText && dur > 0) {
        this.previewTimeText.textContent = `${this.formatShortTime(cur)} / ${this.formatShortTime(dur)}`;
      }
      if (this.previewScrubber && dur > 0 && !this.isPreviewScrubbing) {
        this.previewScrubber.value = (cur / dur) * 100;
      }
    });

    this.previewAudioElement.addEventListener("loadedmetadata", () => {
      const dur = this.previewAudioElement.duration || 0;
      if (this.previewTimeText) {
        this.previewTimeText.textContent = `00:00 / ${this.formatShortTime(dur)}`;
      }
      if (this.previewScrubber) this.previewScrubber.value = 0;
    });

    if (this.previewScrubber) {
      this.previewScrubber.addEventListener("input", () => {
        this.isPreviewScrubbing = true;
      });
      this.previewScrubber.addEventListener("change", (e) => {
        const dur = this.previewAudioElement.duration || 0;
        if (dur > 0) {
          const targetSec = (parseFloat(e.target.value) / 100) * dur;
          this.previewAudioElement.currentTime = targetSec;
        }
        this.isPreviewScrubbing = false;
      });
    }

    if (this.previewVolume) {
      this.previewVolume.addEventListener("input", (e) => {
        const val = parseFloat(e.target.value);
        this.previewAudioElement.volume = val;
        if (this.previewMuteIcon) {
          this.previewMuteIcon.textContent = val === 0 ? "🔇" : "🔊";
        }
      });
    }

    if (this.previewMuteIcon) {
      this.previewMuteIcon.addEventListener("click", () => {
        if (this.previewAudioElement.volume > 0) {
          this.previewLastVolume = this.previewAudioElement.volume;
          this.previewAudioElement.volume = 0;
          if (this.previewVolume) this.previewVolume.value = 0;
          this.previewMuteIcon.textContent = "🔇";
        } else {
          const restore = this.previewLastVolume || 0.9;
          this.previewAudioElement.volume = restore;
          if (this.previewVolume) this.previewVolume.value = restore;
          this.previewMuteIcon.textContent = "🔊";
        }
      });
    }
  }

  formatShortTime(sec) {
    if (!sec || isNaN(sec)) return "00:00";
    const m = Math.floor(sec / 60);
    const s = Math.floor(sec % 60);
    return `${String(m).padStart(2, "0")}:${String(s).padStart(2, "0")}`;
  }

  loadSourcePreview(url, name) {
    if (!this.audioPreviewCard || !this.previewAudioElement) return;
    if (this.currentPreviewUrl && this.currentPreviewUrl.startsWith("blob:")) {
      try { URL.revokeObjectURL(this.currentPreviewUrl); } catch { }
    }
    this.currentPreviewUrl = url;
    this.previewAudioElement.src = url;
    this.previewAudioElement.load();
    if (this.previewFileName) this.previewFileName.textContent = name;
    this.audioPreviewCard.style.display = "flex";
    if (this.btnPreviewPlay) {
      this.btnPreviewPlay.textContent = "▶ 播放試聽";
      this.btnPreviewPlay.classList.remove("playing");
    }
  }

  showToast(msg) {
    let t = document.getElementById("dawToast");
    if (!t) {
      t = document.createElement("div");
      t.id = "dawToast";
      t.style.cssText = "position:fixed; bottom:24px; right:24px; background:#0f172a; color:#38bdf8; border:1px solid #0284c7; padding:12px 20px; border-radius:8px; box-shadow:0 8px 24px rgba(0,0,0,0.6); font-size:14px; font-weight:700; z-index:99999; transition:all 0.3s ease; opacity:0; transform:translateY(10px); pointer-events:none;";
      document.body.appendChild(t);
    }
    t.innerHTML = msg;
    t.style.opacity = "1";
    t.style.transform = "translateY(0)";
    clearTimeout(this._toastTimer);
    this._toastTimer = setTimeout(() => {
      t.style.opacity = "0";
      t.style.transform = "translateY(10px)";
    }, 2800);
  }

  initTrimWaveformInteractions() {
    const canvas = this.trimWaveformCanvas;
    if (!canvas) return;

    const handlePointer = (e) => {
      const rect = canvas.getBoundingClientRect();
      const clientX = e.clientX || (e.touches && e.touches[0] ? e.touches[0].clientX : 0);
      const x = Math.max(0, Math.min(rect.width, clientX - rect.left));
      const viewSec = this.trimMaxViewSec || 6.0;
      const sec = (x / rect.width) * viewSec;
      this.setTrimCutSec(sec, null);
    };

    canvas.addEventListener("mousedown", (e) => {
      this.isDraggingTrim = true;
      handlePointer(e);
    });

    window.addEventListener("mousemove", (e) => {
      if (this.isDraggingTrim) {
        handlePointer(e);
      }
    });

    window.addEventListener("mouseup", () => {
      if (this.isDraggingTrim) {
        this.isDraggingTrim = false;
      }
    });

    canvas.addEventListener("touchstart", (e) => {
      if (e.touches.length > 0) {
        this.isDraggingTrim = true;
        handlePointer(e.touches[0]);
      }
    }, { passive: true });

    window.addEventListener("touchmove", (e) => {
      if (this.isDraggingTrim && e.touches.length > 0) {
        handlePointer(e.touches[0]);
      }
    }, { passive: true });

    window.addEventListener("touchend", () => {
      this.isDraggingTrim = false;
    });
  }

  initTrimPresetControls() {
    if (this.trimPresetButtons) {
      const btns = this.trimPresetButtons.querySelectorAll(".trim-preset-btn");
      btns.forEach((btn) => {
        btn.addEventListener("click", () => {
          const preset = btn.dataset.preset;
          const bpm = this.getCurrentBpm() || 120;
          const beatSec = 60.0 / bpm;
          let cut = 0;

          switch (preset) {
            case "tight":
              cut = Math.max(0, this.detectedOnsetSec - 0.015);
              break;
            case "half":
              cut = Math.max(0, this.detectedOnsetSec - 0.5 * beatSec);
              if (this.selCountInPickup) {
                this.selCountInPickup.value = "0.5";
                localStorage.setItem("stemforge_countin_pickup", "0.5");
              }
              break;
            case "1beat":
              cut = Math.max(0, this.detectedOnsetSec - 1.0 * beatSec);
              if (this.selCountInPickup) {
                this.selCountInPickup.value = "1";
                localStorage.setItem("stemforge_countin_pickup", "1");
              }
              break;
            case "2beats":
              cut = Math.max(0, this.detectedOnsetSec - 2.0 * beatSec);
              if (this.selCountInPickup) {
                this.selCountInPickup.value = "2";
                localStorage.setItem("stemforge_countin_pickup", "2");
              }
              break;
            case "1bar":
              cut = Math.max(0, this.detectedOnsetSec - 4.0 * beatSec);
              if (this.selCountInPickup) {
                this.selCountInPickup.value = "0";
                localStorage.setItem("stemforge_countin_pickup", "0");
              }
              break;
            default:
              cut = this.detectedOnsetSec;
          }

          this.setTrimCutSec(cut, preset);
        });
      });
    }

    if (this.btnNudgeTrimM50) {
      this.btnNudgeTrimM50.addEventListener("click", () => this.setTrimCutSec(this.currentTrimCutSec - 0.050));
    }
    if (this.btnNudgeTrimM10) {
      this.btnNudgeTrimM10.addEventListener("click", () => this.setTrimCutSec(this.currentTrimCutSec - 0.010));
    }
    if (this.btnNudgeTrimP10) {
      this.btnNudgeTrimP10.addEventListener("click", () => this.setTrimCutSec(this.currentTrimCutSec + 0.010));
    }
    if (this.btnNudgeTrimP50) {
      this.btnNudgeTrimP50.addEventListener("click", () => this.setTrimCutSec(this.currentTrimCutSec + 0.050));
    }

    if (this.trimManualInput) {
      this.trimManualInput.addEventListener("input", (e) => {
        const v = parseFloat(e.target.value);
        if (!isNaN(v)) {
          this.setTrimCutSec(v, null);
        }
      });
    }

    if (this.btnAuditionTrim) {
      this.btnAuditionTrim.addEventListener("click", () => this.toggleTrimAudition());
    }
  }

  updateTrimWaveformHeader() {
    const lbl = document.getElementById("trimWaveformHeaderLabel");
    if (lbl) {
      const secStr = (this.trimMaxViewSec || 6.0).toFixed(1);
      lbl.textContent = `前奏波形視覺放大 (前 ${secStr} 秒) — 可直接點擊波形調整起點：`;
    }
  }

  setTrimCutSec(sec, presetName = null) {
    if (sec > (this.trimMaxViewSec || 6.0)) {
      this.trimMaxViewSec = Math.min(this.durationSec || 60.0, Math.ceil((sec + 2.5) * 2) / 2);
      this.updateTrimWaveformHeader();
    }
    const maxView = this.trimMaxViewSec || 6.0;
    this.currentTrimCutSec = Math.max(0, Math.min(maxView, Math.round(sec * 1000) / 1000));

    if (this.trimManualInput) {
      this.trimManualInput.value = this.currentTrimCutSec.toFixed(3);
    }

    if (this.trimPresetButtons) {
      const btns = this.trimPresetButtons.querySelectorAll(".trim-preset-btn");
      btns.forEach((btn) => {
        if (presetName && btn.dataset.preset === presetName) {
          btn.classList.add("active");
        } else if (!presetName) {
          btn.classList.remove("active");
        }
      });
    }

    this.drawTrimWaveform();
  }

  drawTrimWaveform() {
    const canvas = this.trimWaveformCanvas;
    if (!canvas) return;

    const parentW = canvas.parentElement ? canvas.parentElement.clientWidth : 630;
    const width = parentW > 0 ? parentW : 630;
    const height = 95;

    const dpr = window.devicePixelRatio || 1;
    if (canvas.width !== Math.floor(width * dpr) || canvas.height !== Math.floor(height * dpr)) {
      canvas.width = Math.floor(width * dpr);
      canvas.height = Math.floor(height * dpr);
    }

    const ctx = canvas.getContext("2d");
    ctx.save();
    ctx.scale(dpr, dpr);

    // Background
    ctx.fillStyle = "#070a12";
    ctx.fillRect(0, 0, width, height);

    const viewSec = this.trimMaxViewSec || 6.0;

    // 自適應時間軸刻度與標籤間距
    let gridStep = 0.5;
    let labelStep = 1.0;
    if (viewSec > 25) {
      gridStep = 2.0;
      labelStep = 5.0;
    } else if (viewSec > 12) {
      gridStep = 1.0;
      labelStep = 2.0;
    } else if (viewSec > 6) {
      gridStep = 0.5;
      labelStep = 1.0;
    } else {
      gridStep = 0.5;
      labelStep = 0.5;
    }

    ctx.lineWidth = 1;
    ctx.font = "10px Consolas, monospace";
    for (let s = gridStep; s <= viewSec; s += gridStep) {
      const x = (s / viewSec) * width;
      const isMajor = Math.abs(Math.round(s / labelStep) * labelStep - s) < 0.01;
      ctx.strokeStyle = isMajor ? "rgba(148, 163, 184, 0.35)" : "rgba(148, 163, 184, 0.12)";
      ctx.beginPath();
      ctx.moveTo(x, 0);
      ctx.lineTo(x, height);
      ctx.stroke();

      if (isMajor) {
        ctx.fillStyle = "#94a3b8";
        ctx.textAlign = "center";
        const labelText = s % 1 === 0 ? `${s.toFixed(0)}s` : `${s.toFixed(1)}s`;
        ctx.fillText(labelText, x, height - 6);
      }
    }

    // Draw composite waveform from active stems
    if (this.tracks && this.tracks.length > 0) {
      const sampleRate = this.tracks[0].audioBuffer.sampleRate || 44100;
      const maxSamples = Math.floor(viewSec * sampleRate);

      const numCols = Math.floor(width);
      const peaks = new Float32Array(numCols);
      const samplesPerPixel = maxSamples / numCols;

      for (const t of this.tracks) {
        if (!t.audioBuffer || t.hiddenByTree || t.meta?.isOriginalReference) continue;
        const ch0 = t.audioBuffer.getChannelData(0);
        const ch1 = t.audioBuffer.numberOfChannels > 1 ? t.audioBuffer.getChannelData(1) : ch0;
        const trackLen = Math.min(ch0.length, maxSamples);

        for (let col = 0; col < numCols; col++) {
          const start = Math.floor(col * samplesPerPixel);
          const end = Math.min(Math.floor((col + 1) * samplesPerPixel), trackLen);
          if (start >= trackLen) break;

          let maxVal = 0;
          const step = Math.max(1, Math.floor((end - start) / 8));
          for (let idx = start; idx < end; idx += step) {
            const v = Math.max(Math.abs(ch0[idx]), Math.abs(ch1[idx]));
            if (v > maxVal) maxVal = v;
          }
          if (maxVal > peaks[col]) peaks[col] = maxVal;
        }
      }

      const midY = (height - 18) / 2 + 6;
      const ampY = (height - 24) / 2;

      ctx.fillStyle = "rgba(56, 189, 248, 0.65)";
      for (let col = 0; col < numCols; col++) {
        const h = Math.max(2, peaks[col] * ampY);
        ctx.fillRect(col, midY - h, 1, h * 2);
      }
    }

    // Draw Trimmed / Muted zone (0 to cutSec)
    const cutSec = this.currentTrimCutSec || 0;
    const cutX = Math.max(0, Math.min(width, (cutSec / viewSec) * width));

    if (cutX > 0) {
      ctx.fillStyle = "rgba(239, 68, 68, 0.18)";
      ctx.fillRect(0, 0, cutX, height);

      ctx.strokeStyle = "rgba(239, 68, 68, 0.15)";
      ctx.lineWidth = 1;
      ctx.beginPath();
      for (let x = -height; x < cutX; x += 12) {
        ctx.moveTo(Math.max(0, x), 0);
        ctx.lineTo(Math.min(cutX, x + height), height);
      }
      ctx.stroke();

      if (cutX > 60) {
        ctx.fillStyle = "rgba(239, 68, 68, 0.85)";
        ctx.font = "bold 9px sans-serif";
        ctx.textAlign = "left";
        ctx.fillText(`✂️ 切除 0s ~ ${cutSec.toFixed(3)}s`, 6, 14);
      }
    }

    // Draw Onset Line (dashed cyan)
    if (this.detectedOnsetSec > 0 && this.detectedOnsetSec <= viewSec) {
      const onsetX = (this.detectedOnsetSec / viewSec) * width;
      ctx.save();
      ctx.strokeStyle = "#38bdf8";
      ctx.lineWidth = 1.5;
      ctx.setLineDash([4, 4]);
      ctx.beginPath();
      ctx.moveTo(onsetX, 0);
      ctx.lineTo(onsetX, height);
      ctx.stroke();
      ctx.restore();

      const labelW = 76;
      const labelX = Math.max(labelW / 2 + 2, Math.min(width - labelW / 2 - 2, onsetX));
      ctx.fillStyle = "#0284c7";
      ctx.beginPath();
      if (ctx.roundRect) ctx.roundRect(labelX - labelW / 2, 18, labelW, 15, 3);
      else ctx.rect(labelX - labelW / 2, 18, labelW, 15);
      ctx.fill();

      ctx.fillStyle = "#ffffff";
      ctx.font = "bold 9px sans-serif";
      ctx.textAlign = "center";
      ctx.fillText(`⚡起音 ${this.detectedOnsetSec.toFixed(3)}s`, labelX, 29);
    }

    // Draw Cut Point Vertical Line (Emerald Green)
    ctx.save();
    ctx.strokeStyle = "#10b981";
    ctx.lineWidth = 2.5;
    ctx.shadowColor = "#10b981";
    ctx.shadowBlur = 8;
    ctx.beginPath();
    ctx.moveTo(cutX, 0);
    ctx.lineTo(cutX, height);
    ctx.stroke();
    ctx.restore();

    // Green Flag Badge at the cut line
    const badgeW = 76;
    const badgeH = 16;
    const badgeX = Math.max(2, Math.min(width - badgeW - 2, cutX - badgeW / 2));
    const badgeY = 2;

    ctx.fillStyle = "#059669";
    ctx.beginPath();
    if (ctx.roundRect) ctx.roundRect(badgeX, badgeY, badgeW, badgeH, 4);
    else ctx.rect(badgeX, badgeY, badgeW, badgeH);
    ctx.fill();

    ctx.fillStyle = "#ffffff";
    ctx.font = "bold 10px Consolas, monospace";
    ctx.textAlign = "center";
    ctx.fillText(`🚩 起點 ${cutSec.toFixed(3)}s`, badgeX + badgeW / 2, badgeY + 12);

    ctx.restore();
  }

  toggleTrimAudition() {
    if (this.isAuditioningTrim) {
      this.stopTrimAudition();
    } else {
      this.startTrimAudition();
    }
  }

  stopTrimAudition() {
    if (this.trimAuditionTimer) {
      clearTimeout(this.trimAuditionTimer);
      this.trimAuditionTimer = null;
    }
    if (this.trimAuditionSources && this.trimAuditionSources.length > 0) {
      for (const src of this.trimAuditionSources) {
        try {
          src.stop();
          src.disconnect();
        } catch (e) {}
      }
      this.trimAuditionSources = [];
    }
    this.isAuditioningTrim = false;
    if (this.btnAuditionTrim) {
      this.btnAuditionTrim.textContent = "🔊 試聽起點 (播 2 秒)";
      this.btnAuditionTrim.style.background = "#0284c7";
    }
  }

  startTrimAudition() {
    if (!this.audioCtx) return;
    if (this.audioCtx.state === "suspended") this.audioCtx.resume();
    if (this.isPlaying) this.pause();

    this.stopTrimAudition();

    const startSec = Math.max(0, this.currentTrimCutSec || 0);
    const auditionDuration = 2.0;

    this.isAuditioningTrim = true;
    if (this.btnAuditionTrim) {
      this.btnAuditionTrim.textContent = "⏹️ 停止試聽";
      this.btnAuditionTrim.style.background = "#dc2626";
    }

    const anySolo = this.tracks.some((t) => t.solo && !t.hiddenByTree);

    for (const t of this.tracks) {
      if (!t.audioBuffer) continue;
      const isAudible = !t.hiddenByTree && (anySolo ? t.solo && !t.mute : !t.mute);
      if (!isAudible) continue;

      const src = this.audioCtx.createBufferSource();
      src.buffer = t.audioBuffer;

      if (t.gainNode) {
        src.connect(t.gainNode);
      } else {
        src.connect(this.audioCtx.destination);
      }

      try {
        src.start(0, startSec, auditionDuration);
        this.trimAuditionSources.push(src);
      } catch (err) {
        console.warn("Audition error:", err);
      }
    }

    this.trimAuditionTimer = setTimeout(() => {
      this.stopTrimAudition();
    }, auditionDuration * 1000 + 50);
  }

  openTrimSilenceModal() {
    if (!this.tracks || this.tracks.length === 0) {
      alert("請先載入或分離一首歌曲！");
      return;
    }
    if (!this.trimSilenceModal) return;

    if (this.isPlaying) this.pause();
    this.stopTrimAudition();

    this.trimSilenceModal.classList.add("active");

    if (this.trimDetectInfo) {
      this.trimDetectInfo.innerHTML = "🔍 正在進行暫態躍變與底噪分析，精確定位首個音符瞬間...";
    }

    setTimeout(() => {
      const onsetSec = this.analyzeAccurateOnset();
      this.detectedOnsetSec = onsetSec;

      const bpm = this.getCurrentBpm() || 120;
      const beatSec = 60.0 / bpm;

      let defaultCut = 0;
      let initialPreset = "tight";
      if (this.leadTrimSec && this.leadTrimSec > 0) {
        defaultCut = this.leadTrimSec;
        initialPreset = null;
      } else {
        defaultCut = Math.max(0, onsetSec - 0.015);
        initialPreset = "tight";
      }

      // 動態設定可視視野 trimMaxViewSec：至少涵蓋起音點後 3 秒，最少 6 秒
      let neededView = onsetSec > 0 ? onsetSec + 3.0 : 6.0;
      if (this.leadTrimSec && this.leadTrimSec > 0) {
        neededView = Math.max(neededView, this.leadTrimSec + 2.5);
      }
      this.trimMaxViewSec = Math.max(6.0, Math.min(this.durationSec || 60.0, Math.ceil(neededView * 2) / 2));
      this.updateTrimWaveformHeader();

      if (this.trimDetectInfo) {
        if (onsetSec > 0.04) {
          this.trimDetectInfo.innerHTML =
            `💡 <strong>高精度躍變起音定位：</strong>首個音符於 <code>${onsetSec.toFixed(3)}s</code> 響起 (BPM: <strong>${bpm}</strong>，每拍 ${(beatSec * 1000).toFixed(0)}ms)<br>` +
            `• <strong>弱起搶拍提示：</strong>若本曲開頭有半拍或末拍搶音進歌，請點下方弱起快捷鍵預留拍數；<br>` +
            `• <strong>可視化起點微調：</strong>可直接點選/拖曳波形綠線，並點擊右方 <strong>[ 🔊 試聽起點 ]</strong> 親耳確認無誤後套用！`;
        } else {
          this.trimDetectInfo.innerHTML =
            `✅ <strong>開頭已極度緊湊！</strong>首個音符在 <code>${onsetSec.toFixed(3)}s</code>，未發現顯著死靜音。<br>` +
            `您仍可直接在下方波形手動微調或預留弱起空間。`;
        }
      }

      this.setTrimCutSec(defaultCut, initialPreset);
    }, 60);
  }

  closeTrimSilenceModal() {
    this.stopTrimAudition();
    if (this.trimSilenceModal) {
      this.trimSilenceModal.classList.remove("active");
    }
  }

  analyzeAccurateOnset() {
    if (!this.tracks || this.tracks.length === 0) return 0;

    const sampleRate = this.tracks[0].audioBuffer.sampleRate || 44100;
    // 支援超長前奏靜音掃描（擴展至 60 秒，涵蓋大於 4 秒、10 秒甚至長錄音）
    const scanSec = Math.min(60.0, this.durationSec || 60.0);
    const hopSize = Math.floor(sampleRate * 0.005); // 5ms 解析度
    const numFrames = Math.floor((scanSec * sampleRate) / hopSize);
    if (numFrames <= 0) return 0;

    const energies = new Float32Array(numFrames);

    // Sum energies across active tracks
    for (const t of this.tracks) {
      if (!t.audioBuffer || t.hiddenByTree || t.meta?.isOriginalReference) continue;
      const ch0 = t.audioBuffer.getChannelData(0);
      const ch1 = t.audioBuffer.numberOfChannels > 1 ? t.audioBuffer.getChannelData(1) : ch0;
      const maxLen = Math.min(ch0.length, Math.floor(scanSec * sampleRate));

      for (let f = 0; f < numFrames; f++) {
        const start = f * hopSize;
        const end = Math.min(start + hopSize, maxLen);
        if (start >= maxLen) break;
        let sumSq = 0;
        let count = 0;
        for (let s = start; s < end; s += 2) {
          const v = Math.max(Math.abs(ch0[s]), Math.abs(ch1[s]));
          sumSq += v * v;
          count++;
        }
        if (count > 0) {
          const rms = Math.sqrt(sumSq / count);
          if (rms > energies[f]) energies[f] = rms;
        }
      }
    }

    // Step 1: 穩健估算安靜底噪基準 (取前 2 秒或前 400 幀中的最小滾動 RMS，防止開頭微小 Pop 聲干擾)
    let minNoiseRms = 1.0;
    const initScanFrames = Math.min(400, numFrames);
    for (let f = 0; f < initScanFrames; f += 5) {
      let localAvg = 0;
      let cnt = 0;
      for (let k = f; k < Math.min(f + 10, initScanFrames); k++) {
        localAvg += energies[k];
        cnt++;
      }
      localAvg /= Math.max(1, cnt);
      if (localAvg < minNoiseRms) minNoiseRms = localAvg;
    }
    const noiseFloor = Math.max(0.0008, Math.min(0.02, minNoiseRms * 2.0));

    // Step 2: 半波整流階躍躍變偵測 (Transient & Energy Jump Detection)
    let detectedSec = 0;
    for (let f = 1; f < numFrames - 3; f++) {
      const delta = energies[f] - energies[f - 1];
      const isAboveNoise = energies[f] > Math.max(noiseFloor * 1.8, 0.003);
      const isJump = delta > Math.max(0.003, noiseFloor * 1.5) || energies[f] > 0.015;
      const isSustained = energies[f + 1] > noiseFloor * 1.2 && energies[f + 2] > noiseFloor * 1.2;

      if (isAboveNoise && isJump && isSustained) {
        detectedSec = f * 0.005;
        break;
      }
    }

    // Step 3: 若前奏為緩慢漸入 (Fade-in / Ambient Pad) 未產生急劇突波，執行第二道平滑門檻偵測
    if (detectedSec === 0) {
      for (let f = 0; f < numFrames - 2; f++) {
        if (energies[f] > Math.max(noiseFloor * 2.5, 0.005) && energies[f + 1] > Math.max(noiseFloor * 2.0, 0.004)) {
          detectedSec = f * 0.005;
          break;
        }
      }
    }

    return Math.round(detectedSec * 1000) / 1000;
  }

  confirmTrimSilence() {
    this.stopTrimAudition();
    this.closeTrimSilenceModal();
    this.applyLeadTrim(this.currentTrimCutSec, true);
  }

  applyLeadTrim(sec, showToast = true) {
    this.leadTrimSec = Math.max(0, sec);
    if (this.btnResetTrim) {
      this.btnResetTrim.style.display = this.leadTrimSec > 0 ? "inline-block" : "none";
    }
    this.pauseOffsetSec = 0;
    this.updatePlayheadUi(0);
    this.renderLeadTrimOverlays();

    if (showToast) {
      if (this.leadTrimSec > 0) {
        this.showToast(`✂️ 已自動切齊開頭 ${this.leadTrimSec.toFixed(2)} 秒前置靜音！起點對齊 00:00.000`);
      } else {
        this.showToast(`⟲ 已恢復原始音軌開頭長度`);
      }
    }
  }

  renderLeadTrimOverlays() {
    for (const t of this.tracks) {
      if (!t.rowEl) continue;
      const wrap = t.rowEl.querySelector(".track-waveform-wrap");
      if (!wrap) continue;
      let overlay = wrap.querySelector(".lead-trim-overlay");
      if (this.leadTrimSec > 0 && this.durationSec > 0) {
        if (!overlay) {
          overlay = document.createElement("div");
          overlay.className = "lead-trim-overlay";
          wrap.appendChild(overlay);
        }
        const pct = (this.leadTrimSec / this.durationSec) * 100;
        overlay.style.width = `${pct}%`;
        overlay.innerHTML = `<span class="lead-trim-badge">🚩 起點 [-${this.leadTrimSec.toFixed(2)}s]</span>`;
      } else if (overlay) {
        overlay.remove();
      }
    }
  }

  stopSourcePreview() {
    if (this.previewAudioElement && !this.previewAudioElement.paused) {
      this.previewAudioElement.pause();
    }
  }
}

window.addEventListener("DOMContentLoaded", () => {
  window.stemForgeApp = new StemForgeDawApp();
  const urlParams = new URLSearchParams(window.location.search);
  const autoLoadId = urlParams.get("load");
  if (autoLoadId) {
    setTimeout(() => {
      window.stemForgeApp.loadHistoryProject(autoLoadId);
    }, 400);
  }
});
