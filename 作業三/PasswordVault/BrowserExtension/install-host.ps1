param(
  [Parameter(Mandatory = $true)][string]$ExtensionId,
  [string]$Configuration = "Release"
)
$ErrorActionPreference = "Stop"

$exe = (Resolve-Path (Join-Path $PSScriptRoot "..\Vault.NativeHost\bin\$Configuration\net10.0\Vault.NativeHost.exe")).Path
$dir = Join-Path $env:LOCALAPPDATA "PasswordVault"
New-Item -ItemType Directory $dir -Force | Out-Null

$template = Get-Content (Join-Path $PSScriptRoot "com.passwordvault.host.json") -Raw
$manifest = Join-Path $dir "com.passwordvault.host.json"
$template.Replace("__HOST_EXE_PATH__", $exe.Replace("\", "\\")).Replace("__EXTENSION_ID__", $ExtensionId) |
  Set-Content $manifest -Encoding UTF8

foreach ($base in @(
  "HKCU:\Software\Google\Chrome\NativeMessagingHosts",
  "HKCU:\Software\Microsoft\Edge\NativeMessagingHosts"
)) {
  $key = Join-Path $base "com.passwordvault.host"
  New-Item $key -Force | Out-Null
  Set-ItemProperty $key -Name "(default)" -Value $manifest
}

Write-Output "installed: $manifest"
