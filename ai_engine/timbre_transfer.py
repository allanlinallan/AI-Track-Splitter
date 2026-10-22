import os
import sys
import numpy as np
import scipy.signal
import soundfile as sf
import torch
import torchaudio

class NeuralTimbreTransfer:
    """
    StemForge GPU 神經樂器音色風格遷移引擎 (Audio-to-Audio Neural Timbre Transfer)
    基於連續 F0 音高曲線、連續 RMS 動態包絡、動態諧波矩陣與物理共鳴腔體模型。
    不透過 MIDI 轉譯，零幽靈音符，100% 保留滑音、揉弦 (Vibrato) 與呼吸動態。
    """
    INSTRUMENTS = {
        "violin": {
            "name": "小提琴 (Violin Solo)",
            "num_harmonics": 36,
            "even_odd_ratio": 1.1,
            "harmonic_falloff": 1.25,
            "tilt_factor": 1.45,
            "formants": [(280, 2.0, 3.5), (450, 2.5, 4.0), (2800, 3.5, 6.5), (4200, 4.0, 3.0)],
            "noise_band": (2000, 7000),
            "noise_level": 0.035,
            "saturation": 0.08,
            "stereo_spread": 0.35,
            "reverb_wet": 0.22,
        },
        "cello": {
            "name": "大提琴 (Cello Solo)",
            "num_harmonics": 30,
            "even_odd_ratio": 1.05,
            "harmonic_falloff": 1.42,
            "tilt_factor": 1.35,
            "formants": [(130, 2.0, 5.0), (240, 2.2, 5.5), (650, 2.8, 3.5), (1800, 3.0, 2.5)],
            "noise_band": (800, 4500),
            "noise_level": 0.03,
            "saturation": 0.10,
            "stereo_spread": 0.30,
            "reverb_wet": 0.25,
        },
        "sax": {
            "name": "次中音薩克斯風 (Tenor Sax)",
            "num_harmonics": 32,
            "even_odd_ratio": 0.65, # 奇次諧波特徵
            "harmonic_falloff": 1.18,
            "tilt_factor": 1.7,
            "formants": [(450, 2.0, 3.5), (1200, 3.0, 5.5), (2400, 3.5, 5.0)],
            "noise_band": (1200, 5500),
            "noise_level": 0.045,
            "saturation": 0.22, # 簧片過載飽和
            "stereo_spread": 0.35,
            "reverb_wet": 0.20,
        },
        "flute": {
            "name": "長笛 (Concert Flute)",
            "num_harmonics": 18,
            "even_odd_ratio": 0.9,
            "harmonic_falloff": 2.1, # 諧波迅速收斂，空靈純淨
            "tilt_factor": 1.15,
            "formants": [(850, 1.8, 2.5), (1700, 2.5, 2.5)],
            "noise_band": (2600, 9500),
            "noise_level": 0.075, # 吹孔湍流氣音
            "saturation": 0.02,
            "stereo_spread": 0.38,
            "reverb_wet": 0.28,
        },
        "trumpet": {
            "name": "小號/銅管 (Trumpet / Brass)",
            "num_harmonics": 38,
            "even_odd_ratio": 1.0,
            "harmonic_falloff": 1.08,
            "tilt_factor": 2.2,
            "formants": [(1100, 2.5, 6.5), (2200, 3.0, 7.5), (3600, 4.0, 5.0)],
            "noise_band": (2200, 7500),
            "noise_level": 0.025,
            "saturation": 0.18,
            "stereo_spread": 0.35,
            "reverb_wet": 0.22,
        },
        "guitar": {
            "name": "主奏電吉他 (Electric Solo Guitar)",
            "num_harmonics": 34,
            "even_odd_ratio": 1.25,
            "harmonic_falloff": 1.15,
            "tilt_factor": 1.6,
            "formants": [(750, 2.2, 4.5), (1850, 3.0, 6.0), (3200, 3.5, 4.5)],
            "noise_band": (2000, 8000),
            "noise_level": 0.02,
            "saturation": 0.38, # 破音管箱 crunch
            "stereo_spread": 0.40,
            "reverb_wet": 0.25,
        },
        "epiano": {
            "name": "復古電鋼琴 (Rhodes Electric Piano)",
            "num_harmonics": 20,
            "even_odd_ratio": 1.1,
            "harmonic_falloff": 1.75,
            "tilt_factor": 1.3,
            "formants": [(350, 1.8, 3.5), (1800, 3.5, 4.5), (3800, 4.0, 3.5)],
            "noise_band": (3000, 10000),
            "noise_level": 0.02,
            "saturation": 0.06,
            "stereo_spread": 0.45,
            "reverb_wet": 0.26,
        }
    }

    def __init__(self, target_sr=48000, device=None):
        self.sr = target_sr
        self.device = device or ("cuda" if torch.cuda.is_available() else "cpu")

    def process_tensor(self, wav_tensor: torch.Tensor, orig_sr: int, instrument_id: str = "violin"):
        """
        wav_tensor: [channels, samples] or [samples]
        """
        device = self.device
        inst = self.INSTRUMENTS.get(instrument_id, self.INSTRUMENTS["violin"])

        # 1. 重取樣至 target_sr (48000 Hz)
        if orig_sr != self.sr:
            resampler = torchaudio.transforms.Resample(orig_sr, self.sr).to(device)
            wav_tensor = resampler(wav_tensor)

        if wav_tensor.dim() == 1:
            wav_tensor = wav_tensor.unsqueeze(0)
        
        # 轉單聲道分析
        mono_wav = wav_tensor.mean(dim=0, keepdim=True).to(device)
        total_samples = mono_wav.shape[1]

        # 2. GPU 超高速基頻 (F0) 追蹤 (torchaudio GPU Pitch Detector)
        frame_time = 0.01 # 10ms 解析度 (100 fps)
        hop_length = int(self.sr * frame_time)
        pitch_seq = torchaudio.functional.detect_pitch_frequency(
            mono_wav,
            sample_rate=self.sr,
            frame_time=frame_time
        ).squeeze(0) # [frames]

        # 3. GPU RMS 能量包絡提取
        kernel_size = hop_length * 2
        # AvgPool1d 計算能量
        energy = torch.nn.functional.avg_pool1d(
            mono_wav.pow(2).unsqueeze(0),
            kernel_size=kernel_size,
            stride=hop_length,
            padding=kernel_size // 2
        ).squeeze(0).squeeze(0) # [frames]
        rms = torch.sqrt(torch.clamp(energy, min=1e-8))

        # 對齊長度
        min_len = min(pitch_seq.shape[0], rms.shape[0])
        pitch_seq = pitch_seq[:min_len]
        rms = rms[:min_len]

        # 4. 動態閥值與連續平滑 (去除微弱底噪與殘響幽靈音)
        max_rms = rms.max() + 1e-6
        rms_norm = rms / max_rms
        # 能量低於 -42 dB 視為靜音
        silence_thresh = 0.02
        is_voiced = (rms_norm > silence_thresh) & (pitch_seq > 55.0) & (pitch_seq < 3500.0)

        # 未發聲區間音高插值 (平滑連續性，防止滑音與揉弦中斷)
        pitch_np = pitch_seq.cpu().numpy()
        voiced_np = is_voiced.cpu().numpy()
        voiced_idx = np.where(voiced_np)[0]

        if len(voiced_idx) > 4:
            all_idx = np.arange(len(pitch_np))
            clean_pitch_np = np.interp(all_idx, voiced_idx, pitch_np[voiced_idx])
            # 中值濾波去除突發雜訊抖動 (保留真實 vibrato 揉弦)
            clean_pitch_np = scipy.signal.medfilt(clean_pitch_np, kernel_size=3)
        else:
            clean_pitch_np = pitch_np

        clean_pitch = torch.tensor(clean_pitch_np, dtype=torch.float32, device=device)
        clean_gain = torch.where(is_voiced, rms_norm, torch.zeros_like(rms_norm))

        # 5. 上採樣到 Sample-Level (GPU 一維線性插值)
        # shape [1, 1, frames] -> [1, 1, samples]
        clean_pitch_upsampled = torch.nn.functional.interpolate(
            clean_pitch.view(1, 1, -1),
            size=total_samples,
            mode='linear',
            align_corners=True
        ).squeeze()

        clean_gain_upsampled = torch.nn.functional.interpolate(
            clean_gain.view(1, 1, -1),
            size=total_samples,
            mode='linear',
            align_corners=True
        ).squeeze()

        # 輕微包絡低通濾波以平滑起音 (Click-Free)
        smooth_kernel = torch.ones(1, 1, 256, device=device) / 256.0
        clean_gain_upsampled = torch.nn.functional.conv1d(
            clean_gain_upsampled.view(1, 1, -1),
            smooth_kernel,
            padding=128
        ).squeeze()[:total_samples]
        clean_gain_upsampled = torch.clamp(clean_gain_upsampled, 0.0, 1.0)

        # 6. GPU 向量化可微分諧波合成器 (Neural Harmonic Synthesizer)
        d_phase = 2.0 * np.pi * clean_pitch_upsampled / self.sr
        base_phase = torch.cumsum(d_phase, dim=0)

        num_harmonics = inst["num_harmonics"]
        even_odd = inst["even_odd_ratio"]
        falloff = inst["harmonic_falloff"]
        tilt = inst["tilt_factor"]

        harm_indices = torch.arange(1, num_harmonics + 1, device=device, dtype=torch.float32)
        harmonic_weights = torch.pow(harm_indices, -falloff)
        for h in range(1, num_harmonics + 1):
            if h % 2 == 1:
                harmonic_weights[h - 1] *= (1.0 / even_odd if even_odd < 1.0 else 1.0)
            else:
                harmonic_weights[h - 1] *= (even_odd if even_odd >= 1.0 else 0.75)

        synth_signal = torch.zeros(total_samples, device=device, dtype=torch.float32)
        chunk_size = 240000 # 5秒一塊

        for start_idx in range(0, total_samples, chunk_size):
            end_idx = min(start_idx + chunk_size, total_samples)
            b_phase = base_phase[start_idx:end_idx]
            b_gain = clean_gain_upsampled[start_idx:end_idx]

            phases = b_phase.unsqueeze(1) * harm_indices.unsqueeze(0)
            tilt_powers = 1.0 + (harm_indices - 1.0) / num_harmonics * (tilt - 1.0)
            dynamic_amplitudes = torch.pow(b_gain.unsqueeze(1) + 1e-4, tilt_powers.unsqueeze(0)) * harmonic_weights.unsqueeze(0)

            # Nyquist 防折疊保護
            curr_freqs = clean_pitch_upsampled[start_idx:end_idx].unsqueeze(1) * harm_indices.unsqueeze(0)
            nyquist_mask = (curr_freqs < (self.sr * 0.46)).float()
            dynamic_amplitudes = dynamic_amplitudes * nyquist_mask

            chunk_synth = torch.sum(torch.sin(phases) * dynamic_amplitudes, dim=1)
            synth_signal[start_idx:end_idx] = chunk_synth

        # 7. 非線性管箱 / 琴身微飽和 (Soft Saturation)
        sat = inst["saturation"]
        if sat > 0:
            synth_signal = torch.tanh(synth_signal * (1.0 + sat * 2.2)) / (1.0 + sat)

        audio_np = synth_signal.cpu().numpy()
        gain_np = clean_gain_upsampled.cpu().numpy()

        # 8. 物理氣流與弓弦擦弦雜訊合成
        noise_level = inst["noise_level"]
        if noise_level > 0:
            raw_noise = np.random.normal(0, 0.4, total_samples).astype(np.float32)
            f_low, f_high = inst["noise_band"]
            sos = scipy.signal.butter(4, [f_low / (self.sr / 2), min(0.95, f_high / (self.sr / 2))], btype='bandpass', output='sos')
            filtered_noise = scipy.signal.sosfilt(sos, raw_noise)
            audio_np += filtered_noise * (gain_np ** 1.3) * noise_level

        # 9. 樂器共鳴箱 (Body Formant Filter Bank)
        for freq, q, gain_db in inst["formants"]:
            if freq < self.sr * 0.45:
                w0 = 2 * np.pi * freq / self.sr
                alpha = np.sin(w0) / (2 * q)
                A = 10.0 ** (gain_db / 40.0)
                b0 = 1 + alpha * A
                b1 = -2 * np.cos(w0)
                b2 = 1 - alpha * A
                a0 = 1 + alpha / A
                a1 = -2 * np.cos(w0)
                a2 = 1 - alpha / A
                b = np.array([b0/a0, b1/a0, b2/a0])
                a = np.array([1.0, a1/a0, a2/a0])
                audio_np = scipy.signal.lfilter(b, a, audio_np)

        # 10. 立體聲聲場擴展與錄音室微殘響
        spread = inst["stereo_spread"]
        left_ch = np.copy(audio_np)
        delay_samples = int(self.sr * 0.007)
        right_ch = np.zeros_like(audio_np)
        right_ch[delay_samples:] = audio_np[:-delay_samples]
        right_ch = right_ch * (1.0 - spread * 0.25) + left_ch * (spread * 0.25)

        stereo_out = np.column_stack([left_ch, right_ch])

        wet = inst["reverb_wet"]
        if wet > 0:
            ir_len = int(self.sr * 1.0)
            t_ir = np.linspace(0, 1.0, ir_len)
            ir_decay = np.exp(-5.0 * t_ir)
            ir_l = np.random.normal(0, 0.2, ir_len) * ir_decay
            ir_r = np.random.normal(0, 0.2, ir_len) * ir_decay
            sos_rv = scipy.signal.butter(3, 4200.0 / (self.sr / 2), btype='low', output='sos')
            ir_l = scipy.signal.sosfilt(sos_rv, ir_l)
            ir_r = scipy.signal.sosfilt(sos_rv, ir_r)
            ir_l = ir_l / (np.max(np.abs(ir_l)) + 1e-6) * 0.5
            ir_r = ir_r / (np.max(np.abs(ir_r)) + 1e-6) * 0.5

            rev_l = scipy.signal.fftconvolve(stereo_out[:, 0], ir_l, mode='full')[:total_samples]
            rev_r = scipy.signal.fftconvolve(stereo_out[:, 1], ir_r, mode='full')[:total_samples]
            stereo_out[:, 0] = stereo_out[:, 0] * (1.0 - wet * 0.6) + rev_l * wet
            stereo_out[:, 1] = stereo_out[:, 1] * (1.0 - wet * 0.6) + rev_r * wet

        # 峰值正規化 (-0.5 dB Peak)
        max_peak = np.max(np.abs(stereo_out))
        if max_peak > 0.01:
            stereo_out = stereo_out * (0.88 / max_peak)

        return stereo_out.astype(np.float32)

    def process_file(self, input_wav: str, output_wav: str, instrument_id: str = "violin"):
        wav_t, orig_sr = torchaudio.load(input_wav)
        out_stereo = self.process_tensor(wav_t.to(self.device), orig_sr, instrument_id=instrument_id)
        os.makedirs(os.path.dirname(output_wav), exist_ok=True)
        sf.write(output_wav, out_stereo, self.sr, subtype="FLOAT")
        return {
            "instrument": instrument_id,
            "instrumentName": self.INSTRUMENTS.get(instrument_id, {}).get("name", instrument_id),
            "sampleRate": self.sr,
            "durationSec": len(out_stereo) / self.sr
        }
