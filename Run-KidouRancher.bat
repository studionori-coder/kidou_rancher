@echo off
setlocal
cd /d "%~dp0"
dotnet run --project "%~dp0src\KidouRancher\KidouRancher.csproj"
if errorlevel 1 pause
endlocal