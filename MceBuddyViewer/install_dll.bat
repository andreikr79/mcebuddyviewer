ECHO. Jas Manghera, Install.bat

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

echo "%1MCEBuddyMCE.AppWrapper.dll"

%gacUtil% /i "%1MCEBuddyMCE.AppWrapper.dll"
%gacUtil% /i "%1MCEBuddyMCE.CommercialScan.dll"
%gacUtil% /i "%1MCEBuddyMCE.Configuration.dll"
%gacUtil% /i "%1MCEBuddyMCE.DirectShowLib.dll"
%gacUtil% /i "%1MCEBuddyMCE.Engine.dll"
%gacUtil% /i "%1MCEBuddyMCE.Globals.dll"
%gacUtil% /i "%1MCEBuddyMCE.MetaData.dll"
%gacUtil% /i "%1MCEBuddyMCE.RemuxMediaCenter.dll"
%gacUtil% /i "%1MCEBuddyMCE.TagLib.dll"
%gacUtil% /i "%1MCEBuddyMCE.Transcode.dll"
%gacUtil% /i "%1MCEBuddyMCE.Util.dll"
%gacUtil% /i "%1MCEBuddyMCE.VideoProperties.dll"
