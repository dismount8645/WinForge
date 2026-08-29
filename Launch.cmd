@echo off
:: ViVeToolApp launcher — handles UAC elevation and both publish locations
set "EXE1=C:\Tools\ViVeToolApp\bin\Release\publish\ViVeToolApp.exe"
set "EXE2=C:\Tools\ViVeToolApp\bin\x64\Release\net9.0-windows10.0.19041.0\win-x64\publish\ViVeToolApp.exe"
set "TARGET="
if exist "%EXE1%" set "TARGET=%EXE1%"
if "%TARGET%"=="" if exist "%EXE2%" set "TARGET=%EXE2%"
if "%TARGET%"=="" (
  echo [ERROR] ViVeToolApp.exe not found.
  echo Looked in:
  echo   %EXE1%
  echo   %EXE2%
  echo Rebuild with: dotnet publish ViVeToolApp.csproj -p:Platform=x64 -c Release -r win-x64 -o "C:\Tools\ViVeToolApp\bin\Release\publish"
  pause
  exit /b 1
)
echo Launching %TARGET% (UAC prompt expected — app.manifest requireAdministrator)...
powershell -NoProfile -Command "Try { Start-Process -FilePath '%TARGET%' -Verb RunAs -ErrorAction Stop } Catch { Write-Host \"[ERROR] Elevation failed or UAC denied: $($_.Exception.Message)\" -ForegroundColor Red; pause; exit 1 }"
if errorlevel 1 (
  echo [WARN] PowerShell elevation failed, trying fallback start...
  start "" "%TARGET%"
)
exit /b
