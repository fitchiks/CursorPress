@echo off
cd /d "%~dp0"
if exist "CursorPress.exe" (
  start "" /wait "CursorPress.exe" --restore
) else (
  powershell.exe -NoProfile -Command "Add-Type -TypeDefinition 'using System; using System.Runtime.InteropServices; public static class CursorRecovery { [DllImport(\"user32.dll\")] public static extern bool SystemParametersInfo(uint a,uint b,IntPtr c,uint d); }'; [void][CursorRecovery]::SystemParametersInfo(87,0,[IntPtr]::Zero,0)"
)
