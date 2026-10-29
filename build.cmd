@echo off
rem Build DSH-Isolated.exe from src\launcher.cs
setlocal
cd /d "%~dp0"

set "CSC=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
if not exist "%CSC%" set "CSC=%WINDIR%\Microsoft.NET\Framework\v4.0.30319\csc.exe"
if not exist "%CSC%" (
  echo [ERROR] csc.exe not found. .NET Framework 4.x is required.
  exit /b 1
)

if not exist "dist" mkdir "dist"

"%CSC%" -nologo -target:winexe -optimize+ -codepage:65001 ^
  -win32icon:assets\app.ico ^
  -out:dist\DSH-Isolated.exe ^
  src\launcher.cs ^
  -r:System.Windows.Forms.dll -r:System.Drawing.dll

if errorlevel 1 (
  echo [ERROR] Build failed.
  exit /b 1
)

copy /y launcher.ini dist\launcher.ini >nul
echo [OK] dist\DSH-Isolated.exe
