@echo off
title StemForge Studio - Project Sync & Launcher
cd /d "%~dp0"

set "MAIN_PROJECTS=%~dp0..\workspace_data\projects"
set "PORTABLE_PROJECTS=%~dp0workspace_data\projects"

if not exist "%MAIN_PROJECTS%" mkdir "%MAIN_PROJECTS%"
if not exist "%PORTABLE_PROJECTS%" mkdir "%PORTABLE_PROJECTS%"

echo ====================================================================
echo  [Step 1/3] Syncing: Main Projects to Portable (Skip existing)...
echo ====================================================================
robocopy "%MAIN_PROJECTS%" "%PORTABLE_PROJECTS%" /E /XO /R:1 /W:1 /MT:8 /NP /NDL
echo  Step 1 finished.
echo.

echo ====================================================================
echo  [Step 2/3] Starting StemForge.Web.exe...
echo  * Auto-Sync: Closing all browser windows will auto-exit server in 5s
echo  * Manual: Or press Ctrl+C in this console to exit & sync immediately
echo ====================================================================
"%~dp0StemForge.Web.exe"
echo.

echo ====================================================================
echo  [Step 3/3] Syncing: Portable to Main Projects (Sync new & updated)...
echo ====================================================================
robocopy "%PORTABLE_PROJECTS%" "%MAIN_PROJECTS%" /E /XO /R:1 /W:1 /MT:8 /NP /NDL
echo.
echo ====================================================================
echo  All projects synced back to Main successfully!
echo ====================================================================
pause
