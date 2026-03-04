#!/bin/bash
# =============================================================================
# Fantasy Football Analytics Platform — GitHub Setup Script
# =============================================================================
# Prerequisites:
#   1. GitHub CLI installed: https://cli.github.com/
#   2. Authenticated: run `gh auth login` first
#   3. Repo already created and you're in the local repo folder
#   4. Update REPO below to match your GitHub username/repo-name
#
# Usage:
#   chmod +x github_setup.sh
#   ./github_setup.sh
# =============================================================================

REPO="paulmrice-72/FantasyFootball2026"   # <-- UPDATE THIS
PROJECT_NAME="Fantasy Football Analytics Platform"

echo "=============================================="
echo " FF Analytics — GitHub Project Setup"
echo "=============================================="
echo ""
echo "Target repo: $REPO"
echo ""

# ── LABELS ────────────────────────────────────────────────────────────────────
echo "Creating labels..."

gh label create "epic"      --color "1F4E79" --description "Top-level epic"           --repo $REPO --force
gh label create "feature"   --color "2E75B6" --description "Feature group"            --repo $REPO --force
gh label create "pbi"       --color "2F5496" --description "Product Backlog Item"     --repo $REPO --force
gh label create "task"      --color "70AD47" --description "Individual task"          --repo $REPO --force
gh label create "phase-1"   --color "E2EFDA" --description "Phase 1: Foundation"      --repo $REPO --force
gh label create "phase-2"   --color "FFF2CC" --description "Phase 2: Data Ingestion"  --repo $REPO --force
gh label create "phase-3"   --color "FCE4D6" --description "Phase 3: Weekly Engine"   --repo $REPO --force
gh label create "phase-4"   --color "EAD1DC" --description "Phase 4: Dynasty"         --repo $REPO --force
gh label create "phase-5"   --color "D9EAD3" --description "Phase 5: DFS"             --repo $REPO --force
gh label create "phase-6"   --color "CFE2F3" --description "Phase 6: Polish"          --repo $REPO --force
gh label create "backend"   --color "F4CCCC" --description "Backend / API work"       --repo $REPO --force
gh label create "frontend"  --color "D9D2E9" --description "Blazor / UI work"         --repo $REPO --force
gh label create "data"      --color "FFF2CC" --description "Data / ingestion work"    --repo $REPO --force
gh label create "analytics" --color "D0E0E3" --description "Analytics / modeling"     --repo $REPO --force
gh label create "infra"     --color "F9CB9C" --description "Infrastructure / DevOps"  --repo $REPO --force

echo "Labels created."
echo ""

# ── MILESTONES (one per Phase) ─────────────────────────────────────────────────
echo "Skipping milestones (already created)..."

# gh api repos/$REPO/milestones -f title="Phase 1 — Platform Foundation"    -f description="Solution scaffold, databases, CQRS, auth, Blazor shell, CI/CD" -f state="open" 2>/dev/null  # already created
# gh api repos/$REPO/milestones -f title="Phase 2 — Data Ingestion"         -f description="Sleeper API, NFL stats, historical data loading" -f state="open" 2>/dev/null  # already created
# gh api repos/$REPO/milestones -f title="Phase 3 — Weekly Engine"          -f description="Projections, Monte Carlo simulation, lineup optimizer" -f state="open" 2>/dev/null  # already created
# gh api repos/$REPO/milestones -f title="Phase 4 — Dynasty Engine"         -f description="Aging curves, breakout classifier, trade analyzer" -f state="open" 2>/dev/null  # already created
# gh api repos/$REPO/milestones -f title="Phase 5 — DFS Builder"            -f description="DFS slate importer, ownership model, multi-lineup export" -f state="open" 2>/dev/null  # already created
# gh api repos/$REPO/milestones -f title="Phase 6 — Polish & Hardening"     -f description="Alerts, caching, architecture tests, production readiness" -f state="open" 2>/dev/null  # already created

echo "Milestones created."
echo ""

# Helper: get milestone number by title

M1=1
M2=2
M3=3
M4=4
M5=5
M6=6

echo "Milestone IDs: M1=1 M2=2 M3=3 M4=4 M5=5 M6=6"
echo ""

# ── CREATE PROJECT BOARD ───────────────────────────────────────────────────────
echo "Creating GitHub Project board..."
PROJECT_URL=$(gh project create --owner "${REPO%%/*}" --title "$PROJECT_NAME" --format json 2>/dev/null | jq -r '.url // empty')
if [ -z "$PROJECT_URL" ]; then
  echo "  Note: Project board may already exist or requires org-level permissions."
  echo "  You can manually create it at: https://github.com/${REPO%%/*}?tab=projects"
else
  echo "  Project created: $PROJECT_URL"
fi
echo ""

# ── ISSUE CREATION HELPER ─────────────────────────────────────────────────────
# Usage: create_issue "title" "body" "label1,label2" milestone_number
create_issue() {
  local TITLE="$1"
  local BODY="$2"
  local LABELS="$3"
  local MILESTONE="$4"
  gh issue create \
    --repo "$REPO" \
    --title "$TITLE" \
    --body "$BODY" \
    --label "$LABELS" \
    --milestone "$MILESTONE"
  sleep 0.4   # avoid rate limiting
}

