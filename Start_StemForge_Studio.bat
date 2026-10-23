@echo off
title StemForge Studio - 19+ Stem AI Multitrack Separation Studio
cd /d "%~dp0"

echo ====================================================================
echo   StemForge Studio - 19+ Stem AI Multitrack Separation Studio
echo ====================================================================
echo.

set "MODELS_DIR=%~dp0workspace_data\models"
if not exist "%MODELS_DIR%" (
    if exist "%~dp0models" (
        set "MODELS_DIR=%~dp0models"
    )
)

echo [*] 正在檢查 AI 模型庫狀態...

set "MISSING_MODELS=0"

if exist "%MODELS_DIR%\UVR_MDXNET_KARA_2.onnx" (
    echo   [OK] UVR 人聲和聲分離模型: UVR_MDXNET_KARA_2.onnx
) else (
    echo   [!] 未找到: UVR_MDXNET_KARA_2.onnx
    set "MISSING_MODELS=1"
)

if exist "%MODELS_DIR%\Reverb_HQ_By_FoxJoy.onnx" (
    echo   [OK] UVR 乾音殘響分離模型: Reverb_HQ_By_FoxJoy.onnx
) else (
    echo   [!] 未找到: Reverb_HQ_By_FoxJoy.onnx
    set "MISSING_MODELS=1"
)

if exist "%MODELS_DIR%\huggingface\hub\models--adefossez--HTDemucs" (
    echo   [OK] Demucs v4 4-Stem 神經主幹模型 [離線版就緒]
) else (
    echo   [!] 未找到離線 Demucs 4-Stem 模型
    set "MISSING_MODELS=1"
)

if exist "%MODELS_DIR%\huggingface\hub\models--adefossez--HTDemucs-6s" (
    echo   [OK] Demucs v4 6-Stem 神經細分模型 [離線版就緒]
) else (
    echo   [!] 未找到離線 Demucs 6-Stem 模型
    set "MISSING_MODELS=1"
)

if "%MISSING_MODELS%"=="1" (
    echo.
    echo [提示] 部分 AI 模型尚未放置到位。
    echo 如需安裝或手動定位模型，請執行: 模型安裝與檢測_Setup_Models.bat
    echo 或查閱: 模型放置說明_MODELS_SETUP.md
    echo.
) else (
    echo.
    echo   [OK] 全套 AI 神經模型庫已 100%% 完整就緒！
    echo.
)

echo   Starting Studio at: http://localhost:5188 ...
echo   (請保留此黑底視窗，關閉視窗即停止服務)
echo ====================================================================
echo.

start "" cmd /c "timeout /t 2 /nobreak >nul & start http://localhost:5188"
"%~dp0StemForge.Web.exe" --urls http://localhost:5188
pause
