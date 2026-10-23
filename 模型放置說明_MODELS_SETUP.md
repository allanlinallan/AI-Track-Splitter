# 🧠 StemForge Studio — AI 模型庫放置與安裝指引

本手冊指導使用者在解壓縮 `StemForge Studio` 綠色免安裝版（或獨立模型壓縮包）後，如何正確確認或放置 AI 神經網路模型檔案。

---

## 📌 一、模型標準存放目錄

所有 AI 模型檔案的標準存放目錄為：  
👉 **`[工作站根目錄]\workspace_data\models\`**

完整目錄結構應如下所示：

```text
StemForge_Portable/
├── Start_StemForge_Studio.bat          <-- 啟動主程式
├── 模型安裝與檢測_Setup_Models.bat     <-- 一鍵檢測模型是否放對
├── Install_GPU_Environment.bat         <-- NVIDIA GPU 環境安裝腳本
├── StemForge.Web.exe                   <-- 核心服務執行檔
│
└── workspace_data/
    └── models/                         <-- 【★ AI 模型標準存放處】
        ├── UVR_MDXNET_KARA_2.onnx      <-- (52.7 MB) 主唱與和聲分離 ONNX 模型
        ├── Reverb_HQ_By_FoxJoy.onnx    <-- (66.7 MB) 人聲純乾音與殘響分離 ONNX 模型
        ├── mdx_model_data.json         <-- 模型配置參數
        ├── vr_model_data.json          <-- 模型配置參數
        ├── download_checks.json        <-- 雜湊校驗檔案
        └── huggingface/                <-- 【Demucs v4 離線神經模型庫】
            └── hub/
                ├── models--adefossez--HTDemucs/      <-- 4-Stem (鼓/貝斯/人聲/旋律)
                └── models--adefossez--HTDemucs-6s/   <-- 6-Stem (加入吉他/鋼琴細分)
```

---

## 🛠️ 二、解壓縮與安裝教學

### 狀況 A：您下載的是「完整內建模型整合版」
- **完全免設定！**
- 解壓縮 ZIP 檔後，模型已預先存放在 `workspace_data\models\` 內。
- 雙擊執行 **`模型安裝與檢測_Setup_Models.bat`**，看到全數勾選 `[✔]` 即可直接開始使用。

---

### 狀況 B：您另外下載了獨立的「AI 模型壓縮包 (models.zip)」
如果您是單獨下載模型包，請依照下列三種方式之一放置：

#### 方法 1（最簡單 — 智慧自動整合）：
1. 將下載的 `models.zip` 解壓縮在 `StemForge` 根目錄，產生名為 `models` 的資料夾。
2. 雙擊執行根目錄的 **`模型安裝與檢測_Setup_Models.bat`**。
3. 腳本會自動識別並將所有模型檔案自動複製/整合至 `workspace_data\models\` 之中！

#### 方法 2（手動複製貼上）：
1. 打開 `StemForge` 資料夾內的 `workspace_data\models\`。
2. 將所有 `.onnx` 檔案、`.json` 設定檔以及 `huggingface\` 資料夾直接拖入 `workspace_data\models\` 內。

---

## 🔍 三、各模型功用與規格說明

| 模型檔案 / 資料夾 | 大小 | 負責分軌任務 |
| :--- | :--- | :--- |
| **`models--adefossez--HTDemucs`** | ~168 MB | **Demucs v4 第 1 級主幹分離**：將混合音訊拆分為純淨的鼓組、貝斯、人聲、其他樂器。 |
| **`models--adefossez--HTDemucs-6s`** | ~109 MB | **Demucs v4 第 2 級進階細分**：從樂器中精準分離出獨立的**吉他 (Guitar)** 與**鋼琴/鍵盤 (Piano)**。 |
| **`UVR_MDXNET_KARA_2.onnx`** | ~52.7 MB | **UVR 神經分離**：從人聲總軌中極限分離出**主唱純音 (Lead)** 與**合音/和聲 (Backing)**。 |
| **`Reverb_HQ_By_FoxJoy.onnx`** | ~66.7 MB | **UVR 殘響消除**：分離出**錄音室純乾音 (Dry)** 與**空間殘響 (Reverb)**。 |

---

## ⚡ 四、一鍵驗證方式

解壓縮完成後，隨時雙擊根目錄的 **`模型安裝與檢測_Setup_Models.bat`**：
- 若顯示 `【狀態: 完美！所有 AI 模型庫皆已正確安裝於指定目錄】`，代表工作站已 100% 具備完全離線運算能力。
- 接著雙擊 **`Start_StemForge_Studio.bat`**，即可直接進入 19+ 軌深度分離工作站！
