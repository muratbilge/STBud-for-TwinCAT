@echo off
echo Deploying STFormatter files...
echo.

set SRC=C:\Users\murat\Desktop\Playground\TwinCATPlugins\CodeFormatter\src\STFormatter.Host\bin\Debug\net48
set DST=C:\Program Files (x86)\Beckhoff\TcXaeShell\Common7\IDE\Extensions\STFormatter

copy /Y "%SRC%\STFormatter.Host.exe" "%DST%\"
copy /Y "%SRC%\STFormatter.Core.dll" "%DST%\"
copy /Y "%SRC%\STFormatter.UI.dll" "%DST%\"
copy /Y "%SRC%\Microsoft.VisualStudio.Interop.dll" "%DST%\"
copy /Y "%SRC%\System.Text.Json.dll" "%DST%\"

echo.
echo Deployment complete. Press any key to exit...
pause >nul