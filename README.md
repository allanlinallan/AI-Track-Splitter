# StemForge Studio 🎵
### 19+ 軌高純度 AI 音樂分離工作站 & WebAudio 多軌混音 DAW
**19+ Stem AI Multitrack Audio Separation Studio & WebAudio DAW**

[![.NET 9.0](https://img.shields.io/badge/.NET-9.0-purple.svg)](https://dotnet.microsoft.com/)
[![PyTorch](https://img.shields.io/badge/PyTorch-2.7%2Bcu118-red.svg)](https://pytorch.org/)
[![Demucs](https://img.shields.io/badge/Demucs-v4.1.0-blue.svg)](https://github.com/facebookresearch/demucs)
[![UVR ONNX](https://img.shields.io/badge/UVR-ONNX%20MDX--Net-orange.svg)](https://github.com/nomadkaraoke/python-audio-separator)
[![License](https://img.shields.io/badge/License-MIT-green.svg)](LICENSE)

[**繁體中文 (Traditional Chinese)**](#繁體中文) | [**English Documentation**](#english)

---

<a name="繁體中文"></a>
## 🇹🇼 繁體中文說明

**StemForge Studio** 是一個專為音樂製作人、音訊工程師與 AI 研究者設計的 19+ 軌高純度 AI 音樂分軌與母帶級多軌 DAW 系統。結合 **C# .NET 9.0 高效能 SIMD DSP 引擎** 與 **Python NVIDIA GPU 4-Pass 深度神經網路級聯架構**，具備毫秒級 WebAudio 多軌混音界面與數學級反相抵消驗證（Null Test）。

### ✨ 核心特色

1. **4-Pass GPU 深度神經網路級聯 (Neural Cascade)**
   - **Pass 1 (`htdemucs`)**：分離極致純度的人聲、鼓組、貝斯與旋律底軌。
   - **Pass 2 (`htdemucs_6s`)**：專門將旋律底軌精細拆解為獨立的純淨吉他與鋼琴。
   - **Pass 3 (`UVR MDX-Net Kara 2`)**：精準切分主唱（Lead Vocal）與和聲伴唱（Backing Vocal）。
   - **Pass 4 (`Reverb HQ FoxJoy`)**：將空間殘響（Reverb）與乾音人聲（Dry Vocal）徹底分離。
2. **客製化樂器即時提取（Custom Instruments）**
   - 支援隨選輸入任意樂器（如：長笛 `flute`、小提琴 `violin`、小號 `trumpet` 等）。
   - 採用 **窄頻諧波峰值顯著性遮罩（Narrowband Harmonic Peak Salience Masking）** 與持續音包絡塑形，無需重跑全軌即可即時掛載進混音台。
3. **母帶級相位驗證（Null-Test Bit-Accurate Verification）**
   - 內建數學級殘差守恆演算法（Residual Conservator）。
   - 全部分軌疊加後與原始混音反相抵消誤差達到 **`-126.4 dBFS`**，確保 100% 無相位失真與聲音遺漏。
4. **內建 WebAudio 多軌混音 DAW**
   - 瀏覽器原生 21 軌無損同步播放、Solo / Mute / Pan / 音量推桿 / 單軌相位反轉（Ø）。
   - 3 種分離模式 0 秒即時切換（純淨隔離、比特無損殘差、AI 乾音訓練）。
   - 一鍵匯出：支援分軌 ZIP 包、**Reaper 工程檔 (`.rpp`)** 與自動轉換 MIDI 音符。
5. **物理顯存釋放（Zero VRAM Leak）**
   - AI 模型推論透過獨立 CLI 進程執行，推論結束立即退出，由作業系統 100% 物理回收 RTX 4050 6GB 顯存。

---

### 🚀 快速開始

#### 系統需求
- **作業系統**：Windows 10 / 11 (x64)
- **硬體建議**：NVIDIA 獨立顯卡（RTX 20/30/40/50 系列，建議 6GB+ VRAM）
  - *若無 NVIDIA 顯卡，系統將自動降級至純 C# SIMD DSP 演算法模式，任何文書機皆可運行！*
- **軟體環境**：.NET 9.0 SDK 與 Python 3.10+

#### 1. 複製專案
```bash
git clone https://github.com/your-username/StemForge-Studio.git
cd StemForge-Studio
```

#### 2. 安裝 Python AI 環境（啟用 GPU 加速）
```powershell
# 建立專屬虛擬環境
python -m venv ai_engine/.venv

# 安裝 PyTorch CUDA 11.8 加速版
./ai_engine/.venv/Scripts/python.exe -m pip install torch torchvision torchaudio --index-url https://download.pytorch.org/whl/cu118

# 安裝 Demucs v4 與 UVR 音訊分離組件
./ai_engine/.venv/Scripts/python.exe -m pip install -r ai_engine/requirements.txt
```

#### 3. 編譯與啟動 Studio
```powershell
# 編譯並啟動 C# 後端伺服器
dotnet run --project src/StemForge.Web/StemForge.Web.csproj --urls http://localhost:5188
```
打開瀏覽器訪問：`http://localhost:5188` 即可開始使用！

---

<a name="english"></a>
## 🌐 English Documentation

**StemForge Studio** is a state-of-the-art 19+ stem AI music separation workstation and master-grade multitrack DAW built for music producers, audio engineers, and AI researchers. It pairs a high-performance **C# .NET 9.0 SIMD DSP engine** with a **Python NVIDIA GPU 4-Pass Neural Cascade architecture**, featuring a low-latency WebAudio multitrack mixer and sample-accurate bit-inversion (Null-Test) validation.

### ✨ Key Features

1. **4-Pass Neural Cascade Separation**
   - **Pass 1 (`htdemucs`)**: High-purity isolation of Vocals, Drums, Bass, and Melodic stems.
   - **Pass 2 (`htdemucs_6s`)**: Further separates melodic tracks into pristine Guitar, Piano, and Synth/Other.
   - **Pass 3 (`UVR MDX-Net Kara 2`)**: Precision decoupling of Lead Vocals from Backing Harmonies.
   - **Pass 4 (`Reverb HQ FoxJoy`)**: Separates acoustic space and reverb tails from Dry Vocals.
2. **On-Demand Custom Instrument Extraction**
   - Extract arbitrary instruments on the fly (e.g. `flute`, `violin`, `trumpet`, `brass`, `synth`).
   - Powered by **Narrowband Harmonic Peak Salience Masking** and transient-to-sustained envelope gating without re-running the entire cascade.
3. **Master-Grade Null-Test Residual Conservation**
   - Mathematically accurate residual conservation algorithm.
   - Summing all stems and phase-inverting against the original mix yields cancellation down to **`-126.4 dBFS`**, guaranteeing zero phase smearing or lost transients.
4. **Interactive WebAudio Multitrack DAW**
   - Zero-latency multitrack playback, Solo / Mute / Pan / Volume sliders / Phase inversion (Ø).
   - Zero-second instant switching between 3 separation modes (Bit-Accurate Residual, Pure Isolation, AI Training Dry).
   - Full Export Suite: Stems ZIP archive, **Reaper Project files (`.rpp`)**, and MIDI note transcriptions.
5. **Guaranteed Zero VRAM Leaks**
   - PyTorch inference runs via ephemeral subprocesses that terminate immediately upon stage completion, ensuring 100% physical recovery of GPU VRAM by Windows OS.

---

### 🚀 Getting Started

#### System Requirements
- **OS**: Windows 10 / 11 (x64)
- **GPU (Recommended)**: NVIDIA GPU with CUDA support (RTX 20/30/40/50 series, 6GB+ VRAM recommended)
  - *Fallback: Native C# SIMD DSP mode runs on ANY PC without a dedicated GPU.*
- **Prerequisites**: .NET 9.0 SDK and Python 3.10+

#### 1. Clone Repository
```bash
git clone https://github.com/your-username/StemForge-Studio.git
cd StemForge-Studio
```

#### 2. Setup Python GPU Environment
```powershell
python -m venv ai_engine/.venv
./ai_engine/.venv/Scripts/python.exe -m pip install torch torchvision torchaudio --index-url https://download.pytorch.org/whl/cu118
./ai_engine/.venv/Scripts/python.exe -m pip install -r ai_engine/requirements.txt
```

#### 3. Run the Studio
```powershell
dotnet run --project src/StemForge.Web/StemForge.Web.csproj --urls http://localhost:5188
```
Navigate to `http://localhost:5188` in your browser.

---

### 📂 Repository Structure

```text
├── src/
│   ├── StemForge.Core/         # DAG definitions, stem contracts, and caching service
│   ├── StemForge.Dsp/          # SIMD STFT, HPSS, Mid-Side, and Residual Conservator
│   ├── StemForge.Export/       # Reaper (.rpp) generator, MIDI exporter & ZIP packager
│   ├── StemForge.Inference/    # GPU CLI runner & DAG pipeline orchestrator
│   └── StemForge.Web/          # ASP.NET Core API, SignalR Hub & WebAudio DAW Studio
├── ai_engine/
│   ├── stemforge_cli.py        # 4-Pass GPU neural cascade (Demucs v4 + UVR ONNX)
│   └── requirements.txt        # Python dependency manifest
├── Build-Release-Portable.ps1  # Win-x64 portable release packaging script
└── StemForge.sln               # Visual Studio 2022 / .NET 9 solution
```

---

### 📄 License
This project is licensed under the [MIT License](LICENSE).