# ==============================================================================
# EPIC 1 — Platform Foundation & Architecture
# ==============================================================================
echo "Creating Epic 1 issues..."

create_issue \
  "[EPIC] E1: Platform Foundation & Architecture" \
  "## Epic: Platform Foundation & Architecture\n\nEstablish solution structure, project scaffolding, CI/CD pipeline, and core cross-cutting infrastructure.\n\n### Features\n- F1.1 Solution Scaffolding\n- F1.2 Database Setup\n- F1.3 CQRS & MediatR Pipeline\n- F1.4 API & Auth Scaffolding\n- F1.5 Blazor Frontend Shell\n- F1.6 CI/CD Pipeline\n\n**Phase:** 1 | **Priority:** Critical Path" \
  "epic,phase-1,backend,infra" $M1

create_issue \
  "[FEATURE] F1.1: Solution Scaffolding" \
  "## Feature: Solution Scaffolding\n\nCreate the Visual Studio solution with all Clean Architecture projects, configure dependencies, and establish coding standards.\n\n**Epic:** E1 — Platform Foundation\n**PBIs:** PBI-001, PBI-002, PBI-003" \
  "feature,phase-1,backend" $M1

create_issue \
  "[PBI-001] Create solution with Clean Architecture projects" \
  "## PBI-001: Create solution with Clean Architecture projects\n\n**Feature:** F1.1 Solution Scaffolding\n\n### Tasks\n- [ ] Create FF.sln with all project references (FF.SharedKernel, FF.Domain, FF.Application, FF.Infrastructure, FF.API, FF.Web)\n- [ ] Configure project dependencies per layer rules\n- [ ] Add global usings and nullable enable to all projects\n- [ ] Add .editorconfig and .gitignore" \
  "pbi,phase-1,backend" $M1

create_issue \
  "[PBI-002] Implement SharedKernel base types" \
  "## PBI-002: Implement SharedKernel base types\n\n**Feature:** F1.1 Solution Scaffolding\n\n### Tasks\n- [ ] Create Result<T> and Error types\n- [ ] Create PagedList<T> and PaginationParams\n- [ ] Create Guard utility class\n- [ ] Create base Entity and AggregateRoot" \
  "pbi,phase-1,backend" $M1

create_issue \
  "[PBI-003] Configure Serilog + Seq logging" \
  "## PBI-003: Configure Serilog + Seq logging\n\n**Feature:** F1.1 Solution Scaffolding\n\n### Tasks\n- [ ] Install Serilog packages\n- [ ] Configure structured logging with correlation IDs\n- [ ] Set up Seq local Docker container\n- [ ] Add request logging middleware" \
  "pbi,phase-1,backend,infra" $M1

create_issue \
  "[FEATURE] F1.2: Database Setup" \
  "## Feature: Database Setup\n\nConfigure SQL Server LocalDB with EF Core and MongoDB Atlas free tier. Implement repository pattern.\n\n**Epic:** E1 — Platform Foundation\n**PBIs:** PBI-004, PBI-005, PBI-006" \
  "feature,phase-1,backend,data" $M1

create_issue \
  "[PBI-004] Configure SQL Server LocalDB + EF Core" \
  "## PBI-004: Configure SQL Server LocalDB + EF Core\n\n**Feature:** F1.2 Database Setup\n\n### Tasks\n- [ ] Install EF Core packages (SqlServer, Tools, Design)\n- [ ] Create AppDbContext with entity configurations\n- [ ] Create initial migration\n- [ ] Seed reference data (positions, NFL teams)" \
  "pbi,phase-1,backend,data" $M1

create_issue \
  "[PBI-005] Configure MongoDB Atlas connection" \
  "## PBI-005: Configure MongoDB Atlas connection\n\n**Feature:** F1.2 Database Setup\n\n### Tasks\n- [ ] Create free MongoDB Atlas cluster (512MB free tier)\n- [ ] Install MongoDB.Driver NuGet package\n- [ ] Create MongoDbContext wrapper class\n- [ ] Implement generic repository pattern for Mongo collections\n- [ ] Write connection health check endpoint" \
  "pbi,phase-1,backend,data" $M1

create_issue \
  "[PBI-006] Implement SQL repository pattern" \
  "## PBI-006: Implement SQL repository pattern\n\n**Feature:** F1.2 Database Setup\n\n### Tasks\n- [ ] Create IRepository<T> interface in FF.Application\n- [ ] Implement EF Core repository in FF.Infrastructure\n- [ ] Add Unit of Work pattern\n- [ ] Write repository unit tests" \
  "pbi,phase-1,backend" $M1

