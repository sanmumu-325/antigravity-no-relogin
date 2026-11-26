[CmdletBinding()]
param(
    [ValidateSet('save', 'use', 'list', 'status', 'open', 'delete')]
    [string]$Command = 'list',
    [string]$Name,
    [switch]$NoLaunch
)

$ErrorActionPreference = 'Stop'

$HomeDir     = $env:USERPROFILE
$AppExe      = Join-Path $env:LOCALAPPDATA 'Programs\antigravity\Antigravity.exe'
$TokenFile   = Join-Path $HomeDir '.gemini\jetski-standalone-oauth-token'
$CliToken    = Join-Path $HomeDir '.gemini\antigravity-cli\antigravity-oauth-token'
$OauthCreds  = Join-Path $HomeDir '.gemini\oauth_creds.json'
$StorageFile = Join-Path $env:APPDATA 'Antigravity\app_storage.json'
$RootDir     = Join-Path $HomeDir '.antigravity-switch'
$ProfileDir  = Join-Path $RootDir 'profiles'
$BackupDir   = Join-Path $RootDir 'backups'
$CurrentFile = Join-Path $RootDir 'current.json'
$ProcessNames = @('Antigravity', 'language_server', 'agy')

function Write-Info($text) { Write-Host $text -ForegroundColor Cyan }
function Write-Warn($text) { Write-Host $text -ForegroundColor Yellow }
function Write-Err($text)  { Write-Host $text -ForegroundColor Red }

function Get-JwtPayload($jwt) {
    if (-not $jwt) { return $null }
    $parts = $jwt.Split('.')
    if ($parts.Count -lt 2) { return $null }
    $p = $parts[1].Replace('-', '+').Replace('_', '/')
    while ($p.Length % 4 -ne 0) { $p += '=' }
    try {
        $json = [Text.Encoding]::UTF8.GetString([Convert]::FromBase64String($p))
        return $json | ConvertFrom-Json
    } catch { return $null }
}

function Get-TokenEmail($token) {
    if (-not $token) { return '' }
    if ($token.id_token_email) { return [string]$token.id_token_email }
    if ($token.email) { return [string]$token.email }
    $inner = if ($token.token) { $token.token } else { $token }
    $payload = Get-JwtPayload $inner.id_token
    if ($payload -and $payload.email) { return [string]$payload.email }
    return ''
}

function Read-Token($path) {
    if (-not (Test-Path -LiteralPath $path)) { return $null }
    try { return (Get-Content -LiteralPath $path -Raw -Encoding UTF8) | ConvertFrom-Json }
    catch { throw "令牌文件不是有效 JSON：$path" }
}

function Get-Running {
    $found = @()
    foreach ($n in $ProcessNames) {
        $found += Get-Process -Name $n -ErrorAction SilentlyContinue
    }
    return $found
}

function Initialize-CredentialApi {
    if ('AgyCred' -as [type]) { return }
    Add-Type -TypeDefinition @"
using System;
using System.Runtime.InteropServices;
public static class AgyCred {
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct CREDENTIAL {
        public uint Flags;
        public uint Type;
        public IntPtr TargetName;
        public IntPtr Comment;
        public System.Runtime.InteropServices.ComTypes.FILETIME LastWritten;
        public uint CredentialBlobSize;
        public IntPtr CredentialBlob;
        public uint Persist;
        public uint AttributeCount;
        public IntPtr Attributes;
        public IntPtr TargetAlias;
        public IntPtr UserName;
    }
    [DllImport("advapi32.dll", EntryPoint = "CredReadW", CharSet = CharSet.Unicode, SetLastError = true)]
    public static extern bool CredRead(string target, uint type, uint reserved, out IntPtr credential);
    [DllImport("advapi32.dll", EntryPoint = "CredWriteW", CharSet = CharSet.Unicode, SetLastError = true)]
    public static extern bool CredWrite(ref CREDENTIAL credential, uint flags);
    [DllImport("advapi32.dll", SetLastError = true)]
    public static extern bool CredFree(IntPtr credential);
    [DllImport("advapi32.dll", EntryPoint = "CredEnumerateW", CharSet = CharSet.Unicode, SetLastError = true)]
    public static extern bool CredEnumerate(string filter, uint flags, out uint count, out IntPtr credentials);
    [DllImport("advapi32.dll", EntryPoint = "CredDeleteW", CharSet = CharSet.Unicode, SetLastError = true)]
    public static extern bool CredDelete(string target, uint type, uint flags);
}
"@
}

function Test-KeyringTarget($target) {
    return $target -match '(?i)antigravity|jetski'
}

