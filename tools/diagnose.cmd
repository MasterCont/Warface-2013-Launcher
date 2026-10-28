@echo off
rem Диагностика запуска SubootLauncher. Положите этот файл рядом с SubootLauncher.exe и запустите двойным щелчком.
rem Отчёт появится на рабочем столе: suboot-diagnostics.txt. Пароли и сессии в отчёт не попадают.
chcp 65001 >nul
echo Собираю сведения о системе, это займёт около 20 секунд...
powershell -NoProfile -ExecutionPolicy Bypass -Command "& { $here = '%~dp0'.TrimEnd('\'); $raw = [IO.File]::ReadAllText('%~f0', [Text.Encoding]::UTF8); $code = $raw.Substring($raw.LastIndexOf('#PS-BEGIN#') + 10); & ([ScriptBlock]::Create($code)) $here }"
echo.
echo Готово. Отправьте файл suboot-diagnostics.txt с рабочего стола администратору.
pause
exit /b

#PS-BEGIN#
param($here)
$out = Join-Path ([Environment]::GetFolderPath('Desktop')) 'suboot-diagnostics.txt'
$report = New-Object System.Collections.Generic.List[string]
function Add($title, $value) { $report.Add("== $title"); $report.Add(($value | Out-String).TrimEnd()); $report.Add('') }
function Try($block) { try { & $block } catch { "ошибка: $($_.Exception.Message)" } }

$exe = Join-Path $here 'SubootLauncher.exe'
Add 'Время' (Get-Date -Format 'yyyy-MM-dd HH:mm:ss')
Add 'Windows' (Try { $os = Get-CimInstance Win32_OperatingSystem; "$($os.Caption) $($os.Version) build $($os.BuildNumber), $($os.OSArchitecture)" })
Add 'Папка' $here
Add 'Файл лаунчера' (Try {
    if (-not (Test-Path $exe)) { 'SubootLauncher.exe НЕ найден рядом со скриптом' } else {
        $f = Get-Item $exe
        "размер: $($f.Length) байт ($([math]::Round($f.Length / 1MB, 1)) МБ)  (правильная сборка — около 62 МБ; пара сотен КБ — неправильная)"
        "изменён: $($f.LastWriteTime)"
        $zone = Get-Content $exe -Stream Zone.Identifier -ErrorAction SilentlyContinue
        if ($zone) { 'пометка «скачан из интернета»: ЕСТЬ'; $zone } else { 'пометка «скачан из интернета»: нет' }
    } })

# Smart App Control: 0 — выключен, 1 — включён, 2 — режим оценки
Add 'Smart App Control' (Try {
    $v = (Get-ItemProperty 'HKLM:\SYSTEM\CurrentControlSet\Control\CI\Policy' -Name VerifiedAndReputablePolicyState -ErrorAction Stop).VerifiedAndReputablePolicyState
    switch ($v) { 0 { '0 — выключен' } 1 { '1 — ВКЛЮЧЁН (блокирует неподписанные программы)' } 2 { '2 — режим оценки' } default { $v } } })
Add 'Блокировки Code Integrity / Smart App Control (7 дней)' (Try {
    Get-WinEvent -FilterHashtable @{ LogName = 'Microsoft-Windows-CodeIntegrity/Operational'; StartTime = (Get-Date).AddDays(-7) } -ErrorAction Stop |
        Where-Object { $_.Message -match 'SubootLauncher|Game\.exe|Warface' } | Select-Object -First 10 |
        ForEach-Object { "$($_.TimeCreated) [$($_.Id)] " + ($_.Message -split "`n")[0] } })

Add 'Защитник Windows: срабатывания' (Try {
    Get-MpThreatDetection -ErrorAction Stop | Where-Object { ($_.Resources -join ' ') -match 'SubootLauncher|Warface' } |
        ForEach-Object { "$($_.InitialDetectionTime) $($_.ThreatID) $($_.Resources -join ', ')" } })

Add 'Процессы SubootLauncher до запуска' (Try { Get-Process SubootLauncher -ErrorAction SilentlyContinue | ForEach-Object { "$($_.Id) $($_.Path) окно: '$($_.MainWindowTitle)'" } })

Add 'Пробный запуск (8 секунд)' (Try {
    if (Test-Path $exe) {
        $p = Start-Process $exe -WorkingDirectory $here -PassThru
        Start-Sleep 8
        if ($p.HasExited) { "процесс завершился, код $($p.ExitCode) (0x{0:X8})" -f $p.ExitCode } else { $p.Refresh(); "работает, окно: '$($p.MainWindowTitle)'" }
    } })

Add 'Ошибки приложений (1 день)' (Try {
    Get-WinEvent -FilterHashtable @{ LogName = 'Application'; StartTime = (Get-Date).AddDays(-1) } -ErrorAction Stop |
        Where-Object { $_.Message -match 'SubootLauncher' } | Select-Object -First 6 |
        ForEach-Object { "---- $($_.TimeCreated) $($_.ProviderName)"; $_.Message.Substring(0, [Math]::Min(1200, $_.Message.Length)) } })

Add 'Журнал лаунчера (последние строки)' (Try {
    $log = Join-Path $env:APPDATA 'SubootGames\launcher.log'
    if (Test-Path $log) { Get-Content $log -Encoding UTF8 -Tail 40 } else { 'launcher.log нет — лаунчер не успел начать работу (обычно его блокирует Windows или антивирус)' } })

[IO.File]::WriteAllLines($out, $report, (New-Object Text.UTF8Encoding $true))
Write-Host "Отчёт: $out"
