param(
    [switch]$IncludeModels = $true,
    [switch]$CreateZip = $false
)

$ErrorActionPreference = "Stop"
$RootDir = $PSScriptRoot
$OutDir  = Join-Path $RootDir "Release_Portable"

Write-Host "==========================================================" -ForegroundColor Cyan
Write-Host "  StemForge Studio - Win-x64 Portable Release Builder" -ForegroundColor Cyan
Write-Host "==========================================================" -ForegroundColor Cyan

if (Test-Path $OutDir) {
    Write-Host "[1/5] Cleaning previous Release_Portable directory..." -ForegroundColor Yellow
    Remove-Item -Path $OutDir -Recurse -Force
}

Write-Host "[2/5] Compiling C# .NET 9.0 Self-Contained (No .NET Runtime required)..." -ForegroundColor Green
dotnet publish (Join-Path $RootDir "src\StemForge.Web\StemForge.Web.csproj") `
    -c Release `
    -r win-x64 `
    --self-contained true `
    -p:PublishSingleFile=false `
    -p:DebugType=None `
    -p:DebugSymbols=false `
    -o $OutDir

Write-Host "[3/5] Copying Python AI scripts (ai_engine)..." -ForegroundColor Green
$AiOutDir = Join-Path $OutDir "ai_engine"
New-Item -ItemType Directory -Path $AiOutDir -Force | Out-Null
Copy-Item (Join-Path $RootDir "ai_engine\stemforge_cli.py") -Destination $AiOutDir -Force
Copy-Item (Join-Path $RootDir "ai_engine\requirements.txt") -Destination $AiOutDir -Force

$ProjectsOutDir = Join-Path $OutDir "workspace_data\projects"
$ModelsOutDir   = Join-Path $OutDir "workspace_data\models"
New-Item -ItemType Directory -Path $ProjectsOutDir -Force | Out-Null
New-Item -ItemType Directory -Path $ModelsOutDir -Force | Out-Null

# Copy demo project stems
$LatestJobJson = Join-Path $RootDir "src\StemForge.Web\wwwroot\latest_gpu_job.json"
if (Test-Path $LatestJobJson) {
    try {
        $jobObj = Get-Content $LatestJobJson -Raw | ConvertFrom-Json
        $demoJobId = $jobObj.jobId
        $demoSrcDir = Join-Path $RootDir "workspace_data\projects\$demoJobId"
        if ($demoJobId -and (Test-Path $demoSrcDir)) {
            Write-Host "      -> Bundling 21-stem GPU demo audio ($demoJobId)..." -ForegroundColor Gray
            Copy-Item -Path $demoSrcDir -Destination (Join-Path $ProjectsOutDir $demoJobId) -Recurse -Force
        }
    } catch {}
}

if ($IncludeModels) {
    Write-Host "[4/5] Copying cached neural weights (UVR ONNX + Demucs v4)..." -ForegroundColor Green
    $SrcModels = Join-Path $RootDir "workspace_data\models"
    if (Test-Path $SrcModels) {
        Copy-Item -Path "$SrcModels\*" -Destination $ModelsOutDir -Recurse -Force
    }
} else {
    Write-Host "[4/5] Skipping neural weights..." -ForegroundColor Yellow
}

Write-Host "[5/5] Generating launcher batch scripts..." -ForegroundColor Green

# 1. Start_StemForge_Studio.bat
$StartBat = @"
@echo off
chcp 65001 >nul
title StemForge Studio - 19+ Stem AI Multitrack Separation Studio
cd /d "%~dp0"

echo ==========================================================
echo   StemForge Studio is starting...
echo   Opening browser at: http://localhost:5188
echo   (Keep this window open while using the Studio)
echo ==========================================================
echo.

start "" cmd /c "timeout /t 2 /nobreak >nul & start http://localhost:5188"
"%~dp0StemForge.Web.exe" --urls http://localhost:5188
pause
"@
[System.IO.File]::WriteAllText((Join-Path $OutDir "Start_StemForge_Studio.bat"), $StartBat, [System.Text.Encoding]::UTF8)

# 2. Install_GPU_Environment.bat
$InstallGpuBat = @"
@echo off
chcp 65001 >nul
title StemForge Studio - NVIDIA GPU AI Setup
cd /d "%~dp0"

echo ====================================================================
echo   StemForge Studio - NVIDIA GPU Neural Cascade Setup
echo.
echo   NOTE:
echo   - If your PC has NO NVIDIA GPU, you DO NOT need to run this script.
echo     Double-click Start_StemForge_Studio.bat to use the native C# SIMD DSP!
echo   - If you have an NVIDIA RTX/GTX GPU, this will setup PyTorch CUDA + Demucs.
echo ====================================================================
echo.

where python >nul 2>nul
if %errorlevel% neq 0 (
    echo [!] Python not found. Installing Python 3.10 via Winget...
    winget install -e --id Python.Python.3.10 --accept-package-agreements --accept-source-agreements
)

echo [1/3] Creating virtual environment in ai_engine\.venv ...
python -m venv "%~dp0ai_engine\.venv"
if not exist "%~dp0ai_engine\.venv\Scripts\python.exe" (
    echo [ERROR] Virtual environment creation failed. Please ensure Python 3.10/3.11 is installed.
    pause
    exit /b 1
)

echo [2/3] Installing PyTorch with CUDA 11.8...
"%~dp0ai_engine\.venv\Scripts\python.exe" -m pip install --upgrade pip
"%~dp0ai_engine\.venv\Scripts\python.exe" -m pip install torch torchvision torchaudio --index-url https://download.pytorch.org/whl/cu118

echo [3/3] Installing Demucs v4 and UVR audio-separator (GPU)...
"%~dp0ai_engine\.venv\Scripts\python.exe" -m pip install "audio-separator[gpu]>=0.47.0" "demucs>=4.0.0" "onnxruntime-gpu>=1.19.0" soundfile numpy scipy

echo.
echo ====================================================================
echo   [SUCCESS] GPU Neural Environment Installed!
echo   Now run Start_StemForge_Studio.bat to begin!
echo ====================================================================
pause
"@
[System.IO.File]::WriteAllText((Join-Path $OutDir "Install_GPU_Environment.bat"), $InstallGpuBat, [System.Text.Encoding]::UTF8)

# 3. README.txt
$ReadmeTxt = @"
========================================================================
StemForge Studio - 19+ 軌 AI 音樂分離工作站（便攜免安裝發布版）
========================================================================

【使用說明】：
1. 立即啟動（任何 Windows 10/11 電腦，免裝 .NET，免裝 Python）：
   直接雙擊「Start_StemForge_Studio.bat」
   -> 系統會在 2 秒後自動於瀏覽器開啟 http://localhost:5188
   -> 畫面已預載好 21 軌無損分離音軌，可立即 Solo/Mute/調整推桿試聽！
   -> 內建 C# SIMD DSP 引擎支援直接拖放任意音樂檔案進行即時分軌！

2. 啟用極致音質 NVIDIA GPU 神經網路加速（若電腦有 RTX/GTX 獨顯）：
   雙擊執行「Install_GPU_Environment.bat」
   -> 系統會自動建立專屬的 ai_engine\.venv 並安裝 CUDA 11.8 PyTorch 與 Demucs v4。
   -> 安裝完成後，日後只要雙擊「Start_StemForge_Studio.bat」即可享受秒級 GPU AI 深度推論！

3. 匯出：
   完成分軌後可一鍵下載 ZIP 包或直接匯出 Reaper (.rpp) 工程檔。
========================================================================
"@
[System.IO.File]::WriteAllText((Join-Path $OutDir "README.txt"), $ReadmeTxt, [System.Text.Encoding]::UTF8)

if ($CreateZip) {
    $ZipPath = Join-Path $RootDir "StemForge_Portable_Win64.zip"
    if (Test-Path $ZipPath) { Remove-Item $ZipPath -Force }
    Write-Host "Compressing to $ZipPath ..." -ForegroundColor Cyan
    Compress-Archive -Path "$OutDir\*" -DestinationPath $ZipPath -CompressionLevel Optimal
    Write-Host "ZIP created: $ZipPath" -ForegroundColor Green
}

Write-Host ""
Write-Host "==========================================================" -ForegroundColor Green
Write-Host "  Build successful! Path: $OutDir" -ForegroundColor Green
Write-Host "==========================================================" -ForegroundColor Green
