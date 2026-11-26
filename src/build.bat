@echo off
rem Builds WheelMod.ivsdk.dll straight into the game's IV-SDK .NET scripts folder
setlocal
set "SRC=%~dp0"
set "GAME=D:\Grand Theft Auto IV - The Complete Edition\Grand Theft Auto IV - The Complete Edition\GTAIV"
set "CSC=%WINDIR%\Microsoft.NET\Framework\v4.0.30319\csc.exe"

if not exist "%CSC%" ( echo C# compiler not found at %CSC% & pause & exit /b 1 )
if not exist "%GAME%\IVSDKDotNetWrapper.dll" ( echo Game folder not found: %GAME% & pause & exit /b 1 )

"%CSC%" /nologo /target:library /platform:x86 /optimize+ ^
  /out:"%GAME%\IVSDKDotNet\scripts\WheelMod.ivsdk.dll" ^
  /reference:"%GAME%\IVSDKDotNetWrapper.dll" ^
  /reference:System.Numerics.dll /reference:System.Windows.Forms.dll ^
  "%SRC%*.cs"

if errorlevel 1 ( echo. & echo BUILD FAILED - send the error lines above to Claude. ) else ( echo. & echo Build OK: %GAME%\IVSDKDotNet\scripts\WheelMod.ivsdk.dll )
pause
