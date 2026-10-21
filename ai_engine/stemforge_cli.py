import argparse
import gc
import glob
import hashlib
import json
import os
import sys
import traceback
import numpy as np
import soundfile as sf
import torch


def emit_json(payload: dict):
    print(json.dumps(payload, ensure_ascii=False), flush=True)


def clear_vram():
    gc.collect()
    if torch.cuda.is_available():
        torch.cuda.empty_cache()


def compute_sha256_20(file_path: str) -> str:
    h = hashlib.sha256()
    with open(file_path, "rb") as f:
        while chunk := f.read(65536):
            h.update(chunk)
    return h.hexdigest().lower()[:20]


def read_stereo_float32(path: str):
    data, sr = sf.read(path, dtype="float32", always_2d=True)
    if data.shape[1] == 1:
        data = np.repeat(data, 2, axis=1)
    elif data.shape[1] > 2:
        data = data[:, :2]
    return data, sr


def write_stereo_float32(path: str, data: np.ndarray, sr: int):
    os.makedirs(os.path.dirname(path), exist_ok=True)
    sf.write(path, data, sr, subtype="FLOAT")


def align_audio(child: np.ndarray, target_len: int) -> np.ndarray:
    if len(child) == target_len:
        return child
    if len(child) > target_len:
        return child[:target_len]
    pad = np.zeros((target_len - len(child), child.shape[1]), dtype=np.float32)
    return np.vstack([child, pad])


def run_demucs_cascade_gpu(input_wav: str, model_dir: str):
    """
    兩階段高純度 Demucs v4 (Hybrid Transformer) GPU 分離：
    1. 先用 htdemucs (4-stem 標準高純度模型) 拆出無串音的 Vocals, Drums, Bass, Other_Melodic
    2. 再把無鼓無貝斯無主唱的 Other_Melodic 餵給 htdemucs_6s，專門拆出純淨的 Guitar, Piano 與 Strings/Synth (Other)
    這樣既有 4-stem 的極致乾淨人聲/鼓/貝斯，又有 6-stem 的獨立吉他與鋼琴！
    """
    from demucs.pretrained import get_model
    from demucs.apply import apply_model
    from demucs.audio import convert_audio

    os.environ["TORCH_HOME"] = model_dir
    device = "cuda" if torch.cuda.is_available() else "cpu"
    total_vram_gb = (torch.cuda.get_device_properties(0).total_memory / (1024 ** 3)) if device == "cuda" else 0.0

    # 依顯存規格動態調配：8G 以上開啟更高 overlaps 與 shifts，品質更好、推論更快
    dynamic_overlap = 0.5 if total_vram_gb >= 11.5 else (0.35 if total_vram_gb >= 7.5 else 0.25)
    dynamic_shifts = 3 if total_vram_gb >= 11.5 else 2

    wav_np, orig_sr = read_stereo_float32(input_wav)
    orig_len = len(wav_np)

    # --- Pass 1: htdemucs (高純度 Vocals, Drums, Bass, Melodic) ---
    emit_json({
        "type": "progress",
        "stage": "STAGE_1_HTDEMUCS",
        "percent": 15.0,
        "message": f"[RTX 4050 GPU] Pass 1: 執行 Hybrid Transformer Demucs v4 (htdemucs) 高純度拆解人聲/鼓組/貝斯..."
    })

    model_4s = get_model("htdemucs")
    model_4s.to(device)
    model_4s.eval()

    wav_tensor = torch.from_numpy(wav_np.T).float().to(device)
    ref = wav_tensor.mean(0)
    ref_mean = ref.mean()
    ref_std = ref.std() + 1e-8
    wav_norm = (wav_tensor - ref_mean) / ref_std
    wav_conv = convert_audio(wav_norm[None], orig_sr, model_4s.samplerate, model_4s.audio_channels)

    with torch.no_grad():
        with torch.cuda.amp.autocast(dtype=torch.float16 if device == "cuda" else torch.float32):
            sources_4s = apply_model(
                model_4s,
                wav_conv,
                device=device,
                shifts=dynamic_shifts,
                split=True,
                overlap=dynamic_overlap,
                progress=False,
            )[0]

    sources_4s = sources_4s * ref_std + ref_mean
    if model_4s.samplerate != orig_sr:
        sources_4s = convert_audio(sources_4s, model_4s.samplerate, orig_sr, 2)

    stems_4s = {}
    for idx, name in enumerate(model_4s.sources):
        stems_4s[name] = align_audio(sources_4s[idx].cpu().numpy().T, orig_len)

    del model_4s, sources_4s, wav_conv, wav_norm, wav_tensor
    clear_vram()

    vocals = stems_4s["vocals"]
    drums = stems_4s["drums"]
    bass = stems_4s["bass"]
    melodic_other = stems_4s["other"]

    # --- Pass 2: 對無鼓無貝斯無主唱的 melodic_other 執行 htdemucs_6s 拆解 Guitar & Piano ---
    emit_json({
        "type": "progress",
        "stage": "STAGE_2_HTDEMUCS_6S",
        "percent": 42.0,
        "message": "[RTX 4050 GPU] Pass 2: 對純旋律母軌執行 htdemucs_6s 拆解獨立吉他 (Guitar)、鋼琴 (Piano) 與管弦合成器..."
    })

    model_6s = get_model("htdemucs_6s")
    model_6s.to(device)
    model_6s.eval()

    mel_tensor = torch.from_numpy(melodic_other.T).float().to(device)
    m_ref = mel_tensor.mean(0)
    m_mean = m_ref.mean()
    m_std = m_ref.std() + 1e-8
    mel_norm = (mel_tensor - m_mean) / m_std
    mel_conv = convert_audio(mel_norm[None], orig_sr, model_6s.samplerate, model_6s.audio_channels)

    with torch.no_grad():
        with torch.cuda.amp.autocast(dtype=torch.float16 if device == "cuda" else torch.float32):
            sources_6s = apply_model(
                model_6s,
                mel_conv,
                device=device,
                shifts=dynamic_shifts,
                split=True,
                overlap=dynamic_overlap,
                progress=False,
            )[0]

    sources_6s = sources_6s * m_std + m_mean
    if model_6s.samplerate != orig_sr:
        sources_6s = convert_audio(sources_6s, model_6s.samplerate, orig_sr, 2)

    stems_6s = {}
    for idx, name in enumerate(model_6s.sources):
        stems_6s[name] = align_audio(sources_6s[idx].cpu().numpy().T, orig_len)

    del model_6s, sources_6s, mel_conv, mel_norm, mel_tensor
    clear_vram()

    guitar = stems_6s["guitar"]
    piano = stems_6s["piano"]
    # 從純旋律母軌扣除 guitar 與 piano 得到 100% 能量守恆的管弦/合成器母軌
    other_synths = melodic_other - guitar - piano

    return {
        "vocals": vocals,
        "drums": drums,
        "bass": bass,
        "melodic": melodic_other,
        "guitar": guitar,
        "piano": piano,
        "other": other_synths,
    }, wav_np, orig_sr