function Get-KeyringRecords {
    Initialize-CredentialApi
    $count = 0
    $ptr = [IntPtr]::Zero
    if (-not [AgyCred]::CredEnumerate($null, 0, [ref]$count, [ref]$ptr)) { return @() }
    $records = @()
    try {
        for ($i = 0; $i -lt $count; $i++) {
            $itemPtr = [Runtime.InteropServices.Marshal]::ReadIntPtr($ptr, $i * [IntPtr]::Size)
            $cred = [Runtime.InteropServices.Marshal]::PtrToStructure($itemPtr, [type][AgyCred+CREDENTIAL])
            if ($cred.Type -ne 1) { continue }
            $target = [Runtime.InteropServices.Marshal]::PtrToStringUni($cred.TargetName)
            if (-not (Test-KeyringTarget $target)) { continue }
            $user = if ($cred.UserName -ne [IntPtr]::Zero) { [Runtime.InteropServices.Marshal]::PtrToStringUni($cred.UserName) } else { '' }
            $size = [int]$cred.CredentialBlobSize
            $bytes = New-Object byte[] $size
            if ($size -gt 0) { [Runtime.InteropServices.Marshal]::Copy($cred.CredentialBlob, $bytes, 0, $size) }
            $blob = [Text.Encoding]::UTF8.GetString($bytes)
            $records += [pscustomobject]@{
                target   = $target
                username = $user
                persist  = [int]$cred.Persist
                blob     = $blob
            }
        }
    } finally {
        [AgyCred]::CredFree($ptr) | Out-Null
    }
    return $records
}

function Remove-KeyringRecords {
    Initialize-CredentialApi
    foreach ($record in Get-KeyringRecords) {
        [AgyCred]::CredDelete($record.target, 1, 0) | Out-Null
    }
}

function Write-KeyringRecord($record) {
    Initialize-CredentialApi
    $targetPtr = [Runtime.InteropServices.Marshal]::StringToHGlobalUni($record.target)
    $userPtr = [Runtime.InteropServices.Marshal]::StringToHGlobalUni($record.username)
    $bytes = [Text.Encoding]::UTF8.GetBytes($record.blob)
    $blobPtr = [Runtime.InteropServices.Marshal]::AllocHGlobal([Math]::Max($bytes.Length, 1))
    try {
        [Runtime.InteropServices.Marshal]::Copy($bytes, 0, $blobPtr, $bytes.Length)
        $cred = [AgyCred+CREDENTIAL]::new()
        $cred.Type = 1
        $cred.TargetName = $targetPtr
        $cred.UserName = $userPtr
        $cred.CredentialBlob = $blobPtr
        $cred.CredentialBlobSize = [uint32]$bytes.Length
        $cred.Persist = [uint32]$(if ($record.persist -in 1, 2, 3) { $record.persist } else { 2 })
        if (-not [AgyCred]::CredWrite([ref]$cred, 0)) {
            throw "写入凭据管理器失败：$($record.target)（错误码 $([Runtime.InteropServices.Marshal]::GetLastWin32Error())）"
        }
    } finally {
        [Runtime.InteropServices.Marshal]::FreeHGlobal($targetPtr)
        [Runtime.InteropServices.Marshal]::FreeHGlobal($userPtr)
        [Runtime.InteropServices.Marshal]::FreeHGlobal($blobPtr)
    }
}

function Stop-Antigravity {
    $procs = Get-Running
    if (-not $procs) { return }
    Write-Info "正在关闭反重力（$($procs.Count) 个进程）..."
    $procs | Stop-Process -Force -ErrorAction SilentlyContinue
    $deadline = (Get-Date).AddSeconds(10)
    while ((Get-Running) -and (Get-Date) -lt $deadline) { Start-Sleep -Milliseconds 300 }
    if (Get-Running) { throw '反重力进程没有完全退出，请手动结束后再试。' }
    Start-Sleep -Milliseconds 500
}

function Backup-File($path, $label) {
    if (-not (Test-Path -LiteralPath $path)) { return }
    New-Item -ItemType Directory -Path $BackupDir -Force | Out-Null
    $stamp = Get-Date -Format 'yyyyMMdd-HHmmss'
    Copy-Item -LiteralPath $path -Destination (Join-Path $BackupDir "$label-$stamp.bak") -Force
}

