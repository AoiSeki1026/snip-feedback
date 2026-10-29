# 画面切り取りフィードバック（v2）をビルドして %USERPROFILE%\.snipfeedback\app に置き、
# タスク スケジューラに登録して起動する（N-03、r10）。-Uninstall で登録を外して止める（置いたファイルと保存先は消さない）
#
# 置き場所を AppData の外にする理由（H-M31）: Claude デスクトップアプリ（パッケージのアプリ）の中から実行すると、
# AppData への書き込みはそのパッケージ専用の場所に振り替えられ、外から見えない。
# タスク スケジューラで起動する理由（H-M35）:
#   - スタートアップのフォルダーは他の常駐の後に回され、サインインから数分遅れる（10/14 は約 3 分半）
#   - シャットダウンが他のアプリに止められると、常駐だけ先に閉じられて消える（10/19 は Excel が止めた）
#   → サインインしたとき・ロックを解除したとき・スリープから戻ったとき・5 分ごとに起動する。
#     動いている間はタスクの「実行中」として数えられ、二重には起動しない（消えたら次の起動の合図で戻る）。
#   - タスク スケジューラから起動するので、Claude のジョブの外で動く（Claude の更新で一緒に閉じられない）
param([switch]$Uninstall)
$ErrorActionPreference = 'Stop'

$name = '画面切り取りフィードバック'
$task = 'SnipFeedback'
$app = Join-Path $env:USERPROFILE '.snipfeedback\app'
$exe = Join-Path $app 'SnipFeedback.exe'
$lnk = Join-Path ([Environment]::GetFolderPath('Startup')) "$name.lnk"
$user = "$env:USERDOMAIN\$env:USERNAME"

# 先にタスクを無効にしてから止める。止めてからビルド・登録が終わるまでの間に、見張りの合図で古い exe が
# 起動されないように（ファイルが掴まれてビルドが失敗する・一部だけ置き換わる。verify K1・K4）
if (Get-ScheduledTask -TaskName $task -ErrorAction SilentlyContinue) {
    Disable-ScheduledTask -TaskName $task | Out-Null
    Stop-ScheduledTask -TaskName $task -ErrorAction SilentlyContinue
}
Get-Process SnipFeedback -ErrorAction SilentlyContinue | Stop-Process -Force
Start-Sleep -Milliseconds 300
if (Test-Path $lnk) { Remove-Item $lnk }   # 前の版のスタートアップのショートカット

if ($Uninstall) {
    if (Get-ScheduledTask -TaskName $task -ErrorAction SilentlyContinue) { Unregister-ScheduledTask -TaskName $task -Confirm:$false }
    Write-Output "タスクの登録を外し、止めました（$app と保存先は残しています）"
    return
}

$dotnet = (Get-Command dotnet -ErrorAction SilentlyContinue).Source
if (-not $dotnet) { $dotnet = 'C:\Program Files\dotnet\dotnet.exe' }
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$proj = Join-Path $PSScriptRoot '..\src\SnipFeedback\SnipFeedback.csproj'
& $dotnet publish $proj -c Release -o $app --nologo -v q
if ($LASTEXITCODE -ne 0) {
    # 失敗したら無効にしたタスクを戻し、前の版で常駐を続ける（無効のまま・止めたままで終わらない。verify-b K9）
    if (Get-ScheduledTask -TaskName $task -ErrorAction SilentlyContinue) {
        Enable-ScheduledTask -TaskName $task | Out-Null
        Start-ScheduledTask -TaskName $task -ErrorAction SilentlyContinue
    }
    throw "ビルドに失敗しました（$LASTEXITCODE）。前の版で常駐を続けます"
}

$ns = 'Root/Microsoft/Windows/TaskScheduler'
# サインインしたとき
$logon = New-ScheduledTaskTrigger -AtLogOn -User $user
# ロックを解除したとき（StateChange 8 = SessionUnlock）
$unlock = New-CimInstance -CimClass (Get-CimClass -Namespace $ns -ClassName MSFT_TaskSessionStateChangeTrigger) -ClientOnly
$unlock.StateChange = 8
$unlock.UserId = $user
$unlock.Enabled = $true
# スリープから戻ったとき（System の Power-Troubleshooter イベント 1）
$resume = New-CimInstance -CimClass (Get-CimClass -Namespace $ns -ClassName MSFT_TaskEventTrigger) -ClientOnly
$resume.Subscription = '<QueryList><Query Id="0" Path="System"><Select Path="System">*[System[Provider[@Name=''Microsoft-Windows-Power-Troubleshooter''] and EventID=1]]</Select></Query></QueryList>'
$resume.Enabled = $true
# 5 分ごと（消えていたら戻す）。期間を指定しないので繰り返しは続く
$every = New-ScheduledTaskTrigger -Once -At (Get-Date).AddMinutes(1) -RepetitionInterval (New-TimeSpan -Minutes 5)

$action = New-ScheduledTaskAction -Execute $exe -WorkingDirectory $app
# 実行時間の上限なし（既定の 72 時間で止められないように）・二重に起動しない・優先度は普通（既定の 7 は低すぎる）・電池でも動く
$settings = New-ScheduledTaskSettingsSet -ExecutionTimeLimit ([TimeSpan]::Zero) -MultipleInstances IgnoreNew -Priority 4 `
    -AllowStartIfOnBatteries -DontStopIfGoingOnBatteries -StartWhenAvailable
$principal = New-ScheduledTaskPrincipal -UserId $user -LogonType Interactive -RunLevel Limited
# 登録し直す（-Force で上書き。上書きした登録は有効になる）
Register-ScheduledTask -TaskName $task -Description $name -Action $action -Trigger @($logon, $unlock, $resume, $every) `
    -Settings $settings -Principal $principal -Force | Out-Null
Enable-ScheduledTask -TaskName $task | Out-Null

# トレイの「終了」で置いた「次のサインインまで止める」印を消してから起動する（H-M35 1a）。
# Claude デスクトップアプリの中から実行するとレジストリへの書き込みも Claude 専用の場所に振り替えられるので、
# WMI で起動した reg.exe（Claude の外）でも消す（2026-10-19 に確認）
Remove-ItemProperty -Path 'HKCU:\Volatile Environment' -Name SnipFeedbackPaused -ErrorAction SilentlyContinue
$r = Invoke-CimMethod -ClassName Win32_Process -MethodName Create -Arguments @{ CommandLine = 'reg.exe delete "HKCU\Volatile Environment" /v SnipFeedbackPaused /f' }
Start-Sleep -Milliseconds 800
Start-ScheduledTask -TaskName $task
Write-Output "登録しました: タスク「$task」→ $exe（サインイン・ロック解除・スリープ明け・5 分ごと。起動しました）"
