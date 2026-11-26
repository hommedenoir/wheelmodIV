@echo off
rem Round 2 of the self-measuring force feedback test (32-bit, like GTA IV).
rem Result is saved next to this file: FfbTest2_results.txt
setlocal
set "SRC=%~dp0"
set "CSC=%WINDIR%\Microsoft.NET\Framework\v4.0.30319\csc.exe"
if not exist "%CSC%" ( echo C# compiler not found at %CSC% & pause & exit /b 1 )
"%CSC%" /nologo /target:exe /platform:x86 /optimize+ /out:"%SRC%FfbTest2.exe" /reference:System.Windows.Forms.dll "%SRC%FfbTest2.cs" "%SRC%..\..\src\ForceFeedback.cs"
if errorlevel 1 ( echo. & echo BUILD FAILED - send the error lines above to Claude. & pause & exit /b 1 )
"%SRC%FfbTest2.exe"
echo.
echo All done. Tell Claude "test done".
pause