function Update-StorageEmail($email) {
    if (-not (Test-Path -LiteralPath $StorageFile)) { return }
    Backup-File $StorageFile 'app_storage'
    $text = [IO.File]::ReadAllText($StorageFile)
    $obj = $text | ConvertFrom-Json
    if ($obj.PSObject.Properties.Name -contains 'agy.onboarding.lastLoginUsername') {
        $obj.'agy.onboarding.lastLoginUsername' = $email
    } else {
        $obj | Add-Member -NotePropertyName 'agy.onboarding.lastLoginUsername' -NotePropertyValue $email
    }
    $out = $obj | ConvertTo-Json -Depth 64 -Compress
    [IO.File]::WriteAllText($StorageFile, $out, (New-Object Text.UTF8Encoding($false)))
}

function Get-Profiles {
    if (-not (Test-Path -LiteralPath $ProfileDir)) { return @() }
    Get-ChildItem -LiteralPath $ProfileDir -Directory | ForEach-Object {
        $metaPath = Join-Path $_.FullName 'meta.json'
        if (Test-Path -LiteralPath $metaPath) {
            $meta = Get-Content -LiteralPath $metaPath -Raw -Encoding UTF8 | ConvertFrom-Json
            [pscustomobject]@{
                Name    = $_.Name
                Email   = [string]$meta.email
                SavedAt = [string]$meta.savedAt
                Dir     = $_.FullName
            }
        }
    }
}

function Save-Profile {
    $token = Read-Token $TokenFile
    $keyring = @(Get-KeyringRecords)
    if (-not $token) {
        foreach ($record in $keyring) {
            try {
                $candidate = $record.blob | ConvertFrom-Json
                $payload = if ($candidate.token) { $candidate.token } else { $candidate }
                if ($payload.access_token -or $payload.refresh_token) { $token = $candidate; break }
            } catch {}
        }
    }
    if (-not $token) {
        throw "没有找到登录令牌：$TokenFile`n请先在反重力里登录一个账号，再执行 save。"
    }
    $email = Get-TokenEmail $token
    if (-not $Name) {
        $Name = if ($email) { ($email -split '@')[0] } else { 'account' }
    }
    $slug = ($Name -replace '[^\w\-.@]', '_').Trim('_')
    $dir = Join-Path $ProfileDir $slug
    New-Item -ItemType Directory -Path $dir -Force | Out-Null
    if (Test-Path -LiteralPath $TokenFile) {
        Copy-Item -LiteralPath $TokenFile -Destination (Join-Path $dir 'token.json') -Force
    } else {
        Remove-Item -LiteralPath (Join-Path $dir 'token.json') -Force -ErrorAction SilentlyContinue
    }
    if (Test-Path -LiteralPath $CliToken) {
        Copy-Item -LiteralPath $CliToken -Destination (Join-Path $dir 'cli-token.json') -Force
    }
    if (Test-Path -LiteralPath $OauthCreds) {
        Copy-Item -LiteralPath $OauthCreds -Destination (Join-Path $dir 'oauth-creds.json') -Force
    }
    if ($keyring.Count -gt 0) {
        $keyring | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $dir 'keyring.json') -Encoding UTF8
    } else {
        Remove-Item -LiteralPath (Join-Path $dir 'keyring.json') -Force -ErrorAction SilentlyContinue
    }
    $meta = [pscustomobject]@{
        name    = $slug
        email   = $email
        savedAt = (Get-Date).ToString('s')
    }
    $meta | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $dir 'meta.json') -Encoding UTF8
    try {
        icacls $RootDir /inheritance:r /grant:r "$($env:USERNAME):(OI)(CI)F" | Out-Null
    } catch {}
    $emailText = if ($email) { " ($email)" } else { '' }
    Write-Info "已保存账号 [$slug]$emailText（凭据管理器条目：$($keyring.Count)）"
}

