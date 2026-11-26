@echo off
rem Builds FfbTest.exe (standalone force feedback test, uses ..\..\src\ForceFeedback.cs)
setlocal
set "SRC=%~dp0"
set "CSC=%WINDIR%\Microsoft.NET\Framework\v4.0.30319\csc.exe"
if not exist "%CSC%" ( echo C# compiler not found at %CSC% & pause & exit /b 1 )

"%CSC%" /nologo /target:exe /platform:x86 /optimize+ ^
  /out:"%SRC%FfbTest.exe" /reference:System.Windows.Forms.dll ^
  "%SRC%FfbTest.cs" "%SRC%..\..\src\ForceFeedback.cs"

if errorlevel 1 ( echo. & echo BUILD FAILED - send the error lines above to Claude. & pause & exit /b 1 )
echo.
echo Build OK - starting the test...
echo.
"%SRC%FfbTest.exe"
