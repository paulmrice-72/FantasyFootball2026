# fan178_week1_golive.ps1
# FAN-178 / FAN-139 / FAN-132 - bring PRODUCTION to real Week 1 before Sunday.
#
# ORDER MATTERS. The week-0 override is currently the only thing keeping prod off
# the calendar heuristic, and the heuristic says WEEK 2 today (it anchors on the
# first Thursday on or after Sept 1 = 2026-09-03; ten days later reads week 2).
# Clearing the override before the schedule is imported is strictly WORSE than
# leaving it pinned: week 2 with no week-2 rows blanks Start/Sit entirely.
#
# So: deploy -> import schedule -> confirm scheduleWeek = 1 -> pin week 1 ->
# run projections -> verify rows exist -> only then clear the override.
#
# Usage:
#   .\fan178_week1_golive.ps1                 # status only, no auth
#   .\fan178_week1_golive.ps1 -Step 1         # import schedule
#   .\fan178_week1_golive.ps1 -Step all       # steps 1-5 in order, with prompts

[CmdletBinding()]
param(
    [ValidateSet('status','1','2','3','4','5','all')]
    [string]$Step = 'status',

    [string]$BaseUrl = 'https://fantasycombineai.com',
    [string]$Email   = 'paulmrice@gmail.com',
    [int]$Season     = 2026,
    [int]$TargetWeek = 1
)

$ErrorActionPreference = 'Stop'

# Same two lines as fan132_prod_backfill.ps1. PS 5.1 sends "Expect: 100-continue"
# on every POST and a zero-length POST with that header died against prod on 09-06.
[System.Net.ServicePointManager]::Expect100Continue = $false
[System.Net.ServicePointManager]::SecurityProtocol  =
    [System.Net.SecurityProtocolType]::Tls12 -bor [System.Net.SecurityProtocolType]::Tls13

Write-Host ("PowerShell {0} ({1})" -f $PSVersionTable.PSVersion, $PSVersionTable.PSEdition) -ForegroundColor DarkGray

# -- Auth ----------------------------------------------------------------
function Get-Token {
    $secure = Read-Host "Password for $($script:Email) [$($script:BaseUrl)]" -AsSecureString
    $plain  = [Runtime.InteropServices.Marshal]::PtrToStringAuto(
                  [Runtime.InteropServices.Marshal]::SecureStringToBSTR($secure))

    $body = @{ email = $script:Email; password = $plain } | ConvertTo-Json

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

    # A body goes only on verbs that allow one. PS 5.1 sits on HttpWebRequest,
    # which throws ProtocolViolationException - "Cannot send a content-body with
    # this verb-type" - if a GET carries any content at all.
    #
    # fan132_prod_backfill.ps1's "always send a body" rule is a POST rule, not a
    # universal one: a zero-length POST is what died against prod on 09-06. That
    # script only ever put POSTs through this helper, so the distinction never
    # came up; this one reads context on GET and clears it on DELETE.
    if ($Method -in @('Post', 'Put', 'Patch')) {
        $params.ContentType = 'application/json'
        $params.Body        = if ($null -ne $Body) { $Body | ConvertTo-Json -Compress } else { '{}' }
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
        $resp = $_.Exception.Response
        if ($resp) {
            Write-Host ("  <- HTTP {0} {1}" -f [int]$resp.StatusCode, $resp.StatusCode) -ForegroundColor Red
        }
        else {
            Write-Host "  <- No HTTP response - connection closed before any status." -ForegroundColor Yellow
        }
        if ($_.ErrorDetails.Message) { Write-Host $_.ErrorDetails.Message -ForegroundColor Red }
        throw
    }
}

# -- Context helpers -----------------------------------------------------

# The AUTHENTICATED nfl-context is the one worth reading. It reports weekSource
# and scheduleWeek alongside the active week, so it shows what the week WOULD
# resolve to if the override were cleared - the whole point of the pre-flight.
function Get-Context {
    Invoke-Api -Method Get -Path '/api/v1/admin/nfl-context'
}