create_issue \
  "[FEATURE] F1.3: CQRS & MediatR Pipeline" \
  "## Feature: CQRS & MediatR Pipeline\n\nWire up MediatR with pipeline behaviors and scaffold Hangfire for background jobs.\n\n**Epic:** E1 — Platform Foundation\n**PBIs:** PBI-007, PBI-008" \
  "feature,phase-1,backend" $M1

create_issue \
  "[PBI-007] Configure MediatR with pipeline behaviors" \
  "## PBI-007: Configure MediatR with pipeline behaviors\n\n**Feature:** F1.3 CQRS & MediatR Pipeline\n\n### Tasks\n- [ ] Install MediatR NuGet package\n- [ ] Add LoggingBehavior pipeline behavior\n- [ ] Add ValidationBehavior with FluentValidation\n- [ ] Add PerformanceBehavior (warn on queries > 500ms)" \
  "pbi,phase-1,backend" $M1

create_issue \
  "[PBI-008] Scaffold Hangfire background jobs" \
  "## PBI-008: Scaffold Hangfire background jobs\n\n**Feature:** F1.3 CQRS & MediatR Pipeline\n\n### Tasks\n- [ ] Install Hangfire with SQL Server storage\n- [ ] Configure Hangfire dashboard (local access only)\n- [ ] Create IBackgroundJobService abstraction\n- [ ] Write sample health-check recurring job" \
  "pbi,phase-1,backend,infra" $M1

create_issue \
  "[FEATURE] F1.4: API & Auth Scaffolding" \
  "## Feature: API & Auth Scaffolding\n\nBuild the ASP.NET Core Web API base with Swagger, global error handling, and JWT authentication.\n\n**Epic:** E1 — Platform Foundation\n**PBIs:** PBI-009, PBI-010" \
  "feature,phase-1,backend" $M1

create_issue \
  "[PBI-009] Build ASP.NET Core API base" \
  "## PBI-009: Build ASP.NET Core API base\n\n**Feature:** F1.4 API & Auth Scaffolding\n\n### Tasks\n- [ ] Configure Swagger / OpenAPI (Swashbuckle)\n- [ ] Add global exception handling middleware\n- [ ] Add API versioning\n- [ ] Configure CORS policy for Blazor WASM origin" \
  "pbi,phase-1,backend" $M1

create_issue \
  "[PBI-010] Implement JWT authentication" \
  "## PBI-010: Implement JWT authentication\n\n**Feature:** F1.4 API & Auth Scaffolding\n\n### Tasks\n- [ ] Set up ASP.NET Core Identity with SQL Server\n- [ ] Implement register / login / refresh token endpoints\n- [ ] Add JWT bearer middleware\n- [ ] Write auth integration tests" \
  "pbi,phase-1,backend" $M1

create_issue \
  "[FEATURE] F1.5: Blazor Frontend Shell" \
  "## Feature: Blazor Frontend Shell\n\nCreate the Blazor WASM project with MudBlazor layout shell and JWT-aware HTTP client.\n\n**Epic:** E1 — Platform Foundation\n**PBIs:** PBI-011, PBI-012" \
  "feature,phase-1,frontend" $M1

create_issue \
  "[PBI-011] Create Blazor WASM project with MudBlazor" \
  "## PBI-011: Create Blazor WASM project with MudBlazor\n\n**Feature:** F1.5 Blazor Frontend Shell\n\n### Tasks\n- [ ] Scaffold Blazor WASM hosted project (FF.Web)\n- [ ] Install and configure MudBlazor\n- [ ] Create AppBar, NavMenu drawer, and MainLayout shell\n- [ ] Configure HttpClient with JWT Authorization header injection" \
  "pbi,phase-1,frontend" $M1

create_issue \
  "[PBI-012] Implement login/auth UI" \
  "## PBI-012: Implement login/auth UI\n\n**Feature:** F1.5 Blazor Frontend Shell\n\n### Tasks\n- [ ] Build login and register Blazor pages\n- [ ] Implement custom AuthenticationStateProvider\n- [ ] Persist JWT in memory (not localStorage — not supported in Claude artifacts env)\n- [ ] Add route guards for protected pages using [Authorize]" \
  "pbi,phase-1,frontend" $M1

create_issue \
  "[FEATURE] F1.6: CI/CD Pipeline" \
  "## Feature: CI/CD Pipeline\n\nGitHub Actions pipeline for automated build and test on every pull request.\n\n**Epic:** E1 — Platform Foundation\n**PBIs:** PBI-013" \
  "feature,phase-1,infra" $M1

create_issue \
  "[PBI-013] GitHub Actions build and test pipeline" \
  "## PBI-013: GitHub Actions build and test pipeline\n\n**Feature:** F1.6 CI/CD Pipeline\n\n### Tasks\n- [ ] Create .github/workflows/build.yml\n- [ ] Run dotnet build and xUnit tests on every PR\n- [ ] Generate code coverage report\n- [ ] Add branch protection rules on main" \
  "pbi,phase-1,infra" $M1

# ==============================================================================
# EPIC 2 — Data Ingestion & Integration
# ==============================================================================
echo "Creating Epic 2 issues..."