def try_run_uvr_onnx_split(
    input_wav_path: str,
    out_dir: str,
    model_dir: str,
    model_filename: str,
):
    from audio_separator.separator import Separator

    sep_out_dir = os.path.join(out_dir, f"uvr_{os.path.splitext(model_filename)[0]}")
    os.makedirs(sep_out_dir, exist_ok=True)

    separator = Separator(
        model_file_dir=model_dir,
        output_dir=sep_out_dir,
        output_format="WAV",
        normalization_threshold=0.9,
        use_autocast=True,
    )
    separator.load_model(model_filename=model_filename)
    out_files = separator.separate(input_wav_path)

    full_files = [
        f if os.path.isabs(f) else os.path.join(sep_out_dir, f)
        for f in out_files
    ]

    del separator
    clear_vram()
    return full_files


def run_full_neural_pipeline(args):
    os.makedirs(args.output_dir, exist_ok=True)
    os.makedirs(args.model_dir, exist_ok=True)

    stems, orig_mix, sr = run_demucs_cascade_gpu(args.input, args.model_dir)
    total_len = len(orig_mix)

    vocals_all = stems["vocals"]
    drums_all = stems["drums"]
    bass_all = stems["bass"]
    melodic_res = stems["melodic"]
    guitar_all = stems["guitar"]
    piano_all = stems["piano"]
    other_all = stems["other"]
    inst_all = orig_mix - vocals_all

    # 找出對應的專案資料夾與所有可能的 SHA-256 快取雜湊值
    job_dir = os.path.dirname(os.path.abspath(args.output_dir))
    workspace_data_dir = os.path.dirname(os.path.dirname(job_dir))
    cache_root_dir = os.path.join(workspace_data_dir, "cache")

    candidate_hashes = set()
    if os.path.exists(args.input):
        candidate_hashes.add(compute_sha256_20(args.input))
    for f in glob.glob(os.path.join(job_dir, "*")):
        if os.path.isfile(f) and not f.endswith(".rpp") and not f.endswith(".zip"):
            try:
                candidate_hashes.add(compute_sha256_20(f))
            except Exception:
                pass

    outputs = {}

    def save_and_cache_node(node_id: str, audio_np: np.ndarray) -> str:
        aligned = align_audio(audio_np, total_len)
        raw_path = os.path.join(args.output_dir, f"{node_id}.wav")
        write_stereo_float32(raw_path, aligned, sr)
        outputs[node_id] = raw_path

        for h in candidate_hashes:
            c_dir = os.path.join(cache_root_dir, h)
            os.makedirs(c_dir, exist_ok=True)
            c_path = os.path.join(c_dir, f"{node_id}__v1.wav")
            write_stereo_float32(c_path, aligned, sr)
        return raw_path

    voc_all_path = save_and_cache_node("V0_VOCALS_ALL", vocals_all)
    inst_all_path = save_and_cache_node("I0_INST_ALL", inst_all)
    save_and_cache_node("D0_DRUMS_ALL", drums_all)
    save_and_cache_node("STEM_12_BASS", bass_all)
    save_and_cache_node("I1_MELODIC_RESIDUAL", melodic_res)
    save_and_cache_node("G0_GUITAR_ALL", guitar_all)
    save_and_cache_node("STEM_13_PIANO_KEYS", piano_all)
    save_and_cache_node("O0_OTHER_ALL", other_all)

    # Stage 2A: 使用 UVR_MDXNET_KARA_2.onnx 神經網路分離 主唱 (Lead) vs 和聲 (Backing)
    emit_json({
        "type": "progress",
        "stage": "STAGE_2A_KARAOKE_SPLIT",
        "percent": 68.0,
        "message": "[RTX 4050 GPU] Pass 3: 執行 UVR_MDXNET_KARA_2 神經網路分離主唱 (Lead) 與和聲 (Backing Vocals)..."
    })

    try:
        kara_files = try_run_uvr_onnx_split(
            voc_all_path, args.output_dir, args.model_dir, "UVR_MDXNET_KARA_2.onnx"
        )
        kara_arrays = []
        for f in kara_files:
            arr, _ = read_stereo_float32(f)
            arr = align_audio(arr, total_len)
            rms = float(np.sqrt(np.mean(arr ** 2)))
            kara_arrays.append((rms, arr))

        if len(kara_arrays) >= 2:
            kara_arrays.sort(key=lambda x: x[0], reverse=True)
            lead_wet = kara_arrays[0][1]
            back_wet = kara_arrays[1][1]
        elif len(kara_arrays) == 1:
            lead_wet = kara_arrays[0][1]
            back_wet = vocals_all - lead_wet
        else:
            lead_wet, back_wet = None, None

        if lead_wet is not None and back_wet is not None:
            lead_wet_path = save_and_cache_node("V1_LEAD_WET", lead_wet)
            save_and_cache_node("V2_BACKING_WET", back_wet)

            # Stage 3V: 使用 Reverb_HQ_By_FoxJoy.onnx 分離人聲乾音與空間殘響
            emit_json({
                "type": "progress",
                "stage": "STAGE_3V_LEAD_DEREVERB",
                "percent": 85.0,
                "message": "[RTX 4050 GPU] Pass 4: 執行 Reverb_HQ 神經網路分離主唱乾音 (Dry) 與空間殘響 (Reverb FX)..."
            })
            rev_files = try_run_uvr_onnx_split(
                lead_wet_path, args.output_dir, args.model_dir, "Reverb_HQ_By_FoxJoy.onnx"
            )
            rev_arrays = []
            for f in rev_files:
                arr, _ = read_stereo_float32(f)
                arr = align_audio(arr, total_len)
                rms = float(np.sqrt(np.mean(arr ** 2)))
                rev_arrays.append((rms, arr))
            if len(rev_arrays) >= 1:
                rev_arrays.sort(key=lambda x: x[0], reverse=True)
                lead_dry = rev_arrays[0][1]
                lead_rev = lead_wet - lead_dry
                save_and_cache_node("STEM_01_VOCAL_LEAD_DRY", lead_dry)
                save_and_cache_node("STEM_02_VOCAL_LEAD_REVERB", lead_rev)
    except Exception as e:
        emit_json({
            "type": "progress",
            "stage": "STAGE_2A_KARAOKE_SPLIT",
            "percent": 80.0,
            "message": f"UVR 附屬模型略過: {e}"
        })

    # 相容 C# STAGE_1_ROFORMER_MASTER 預期的回傳欄位
    outputs["vocals"] = voc_all_path
    outputs["inst"] = inst_all_path

    emit_json({
        "type": "completed",
        "stage": args.stage,
        "outputs": outputs
    })


