@echo off
setlocal
set "BIANDAN_CONFIG_EXE=%~dp0biandan-mcp.exe"
powershell.exe -NoProfile -Command "$ErrorActionPreference='Stop'; $config = & $env:BIANDAN_CONFIG_EXE --print-http-config; if ($LASTEXITCODE -ne 0 -or -not $config) { exit 1 }; Set-Clipboard -Value ($config -join [Environment]::NewLine)"
if errorlevel 1 (
  echo Could not copy the MCP configuration. See README.md.
  pause
  exit /b 1
)
echo Copied. Paste the HTTP configuration into your AI client's MCP settings.
pause