create_issue \
  "[EPIC] E2: Data Ingestion & Integration" \
  "## Epic: Data Ingestion & Integration\n\nBuild all external data pipelines — Sleeper API, NFL.com stats, and historical data loading.\n\n### Features\n- F2.1 Sleeper API Integration\n- F2.2 NFL Stats Ingestion\n- F2.3 Data Quality & Monitoring\n\n**Phase:** 2" \
  "epic,phase-2,data" $M2

create_issue \
  "[FEATURE] F2.1: Sleeper API Integration" \
  "## Feature: Sleeper API Integration\n\nBuild typed Sleeper API client, ingest league data and sync player universe.\n\n**PBIs:** PBI-014, PBI-015, PBI-016" \
  "feature,phase-2,data,backend" $M2

create_issue \
  "[PBI-014] Build Sleeper API client" \
  "## PBI-014: Build Sleeper API client\n\n**Feature:** F2.1 Sleeper API Integration\n\n### Tasks\n- [ ] Map all required Sleeper API endpoints (players, leagues, rosters, transactions)\n- [ ] Implement typed Refit interface\n- [ ] Add Polly retry policy (exponential backoff)\n- [ ] Write contract tests against Sleeper API" \
  "pbi,phase-2,data,backend" $M2

create_issue \
  "[PBI-015] Ingest Sleeper league data" \
  "## PBI-015: Ingest Sleeper league data\n\n**Feature:** F2.1 Sleeper API Integration\n\n### Tasks\n- [ ] Import league settings and scoring configuration\n- [ ] Import current rosters and ownership per team\n- [ ] Import full transaction history (trades, adds, drops, waivers)\n- [ ] Store all to SQL Server via EF Core" \
  "pbi,phase-2,data,backend" $M2

create_issue \
  "[PBI-016] Sync Sleeper player universe" \
  "## PBI-016: Sync Sleeper player universe\n\n**Feature:** F2.1 Sleeper API Integration\n\n### Tasks\n- [ ] Pull full NFL player list from Sleeper /players/nfl endpoint\n- [ ] Map to Player domain entity\n- [ ] Schedule weekly delta sync Hangfire job\n- [ ] Handle player status changes (IR, suspended, retired)" \
  "pbi,phase-2,data,backend" $M2

create_issue \
  "[FEATURE] F2.2: NFL Stats Ingestion" \
  "## Feature: NFL Stats Ingestion\n\nBuild NFL stats client, load historical game logs into MongoDB, and schedule weekly syncs.\n\n**PBIs:** PBI-017, PBI-018, PBI-019" \
  "feature,phase-2,data,backend" $M2

create_issue \
  "[PBI-017] Build NFL.com stats client" \
  "## PBI-017: Build NFL.com stats client\n\n**Feature:** F2.2 NFL Stats Ingestion\n\n### Tasks\n- [ ] Identify NFL.com / nfl-verse data endpoints\n- [ ] Implement HttpClient with rate limiting (Polly)\n- [ ] Parse game log response into PlayerGameLog documents\n- [ ] Store to MongoDB PlayerGameLogs collection" \
  "pbi,phase-2,data,backend" $M2

create_issue \
  "[PBI-018] Build historical stats loader (CSV pipeline)" \
  "## PBI-018: Build historical stats loader\n\n**Feature:** F2.2 NFL Stats Ingestion\n\nSince nfl-data-py is Python, we source pre-built CSV exports from nflfastR/pro-football-reference and build a C# import pipeline.\n\n### Tasks\n- [ ] Source nflfastR play-by-play CSV exports (2018–present) from GitHub releases\n- [ ] Build CSV import pipeline using CsvHelper in C#\n- [ ] Normalize stats and upsert into MongoDB PlayerGameLogs\n- [ ] Validate data completeness (game count per season/week)" \
  "pbi,phase-2,data,backend" $M2

create_issue \
  "[PBI-019] Schedule automated weekly stats sync" \
  "## PBI-019: Schedule automated weekly stats sync\n\n**Feature:** F2.2 NFL Stats Ingestion\n\n### Tasks\n- [ ] Create Hangfire recurring job (runs Tuesday post-week)\n- [ ] Implement idempotent upsert logic (no duplicates on re-run)\n- [ ] Add failure alert notification via MediatR\n- [ ] Write integration test for full pipeline run" \
  "pbi,phase-2,data,backend" $M2

create_issue \
  "[FEATURE] F2.3: Data Quality & Monitoring" \
  "## Feature: Data Quality & Monitoring\n\nValidate ingested data and expose quality reports in the Blazor dashboard.\n\n**PBIs:** PBI-020" \
  "feature,phase-2,data,backend" $M2

create_issue \
  "[PBI-020] Build data quality validation layer" \
  "## PBI-020: Build data quality validation layer\n\n**Feature:** F2.3 Data Quality & Monitoring\n\n### Tasks\n- [ ] Define data quality rules (completeness, stat range checks, missing games)\n- [ ] Create DataQualityReport domain object\n- [ ] Expose quality report via API endpoint\n- [ ] Display quality summary dashboard page in Blazor" \
  "pbi,phase-2,data,frontend" $M2

