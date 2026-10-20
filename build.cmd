@echo off
rem Builds LcdLabelHelper-<version>.exe with the C# compiler that ships with Windows (.NET Framework 4).
rem The version comes from the "public const string Version" line in LcdLabelHelper.cs.
setlocal
cd /d "%~dp0"
set VERSION=
for /f "tokens=2 delims==" %%v in ('findstr /c:"public const string Version" LcdLabelHelper.cs') do set VERSION=%%v
if not defined VERSION goto noversion
rem Strip the quotes, semicolon and spaces around the value.
set VERSION=%VERSION:"=%
set VERSION=%VERSION:;=%
set VERSION=%VERSION: =%
"%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe" -nologo -target:winexe -optimize -out:LcdLabelHelper-%VERSION%.exe LcdLabelHelper.cs
if errorlevel 1 exit /b 1
echo Built LcdLabelHelper-%VERSION%.exe
exit /b 0

:noversion
echo Could not read Version from LcdLabelHelper.cs
exit /b 1
