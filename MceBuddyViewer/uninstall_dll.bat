ECHO. Jas Manghera, Uninstall.bat

set ProgramFilesPath = %ProgramFiles%
set Netfx20Path = %windir%\Microsoft.NET\Framework64\v2.0.50727

REM Determine whether we are on an 32 or 64 bit machine
if "%PROCESSOR_ARCHITECTURE%"=="x86" if "%PROCESSOR_ARCHITEW6432%"=="" goto x86

:x64
	set ProgramFilesPath=%ProgramFiles%
	ECHO.

:x86
	set ProgramFilesPath=%ProgramFiles%
	ECHO.


if exist "%ProgramFilesPath%\Microsoft.NET\SDK\v2.0\bin\gacutil.exe" (
set gacUtil="%ProgramFilesPath%\Microsoft.NET\SDK\v2.0\bin\gacutil.exe"
) ELSE (
if exist "C:\Program Files\Microsoft SDKs\Windows\v6.0A\Bin\gacutil.exe" (
set gacUtil="C:\Program Files\Microsoft SDKs\Windows\v6.0A\Bin\gacutil.exe"
) else (
set gacUtil="%ProgramFilesPath%\Microsoft Visual Studio 8\SDK\v2.0\Bin\gacutil.exe")
)

echo gac = %gacUtil%

%gacUtil% /u MCEBuddyMCE.AppWrapper
%gacUtil% /u MCEBuddyMCE.CommercialScan
%gacUtil% /u MCEBuddyMCE.Configuration
%gacUtil% /u MCEBuddyMCE.DirectShowLib
%gacUtil% /u MCEBuddyMCE.Engine
%gacUtil% /u MCEBuddyMCE.Globals
%gacUtil% /u MCEBuddyMCE.MetaData
%gacUtil% /u MCEBuddyMCE.RemuxMediaCenter
%gacUtil% /u MCEBuddyMCE.TagLib
%gacUtil% /u MCEBuddyMCE.Transcode
%gacUtil% /u MCEBuddyMCE.Util
%gacUtil% /u MCEBuddyMCE.VideoProperties
