# fan132_prod_backfill.ps1
# FAN-132 production backfill + FAN-139 context pin.
#
# STEP 1 can and should be run RIGHT NOW, before any deploy.
# STEPS 2-5 require the FAN-138 build to be deployed first.
#
# Usage:
#   .\fan132_prod_backfill.ps1 -Step 1
#   .\fan132_prod_backfill.ps1 -Step 2      # after deploy
#   .\fan132_prod_backfill.ps1 -Step all    # steps 2-5 in order

[CmdletBinding()]
param(
    [ValidateSet('1','2','3','4','5','all','status')]
    [string]$Step = 'status',

    [string]$BaseUrl = 'https://fantasycombineai.com',
    [string]$Email   = 'paulmrice@gmail.com'
)

$ErrorActionPreference = 'Stop'

# ── Auth ────────────────────────────────────────────────────────────────
# AuthController route is api/v1/[controller] -> api/v1/Auth
# AuthResponse is (AccessToken, RefreshToken, AccessTokenExpiry), camelCased on the wire.
# Read-Host -AsSecureString prompts INLINE in the console. Get-Credential was used
# here originally and opens a GUI dialog on PowerShell 7 for Windows, which can open
# behind the terminal and look like a hang. Do not put it back.
function Get-Token {
    $secure = Read-Host "Password for $($script:Email) [$($script:BaseUrl)]" -AsSecureString
    $plain  = [Runtime.InteropServices.Marshal]::PtrToStringAuto(
                  [Runtime.InteropServices.Marshal]::SecureStringToBSTR($secure))

    $body = @{
        email    = $script:Email
        password = $plain
    } | ConvertTo-Json

    $resp = Invoke-RestMethod -Method Post `
        -Uri "$script:BaseUrl/api/v1/Auth/login" `
        -ContentType 'application/json' `
        -Body $body

    if (-not $resp.accessToken) { throw "Login succeeded but no accessToken in response." }
    return $resp.accessToken
}

function Invoke-Api {
    param(
        [Parameter(Mandatory)][string]$Method,
        [Parameter(Mandatory)][string]$Path,
        [object]$Body,
        [int]$TimeoutSec = 900
    )
    $params = @{
        Method      = $Method
        Uri         = "$script:BaseUrl$Path"
        Headers     = @{ Authorization = "Bearer $script:Token" }
        TimeoutSec  = $TimeoutSec
        ErrorAction = 'Stop'
    }
    if ($null -ne $Body) {
        $params.ContentType = 'application/json'
        $params.Body        = ($Body | ConvertTo-Json -Compress)
    }

    Write-Host "  -> $Method $Path" -ForegroundColor DarkGray
    $sw = [Diagnostics.Stopwatch]::StartNew()
    try {
        $r = Invoke-RestMethod @params
        $sw.Stop()
        Write-Host ("  <- OK ({0:n1}s)" -f $sw.Elapsed.TotalSeconds) -ForegroundColor Green
        $r | ConvertTo-Json -Depth 4 | Write-Host
        return $r
    }
    catch {
        $sw.Stop()
        Write-Host ("  <- FAILED ({0:n1}s): {1}" -f $sw.Elapsed.TotalSeconds, $_.Exception.Message) -ForegroundColor Red
        if ($_.ErrorDetails.Message) { Write-Host $_.ErrorDetails.Message -ForegroundColor Red }
        throw
    }
}

# ── Steps ───────────────────────────────────────────────────────────────

function Step-Status {
    Write-Host "`n== Current production NFL context ==" -ForegroundColor Cyan
    # This one endpoint is [AllowAnonymous] - no token needed.
    Invoke-RestMethod -Uri "$BaseUrl/api/v1/admin/nfl-context/public" |
        ConvertTo-Json | Write-Host
}

function Step-1-PinContext {
    Write-Host "`n== STEP 1 - FAN-139: pin context to 2026 / week 0 ==" -ForegroundColor Cyan
    Write-Host "  CalcWeek rolled prod to week 1 on 2026-09-03. Real week 1 opens 2026-09-09." -ForegroundColor Yellow
    Write-Host "  Both season and week must be sent together - SetNflContext assigns both unconditionally." -ForegroundColor Yellow
    Invoke-Api -Method Post -Path '/api/v1/admin/nfl-context' -Body @{ season = 2026; week = 0 }
    Step-Status
}

function Step-2-SnapCounts {
    Write-Host "`n== STEP 2 - snap counts 2025 (import then merge) ==" -ForegroundColor Cyan
    Write-Host "  Target: snap_counts 53,155 -> ~79,767 (dev)" -ForegroundColor DarkGray
    Invoke-Api -Method Post -Path '/api/v1/snapcounts/import/2025'
    Invoke-Api -Method Post -Path '/api/v1/snapcounts/merge/2025'
}

function Step-3-UsageMetrics {
    Write-Host "`n== STEP 3 - usage metrics 2025 and 2024 (FAN-138 endpoint) ==" -ForegroundColor Cyan
    Write-Host "  Target: player_usage_metrics 559 -> ~1,132 (dev)" -ForegroundColor DarkGray
    Write-Host "  PlayersProcessed = 0 means no game logs for that season - stop and check." -ForegroundColor Yellow
    Invoke-Api -Method Post -Path '/api/v1/admin/jobs/run-usage-metrics?season=2025'
    Invoke-Api -Method Post -Path '/api/v1/admin/jobs/run-usage-metrics?season=2024'
}

function Step-4-Projections {
    Write-Host "`n== STEP 4 - re-run projections ==" -ForegroundColor Cyan
    Write-Host "  ProjectionRefreshJob ignores the season in the body - it reads INflContextService." -ForegroundColor Yellow
    Write-Host "  Confirm the context still reads week 0 before running this." -ForegroundColor Yellow
    Step-Status
    $ok = Read-Host "  Context correct? Type YES to run projections"
    if ($ok -ne 'YES') { Write-Host "  Skipped." -ForegroundColor Yellow; return }
    Invoke-Api -Method Post -Path '/api/v1/admin/jobs/run-projections' -Body @{ season = 2026 }
}

function Step-5-Verify {
    Write-Host "`n== STEP 5 - verify ==" -ForegroundColor Cyan
    Step-Status
    Write-Host @"

  Now check in Mongo Compass against PROD (fantasycombine):

    db.snap_counts.countDocuments({ Season: 2025 })
    db.player_usage_metrics.countDocuments({ Season: 2025 })
    db.player_usage_metrics.countDocuments({ Season: 2024 })
    db.simulation_results.countDocuments({ Season: 2026, Week: 0 })

  Dev reference: snap_counts 79,767 total / player_usage_metrics 1,132 total.

  Then load My Team on the live site - Start/Sit should still show 8 starters,
  and the projections behind them are now informed by 2025 usage metrics
  instead of a defaulted 1.0 usage-trend multiplier.
"@ -ForegroundColor DarkGray
}

# ── Dispatch ────────────────────────────────────────────────────────────

if ($Step -eq 'status') { Step-Status; return }

$script:BaseUrl = $BaseUrl
$script:Email   = $Email
$script:Token   = Get-Token
if (-not $script:Token) { throw "No token - aborting." }
Write-Host "Authenticated against $BaseUrl" -ForegroundColor Green

switch ($Step) {
    '1'   { Step-1-PinContext }
    '2'   { Step-2-SnapCounts }
    '3'   { Step-3-UsageMetrics }
    '4'   { Step-4-Projections }
    '5'   { Step-5-Verify }
    'all' { Step-2-SnapCounts; Step-3-UsageMetrics; Step-4-Projections; Step-5-Verify }
}