function Step-Status {
    Write-Host "`n== Production NFL context (anonymous) ==" -ForegroundColor Cyan
    Invoke-RestMethod -Uri "$BaseUrl/api/v1/admin/nfl-context/public" | ConvertTo-Json | Write-Host
    Write-Host @"

  If this build is pre-FAN-178 the authenticated /nfl-context response will have
  NO weekSource and NO scheduleWeek field. That absence is the deploy check -
  run it before anything else.
"@ -ForegroundColor DarkGray
}

# -- Steps ---------------------------------------------------------------

function Step-0-VerifyDeploy {
    Write-Host "`n== STEP 0 - is the FAN-178 build actually live? ==" -ForegroundColor Cyan
    $ctx = Get-Context
    if ($null -eq $ctx.weekSource) {
        throw "No weekSource in the response - production is still on the pre-FAN-178 build. Merge develop -> master and wait for deploy-production.yml before continuing."
    }
    Write-Host "  weekSource = $($ctx.weekSource); scheduleWeek = $($ctx.scheduleWeek)" -ForegroundColor Green
    return $ctx
}

function Step-1-SyncSchedule {
    Write-Host "`n== STEP 1 - import the $Season schedule ==" -ForegroundColor Cyan
    Write-Host "  Expect gamesImported 272, weeksCovered 18. Dev saw gamesWithSpread 103." -ForegroundColor DarkGray
    Write-Host "  A zero-row parse means nflverse changed the games.csv columns - check the logs." -ForegroundColor Yellow
    Invoke-Api -Method Post -Path "/api/v1/admin/sync-nfl-schedule?season=$Season"
}

function Step-2-VerifySchedule {
    Write-Host "`n== STEP 2 - read back week $TargetWeek ==" -ForegroundColor Cyan
    Write-Host "  Expect gamesInSeason 272, gamesInWeek 16, and matchups rendered as AWAY at HOME." -ForegroundColor DarkGray
    Write-Host "  Confirm the Rams read LAR, not LA - that is NflTeamNormalizer firing." -ForegroundColor DarkGray
    Invoke-Api -Method Get -Path "/api/v1/admin/nfl-schedule?season=$Season&week=$TargetWeek"

    Write-Host "`n  Pre-flight on the week resolution:" -ForegroundColor Cyan
    $ctx = Get-Context
    if ($ctx.scheduleWeek -ne $TargetWeek) {
        throw "scheduleWeek reads '$($ctx.scheduleWeek)', expected $TargetWeek. DO NOT clear the override - the calendar heuristic would take over and it says week 2."
    }
    Write-Host "  scheduleWeek = $TargetWeek. Safe to proceed." -ForegroundColor Green
}

function Step-3-PinWeek {
    Write-Host "`n== STEP 3 - pin the context to week $TargetWeek ==" -ForegroundColor Cyan
    Write-Host "  SetNflContext assigns season AND week unconditionally - both must be sent." -ForegroundColor Yellow
    Write-Host "  Pinning first, rather than clearing first, means week-$TargetWeek rows are written" -ForegroundColor DarkGray
    Write-Host "  before anything on the site starts asking for them." -ForegroundColor DarkGray
    Invoke-Api -Method Post -Path '/api/v1/admin/nfl-context' -Body @{ season = $Season; week = $TargetWeek }
    Get-Context | Out-Null
}

