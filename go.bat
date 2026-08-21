@echo off
cd /d "%~dp0"
cd src && dotnet build && cd Armor.Tui\bin\debug\net10.0 && armor.tui
cd /d "%~dp0"
