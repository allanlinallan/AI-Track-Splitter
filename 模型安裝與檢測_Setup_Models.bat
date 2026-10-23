@echo off
title StemForge Studio - AI 模型庫安裝與完整性檢測
cd /d "%~dp0"

echo ====================================================================
echo      StemForge Studio - AI 神經分軌模型安裝與完整性檢測工具
echo ====================================================================
echo.
echo 本工具將協助您檢查並確保所有 AI 模型檔案已正確放置於工作站所需位置。
echo.

set "TARGET_DIR=%~dp0workspace_data\models"

if exist "%~dp0models" (
    if not exist "%TARGET_DIR%" (
        echo [檢測] 發現根目錄存在 models 資料夾，正在自動整合至 workspace_data\models ...
        mkdir "%TARGET_DIR%" 2>nul
        xcopy "%~dp0models\*" "%TARGET_DIR%\" /E /I /Y >nul
        echo [成功] 已自動將 models 檔案整合至 workspace_data\models !
        echo.
    )
)

if not exist "%TARGET_DIR%" (
    mkdir "%TARGET_DIR%" 2>nul
)

echo ====================================================================
echo   [1/2] 檢查 UVR MDX-Net ONNX 人聲精準分離模型
echo ====================================================================

set UVR_OK=1
if exist "%TARGET_DIR%\UVR_MDXNET_KARA_2.onnx" (
    echo   [OK] UVR_MDXNET_KARA_2.onnx [52.7 MB - 主唱/和聲分離模型]
) else (
    echo   [X] 缺失: UVR_MDXNET_KARA_2.onnx
    set UVR_OK=0
)

if exist "%TARGET_DIR%\Reverb_HQ_By_FoxJoy.onnx" (
    echo   [OK] Reverb_HQ_By_FoxJoy.onnx [66.7 MB - 主唱乾音/殘響分離模型]
) else (
    echo   [X] 缺失: Reverb_HQ_By_FoxJoy.onnx
    set UVR_OK=0
)

if exist "%TARGET_DIR%\mdx_model_data.json" (
    echo   [OK] mdx_model_data.json [模型設定檔]
) else (
    echo   [X] 缺失: mdx_model_data.json
    set UVR_OK=0
)

echo.
echo ====================================================================
echo   [2/2] 檢查 Demucs v4 神經主幹模型 [離線 HuggingFace 庫]
echo ====================================================================

set DEMUCS_OK=1
if exist "%TARGET_DIR%\huggingface\hub\models--adefossez--HTDemucs" (
    echo   [OK] HTDemucs [4-Stem: 鼓組/貝斯/人聲/其他樂器 離線模型]
) else (
    echo   [X] 缺失: models--adefossez--HTDemucs
    set DEMUCS_OK=0
)

if exist "%TARGET_DIR%\huggingface\hub\models--adefossez--HTDemucs-6s" (
    echo   [OK] HTDemucs-6s [6-Stem: 鼓組/貝斯/吉他/鋼琴/人聲/其他 離線模型]
) else (
    echo   [X] 缺失: models--adefossez--HTDemucs-6s
    set DEMUCS_OK=0
)

echo.
echo ====================================================================
echo   檢測結果總結
echo ====================================================================

if "%UVR_OK%"=="1" (
    if "%DEMUCS_OK%"=="1" (
        echo   【狀態: 完美！所有 AI 模型庫皆已正確安裝於指定目錄】
        echo   模型存放位置: %TARGET_DIR%
        echo.
        echo   現在您可以直接執行 Start_StemForge_Studio.bat 開始使用！
        goto END
    )
)

echo   【狀態: 注意！部分模型檔案缺失】
echo.
echo   【模型正確放置路徑】：
echo   請將下載的模型檔案放入以下目錄：
echo   - %TARGET_DIR%
echo.
echo   詳細目錄結構說明請參閱: 模型放置說明_MODELS_SETUP.md

:END
echo ====================================================================
echo.
pause
