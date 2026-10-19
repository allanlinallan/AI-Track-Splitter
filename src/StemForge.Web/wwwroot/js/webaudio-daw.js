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
    this.loadDagCatalog().then(async () => {
      try {
        const latestRes = await fetch(`/latest_gpu_job.json?t=${Date.now()}`);
        if (latestRes.ok) {
          const latestJob = await latestRes.json();
          if (latestJob && latestJob.stems && latestJob.stems.length > 0) {
            await this.loadJobIntoMixer(latestJob);
            return;
          }
        }
      } catch {}

      const saved = sessionStorage.getItem("stemforge_last_job");
      if (saved) {
        try {
          const parsed = JSON.parse(saved);
          if (parsed && parsed.stems && parsed.stems.length > 0) {
            await this.loadJobIntoMixer(parsed);
          }
        } catch {}
      }
    });

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

    this.btnStartSeparate = document.getElementById("btnStartSeparate");
    this.btnRunDemo = document.getElementById("btnRunDemo");
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

    // Drag & Drop File Upload
    this.dropzone.addEventListener("click", () => this.fileInput.click());
    this.fileInput.addEventListener("change", (e) => {
      if (e.target.files.length > 0) {
        this.selectedFile = e.target.files[0];
        this.selectedFileLabel.textContent = `已選擇: ${this.selectedFile.name} (${(this.selectedFile.size / 1048576).toFixed(2)} MB)`;
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
      }
    });

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

    // Start Separation & Demo
    this.btnStartSeparate.addEventListener("click", () => this.startSeparationJob());
    this.btnRunDemo.addEventListener("click", () => this.runStudioDemoJob());

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
      this.seekTo(ratio * this.durationSec);
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
    });

    this.hubConnection.start().catch((err) => console.warn("SignalR:", err));
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
      const visible = this.selectedNodes.has(t.nodeId);
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
      alert("請先選擇 MP3/WAV 檔案，或貼上本機檔案路徑，或點「⚡ 載入內建測試曲」！");
      return;
    }

    this.stopPlayback();
    this.btnStartSeparate.disabled = true;
    this.btnRunDemo.disabled = true;

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
    } finally {
      this.btnStartSeparate.disabled = false;
      this.btnRunDemo.disabled = false;
    }
  }

  async runStudioDemoJob() {
    this.stopPlayback();
    this.btnStartSeparate.disabled = true;
    this.btnRunDemo.disabled = true;

    try {
      const res = await fetch("/api/jobs/demo", { method: "POST" });
      const data = await res.json();
      await this.loadJobIntoMixer(data);
    } catch (err) {
      alert("測試曲執行失敗: " + err.message);
    } finally {
      this.btnStartSeparate.disabled = false;
      this.btnRunDemo.disabled = false;
    }
  }

  async switchJobMode(jobId, mode) {
    const wasPlaying = this.isPlaying;
    const currentPos = this.getCurrentTimeSec();
    this.stopPlayback();

    const res = await fetch(`/api/jobs/${jobId}/mode/${mode}`, { method: "POST" });
    if (res.ok) {
      const updated = await res.json();
      await this.loadJobIntoMixer(updated);
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
      this.masterGainNode.connect(this.audioCtx.destination);
    }
    if (this.audioCtx.state === "suspended") {
      this.audioCtx.resume();
    }
  }

  async loadJobIntoMixer(jobResult) {
    this.currentJob = jobResult;
    try {
      sessionStorage.setItem("stemforge_last_job", JSON.stringify(jobResult));
    } catch {}
    this.ensureAudioContext();

    document.getElementById("nullCancellationBadge").textContent =
      `Null 誤差: ${jobResult.nullTestCancellationDbFs} dBFS`;

    if (jobResult.reaperRppUrl) {
      this.btnDownloadRpp.href = jobResult.reaperRppUrl;
      this.btnDownloadRpp.classList.remove("disabled");
    }
    this.btnExportZip.disabled = false;

    this.mixerTracksContainer.innerHTML = `<div class="empty-mixer-state">正在將 ${jobResult.stems.length} 條 32-bit Float 音軌解碼載入 WebAudio 同步緩衝區...</div>`;

    // 解碼原曲供 Null Test 反相抵消使用
    const srcResp = await fetch(jobResult.sourceAudioUrl);
    const srcArr = await srcResp.arrayBuffer();
    this.originalMixBuffer = await this.audioCtx.decodeAudioData(srcArr);
    this.durationSec = this.originalMixBuffer.duration;
    document.getElementById("totalTimeText").textContent = this.formatTime(this.durationSec);

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

    this.mixerTracksContainer.innerHTML = "";

    for (const { stem, audioBuffer } of decodedStems) {
      const gainNode = this.audioCtx.createGain();
      const panNode = this.audioCtx.createStereoPanner();
      panNode.connect(gainNode);
      gainNode.connect(this.masterGainNode);

      const isResidual = stem.nodeId.startsWith("STEM_19_RESIDUAL");
      const trackObj = {
        nodeId: stem.nodeId,
        meta: stem,
        audioBuffer,
        gainNode,
        panNode,
        solo: false,
        mute: isResidual,
        invertPhase: false,
        volume: 1.0,
        pan: 0.0,
        rowEl: null,
        canvas: null,
        playheadEl: null
      };

      this.tracks.push(trackObj);
      this.renderTrackStrip(trackObj);
    }

    this.updateTrackGains();

    // 確保全部 DOM 掛載完成後繪製高解析度正規化波形
    setTimeout(() => {
      for (const t of this.tracks) {
        this.drawWaveform(t);
      }
    }, 30);

    this.pauseOffsetSec = 0;
    this.btnPlayPause.disabled = false;
    this.btnStop.disabled = false;
    this.btnSoloClear.disabled = false;
    this.btnNullTest.disabled = false;
  }

  renderTrackStrip(track) {
    const { meta } = track;
    const row = document.createElement("div");
    row.className = "track-row";
    track.rowEl = row;

    row.innerHTML = `
      <div class="track-info">
        <div class="track-color-bar" style="background:${meta.colorHex}"></div>
        <div class="track-title-wrap">
          <div class="track-name" title="${meta.displayName}">${meta.displayName}</div>
          <div class="track-meta">Peak: ${meta.peakDbFs} dB | RMS: ${meta.rmsDbFs} dB ${meta.fromCache ? "⚡Cache" : ""}</div>
        </div>
      </div>
      <div class="track-controls">
        <button class="m-btn solo" title="Solo 獨奏此軌">S</button>
        <button class="m-btn mute ${track.mute ? "active" : ""}" title="Mute 靜音此軌">M</button>
        <button class="m-btn invert" title="Phase Invert 180° 反相">Ø</button>
        <div class="fader-wrap">
          <input type="range" class="vol-slider" min="0" max="1.5" step="0.01" value="1.0" title="音量推子 (Volume)" />
          <input type="range" class="pan-slider" min="-1" max="1" step="0.05" value="0" title="立體聲 Pan (L/R)" />
        </div>
      </div>
      <div class="track-waveform-wrap">
        <canvas class="track-waveform-canvas" height="58"></canvas>
        <div class="track-playhead" style="left:0%"></div>
      </div>
      <div class="track-actions">
        <a class="act-link" href="${meta.relativeUrl}" download title="下載 32-bit Float WAV">WAV</a>
        <button class="act-link btn-midi" title="轉出此軌 MIDI 音符檔 (.mid)">🎹 MIDI</button>
      </div>
    `;

    const btnSolo = row.querySelector(".m-btn.solo");
    const btnMute = row.querySelector(".m-btn.mute");
    const btnInvert = row.querySelector(".m-btn.invert");
    const volSlider = row.querySelector(".vol-slider");
    const panSlider = row.querySelector(".pan-slider");
    const wfWrap = row.querySelector(".track-waveform-wrap");
    const canvas = row.querySelector(".track-waveform-canvas");
    const btnMidi = row.querySelector(".btn-midi");

    track.canvas = canvas;
    track.playheadEl = row.querySelector(".track-playhead");

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

    volSlider.addEventListener("input", (e) => {
      track.volume = parseFloat(e.target.value);
      this.updateTrackGains();
    });

    panSlider.addEventListener("input", (e) => {
      track.pan = parseFloat(e.target.value);
      track.panNode.pan.value = track.pan;
    });

    wfWrap.addEventListener("click", (e) => {
      const rect = wfWrap.getBoundingClientRect();
      const ratio = Math.max(0, Math.min(1, (e.clientX - rect.left) / rect.width));
      this.seekTo(ratio * this.durationSec);
    });

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
    for (const t of this.tracks) {
      const isAudible = !t.hiddenByTree && (anySolo ? t.solo && !t.mute : !t.mute);
      const sign = t.invertPhase ? -1.0 : 1.0;
      t.gainNode.gain.value = isAudible ? t.volume * sign : 0.0;
      t.rowEl.classList.toggle("muted-effective", !isAudible);
    }
  }

  clearAllSoloMute() {
    for (const t of this.tracks) {
      t.solo = false;
      t.mute = false;
      t.invertPhase = false;
      t.rowEl.querySelector(".m-btn.solo").classList.remove("active");
      t.rowEl.querySelector(".m-btn.mute").classList.remove("active");
      t.rowEl.querySelector(".m-btn.invert").classList.remove("active");
    }
    this.updateTrackGains();
  }

  toggleNullTest() {
    this.isNullTestActive = !this.isNullTestActive;
    this.btnNullTest.classList.toggle("active", this.isNullTestActive);
    this.btnNullTest.textContent = this.isNullTestActive
      ? "Ø 反相抵消測試 (Null Test: ON — 原曲反相疊加中)"
      : "Ø 反相抵消測試 (Null Test: OFF)";

    const resTrack = this.tracks.find((t) => t.nodeId.startsWith("STEM_19_RESIDUAL"));
    if (resTrack) {
      resTrack.mute = !this.isNullTestActive;
      const mBtn = resTrack.rowEl?.querySelector(".m-btn.mute");
      if (mBtn) mBtn.classList.toggle("active", resTrack.mute);
      this.updateTrackGains();
    }

    if (this.nullTestInvertNode) {
      this.nullTestInvertNode.gain.value = this.isNullTestActive ? -1.0 : 0.0;
    }
  }

  togglePlayPause() {
    if (this.isPlaying) {
      this.pausePlayback();
    } else {
      this.startPlayback();
    }
  }

  startPlayback() {
    if (this.tracks.length === 0) return;
    this.ensureAudioContext();
    this.stopActiveSourcesOnly();

    const offset = this.pauseOffsetSec >= this.durationSec ? 0 : this.pauseOffsetSec;
    this.pauseOffsetSec = offset;

    const syncStartAt = this.audioCtx.currentTime + 0.03;
    this.startTimeCtx = syncStartAt - offset;

    for (const t of this.tracks) {
      const src = this.audioCtx.createBufferSource();
      src.buffer = t.audioBuffer;
      src.connect(t.panNode);
      src.start(syncStartAt, offset);
      this.activeSources.push(src);
    }

    if (this.originalMixBuffer) {
      this.nullTestInvertNode = this.audioCtx.createGain();
      this.nullTestInvertNode.gain.value = this.isNullTestActive ? -1.0 : 0.0;
      this.nullTestInvertNode.connect(this.masterGainNode);

      const mixSrc = this.audioCtx.createBufferSource();
      mixSrc.buffer = this.originalMixBuffer;
      mixSrc.connect(this.nullTestInvertNode);
      mixSrc.start(syncStartAt, offset);
      this.activeSources.push(mixSrc);
    }

    this.updateTrackGains();
    this.isPlaying = true;
    this.btnPlayPause.textContent = "⏸ 暫停 (Pause)";
    this.btnPlayPause.classList.add("playing");

    this.tickPlayhead();
  }

  pausePlayback() {
    if (!this.isPlaying) return;
    this.pauseOffsetSec = this.getCurrentTimeSec();
    this.stopActiveSourcesOnly();
    this.isPlaying = false;
    this.btnPlayPause.textContent = "▶ 播放 (Play)";
    this.btnPlayPause.classList.remove("playing");
  }

  stopPlayback() {
    this.stopActiveSourcesOnly();
    this.isPlaying = false;
    this.pauseOffsetSec = 0;
    this.btnPlayPause.textContent = "▶ 播放 (Play)";
    this.btnPlayPause.classList.remove("playing");
    this.updatePlayheadUi(0);
  }

  seekTo(targetSec) {
    const clamped = Math.max(0, Math.min(this.durationSec, targetSec));
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

  getCurrentTimeSec() {
    if (!this.isPlaying || !this.audioCtx) return this.pauseOffsetSec;
    return Math.max(0, this.audioCtx.currentTime - this.startTimeCtx);
  }

  tickPlayhead() {
    if (!this.isPlaying) return;
    const cur = this.getCurrentTimeSec();
    if (cur >= this.durationSec) {
      this.stopPlayback();
      return;
    }
    this.updatePlayheadUi(cur);
    this.rafId = requestAnimationFrame(() => this.tickPlayhead());
  }

  updatePlayheadUi(sec) {
    document.getElementById("currentTimeText").textContent = this.formatTime(sec);
    const pct = this.durationSec > 0 ? Math.min(100, (sec / this.durationSec) * 100) : 0;
    document.getElementById("scrubberProgress").style.width = `${pct}%`;
    document.getElementById("scrubberHead").style.left = `${pct}%`;

    for (const t of this.tracks) {
      if (t.playheadEl) {
        t.playheadEl.style.left = `${pct}%`;
      }
    }
  }

  async exportFullZip() {
    if (!this.currentJob) return;
    this.btnExportZip.disabled = true;
    this.btnExportZip.textContent = "⏳ 正在打包 ZIP...";
    try {
      const r = await fetch(`/api/jobs/${this.currentJob.jobId}/export-zip`, { method: "POST" });
      const d = await r.json();
      if (d.zipUrl) {
        const a = document.createElement("a");
        a.href = d.midiUrl || d.zipUrl;
        a.download = d.fileName;
        a.click();
      }
    } finally {
      this.btnExportZip.disabled = false;
      this.btnExportZip.textContent = "📦 打包全軌 ZIP (WAV+MIDI+RPP)";
    }
  }

  formatTime(sec) {
    const mins = Math.floor(sec / 60);
    const rem = sec % 60;
    return `${String(mins).padStart(2, "0")}:${rem.toFixed(3).padStart(6, "0")}`;
  }
}

window.addEventListener("DOMContentLoaded", () => {
  window.stemForgeApp = new StemForgeDawApp();
});
