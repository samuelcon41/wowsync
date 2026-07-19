@echo off
rem Builds WoWSyncSetup.exe using the C# compiler that ships with Windows.
rem wowsync-agent.ps1 is embedded into the exe as a resource - keep it in this folder.
C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe /nologo /target:winexe ^
  /out:WoWSyncSetup.exe /win32manifest:app.manifest ^
  /resource:wowsync-agent.ps1,wowsync-agent.ps1 ^
  /r:System.Windows.Forms.dll /r:System.Drawing.dll /r:System.Management.dll WoWSyncSetup.cs
if %errorlevel%==0 echo Built WoWSyncSetup.exe