# ==============================================================================
# EPIC 3 — Weekly Projection & Optimization Engine
# ==============================================================================
echo "Creating Epic 3 issues..."

create_issue \
  "[EPIC] E3: Weekly Projection & Optimization Engine" \
  "## Epic: Weekly Projection & Optimization Engine\n\nCore analytics engine — regression projections, Monte Carlo simulation, and constrained lineup optimizer.\n\n### Features\n- F3.1 Player Projection Model\n- F3.2 Monte Carlo Simulation\n- F3.3 Lineup Optimizer\n- F3.4 Weekly Projection UI\n\n**Phase:** 3" \
  "epic,phase-3,analytics,backend" $M3

create_issue \
  "[FEATURE] F3.1: Player Projection Model" \
  "## Feature: Player Projection Model\n\nAggregate usage metrics and build regression-based weekly projections with matchup adjustments.\n\n**PBIs:** PBI-021, PBI-022, PBI-023" \
  "feature,phase-3,analytics,backend" $M3

create_issue \
  "[PBI-021] Build usage metric aggregation pipeline" \
  "## PBI-021: Build usage metric aggregation pipeline\n\n**Feature:** F3.1 Player Projection Model\n\n### Tasks\n- [ ] Define usage metrics per position (targets, snap %, air yards, carries, red zone looks)\n- [ ] Aggregate rolling weighted averages (3-week, 5-week, season)\n- [ ] Store aggregated metrics to MongoDB as projection input documents\n- [ ] Unit test metric calculations against known game logs" \
  "pbi,phase-3,analytics,backend" $M3

create_issue \
  "[PBI-022] Implement regression-based projection model" \
  "## PBI-022: Implement regression-based projection model\n\n**Feature:** F3.1 Player Projection Model\n\n### Tasks\n- [ ] Build weighted regression using MathNet.Numerics\n- [ ] Apply matchup adjustment coefficients\n- [ ] Apply injury/practice report status adjustments\n- [ ] Output point projection + confidence interval per player" \
  "pbi,phase-3,analytics,backend" $M3

create_issue \
  "[PBI-023] Build matchup analysis engine" \
  "## PBI-023: Build matchup analysis engine\n\n**Feature:** F3.1 Player Projection Model\n\n### Tasks\n- [ ] Calculate opponent defensive rankings by position\n- [ ] Compute fantasy points allowed vs position (rolling 4-week and season)\n- [ ] Produce matchup difficulty score (0–100 scale)\n- [ ] Expose matchup data via CQRS query handler" \
  "pbi,phase-3,analytics,backend" $M3

create_issue \
  "[FEATURE] F3.2: Monte Carlo Simulation" \
  "## Feature: Monte Carlo Simulation\n\nRun probabilistic simulations to produce floor/median/ceiling distributions per player.\n\n**PBIs:** PBI-024, PBI-025" \
  "feature,phase-3,analytics,backend" $M3

create_issue \
  "[PBI-024] Implement Monte Carlo simulation engine" \
  "## PBI-024: Implement Monte Carlo simulation engine\n\n**Feature:** F3.2 Monte Carlo Simulation\n\n### Tasks\n- [ ] Model player performance as probability distribution (Normal/LogNormal)\n- [ ] Run 10,000 simulation iterations per player per week\n- [ ] Produce floor (10th pct), median (50th pct), ceiling (90th pct) outputs\n- [ ] Store SimulationResult documents to MongoDB" \
  "pbi,phase-3,analytics,backend" $M3

create_issue \
  "[PBI-025] Build simulation scheduling job" \
  "## PBI-025: Build simulation scheduling job\n\n**Feature:** F3.2 Monte Carlo Simulation\n\n### Tasks\n- [ ] Schedule simulation runs via Hangfire (Wednesday and Thursday each week)\n- [ ] Implement parallel simulation using Task.WhenAll for performance\n- [ ] Add progress tracking visible in Hangfire dashboard\n- [ ] Performance test: all starters simulated in < 60 seconds" \
  "pbi,phase-3,analytics,backend" $M3

create_issue \
  "[FEATURE] F3.3: Lineup Optimizer" \
  "## Feature: Lineup Optimizer\n\nConstrained optimization engine for both redraft and DFS lineup building.\n\n**PBIs:** PBI-026, PBI-027" \
  "feature,phase-3,analytics,backend" $M3

create_issue \
  "[PBI-026] Implement constrained lineup optimizer" \
  "## PBI-026: Implement constrained lineup optimizer\n\n**Feature:** F3.3 Lineup Optimizer\n\n### Tasks\n- [ ] Define roster constraint model (slots, position eligibility, flex rules)\n- [ ] Integrate Google OR-Tools C# solver\n- [ ] Apply salary cap constraints for DFS mode\n- [ ] Return optimal lineup with projected score and player breakdown" \
  "pbi,phase-3,analytics,backend" $M3

