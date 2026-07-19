@echo off
rem Builds WoWSyncSetup.exe using the C# compiler that ships with Windows.
C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe /nologo /target:winexe ^
  /out:WoWSyncSetup.exe /win32manifest:app.manifest ^
  /r:System.Windows.Forms.dll /r:System.Drawing.dll WoWSyncSetup.cs
if %errorlevel%==0 echo Built WoWSyncSetup.exe
