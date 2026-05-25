@echo off
echo ========================================
echo STFormatter TcXaeShell Installer
echo ========================================
echo.

:: Check for admin privileges
net session >nul 2>&1
if %errorLevel% neq 0 (
    echo ERROR: This script must be run as Administrator.
    echo Right-click and select "Run as administrator".
    pause
    exit /b 1
)

set TCXAE=C:\Program Files (x86)\Beckhoff\TcXaeShell\Common7\IDE
set EXTDIR=%TCXAE%\Extensions\STFormatter
set SLN=%~dp0src\STFormatter.TcXaeShell

:: Stop TcXaeShell if running
echo Stopping TcXaeShell...
taskkill /IM TcXaeShell.exe /F >nul 2>&1
timeout /t 2 /nobreak >nul

:: Build the project
echo Building STFormatter.TcXaeShell...
dotnet build "%SLN%\STFormatter.TcXaeShell.csproj" -c Debug
if %errorLevel% neq 0 (
    echo ERROR: Build failed.
    pause
    exit /b 1
)

:: Create extension directory if needed
if not exist "%EXTDIR%" mkdir "%EXTDIR%"

:: Deploy files
echo Deploying extension files...
copy /Y "%SLN%\bin\Debug\net462\STFormatter.TcXaeShell.dll" "%EXTDIR%\"
copy /Y "%SLN%\bin\Debug\net462\STFormatter.Core.dll" "%EXTDIR%\"

:: Register extension
echo Registering extension in registry...
reg import "%~dp0register_tcxae.reg"
if %errorLevel% neq 0 (
    echo WARNING: Registry import failed. Try importing register_tcxae.reg manually.
)

:: Clear caches
echo Clearing caches...
rd /s /q "%LOCALAPPDATA%\Beckhoff\TcXaeShell\15.0_IsoShell\ComponentModelCache" 2>nul
rd /s /q "%LOCALAPPDATA%\Beckhoff\TcXaeShell\15.0\Extensions" 2>nul

echo.
echo ========================================
echo Installation complete!
echo Start TcXaeShell to use the ST Formatter.
echo ========================================
pause