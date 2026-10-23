# StemForge Studio v2.0.1 獨立免安裝發布版 (Portable Win-x64)

### 🌟 版本核心亮點

本版本為 **StemForge Studio v2.0.1** 獨立免安裝發布包，針對新電腦與跨環境部署進行了完整自包含（Self-Contained）架構優化：

- **全套離線 AI 模型已 100% 內建封裝**：
  模型位於 `workspace_data\models\`，包含：
  - `UVR_MDXNET_KARA_2.onnx`（人聲/和聲分離）
  - `Reverb_HQ_By_FoxJoy.onnx`（乾音/殘響分離）
  - `huggingface\hub\models--adefossez--HTDemucs`（4-Stem 離線模型）
  - `huggingface\hub\models--adefossez--HTDemucs-6s`（6-Stem 離線模型，含吉他/鋼琴）
  
  **新電腦無需連外網下載模型即可離線運算。**

- **附帶防呆檢測與教學手冊**：
  - `模型安裝與檢測_Setup_Models.bat`：一鍵自動掃描並校驗模型檔案完整性，支援從根目錄自動搬移。
  - `模型放置說明_MODELS_SETUP.md`：完整圖文目錄樹與離線模型配置手冊。
  - `README.txt`：繁體中文純文字快速上手指引。
  - `DOC\USER 使用手冊.md`：完整圖文操作說明。

- **免安裝 .NET Runtime / SDK**：
  - 核心採用 .NET 9.0 Win-x64 Self-Contained 封裝，任何 Windows 10/11 電腦解壓直接雙擊 `Start_StemForge_Studio.bat` 即可啟動。
