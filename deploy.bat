@echo off
echo Deploying STFormatter files...
echo.

set TFM=net48
if "%1"=="net462" set TFM=net462

set SRC=C:\Users\murat\Desktop\Playground\TwinCATPlugins\CodeFormatter\src\STFormatter.Host\bin\Debug\%TFM%
set DST=C:\Program Files (x86)\Beckhoff\TcXaeShell\Common7\IDE\Extensions\STFormatter

echo Deploying %TFM% build...
echo.

copy /Y "%SRC%\STFormatter.Host.exe" "%DST%\"
copy /Y "%SRC%\STFormatter.Core.dll" "%DST%\"
copy /Y "%SRC%\STFormatter.UI.dll" "%DST%\"
copy /Y "%SRC%\Microsoft.VisualStudio.Interop.dll" "%DST%\"

if "%TFM%"=="net462" (
    copy /Y "%SRC%\System.Text.Json.dll" "%DST%\"
    copy /Y "%SRC%\Microsoft.Bcl.AsyncInterfaces.dll" "%DST%\"
    copy /Y "%SRC%\System.Buffers.dll" "%DST%\"
    copy /Y "%SRC%\System.Collections.Immutable.dll" "%DST%\"
    copy /Y "%SRC%\System.Memory.dll" "%DST%\"
    copy /Y "%SRC%\System.Numerics.Vectors.dll" "%DST%\"
    copy /Y "%SRC%\System.Runtime.CompilerServices.Unsafe.dll" "%DST%\"
    copy /Y "%SRC%\System.Threading.Tasks.Extensions.dll" "%DST%\"
    copy /Y "%SRC%\System.ValueTuple.dll" "%DST%\"
)

echo.
echo Deployment complete (%TFM% build). Press any key to exit...
pause >nul