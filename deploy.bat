@echo off
echo Deploying STBud for TwinCAT Host files...
echo.

set TFM=net48
if "%1"=="net462" set TFM=net462

set SRC=%~dp0src\STFormatter.Host\bin\Debug\%TFM%
set DST=C:\Program Files (x86)\STBud

echo Deploying %TFM% build...
echo.

if not exist "%DST%" mkdir "%DST%"

copy /Y "%SRC%\STFormatter.Host.exe" "%DST%\"
if errorlevel 1 goto :error
copy /Y "%SRC%\STFormatter.Core.dll" "%DST%\"
if errorlevel 1 goto :error
copy /Y "%SRC%\STFormatter.UI.dll" "%DST%\"
if errorlevel 1 goto :error
copy /Y "%SRC%\Microsoft.VisualStudio.Interop.dll" "%DST%\"
if errorlevel 1 goto :error

for %%F in (
    Microsoft.Bcl.AsyncInterfaces.dll
    System.Buffers.dll
    System.Collections.Immutable.dll
    System.Memory.dll
    System.Numerics.Vectors.dll
    System.Runtime.CompilerServices.Unsafe.dll
    System.Text.Encodings.Web.dll
    System.Text.Json.dll
    System.Threading.Tasks.Extensions.dll
    System.ValueTuple.dll
) do (
    if exist "%SRC%\%%F" (
        copy /Y "%SRC%\%%F" "%DST%\"
        if errorlevel 1 goto :error
    )
)

echo.
echo Deployment complete (%TFM% build).
echo WARNING: Do NOT copy Beckhoff DLLs or VSPackage files into the STFormatter directory.
echo The STFormatter directory should ONLY contain Host files listed above.
goto :done

:error
echo.
echo ERROR: Deployment failed. Run as Administrator and make sure STFormatter.Host.exe is not running.

:done
pause