function Use-Profile {
    if (-not $Name) { throw '用法：switch.ps1 use <账号名>（先运行 list 查看已保存账号）' }
    $profiles = Get-Profiles
    $target = $profiles | Where-Object { $_.Name -eq $Name -or $_.Email -like "*$Name*" } | Select-Object -First 1
    if (-not $target) { throw "找不到账号：$Name。可用账号：$($profiles.Name -join ', ')" }
    $src = Join-Path $target.Dir 'token.json'
    $keyringPath = Join-Path $target.Dir 'keyring.json'
    $hasTokenFile = Test-Path -LiteralPath $src
    $hasKeyring = Test-Path -LiteralPath $keyringPath
    if (-not $hasTokenFile -and -not $hasKeyring) { throw "账号 [$Name] 没有可用的令牌文件或凭据管理器条目。" }

    Stop-Antigravity
    Backup-File $TokenFile 'current-token'
    if ($hasTokenFile) {
        New-Item -ItemType Directory -Path (Split-Path $TokenFile) -Force | Out-Null
        Copy-Item -LiteralPath $src -Destination $TokenFile -Force
    } else {
        Remove-Item -LiteralPath $TokenFile -Force -ErrorAction SilentlyContinue
    }

    $cliSrc = Join-Path $target.Dir 'cli-token.json'
    if (Test-Path -LiteralPath $cliSrc) {
        New-Item -ItemType Directory -Path (Split-Path $CliToken) -Force | Out-Null
        Copy-Item -LiteralPath $cliSrc -Destination $CliToken -Force
    }
    $oauthSrc = Join-Path $target.Dir 'oauth-creds.json'
    if (Test-Path -LiteralPath $oauthSrc) {
        Copy-Item -LiteralPath $oauthSrc -Destination $OauthCreds -Force
    }
    Remove-KeyringRecords
    if ($hasKeyring) {
        $saved = Get-Content -LiteralPath $keyringPath -Raw -Encoding UTF8 | ConvertFrom-Json
        foreach ($record in @($saved)) { Write-KeyringRecord $record }
    } elseif ($hasTokenFile) {
        Write-Info '该账号没有凭据管理器快照，已按文件令牌切换。'
    }
    if ($target.Email) { Update-StorageEmail $target.Email }
    [pscustomobject]@{ name = $target.Name; email = $target.Email; switchedAt = (Get-Date).ToString('s') } |
        ConvertTo-Json | Set-Content -LiteralPath $CurrentFile -Encoding UTF8

    $emailText = if ($target.Email) { " ($($target.Email))" } else { '' }
    Write-Info "已切换到 [$($target.Name)]$emailText"
    if ($NoLaunch) {
        Write-Info '已跳过启动反重力（-NoLaunch）。'
    } elseif (Test-Path -LiteralPath $AppExe) {
        Start-Process -FilePath $AppExe
        Write-Info '反重力已重新启动。'
    } else {
        Write-Warn "未找到 Antigravity.exe，请手动启动：$AppExe"
    }
}

switch ($Command) {
    'save' {
        Save-Profile
    }
    'use' {
        Use-Profile
    }
    'list' {
        $profiles = @(Get-Profiles)
        if (-not $profiles.Count) {
            Write-Warn '还没有保存任何账号。登录反重力后运行：switch.ps1 save <名字>'
            break
        }
        $current = ''
        if (Test-Path -LiteralPath $CurrentFile) {
            try { $current = (Get-Content -LiteralPath $CurrentFile -Raw -Encoding UTF8 | ConvertFrom-Json).name } catch {}
        }
        $profiles | Select-Object Name, Email, SavedAt, @{n = 'Active'; e = { if ($_.Name -eq $current) { '*' } else { '' } } } |
            Format-Table -AutoSize
    }
    'status' {
        $token = Read-Token $TokenFile
        $email = Get-TokenEmail $token
        $running = Get-Running
        if ($token) {
            Write-Info "当前登录令牌：$TokenFile"
            Write-Info "账号：$(if ($email) { $email } else { '（令牌中没有邮箱字段）' })"
        } else {
            Write-Warn "当前未登录（找不到 $TokenFile）"
        }
        Write-Info "运行中的进程：$(if ($running) { ($running | Select-Object -ExpandProperty Name -Unique) -join ', ' } else { '无' })"
        $keyring = @(Get-KeyringRecords)
        if ($keyring.Count -gt 0) {
            foreach ($record in $keyring) {
                $keyEmail = ''
                try { $keyEmail = Get-TokenEmail (($record.blob | ConvertFrom-Json)) } catch {}
                Write-Info "凭据管理器：$($record.target) $(if ($keyEmail) { "($keyEmail)" })"
            }
        } else {
            Write-Info '凭据管理器：无相关条目'
        }
    }
    'open' {
        if (-not (Test-Path -LiteralPath $AppExe)) { throw "找不到 $AppExe" }
        Start-Process -FilePath $AppExe
    }
    'delete' {
        if (-not $Name) { throw '用法：switch.ps1 delete <账号名>' }
        $dir = Join-Path $ProfileDir ($Name -replace '[^\w\-.@]', '_')
        $resolved = [IO.Path]::GetFullPath($dir)
        if (-not $resolved.StartsWith([IO.Path]::GetFullPath($ProfileDir), [StringComparison]::OrdinalIgnoreCase)) {
            throw '账号名不合法。'
        }
        if (Test-Path -LiteralPath $resolved) {
            Remove-Item -LiteralPath $resolved -Recurse -Force
            Write-Info "已删除账号配置：$Name"
        } else {
            Write-Warn "不存在：$Name"
        }
    }
}
