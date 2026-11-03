param([string]$ImageTag = '')
$ErrorActionPreference = 'Stop'
Set-Location (Split-Path $PSScriptRoot -Parent)
if (Test-Path -LiteralPath '.env') { throw 'O .env já existe; mantenha suas credenciais atuais.' }
if (!$ImageTag) { $ImageTag = (git rev-parse HEAD).Trim() }
$adminSecret = 'Aa1!' + [Guid]::NewGuid().ToString('N')
$rabbitSecret = 'Bb2!' + [Guid]::NewGuid().ToString('N')
@"
IMAGE_TAG=$ImageTag
ADMIN_EMAIL=admin@example.test
ADMIN_PASSWORD=$adminSecret
RABBIT_USER=expensehub
RABBIT_PASSWORD=$rabbitSecret
"@ | Set-Content -LiteralPath '.env' -Encoding utf8
Write-Host 'Configuração local criada. Credenciais estão apenas no .env ignorado pelo Git.'
