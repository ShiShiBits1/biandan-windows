@echo off
setlocal
title Biandan MCP
"%~dp0biandan-mcp.exe" --http
if errorlevel 1 pause
