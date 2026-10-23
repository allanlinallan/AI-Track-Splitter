# StemForge Studio v2.0.1 獨立免安裝發布版 (Portable Win-x64)

### 🚀 新電腦開箱 4 步驟流程：

```text
[新電腦] 下載/複製 StemForge_v2.0.1_Portable_Win64.zip
   │
   ├─► 1. 任意目錄右鍵「解壓縮全部」
   │
   ├─► 2. 雙擊「模型安裝與檢測_Setup_Models.bat」 (確認模型就緒 [OK])
   │
   ├─► 3. 若新電腦有 NVIDIA RTX/GTX 顯卡：
   │      雙擊「Install_GPU_Environment.bat」 (一鍵自動裝好 CUDA PyTorch)
   │
   └─► 4. 雙擊「Start_StemForge_Studio.bat」立即開始分軌！
          (自動啟動並打開瀏覽器 http://localhost:5188)
```

---

### 🌟 全套離線 AI 模型已 100% 內建封裝：
模型位於 `workspace_data\models\`，包含：
- `UVR_MDXNET_KARA_2.onnx`（人聲/和聲分離）
- `Reverb_HQ_By_FoxJoy.onnx`（乾音/殘響分離）
- `huggingface\hub\models--adefossez--HTDemucs`（4-Stem 離線模型）
- `huggingface\hub\models--adefossez--HTDemucs-6s`（6-Stem 離線模型，含吉他/鋼琴）

**新電腦無需連外網下載模型即可離線運算。**

### 🛠️ 附帶防呆檢測與教學手冊：
- `模型安裝與檢測_Setup_Models.bat`：一鍵檢測模型完整性，支援從根目錄自動搬移。
- `模型放置說明_MODELS_SETUP.md`：完整圖文存放指引。
- `README.txt`：繁體中文純文字快速上手手冊。
- `DOC\USER 使用手冊.md`：完整圖文操作說明。

### ⚡ 免安裝 .NET 9.0：
採用 Win-x64 Self-Contained 封裝架構，新電腦不需預先安裝任何 .NET SDK 或 Runtime，解壓縮雙擊 `Start_StemForge_Studio.bat` 即可直接運作！