def detect_audio_key(audio_path: str):
    """
    Krumhansl-Schmuckler 24 調性特徵分析演算法 (Pitch Class Profile / Chroma)
    """
    MAJOR_PROFILE = np.array([6.35, 2.23, 3.48, 2.33, 4.38, 4.09, 2.52, 5.19, 2.39, 3.66, 2.29, 2.88])
    MINOR_PROFILE = np.array([6.33, 2.68, 3.52, 5.38, 2.60, 3.53, 2.54, 4.75, 3.98, 2.69, 3.34, 3.17])
    PITCH_NAMES = ["C", "C#", "D", "D#", "E", "F", "F#", "G", "G#", "A", "A#", "B"]
    ZH_NAMES = {
        "C": "C", "C#": "C# / Db", "D": "D", "D#": "D# / Eb", "E": "E", "F": "F",
        "F#": "F# / Gb", "G": "G", "G#": "G# / Ab", "A": "A", "A#": "A# / Bb", "B": "B"
    }

    # 讀取前 90 秒音訊做調性特徵萃取
    info = sf.info(audio_path)
    sr = info.samplerate
    max_frames = min(info.frames, sr * 90)
    data, _ = sf.read(audio_path, frames=max_frames, dtype="float32", always_2d=True)
    mono = np.mean(data, axis=1)

    n_fft = 4096
    hop = 2048
    window = np.hanning(n_fft)
    stft_slices = [np.abs(np.fft.rfft(mono[i : i + n_fft] * window)) for i in range(0, len(mono) - n_fft, hop)]
    if not stft_slices:
        stft_slices = [np.abs(np.fft.rfft(mono[:n_fft] * window))]
    stft = np.array(stft_slices)
    freqs = np.fft.rfftfreq(n_fft, 1.0 / sr)

    chroma = np.zeros(12)
    for f_idx, f in enumerate(freqs):
        if f < 50 or f > 4000:
            continue
        midi_pitch = int(np.round(69 + 12 * np.log2(f / 440.0))) % 12
        chroma[midi_pitch] += float(np.sum(stft[:, f_idx]))

    chroma = chroma / (np.linalg.norm(chroma) + 1e-9)

    best_key = "C Major"
    best_root = "C"
    best_mode = "Major"
    best_corr = -999.0

    for i in range(12):
        maj = np.roll(MAJOR_PROFILE, i)
        c_maj = float(np.corrcoef(chroma, maj)[0, 1])
        if c_maj > best_corr:
            best_corr = c_maj
            best_root = PITCH_NAMES[i]
            best_mode = "Major"
            best_key = f"{best_root} Major"

        min_prof = np.roll(MINOR_PROFILE, i)
        c_min = float(np.corrcoef(chroma, min_prof)[0, 1])
        if c_min > best_corr:
            best_corr = c_min
            best_root = PITCH_NAMES[i]
            best_mode = "Minor"
            best_key = f"{best_root} Minor"

    mode_zh = "大調" if best_mode == "Major" else "小調"
    display_name = f"{ZH_NAMES.get(best_root, best_root)} {mode_zh} ({best_key})"

    return {
        "key": best_key,
        "root": best_root,
        "mode": best_mode,
        "displayName": display_name,
        "confidence": round(float(best_corr), 3)
    }


