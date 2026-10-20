@echo off
chcp 65001 >nul
title StemForge Studio - AI Multitrack DAW
echo ========================================================
echo   StemForge Studio (19+ Stems Separation Engine)
echo   Starting local server and opening browser...
echo ========================================================
start "" "http://localhost:5188"
StemForge.Web.exe --urls "http://localhost:5188"
pause