create_issue \
  "[PBI-027] Add risk-adjusted optimization modes" \
  "## PBI-027: Add risk-adjusted optimization modes\n\n**Feature:** F3.3 Lineup Optimizer\n\n### Tasks\n- [ ] Implement Safe mode (minimize variance / floor optimization)\n- [ ] Implement Ceiling mode (maximize upside / 90th pct optimization)\n- [ ] Implement Contrarian DFS mode (penalize high-ownership players)\n- [ ] Allow per-player lock / exclude constraint overrides" \
  "pbi,phase-3,analytics,backend" $M3

create_issue \
  "[FEATURE] F3.4: Weekly Projection UI" \
  "## Feature: Weekly Projection UI\n\nBlazor pages for viewing projections, simulation outputs, and the lineup optimizer.\n\n**PBIs:** PBI-028, PBI-029" \
  "feature,phase-3,frontend" $M3

create_issue \
  "[PBI-028] Build weekly projections dashboard" \
  "## PBI-028: Build weekly projections dashboard\n\n**Feature:** F3.4 Weekly Projection UI\n\n### Tasks\n- [ ] Projection data table with sort/filter by position, team, matchup\n- [ ] Floor/median/ceiling visualization per player (bar or range chart)\n- [ ] Color-coded matchup difficulty indicators\n- [ ] Export projections to CSV" \
  "pbi,phase-3,frontend" $M3

create_issue \
  "[PBI-029] Build lineup optimizer UI" \
  "## PBI-029: Build lineup optimizer UI\n\n**Feature:** F3.4 Weekly Projection UI\n\n### Tasks\n- [ ] Roster slot builder UI with position dropdowns\n- [ ] Optimizer mode toggle (Safe / Ceiling / Contrarian)\n- [ ] Display optimized lineup with projected total and player scores\n- [ ] Allow manual lock/exclude of players before optimizing" \
  "pbi,phase-3,frontend" $M3

# ==============================================================================
# EPIC 4 — Dynasty Valuation Engine
# ==============================================================================
echo "Creating Epic 4 issues..."

create_issue \
  "[EPIC] E4: Dynasty Valuation Engine" \
  "## Epic: Dynasty Valuation Engine\n\nLong-term player value modeling — aging curves, breakout detection, career simulations, and trade valuation.\n\n### Features\n- F4.1 Aging Curve Modeling\n- F4.2 Breakout Probability Classifier\n- F4.3 Trade Value Model\n- F4.4 Dynasty UI\n\n**Phase:** 4" \
  "epic,phase-4,analytics,backend" $M4

create_issue \
  "[FEATURE] F4.1: Aging Curve Modeling" \
  "## Feature: Aging Curve Modeling\n\nPosition-based aging curves and multi-year Monte Carlo career simulations.\n\n**PBIs:** PBI-030, PBI-031" \
  "feature,phase-4,analytics,backend" $M4

create_issue \
  "[PBI-030] Build position-based aging curve model" \
  "## PBI-030: Build position-based aging curve model\n\n**Feature:** F4.1 Aging Curve Modeling\n\n### Tasks\n- [ ] Source historical aging data by position from game log history\n- [ ] Fit polynomial aging curves using MathNet.Numerics curve fitting\n- [ ] Apply age-based value decay multipliers to dynasty projections\n- [ ] Store curve coefficients as configuration documents in MongoDB" \
  "pbi,phase-4,analytics,backend" $M4

create_issue \
  "[PBI-031] Implement multi-year career simulation" \
  "## PBI-031: Implement multi-year career simulation\n\n**Feature:** F4.1 Aging Curve Modeling\n\n### Tasks\n- [ ] Run Monte Carlo over 5-year career trajectories per player\n- [ ] Model injury probability by age and position\n- [ ] Produce career value distribution (total projected points)\n- [ ] Unit test curve and simulation calculations" \
  "pbi,phase-4,analytics,backend" $M4

create_issue \
  "[FEATURE] F4.2: Breakout Probability Classifier" \
  "## Feature: Breakout Probability Classifier\n\nScore players on likelihood of a breakout season using usage growth, draft capital, age, and opportunity signals.\n\n**PBIs:** PBI-032" \
  "feature,phase-4,analytics,backend" $M4

create_issue \
  "[PBI-032] Build breakout signal detector" \
  "## PBI-032: Build breakout signal detector\n\n**Feature:** F4.2 Breakout Probability Classifier\n\n### Tasks\n- [ ] Define breakout signals (usage growth trend, draft capital, age profile, opportunity score)\n- [ ] Score each player on breakout probability (0–100 scale)\n- [ ] Classify players: Breakout Candidate / On Curve / Declining / Unknown\n- [ ] Store classifications to MongoDB DynastyValuations collection" \
  "pbi,phase-4,analytics,backend" $M4

create_issue \
  "[FEATURE] F4.3: Trade Value Model" \
  "## Feature: Trade Value Model\n\nDiscounted future value modeling and trade analyzer engine.\n\n**PBIs:** PBI-033, PBI-034" \
  "feature,phase-4,analytics,backend" $M4

