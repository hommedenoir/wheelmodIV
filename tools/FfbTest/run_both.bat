@echo off
rem Builds and runs the self-measuring force feedback test, first as 32-bit, then as 64-bit.
rem Results are saved next to this file: FfbTest_results_32bit.txt and FfbTest_results_64bit.txt
setlocal
set "SRC=%~dp0"
set "CSC=%WINDIR%\Microsoft.NET\Framework\v4.0.30319\csc.exe"
if not exist "%CSC%" ( echo C# compiler not found at %CSC% & pause & exit /b 1 )

"%CSC%" /nologo /target:exe /platform:x86 /optimize+ /out:"%SRC%FfbTest.exe" /reference:System.Windows.Forms.dll "%SRC%FfbTest.cs" "%SRC%..\..\src\ForceFeedback.cs"
if errorlevel 1 ( echo. & echo BUILD FAILED - send the error lines above to Claude. & pause & exit /b 1 )
"%CSC%" /nologo /target:exe /platform:x64 /optimize+ /out:"%SRC%FfbTest64.exe" /reference:System.Windows.Forms.dll "%SRC%FfbTest.cs" "%SRC%..\..\src\ForceFeedback.cs"
if errorlevel 1 ( echo. & echo BUILD FAILED - send the error lines above to Claude. & pause & exit /b 1 )

echo.
echo ===== 32-bit run =====
"%SRC%FfbTest.exe"
echo.
echo ===== 64-bit run =====
"%SRC%FfbTest64.exe"
echo.
echo All done. Tell Claude "test done" - Claude can read both result files itself.
pause