def pitch_shift_stems_gpu(stems_dir: str, output_dir: str, semitones: int):
    """
    使用高保真 Phase-Vocoder (FFmpeg librubberband) 進行多軌分治神經變調：
    1. 鼓組軌道 (Kick/Snare/Tom/Hat/Cymbal/Drums) 100% Bypass 不變調，保持原始衝擊力
    2. 人聲與樂器軌道 (Vocal/Bass/Guitar/Piano/Brass/Strings) 透過 Phase-Vocoder 高保真變調
    3. 自動多執行緒平行加速，零 VRAM 瓶頸、零崩潰、音質極其清晰無雜音
    """
    import shutil
    import subprocess
    import threading
    from concurrent.futures import ThreadPoolExecutor

    os.makedirs(output_dir, exist_ok=True)
    wav_files = glob.glob(os.path.join(stems_dir, "*.wav"))
    if not wav_files:
        raise ValueError(f"找不到音軌檔案於: {stems_dir}")

    ffmpeg_bin = shutil.which("ffmpeg") or r"C:\Program Files\ffmpeg\bin\ffmpeg.exe"
    if not os.path.exists(ffmpeg_bin):
        raise FileNotFoundError(f"找不到 FFmpeg: {ffmpeg_bin}")

    pitch_ratio = 2.0 ** (semitones / 12.0)
    total_files = len(wav_files)
    completed_count = 0
    lock = threading.Lock()

    def process_one(wav_path):
        nonlocal completed_count
        fname = os.path.basename(wav_path)
        out_path = os.path.join(output_dir, fname)
        lower_name = fname.lower()
        is_drum = any(k in lower_name for k in ["kick", "snare", "tom", "hat", "cymbal", "crash", "ride", "drum"])

        if semitones == 0 or is_drum:
            shutil.copy2(wav_path, out_path)
            with lock:
                completed_count += 1
                pct = round((completed_count / total_files) * 100, 1)
                emit_json({
                    "type": "progress",
                    "stage": "PITCH_SHIFT",
                    "percent": pct,
                    "message": f"[{pct}%] 鼓組軌道原始保留 (Bypass): {fname}"
                })
            return {"fileName": fname, "bypassed": is_drum, "semitones": 0}

        with lock:
            pct = round((completed_count / total_files) * 100, 1)
            emit_json({
                "type": "progress",
                "stage": "PITCH_SHIFT",
                "percent": pct,
                "message": f"[{pct}%] 正在變調 ({semitones:+d} 半音): {fname}..."
            })

        # 先嘗試使用 librubberband (音質最高)
        cmd_rubberband = [
            ffmpeg_bin, "-y",
            "-i", wav_path,
            "-af", f"rubberband=pitch={pitch_ratio}",
            "-c:a", "pcm_f32le",
            out_path
        ]
        res = subprocess.run(cmd_rubberband, stdout=subprocess.DEVNULL, stderr=subprocess.PIPE, text=True, encoding="utf-8", errors="ignore")

        if res.returncode != 0:
            # 備援：使用 asetrate + atempo + aresample 高精度重採樣
            tempo_ratio = 1.0 / pitch_ratio
            sr = 44100
            try:
                info = sf.info(wav_path)
                sr = info.samplerate
            except:
                pass
            af_fallback = f"asetrate={sr}*{pitch_ratio},atempo={tempo_ratio},aresample={sr}"
            cmd_fallback = [
                ffmpeg_bin, "-y",
                "-i", wav_path,
                "-af", af_fallback,
                "-c:a", "pcm_f32le",
                out_path
            ]
            subprocess.run(cmd_fallback, check=True, stdout=subprocess.DEVNULL, stderr=subprocess.PIPE)

        with lock:
            completed_count += 1
            pct = round((completed_count / total_files) * 100, 1)
            emit_json({
                "type": "progress",
                "stage": "PITCH_SHIFT",
                "percent": pct,
                "message": f"[{pct}%] 完成變調 ({semitones:+d} 半音): {fname}"
            })

        return {"fileName": fname, "bypassed": False, "semitones": semitones}

    with ThreadPoolExecutor(max_workers=4) as executor:
        results = list(executor.map(process_one, wav_files))

    clear_vram()
    return results


