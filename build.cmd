@echo off
rem Builds LcdLabelHelper.exe with the C# compiler that ships with Windows (.NET Framework 4).
rem The version shown in the exe's Properties comes from "public const string Version" in LcdLabelHelper.cs.
cd /d "%~dp0"
"%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe" -nologo -target:winexe -optimize -out:LcdLabelHelper.exe LcdLabelHelper.cs
