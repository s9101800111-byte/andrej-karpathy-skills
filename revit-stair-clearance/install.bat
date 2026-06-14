@echo off
chcp 65001 >nul
setlocal

rem 樓梯淨高檢核 一鍵安裝
rem 把與本檔同層的 DLL 與 .addin 複製到 Revit Addins 資料夾。
rem 用法:直接雙擊 = 安裝到 2026;或在命令列指定版本,例如  install.bat 2024

set "VER=%~1"
if "%VER%"=="" set "VER=2026"

set "SRC=%~dp0"
set "DST=%APPDATA%\Autodesk\Revit\Addins\%VER%"

echo 安裝樓梯淨高檢核 ^(Revit %VER%^)
echo 來源: %SRC%
echo 目標: %DST%
echo.

if not exist "%SRC%StairClearanceCheck.dll" (
  echo [錯誤] 找不到 StairClearanceCheck.dll,請把本檔與 dll/.addin 放在同一資料夾。
  pause
  exit /b 1
)

if not exist "%DST%" mkdir "%DST%"

copy /Y "%SRC%StairClearanceCheck.dll" "%DST%\" >nul && ^
copy /Y "%SRC%StairClearanceCheck.addin" "%DST%\" >nul

if errorlevel 1 (
  echo [錯誤] 複製失敗。請確認 Revit 已關閉後再試。
  pause
  exit /b 1
)

echo 完成!請重新啟動 Revit,於「增益集 → 外部工具 → 樓梯淨高檢核」執行。
pause
