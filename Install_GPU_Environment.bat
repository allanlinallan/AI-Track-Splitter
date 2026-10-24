@echo off
chcp 65001 >nul
title StemForge Studio - NVIDIA GPU Setup
cd /d "%~dp0"

echo ====================================================================
echo   StemForge Studio - NVIDIA GPU Setup (64-bit PyTorch CUDA)
echo ====================================================================
echo.

if not exist "%~dp0ai_engine\.venv\Scripts\python.exe" goto check_system_python

"%~dp0ai_engine\.venv\Scripts\python.exe" -c "import struct, sys; sys.exit(0 if struct.calcsize('P')==8 else 1)" >nul 2>nul
if errorlevel 1 goto remove_broken_venv
goto venv_ready

:remove_broken_venv
echo [!] 偵測到現有的虛擬環境為 32 位元 (x86) 或已損壞！
echo [!] 正在自動移除舊的 32 位元虛擬環境...
rmdir /s /q "%~dp0ai_engine\.venv"
echo [OK] 舊環境已成功清除。
echo.

:check_system_python
set BASE_PY=

where py >nul 2>nul
if errorlevel 1 goto check_python_cmd

py -3.10-64 -c "pass" >nul 2>nul
if errorlevel 1 goto try_311
set BASE_PY=py -3.10-64
goto found_python

:try_311
py -3.11-64 -c "pass" >nul 2>nul
if errorlevel 1 goto try_312
set BASE_PY=py -3.11-64
goto found_python

:try_312
py -3.12-64 -c "pass" >nul 2>nul
if errorlevel 1 goto try_64
set BASE_PY=py -3.12-64
goto found_python

:try_64
py -64 -c "pass" >nul 2>nul
if errorlevel 1 goto check_python_cmd
set BASE_PY=py -64
goto found_python

:check_python_cmd
where python >nul 2>nul
if errorlevel 1 goto check_appdata_python

python -c "import struct, sys; sys.exit(0 if struct.calcsize('P')==8 else 1)" >nul 2>nul
if errorlevel 1 goto check_appdata_python
set BASE_PY=python
goto found_python

:check_appdata_python
if exist "%LOCALAPPDATA%\Programs\Python\Python310\python.exe" goto use_appdata_310
if exist "%LOCALAPPDATA%\Programs\Python\Python311\python.exe" goto use_appdata_311
if exist "C:\Program Files\Python310\python.exe" goto use_programfiles_310
if exist "C:\Program Files\Python311\python.exe" goto use_programfiles_311
goto no_64bit_python

:use_appdata_310
set BASE_PY=%LOCALAPPDATA%\Programs\Python\Python310\python.exe
goto found_python

:use_appdata_311
set BASE_PY=%LOCALAPPDATA%\Programs\Python\Python311\python.exe
goto found_python

:use_programfiles_310
set BASE_PY=C:\Program Files\Python310\python.exe
goto found_python

:use_programfiles_311
set BASE_PY=C:\Program Files\Python311\python.exe
goto found_python

:no_64bit_python
echo.
echo ====================================================================
echo [錯誤] 系統未安裝 64 位元 (x64 / AMD64) Python！
echo.
echo 原因：
echo 您電腦上目前安裝的是「32 位元 Python」，或者尚未安裝 Python。
echo AI 運算模型 (PyTorch CUDA、Demucs、ONNX) 只能在 64 位元 Python 下運行。
echo.
echo 【請執行以下任一方式安裝 64 位元 Python】：
echo.
echo 方式 A (推薦)：以系統管理員身分打開 CMD 執行：
echo   winget install -e --id Python.Python.3.10 -a x64
echo.
echo 方式 B (直接下載官方安裝包)：
echo   https://www.python.org/ftp/python/3.10.11/python-3.10.11-amd64.exe
echo.
echo *重要*：安裝時請務必勾選「Add python.exe to PATH」！
echo 安裝完成後，請重新執行此批次檔。
echo ====================================================================
echo.
pause
exit /b 1

:found_python
echo [*] 鎖定 64 位元 Python 直譯器: %BASE_PY%
echo.

echo [1/3] 正在建立 64 位元虛擬環境 (ai_engine\.venv) ...
%BASE_PY% -m venv "%~dp0ai_engine\.venv"
if not exist "%~dp0ai_engine\.venv\Scripts\python.exe" goto venv_fail
goto venv_ready

:venv_fail
echo [ERROR] 虛擬環境建立失敗！請確認 Python 權限。
pause
exit /b 1

:venv_ready
echo [2/3] 正在升級 pip 並安裝 PyTorch CUDA 11.8...
"%~dp0ai_engine\.venv\Scripts\python.exe" -m pip install --upgrade pip
"%~dp0ai_engine\.venv\Scripts\python.exe" -m pip install torch torchvision torchaudio --index-url https://download.pytorch.org/whl/cu118
if errorlevel 1 goto torch_fail

echo.
echo [3/3] 正在安裝 Demucs 與 UVR 分軌模組 (audio-separator GPU)...
"%~dp0ai_engine\.venv\Scripts\python.exe" -m pip install "audio-separator[gpu]>=0.47.0" "demucs>=4.0.0" "onnxruntime-gpu>=1.19.0" soundfile numpy scipy noisereduce
if errorlevel 1 goto ai_fail

echo.
echo ====================================================================
echo   [SUCCESS] GPU Neural Cascade 環境安裝完成！(64-bit CUDA 就緒)
echo   現在可雙擊執行 Start_StemForge_Studio.bat 立即開始 AI 分軌！
echo ====================================================================
pause
exit /b 0

:torch_fail
echo.
echo [ERROR] PyTorch 安裝失敗！
echo 請確認目前 Python 環境是否為 64 位元。
pause
exit /b 1

:ai_fail
echo.
echo [ERROR] AI 模組安裝失敗！
pause
exit /b 1
