@echo off
docker --context desktop-linux compose --project-directory "%~dp0." --file "%~dp0compose.yaml" --env-file "%~dp0.env" stop
if errorlevel 1 pause