create_issue \
  "[PBI-033] Implement discounted future value (DFV) model" \
  "## PBI-033: Implement discounted future value model\n\n**Feature:** F4.3 Trade Value Model\n\n### Tasks\n- [ ] Define discount rate by position and age\n- [ ] Calculate net present value of projected future production\n- [ ] Normalize to trade value scale (0–100)\n- [ ] Benchmark values against FantasyCalc / KTC for reasonableness check" \
  "pbi,phase-4,analytics,backend" $M4

create_issue \
  "[PBI-034] Build trade analyzer engine" \
  "## PBI-034: Build trade analyzer engine\n\n**Feature:** F4.3 Trade Value Model\n\n### Tasks\n- [ ] Accept trade offer input (players on each side)\n- [ ] Calculate aggregate DFV per side of the trade\n- [ ] Return trade grade (A/B/C/D/F) and win/lose/even recommendation\n- [ ] Store trade analysis history to MongoDB" \
  "pbi,phase-4,analytics,backend" $M4

create_issue \
  "[FEATURE] F4.4: Dynasty UI" \
  "## Feature: Dynasty UI\n\nBlazor pages for dynasty rankings, player detail views, and the trade analyzer.\n\n**PBIs:** PBI-035, PBI-036" \
  "feature,phase-4,frontend" $M4

create_issue \
  "[PBI-035] Build dynasty player rankings page" \
  "## PBI-035: Build dynasty player rankings page\n\n**Feature:** F4.4 Dynasty UI\n\n### Tasks\n- [ ] Sortable rankings table (value, age, position, team)\n- [ ] Breakout candidate badge indicators\n- [ ] Player detail modal with career projection chart (ApexCharts)\n- [ ] Filter by position / team / age range" \
  "pbi,phase-4,frontend" $M4

create_issue \
  "[PBI-036] Build trade analyzer UI" \
  "## PBI-036: Build trade analyzer UI\n\n**Feature:** F4.4 Dynasty UI\n\n### Tasks\n- [ ] Player search and add to trade side A / side B slots\n- [ ] Side-by-side DFV comparison with letter grade\n- [ ] Trade history log view\n- [ ] Copy trade summary to clipboard (share)" \
  "pbi,phase-4,frontend" $M4

# ==============================================================================
# EPIC 5 — DFS Lineup Builder
# ==============================================================================
echo "Creating Epic 5 issues..."

create_issue \
  "[EPIC] E5: DFS Lineup Builder" \
  "## Epic: DFS Lineup Builder\n\nDraftKings / FanDuel contest integration with salary-constrained optimization and ownership projections.\n\n### Features\n- F5.1 DFS Contest Setup\n- F5.2 DFS Ownership Projections\n- F5.3 DFS UI\n\n**Phase:** 5" \
  "epic,phase-5,analytics,backend" $M5

create_issue \
  "[FEATURE] F5.1: DFS Contest Setup" \
  "## Feature: DFS Contest Setup\n\nImport DFS slates and wire salary constraints into the optimizer.\n\n**PBIs:** PBI-037, PBI-038" \
  "feature,phase-5,data,backend" $M5

create_issue \
  "[PBI-037] Build DFS slate importer" \
  "## PBI-037: Build DFS slate importer\n\n**Feature:** F5.1 DFS Contest Setup\n\n### Tasks\n- [ ] Parse DraftKings and FanDuel CSV slate export format\n- [ ] Map DFS player names to internal Player IDs (fuzzy match)\n- [ ] Store slate to DfsContests SQL table\n- [ ] Handle multi-position flex slot eligibility" \
  "pbi,phase-5,data,backend" $M5

create_issue \
  "[PBI-038] Integrate salary constraints into optimizer" \
  "## PBI-038: Integrate salary constraints into optimizer\n\n**Feature:** F5.1 DFS Contest Setup\n\n### Tasks\n- [ ] Add salary cap constraint to OR-Tools optimization model\n- [ ] Support both DraftKings and FanDuel roster configurations\n- [ ] Validate lineup legality before returning result\n- [ ] Unit test salary constraint enforcement" \
  "pbi,phase-5,analytics,backend" $M5

create_issue \
  "[FEATURE] F5.2: DFS Ownership Projections" \
  "## Feature: DFS Ownership Projections\n\nModel projected ownership percentages and generate multiple differentiated lineups.\n\n**PBIs:** PBI-039, PBI-040" \
  "feature,phase-5,analytics,backend" $M5

create_issue \
  "[PBI-039] Build ownership projection model" \
  "## PBI-039: Build ownership projection model\n\n**Feature:** F5.2 DFS Ownership Projections\n\n### Tasks\n- [ ] Model ownership % based on salary, matchup quality, and recent popularity\n- [ ] Flag high-ownership chalk plays (>25% projected ownership)\n- [ ] Compute leverage score (projected value vs projected ownership)\n- [ ] Store ownership projections to MongoDB" \
  "pbi,phase-5,analytics,backend" $M5