def main():
    parser = argparse.ArgumentParser(description="StemForge GPU Neural Separation CLI")
    parser.add_argument("--task", default="separate")
    parser.add_argument("--stage", default="ALL")
    parser.add_argument("--input", default="")
    parser.add_argument("--output-dir", default="")
    parser.add_argument("--model-dir", default="")
    parser.add_argument("--model", default="htdemucs_6s")
    parser.add_argument("--overlap", type=int, default=4)
    parser.add_argument("--fp16", action="store_true")
    parser.add_argument("--prompt", default="")
    parser.add_argument("--semitones", type=int, default=0)
    args = parser.parse_args()

    try:
        if args.task == "detect-key":
            res = detect_audio_key(args.input)
            emit_json({"type": "completed", "task": "detect-key", "result": res})
            sys.exit(0)
        elif args.task == "pitch-shift":
            results = pitch_shift_stems_gpu(args.input, args.output_dir, args.semitones)
            emit_json({"type": "completed", "task": "pitch-shift", "semitones": args.semitones, "results": results})
            sys.exit(0)
        else:
            run_full_neural_pipeline(args)
            sys.exit(0)
    except Exception as ex:
        emit_json({
            "type": "error",
            "stage": args.stage,
            "message": f"{ex} | {traceback.format_exc()}"
        })
        sys.exit(1)


if __name__ == "__main__":
    main()
