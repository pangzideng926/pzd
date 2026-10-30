@echo off
rem Double-click to create the local pzd_test MySQL account for integration tests.
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0Setup-MySqlTestUser.ps1"
echo.
pause
