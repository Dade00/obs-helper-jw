@echo off
REM Compila publish + installer. Eseguire dalla root del repo.
setlocal
dotnet publish -c Release -r win-x64 --self-contained true /p:TargetPlatformVersion=10.0.26100.0
if errorlevel 1 exit /b 1
set ISCC=%LOCALAPPDATA%\Programs\Inno Setup 6\ISCC.exe
if not exist "%ISCC%" set ISCC=C:\Program Files (x86)\Inno Setup 6\ISCC.exe
"%ISCC%" installer\obs-helper-jw.iss
if errorlevel 1 exit /b 1
echo Fatto: dist\