create_issue \
  "[PBI-040] Build multi-lineup generator" \
  "## PBI-040: Build multi-lineup generator\n\n**Feature:** F5.2 DFS Ownership Projections\n\n### Tasks\n- [ ] Generate N unique lineups with configurable player exposure limits\n- [ ] Enforce max exposure % caps per player across lineup set\n- [ ] Rank lineups by ceiling score vs chalk exposure balance\n- [ ] Export full lineup set to DK/FD bulk upload CSV format" \
  "pbi,phase-5,analytics,backend" $M5

create_issue \
  "[FEATURE] F5.3: DFS UI" \
  "## Feature: DFS UI\n\nBlazor DFS lineup builder with slate selection, player pool, and contest export.\n\n**PBIs:** PBI-041" \
  "feature,phase-5,frontend" $M5

create_issue \
  "[PBI-041] Build DFS lineup builder UI" \
  "## PBI-041: Build DFS lineup builder UI\n\n**Feature:** F5.3 DFS UI\n\n### Tasks\n- [ ] Slate selector and slate stats summary display\n- [ ] Player pool table with salary, projection, and ownership columns\n- [ ] Interactive lineup builder with position slot validation\n- [ ] Export button generating DK/FD contest upload CSV" \
  "pbi,phase-5,frontend" $M5

# ==============================================================================
# EPIC 6 — Reporting, Alerts & Platform Polish
# ==============================================================================
echo "Creating Epic 6 issues..."

create_issue \
  "[EPIC] E6: Reporting, Alerts & Platform Polish" \
  "## Epic: Reporting, Alerts & Platform Polish\n\nCross-cutting reporting, injury alerts, waiver recommendations, caching, and production hardening.\n\n### Features\n- F6.1 Alerts & Notifications\n- F6.2 Performance & Hardening\n\n**Phase:** 6" \
  "epic,phase-6,backend,infra" $M6

create_issue \
  "[FEATURE] F6.1: Alerts & Notifications" \
  "## Feature: Alerts & Notifications\n\nReal-time injury alerts via SignalR and waiver wire recommendation engine.\n\n**PBIs:** PBI-042, PBI-043" \
  "feature,phase-6,backend,frontend" $M6

create_issue \
  "[PBI-042] Build injury alert monitoring job" \
  "## PBI-042: Build injury alert monitoring job\n\n**Feature:** F6.1 Alerts & Notifications\n\n### Tasks\n- [ ] Poll practice report / injury status data on schedule\n- [ ] Detect player status changes (Q/D/Out/IR)\n- [ ] Trigger alert domain event via MediatR notification\n- [ ] Push real-time in-app notification via SignalR" \
  "pbi,phase-6,backend" $M6

create_issue \
  "[PBI-043] Build waiver wire recommendation engine" \
  "## PBI-043: Build waiver wire recommendation engine\n\n**Feature:** F6.1 Alerts & Notifications\n\n### Tasks\n- [ ] Score available (unowned) players by projected value over replacement (VORP)\n- [ ] Surface top waiver adds ranked by league scoring format\n- [ ] Display on dedicated Waiver Wire page in Blazor\n- [ ] Filter by position and minimum projected points threshold" \
  "pbi,phase-6,analytics,frontend" $M6

create_issue \
  "[FEATURE] F6.2: Performance & Hardening" \
  "## Feature: Performance & Hardening\n\nCaching layer, architecture tests, and production readiness improvements.\n\n**PBIs:** PBI-044, PBI-045" \
  "feature,phase-6,backend,infra" $M6

create_issue \
  "[PBI-044] Add caching layer" \
  "## PBI-044: Add caching layer\n\n**Feature:** F6.2 Performance & Hardening\n\n### Tasks\n- [ ] Implement IMemoryCache for hot projection and ranking data\n- [ ] Add Docker Redis option for future horizontal scale\n- [ ] Cache projection queries with sliding expiration (15 min)\n- [ ] Benchmark query response times before and after caching" \
  "pbi,phase-6,backend,infra" $M6

create_issue \
  "[PBI-045] Write architecture tests" \
  "## PBI-045: Write architecture tests\n\n**Feature:** F6.2 Performance & Hardening\n\n### Tasks\n- [ ] Install NetArchTest NuGet package\n- [ ] Assert FF.Domain has zero references to FF.Infrastructure\n- [ ] Assert FF.Application has zero references to EF Core\n- [ ] Add architecture test project to GitHub Actions CI pipeline" \
  "pbi,phase-6,backend,infra" $M6

echo ""
echo "=============================================="
echo " All issues created successfully!"
echo ""
echo " Next steps:"
echo " 1. Go to https://github.com/$REPO/issues to verify"
echo " 2. Go to https://github.com/users/${REPO%%/*}/projects to open your board"
echo " 3. Add all issues to the project board"
echo " 4. Set board columns: Backlog | Up Next | In Progress | In Review | Done"
echo " 5. Move Phase 1 issues to 'Up Next' to get started"
echo "=============================================="
