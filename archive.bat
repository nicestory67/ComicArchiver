@echo off
setlocal enabledelayedexpansion
set "arg=%~1"
set "type="
if "!arg!"=="cbz" (
    set "type=cbz"
) else if "!arg!"=="zip" (
    set "type=zip"
)
if defined type (
    for /d %%F in (*) do (
        set "folder=%%F"
        echo "!folder!"
        "C:\Program Files\WinRAR\WinRAR.exe" a -r -x*.db -dr -afzip -ep1 -ibck "!folder!.!type!" "!folder!\"
        del /Q /F /S Thumbs.db >nul 2>&1
        rd /S/Q "!folder!"
    )
) else (
    echo "未指定类型，支持 cbz、zip"
)
pause