@echo off
chcp 65001 >nul
set TARGET=%AppData%\Autodesk\Revit\Addins\2026
if not exist "%TARGET%" mkdir "%TARGET%"
copy /Y "%~dp0FormworkQTO.dll" "%TARGET%" >nul
copy /Y "%~dp0FormworkQTO.addin" "%TARGET%" >nul
echo 安裝完成：%TARGET%
echo 請重新啟動 Revit 2026，於「增益集 - 外部工具」使用。
pause
