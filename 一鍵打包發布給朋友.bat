@echo off
chcp 65001 >nul
title StemForge Studio - 一鍵打包發布給朋友
cd /d "%~dp0"

echo ==========================================================
echo   StemForge Studio - 一鍵產出朋友用綠色便攜安裝包
echo ==========================================================
echo.
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0Build-Release-Portable.ps1"
echo.
pause
