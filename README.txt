========================================================================
StemForge Studio v2.0 - 19+ 軌深度 AI 音樂分軌工作站 (免安裝綠色版)
========================================================================

【快速使用三步驟】：

1. 檢查 AI 模型庫：
   雙擊執行「模型安裝與檢測_Setup_Models.bat」
   -> 確保所有神經模型已就緒於 workspace_data\models\。
   -> 若下載獨立模型包，解壓後執行此腳本會自動幫您搬移至正確目錄！

2. 啟動工作站 (免安裝 .NET、免手動配置環境)：
   雙擊執行「Start_StemForge_Studio.bat」
   -> 系統將自動在瀏覽器中打開: http://localhost:5188
   -> 內建 C# SIMD DSP 可直接處理音訊升降調、變速與多軌消音混音。

3. 啟用 NVIDIA GPU 深度神經分軌 (若電腦有 RTX/GTX 顯卡)：
   首次使用請點擊執行「Install_GPU_Environment.bat」
   -> 自動配置 CUDA 11.8 PyTorch 與 Demucs v4 / UVR 推理環境。
   -> 安裝完成後，直接回到步驟 2 啟動即可享有頂級 GPU 分軌加速！

========================================================================
【AI 模型放置說明】：
標準存放位置：workspace_data\models\
內建模型包含：
- UVR_MDXNET_KARA_2.onnx (主唱/和聲分離)
- Reverb_HQ_By_FoxJoy.onnx (主唱乾音/殘響分離)
- huggingface\hub\models--adefossez--HTDemucs (4-Stem 離線主幹模型)
- huggingface\hub\models--adefossez--HTDemucs-6s (6-Stem 離線細分模型)

詳細圖文說明請參閱: 模型放置說明_MODELS_SETUP.md 或 DOC\USER 使用手冊.md
========================================================================
