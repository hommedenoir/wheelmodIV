@echo off
rem Builds WheelMod.ivsdk.dll straight into the game's IV-SDK .NET scripts folder
setlocal
set "SRC=%~dp0"
rem Game folder: pass it as the first argument, or change the default below
set "GAME=D:\Grand Theft Auto IV - The Complete Edition\Grand Theft Auto IV - The Complete Edition\GTAIV"
if not "%~1"=="" set "GAME=%~1"
set "CSC=%WINDIR%\Microsoft.NET\Framework\v4.0.30319\csc.exe"

if not exist "%CSC%" ( echo C# compiler not found at %CSC% & pause & exit /b 1 )
if not exist "%GAME%\IVSDKDotNetWrapper.dll" ( echo Game folder not found: %GAME% & pause & exit /b 1 )

"%CSC%" /nologo /target:library /platform:x86 /optimize+ ^
  /out:"%GAME%\IVSDKDotNet\scripts\WheelMod.ivsdk.dll" ^
  /reference:"%GAME%\IVSDKDotNetWrapper.dll" ^
  /reference:System.Numerics.dll /reference:System.Windows.Forms.dll ^
  "%SRC%*.cs"

if errorlevel 1 ( echo. & echo BUILD FAILED - see the error lines above. ) else ( echo. & echo Build OK: %GAME%\IVSDKDotNet\scripts\WheelMod.ivsdk.dll )
pause
