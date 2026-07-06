@echo off
REM Thin wrapper - the real deploy (stop Host, copy, VERIFY, restart) lives in deploy.ps1.
REM Usage: deploy.bat [net462] [-NoPause]
powershell -NoProfile -ExecutionPolicy RemoteSigned -File "%~dp0deploy.ps1" %*
exit /b %ERRORLEVEL%
