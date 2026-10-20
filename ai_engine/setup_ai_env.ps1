$ErrorActionPreference = "Stop"
$ScriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
Set-Location $ScriptDir

Write-Host "=== StemForge AI Engine Setup (CUDA 12.8) ===" -ForegroundColor Cyan

if (-not (Test-Path ".venv")) {
    Write-Host "[1/3] 建立獨立 Python .venv 虛擬環境..." -ForegroundColor Yellow
    python -m venv .venv
}

$PipExe = Join-Path $ScriptDir ".venv\Scripts\pip.exe"
$PythonExe = Join-Path $ScriptDir ".venv\Scripts\python.exe"

Write-Host "[2/3] 升級 pip 並安裝 PyTorch CUDA + audio-separator[gpu] + demucs..." -ForegroundColor Yellow
& $PipExe install --upgrade pip
& $PipExe install -r requirements.txt

Write-Host "[3/3] 驗證 CUDA 與 audio-separator 狀態..." -ForegroundColor Yellow
& $PythonExe -c "import torch, audio_separator; print('CUDA Available:', torch.cuda.is_available(), '| Device:', torch.cuda.get_device_name(0) if torch.cuda.is_available() else 'CPU')"

Write-Host "=== 安裝完成！現在可直接在 Visual Studio 啟動 StemForge.Web ===" -ForegroundColor Green
