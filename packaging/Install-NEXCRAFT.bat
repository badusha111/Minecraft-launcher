@echo off
title NEXCRAFT Installer
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0Install-NEXCRAFT.ps1"
if errorlevel 1 (
  echo.
  echo Installation failed. Make sure the ZIP archive was fully extracted.
  pause
)
