@echo off
rem SubootLauncher diagnostics. Put this file next to SubootLauncher.exe and double-click it.
rem Only built-in Windows commands (no PowerShell), ASCII only: nothing to block or misread.
rem The report suboot-diagnostics.txt is written next to this file and opened in Notepad.
setlocal
set "HERE=%~dp0"
set "OUT=%~dp0suboot-diagnostics.txt"
echo SubootLauncher diagnostics. Please wait about 20 seconds...

rem If the launcher folder is read-only, write the report to the user profile instead
(echo test) > "%OUT%" 2>nul || set "OUT=%USERPROFILE%\suboot-diagnostics.txt"

echo == Date > "%OUT%"
echo %DATE% %TIME% >> "%OUT%"
echo [1/9] Windows version
echo. >> "%OUT%"
echo == Windows >> "%OUT%"
ver >> "%OUT%" 2>&1
echo PROCESSOR_ARCHITECTURE=%PROCESSOR_ARCHITECTURE% PROCESSOR_ARCHITEW6432=%PROCESSOR_ARCHITEW6432% >> "%OUT%"
reg query "HKLM\SOFTWARE\Microsoft\Windows NT\CurrentVersion" /v ProductName >> "%OUT%" 2>&1
reg query "HKLM\SOFTWARE\Microsoft\Windows NT\CurrentVersion" /v DisplayVersion >> "%OUT%" 2>&1

echo [2/9] Launcher file
echo. >> "%OUT%"
echo == Folder and files >> "%OUT%"
echo %HERE% >> "%OUT%"
dir "%HERE%SubootLauncher*" >> "%OUT%" 2>&1
echo -- Zone.Identifier (downloaded from internet mark): >> "%OUT%"
more < "%HERE%SubootLauncher.exe:Zone.Identifier" >> "%OUT%" 2>&1

echo [3/9] Smart App Control
echo. >> "%OUT%"
echo == Smart App Control (0 off, 1 ON, 2 evaluation) >> "%OUT%"
reg query "HKLM\SYSTEM\CurrentControlSet\Control\CI\Policy" /v VerifiedAndReputablePolicyState >> "%OUT%" 2>&1

echo [4/9] Code Integrity blocks
echo. >> "%OUT%"
echo == Code Integrity / Smart App Control events (last 15) >> "%OUT%"
wevtutil qe "Microsoft-Windows-CodeIntegrity/Operational" /c:15 /rd:true /f:text >> "%OUT%" 2>&1

echo [5/9] Antivirus
echo. >> "%OUT%"
echo == Antivirus products >> "%OUT%"
wmic /namespace:\\root\SecurityCenter2 path AntiVirusProduct get displayName,productState >> "%OUT%" 2>&1
echo -- Defender detections (last 10) >> "%OUT%"
wevtutil qe "Microsoft-Windows-Windows Defender/Operational" /q:"*[System[(EventID=1116 or EventID=1117)]]" /c:10 /rd:true /f:text >> "%OUT%" 2>&1

echo [6/9] Running launcher processes
echo. >> "%OUT%"
echo == SubootLauncher processes before start >> "%OUT%"
tasklist /fi "imagename eq SubootLauncher.exe" >> "%OUT%" 2>&1

echo [7/9] Trial start (10 seconds)
echo. >> "%OUT%"
echo == Trial start >> "%OUT%"
if exist "%HERE%SubootLauncher.exe" (
    start "" "%HERE%SubootLauncher.exe"
    ping -n 11 127.0.0.1 >nul
    tasklist /v /fi "imagename eq SubootLauncher.exe" >> "%OUT%" 2>&1
) else (
    echo SubootLauncher.exe NOT FOUND next to this script >> "%OUT%"
)

echo [8/9] Application errors
echo. >> "%OUT%"
echo == Application errors (.NET Runtime, Application Error, last 10) >> "%OUT%"
wevtutil qe Application /q:"*[System[Provider[@Name='.NET Runtime' or @Name='Application Error' or @Name='Windows Error Reporting']]]" /c:10 /rd:true /f:text >> "%OUT%" 2>&1

echo [9/9] Launcher log
echo. >> "%OUT%"
echo == launcher.log (missing = launcher never started its code) >> "%OUT%"
if exist "%APPDATA%\SubootGames\launcher.log" (
    type "%APPDATA%\SubootGames\launcher.log" >> "%OUT%" 2>&1
) else (
    echo launcher.log not found >> "%OUT%"
)

echo.
echo Done. Report: %OUT%
echo Send this file to the server administrator.
start "" notepad "%OUT%"
pause
