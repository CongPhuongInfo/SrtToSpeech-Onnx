@echo off
setlocal

echo ================================================
echo   Build SrtToSpeech (VB.NET WinForms - Piper TTS)
echo   [DEV] Khong dong goi thanh 1 file .exe duy nhat
echo ================================================

where dotnet >nul 2>nul
if errorlevel 1 (
    echo [LOI] Khong tim thay "dotnet". Hay cai .NET 9 SDK tai:
    echo       https://dotnet.microsoft.com/download/dotnet/9.0
    exit /b 1
)

pushd "%~dp0src"

echo.
echo [1/2] Khoi phuc goi NuGet (bo qua neu da chay restore_nuget.bat)...
dotnet restore
if errorlevel 1 goto :error

echo.
echo [2/2] Bien dich (Release)...
dotnet build -c Release -o "%~dp0bin" -p:SelfContained=false -p:RuntimeIdentifier=
if errorlevel 1 goto :error

popd

echo.
echo ================================================
echo   Build thanh cong!
echo   File chay : bin\SrtToSpeech.exe
echo   (Framework-dependent - chi vai file .dll can thiet, can may co san .NET 9 Runtime)
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
