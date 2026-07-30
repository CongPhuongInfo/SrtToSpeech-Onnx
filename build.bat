@echo off
setlocal

echo ================================================
echo   Build SrtToSpeech (VB.NET WinForms - Piper TTS)
echo ================================================

where dotnet >nul 2>nul
if errorlevel 1 (
    echo [LOI] Khong tim thay "dotnet". Hay cai .NET 9 SDK tai:
    echo       https://dotnet.microsoft.com/download/dotnet/9.0
    exit /b 1
)

pushd "%~dp0src"

echo.
echo [1/3] Khoi phuc goi NuGet (bo qua neu da chay restore_nuget.bat)...
dotnet restore
if errorlevel 1 goto :error

echo.
echo [2/3] Bien dich (Release)...
dotnet build -c Release
if errorlevel 1 goto :error

echo.
echo [3/3] Dong goi thanh file .exe doc lap (win-x64) vao thu muc bin\...
dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o "%~dp0bin"
if errorlevel 1 goto :error

popd

echo.
echo ================================================
echo   Build thanh cong!
echo   File chay : bin\SrtToSpeech.exe
echo   Thu vien giong doc da duoc copy san: bin\Data\Male, bin\Data\Female
echo.
echo   Nho cai san eSpeak NG va them vao PATH:
echo     https://github.com/espeak-ng/espeak-ng/releases
echo ================================================
exit /b 0

:error
popd
echo.
echo [LOI] Build that bai. Xem log ben tren.
exit /b 1
