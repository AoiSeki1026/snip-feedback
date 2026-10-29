# ショートカット（デスクトップとスタートメニュー）を作る。アイコンは v2（画面切り取りフィードバック）と同じ app.ico（H-M38、r16 R-23）
# 使い方: npm run shortcuts
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$name = '画面赤枠フィードバック'
$icon = Join-Path $root 'win\src\SnipFeedback\app.ico'

if (-not (Test-Path $icon)) { throw "アイコンがありません: $icon" }

$node = (Get-Command node -ErrorAction Stop).Source
$wscript = Join-Path $env:WINDIR 'System32\wscript.exe'
$vbs = Join-Path $root 'tools\launch.vbs'
$shell = New-Object -ComObject WScript.Shell
foreach ($dir in @([Environment]::GetFolderPath('Desktop'), [Environment]::GetFolderPath('Programs'))) {
  $lnk = Join-Path $dir "$name.lnk"
  $s = $shell.CreateShortcut($lnk)
  $s.TargetPath = $wscript
  $s.Arguments = '"' + $vbs + '" "' + $node + '"'
  $s.WorkingDirectory = $root
  $s.IconLocation = "$icon,0"
  $s.Description = '画面赤枠フィードバックを起動（案内ページを開く）'
  $s.Save()
  Write-Output "作成: $lnk"
}
