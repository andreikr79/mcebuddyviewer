ECHO. Jas Manghera, Uninstall.bat

set ProgramFilesPath = %ProgramFiles%
set Netfx20Path = %windir%\Microsoft.NET\Framework64\v2.0.50727

REM Determine whether we are on an 32 or 64 bit machine
if "%PROCESSOR_ARCHITECTURE%"=="x86" if "%PROCESSOR_ARCHITEW6432%"=="" goto x86

:x64
	set ProgramFilesPath=%ProgramFiles(x86)%
	ECHO.

:x86
	set ProgramFilesPath=%ProgramFiles%
	ECHO.


if exist "%ProgramFilesPath%\Microsoft.NET\SDK\v2.0\bin\gacutil.exe" (
set gacUtil="%ProgramFilesPath%\Microsoft.NET\SDK\v2.0\bin\gacutil.exe"
) ELSE (
if exist "%ProgramFilesPath%\Microsoft SDKs\Windows\v7.1\bin\gacutil.exe" (
set gacUtil="%ProgramFilesPath%\Microsoft SDKs\Windows\v7.1\bin\gacutil.exe"
) else (
set gacUtil="%ProgramFilesPath%\Microsoft Visual Studio 8\SDK\v2.0\Bin\gacutil.exe")
)

echo gac = %gacUtil%

%gacUtil% /u %1
"%windir%\ehome\RegisterMCEApp.exe" %2 /u /allusers
rem uninstall_dll.bat