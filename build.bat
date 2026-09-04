@echo off
rem Разовая сборка: двойной щелчок. Результат — в build.log рядом с этим файлом.
cd /d "%~dp0"
dotnet build DitaStudio.sln -c Debug --nologo -v minimal > build.log 2>&1
echo EXITCODE=%ERRORLEVEL% >> build.log
type build.log
pause