function Step-4-RunProjections {
    Write-Host "`n== STEP 4 - run projections ==" -ForegroundColor Cyan
    Write-Host "  ProjectionRefreshJob ignores the season in the body; it reads INflContextService." -ForegroundColor Yellow
    Write-Host "  The response is a fixed string with no counts (noted on FAN-132) - it proves nothing." -ForegroundColor Yellow
    $ctx = Get-Context
    if ($ctx.activeWeek -ne $TargetWeek) {
        throw "activeWeek is $($ctx.activeWeek), expected $TargetWeek. Run step 3 first."
    }
    $ok = Read-Host "  Context reads week $TargetWeek. Type YES to run projections"
    if ($ok -ne 'YES') { Write-Host "  Skipped." -ForegroundColor Yellow; return }
    Invoke-Api -Method Post -Path '/api/v1/admin/jobs/run-projections' -Body @{ season = $Season }

    Write-Host @"

  VERIFY IN COMPASS BEFORE STEP 5 - prod database 'FFAnalytics'.
  NOTE: prod is FFAnalytics; 'fantasycombine' is the LOCAL DEV database name and
  does not exist on prod. Confirmed 2026-09-12.

    db.player_projections.countDocuments({ Season: $Season, Week: $TargetWeek })
    db.simulation_results.countDocuments({ Season: $Season, Week: $TargetWeek })
    db.nfl_schedule.countDocuments({ Season: $Season })

  Dev reference: ~476 PriorSeasonCarryover + ~236 RookieProjection, ~700 sims,
  272 schedule rows. A count near zero means the run did nothing and clearing
  the override next would blank Start/Sit.

  Spot-check one starter while you are in there:

    db.player_projections.findOne({ Season: $Season, Week: $TargetWeek, PlayerName: /Gibbs/ })

  OpponentTeam should be his real week-$TargetWeek opponent, not last January's.
"@ -ForegroundColor DarkGray
}

function Step-5-ClearOverride {
    Write-Host "`n== STEP 5 - hand week resolution to the schedule ==" -ForegroundColor Cyan
    $ctx = Get-Context
    if ($ctx.scheduleWeek -ne $TargetWeek) {
        throw "scheduleWeek reads '$($ctx.scheduleWeek)'. Refusing to clear - the calendar heuristic would take over."
    }
    $ok = Read-Host "  Week-$TargetWeek rows verified in Compass? Type YES to clear the override"
    if ($ok -ne 'YES') { Write-Host "  Skipped - override left in place." -ForegroundColor Yellow; return }

    Invoke-Api -Method Delete -Path '/api/v1/admin/nfl-context'

    $after = Get-Context
    if ($after.weekSource -ne 'schedule' -or $after.activeWeek -ne $TargetWeek) {
        Write-Host "  UNEXPECTED: weekSource '$($after.weekSource)', activeWeek $($after.activeWeek)." -ForegroundColor Red
        Write-Host "  Re-pin immediately:  POST /api/v1/admin/nfl-context { season = $Season; week = $TargetWeek }" -ForegroundColor Red
        return
    }
    Write-Host "  weekSource = schedule, activeWeek = $TargetWeek. FAN-139 mitigation retired." -ForegroundColor Green

    Write-Host @"

  Last gate - load the live site:
    * My Team -> Start/Sit should list every starter, not one rookie.
    * Opponent labels should match Sleeper on all ten.

  Then leave it alone. Tomorrow at 06:00 UTC nfl-schedule-sync-daily refreshes the
  schedule and at 11:00 UTC (7am ET) sunday-projection-refresh re-runs projections
  against the schedule-resolved week, ahead of the 1pm ET kickoffs.

  Expect every number to move slightly overnight. That is FAN-147 - Monte Carlo is
  unseeded, so every run changes every number. It is not a regression.
"@ -ForegroundColor DarkGray
}

# -- Dispatch ------------------------------------------------------------

if ($Step -eq 'status') { Step-Status; return }

$script:BaseUrl = $BaseUrl
$script:Email   = $Email
$script:Token   = Get-Token
if (-not $script:Token) { throw "No token - aborting." }
Write-Host "Authenticated against $BaseUrl" -ForegroundColor Green

Step-0-VerifyDeploy | Out-Null

switch ($Step) {
    '1'   { Step-1-SyncSchedule }
    '2'   { Step-2-VerifySchedule }
    '3'   { Step-3-PinWeek }
    '4'   { Step-4-RunProjections }
    '5'   { Step-5-ClearOverride }
    'all' { Step-1-SyncSchedule; Step-2-VerifySchedule; Step-3-PinWeek; Step-4-RunProjections; Step-5-ClearOverride }
}
