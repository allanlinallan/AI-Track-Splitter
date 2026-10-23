@echo off
chcp 65001 >nul
title StemForge Studio - NVIDIA GPU AI Setup
cd /d "%~dp0"

echo ====================================================================
echo   StemForge Studio - NVIDIA GPU Neural Cascade Setup (64-bit)
echo.
echo   NOTE:
echo   - If your PC has NO NVIDIA GPU, you DO NOT need to run this script.
echo     Double-click Start_StemForge_Studio.bat to use the native C# SIMD DSP!
echo   - If you have an NVIDIA RTX/GTX GPU, this will setup PyTorch CUDA + Demucs.
echo ====================================================================
echo.

:: 1. 檢查現有的 ai_engine\.venv 是否為 32 位元或損壞
if exist "%~dp0ai_engine\.venv\Scripts\python.exe" (
    "%~dp0ai_engine\.venv\Scripts\python.exe" -c "import struct, sys; sys.exit(0 if struct.calcsize('P') == 8 else 1)" >nul 2>nul
    if errorlevel 1 (
        echo [!] 偵測到現有的虛擬環境為 32 位元 (x86) 或已損壞！
        echo [!] 深度學習與 PyTorch CUDA 僅支援 64 位元，正在清理舊環境...
        rmdir /s /q "%~dp0ai_engine\.venv"
        echo [OK] 舊環境清理完成。
        echo.
    )
)

:: 2. 尋找系統可用的 64 位元 (x64) Python
set "PY_CMD="

where python >nul 2>nul
if %errorlevel% equ 0 (
    python -c "import struct, sys; sys.exit(0 if struct.calcsize('P') == 8 else 1)" >nul 2>nul
    if %errorlevel% equ 0 (
        set "PY_CMD=python"
    )
)

if not defined PY_CMD (
    where py >nul 2>nul
    if %errorlevel% equ 0 (
        py -3.10-64 -c "pass" >nul 2>nul && set "PY_CMD=py -3.10-64"
        if not defined PY_CMD (
            py -3.11-64 -c "pass" >nul 2>nul && set "PY_CMD=py -3.11-64"
        )
        if not defined PY_CMD (
            py -3.12-64 -c "pass" >nul 2>nul && set "PY_CMD=py -3.12-64"
        )
        if not defined PY_CMD (
            py -64 -c "pass" >nul 2>nul && set "PY_CMD=py -64"
        )
    )
)

if not defined PY_CMD (
    if exist "%LOCALAPPDATA%\Programs\Python\Python310\python.exe" (
        "%LOCALAPPDATA%\Programs\Python\Python310\python.exe" -c "import struct, sys; sys.exit(0 if struct.calcsize('P') == 8 else 1)" >nul 2>nul
        if %errorlevel% equ 0 set "PY_CMD=%LOCALAPPDATA%\Programs\Python\Python310\python.exe"
    )
)
if not defined PY_CMD (
    if exist "%LOCALAPPDATA%\Programs\Python\Python311\python.exe" (
        "%LOCALAPPDATA%\Programs\Python\Python311\python.exe" -c "import struct, sys; sys.exit(0 if struct.calcsize('P') == 8 else 1)" >nul 2>nul
        if %errorlevel% equ 0 set "PY_CMD=%LOCALAPPDATA%\Programs\Python\Python311\python.exe"
    )
)

if not defined PY_CMD (
    echo [!] 未在環境中偵測到 64 位元 (x64) Python！
    echo     (注意：若系統安裝的是 32 位元 Python，AI 模型庫 PyTorch / ONNX 將無法支援)
    echo [*] 正在透過 Winget 自動安裝 Python 3.10 (64-bit)...
    winget install -e --id Python.Python.3.10 -a x64 --accept-package-agreements --accept-source-agreements
    
    if exist "%LOCALAPPDATA%\Programs\Python\Python310\python.exe" (
        set "PY_CMD=%LOCALAPPDATA%\Programs\Python\Python310\python.exe"
    ) else (
        where py >nul 2>nul && py -3.10-64 -c "pass" >nul 2>nul && set "PY_CMD=py -3.10-64"
    )
)

if not defined PY_CMD (
    echo.
    echo ====================================================================
    echo [錯誤] 找不到 64 位元 (64-bit / x64) 的 Python！
    echo.
    echo 請手動前往 Python 官方下載安裝 64 位元版本：
    echo 推薦下載：https://www.python.org/ftp/python/3.10.11/python-3.10.11-amd64.exe
    echo.
    echo *重要提示*：
    echo 1. 安裝時務必勾選「Add python.exe to PATH」
    echo 2. 安裝完成後，請重新執行此批次檔！
    echo ====================================================================
    echo.
    pause
    exit /b 1
)

echo [*] 使用之 64 位元 Python: %PY_CMD%
echo.

:: 3. 建立虛擬環境
echo [1/3] 正在建立 64 位元虛擬環境 (ai_engine\.venv) ...
%PY_CMD% -m venv "%~dp0ai_engine\.venv"
if not exist "%~dp0ai_engine\.venv\Scripts\python.exe" (
    echo [ERROR] 虛擬環境建立失敗。請確認已正確安裝 Python 3.10 或 3.11 (64-bit)。
    pause
    exit /b 1
)

"%~dp0ai_engine\.venv\Scripts\python.exe" -c "import struct, sys; sys.exit(0 if struct.calcsize('P') == 8 else 1)" >nul 2>nul
if errorlevel 1 (
    echo [ERROR] 建立的虛擬環境仍為 32 位元！請確認系統環境變數優先指向 64 位元 Python。
    pause
    exit /b 1
)

echo [2/3] 正在升級 pip 並安裝 PyTorch CUDA 11.8 ...
"%~dp0ai_engine\.venv\Scripts\python.exe" -m pip install --upgrade pip
"%~dp0ai_engine\.venv\Scripts\python.exe" -m pip install torch torchvision torchaudio --index-url https://download.pytorch.org/whl/cu118

echo [3/3] 正在安裝 Demucs v4 與 UVR audio-separator (GPU)...
"%~dp0ai_engine\.venv\Scripts\python.exe" -m pip install "audio-separator[gpu]>=0.47.0" "demucs>=4.0.0" "onnxruntime-gpu>=1.19.0" soundfile numpy scipy

echo.
echo ====================================================================
echo   [SUCCESS] GPU Neural Environment Installed! (64-bit CUDA Ready)
echo   現在可雙擊 Start_StemForge_Studio.bat 立即開始 AI 分軌！
echo ====================================================================
pause
