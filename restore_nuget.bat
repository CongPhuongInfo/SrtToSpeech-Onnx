@echo off
setlocal

echo ================================================
echo   Tai / khoi phuc goi NuGet cho SrtToSpeech
echo ================================================

where dotnet >nul 2>nul
if errorlevel 1 (
    echo [LOI] Khong tim thay "dotnet". Hay cai .NET 9 SDK tai:
    echo       https://dotnet.microsoft.com/download/dotnet/9.0
    exit /b 1
)

pushd "%~dp0src"
echo.
echo Dang tai / khoi phuc goi NuGet (Microsoft.ML.OnnxRuntime...)
dotnet restore
if errorlevel 1 (
    popd
    echo.
    echo [LOI] Khoi phuc goi NuGet that bai. Kiem tra ket noi mang.
    exit /b 1
)
popd

echo.
echo ================================================
echo   Da tai xong goi NuGet.
echo   Tiep theo: chay build.bat de bien dich ung dung.
echo ================================================
exit /b 0
