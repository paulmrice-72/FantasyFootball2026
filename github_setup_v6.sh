#!/bin/bash
# =============================================================================
# Fantasy Football Analytics Platform — GitHub Setup Script v4
# =============================================================================
# Prerequisites:
#   1. GitHub CLI installed and authenticated (gh auth login)
#   2. Run from inside your local repo folder
#   3. Milestones already created (numbers 1-6)
# Usage:
#   chmod +x github_setup_v4.sh
#   ./github_setup_v4.sh
# =============================================================================

REPO="paulmrice-72/FantasyFootball2026"
PROJECT_NAME="Fantasy Football Analytics Platform"

echo "=============================================="
echo " FF Analytics — GitHub Issues Setup v4"
echo "=============================================="
echo "Target repo: $REPO"
echo ""

# ── LABELS ─────────────────────────────────────────────────────────────────────
echo "Creating labels..."
gh label create "epic"      --color "1F4E79" --description "Top-level epic"          --repo $REPO --force
gh label create "feature"   --color "2E75B6" --description "Feature group"           --repo $REPO --force
gh label create "pbi"       --color "2F5496" --description "Product Backlog Item"    --repo $REPO --force
gh label create "phase-1"   --color "E2EFDA" --description "Phase 1: Foundation"     --repo $REPO --force
gh label create "phase-2"   --color "FFF2CC" --description "Phase 2: Data Ingestion" --repo $REPO --force
gh label create "phase-3"   --color "FCE4D6" --description "Phase 3: Weekly Engine"  --repo $REPO --force
gh label create "phase-4"   --color "EAD1DC" --description "Phase 4: Dynasty"        --repo $REPO --force
gh label create "phase-5"   --color "D9EAD3" --description "Phase 5: DFS"            --repo $REPO --force
gh label create "phase-6"   --color "CFE2F3" --description "Phase 6: Polish"         --repo $REPO --force
gh label create "backend"   --color "F4CCCC" --description "Backend / API work"      --repo $REPO --force
gh label create "frontend"  --color "D9D2E9" --description "Blazor / UI work"        --repo $REPO --force
gh label create "data"      --color "FFF2CC" --description "Data / ingestion work"   --repo $REPO --force
gh label create "analytics" --color "D0E0E3" --description "Analytics / modeling"    --repo $REPO --force
gh label create "infra"     --color "F9CB9C" --description "Infrastructure / DevOps" --repo $REPO --force
echo "Labels done."
echo ""

# Milestone numbers (already created)
M1=7
M2=8
M3=9
M4=10
M5=11
M6=12

# ── ISSUE CREATION HELPER ──────────────────────────────────────────────────────
# Uses gh api directly so milestone number works reliably
create_issue() {
  local TITLE="$1"
  local BODY="$2"
  local LABEL1="$3"
  local LABEL2="$4"
  local LABEL3="$5"
  local MILESTONE="$6"

  echo "  Creating: $TITLE"

  # Build issue via API
  ISSUE_URL=$(gh api repos/$REPO/issues \
    -f title="$TITLE" \
    -f body="$BODY" \
    -F milestone="$MILESTONE" \
    --jq '.html_url' 2>&1)

  # Add labels separately (more reliable)
  ISSUE_NUM=$(gh api repos/$REPO/issues \
    --jq ".[] | select(.title == \"$TITLE\") | .number" 2>/dev/null | head -1)

  if [ -n "$ISSUE_NUM" ]; then
    LABELS="[\"$LABEL1\""
    if [ -n "$LABEL2" ]; then LABELS="$LABELS,\"$LABEL2\""; fi
    if [ -n "$LABEL3" ]; then LABELS="$LABELS,\"$LABEL3\""; fi
    LABELS="$LABELS]"
    gh api repos/$REPO/issues/$ISSUE_NUM/labels \
      -f "labels=$LABELS" --silent 2>/dev/null
  fi

  sleep 0.5
}

# ==============================================================================
# EPIC 1 — Platform Foundation & Architecture
# ==============================================================================
echo ""
echo "=== EPIC 1: Platform Foundation ==="

gh api repos/$REPO/issues -f title="[EPIC] E1: Platform Foundation & Architecture" \
  -f body="## Epic: Platform Foundation & Architecture

Establish solution structure, project scaffolding, CI/CD pipeline, and core cross-cutting infrastructure.

### Features
- F1.1 Solution Scaffolding
- F1.2 Database Setup
- F1.3 CQRS & MediatR Pipeline
- F1.4 API & Auth Scaffolding
- F1.5 Blazor Frontend Shell
- F1.6 CI/CD Pipeline

**Phase:** 1 | **Priority:** Critical Path" \
  -F milestone=$M1 -f "labels[]=epic" -f "labels[]=phase-1" -f "labels[]=backend" -f "labels[]=infra" --jq '.number' > /dev/null
sleep 0.5

gh api repos/$REPO/issues -f title="[FEATURE] F1.1: Solution Scaffolding" \
  -f body="## Feature: Solution Scaffolding

Create the Visual Studio solution with all Clean Architecture projects, configure dependencies, and establish coding standards.

**Epic:** E1 — Platform Foundation
**PBIs:** PBI-001, PBI-002, PBI-003" \
  -F milestone=$M1 -f "labels[]=feature" -f "labels[]=phase-1" -f "labels[]=backend" --jq '.number' > /dev/null
sleep 0.5

gh api repos/$REPO/issues -f title="[PBI-001] Create solution with Clean Architecture projects" \
  -f body="## PBI-001: Create solution with Clean Architecture projects

**Feature:** F1.1 Solution Scaffolding

### Tasks
- [ ] Create FF.sln with all project references (FF.SharedKernel, FF.Domain, FF.Application, FF.Infrastructure, FF.API, FF.Web)
- [ ] Configure project dependencies per layer rules
- [ ] Add global usings and nullable enable to all projects
- [ ] Add .editorconfig and .gitignore" \
  -F milestone=$M1 -f "labels[]=pbi" -f "labels[]=phase-1" -f "labels[]=backend" --jq '.number' > /dev/null
sleep 0.5

gh api repos/$REPO/issues -f title="[PBI-002] Implement SharedKernel base types" \
  -f body="## PBI-002: Implement SharedKernel base types

**Feature:** F1.1 Solution Scaffolding

### Tasks
- [ ] Create Result<T> and Error types
- [ ] Create PagedList<T> and PaginationParams
- [ ] Create Guard utility class
- [ ] Create base Entity and AggregateRoot" \
  -F milestone=$M1 -f "labels[]=pbi" -f "labels[]=phase-1" -f "labels[]=backend" --jq '.number' > /dev/null
sleep 0.5

gh api repos/$REPO/issues -f title="[PBI-003] Configure Serilog + Seq logging" \
  -f body="## PBI-003: Configure Serilog + Seq logging

**Feature:** F1.1 Solution Scaffolding

### Tasks
- [ ] Install Serilog packages
- [ ] Configure structured logging with correlation IDs
- [ ] Set up Seq local Docker container
- [ ] Add request logging middleware" \
  -F milestone=$M1 -f "labels[]=pbi" -f "labels[]=phase-1" -f "labels[]=backend" -f "labels[]=infra" --jq '.number' > /dev/null
sleep 0.5

gh api repos/$REPO/issues -f title="[FEATURE] F1.2: Database Setup" \
  -f body="## Feature: Database Setup

Configure SQL Server LocalDB with EF Core and MongoDB Atlas free tier. Implement repository pattern.

**Epic:** E1 — Platform Foundation
**PBIs:** PBI-004, PBI-005, PBI-006" \
  -F milestone=$M1 -f "labels[]=feature" -f "labels[]=phase-1" -f "labels[]=backend" -f "labels[]=data" --jq '.number' > /dev/null
sleep 0.5

gh api repos/$REPO/issues -f title="[PBI-004] Configure SQL Server LocalDB + EF Core" \
  -f body="## PBI-004: Configure SQL Server LocalDB + EF Core

**Feature:** F1.2 Database Setup

### Tasks
- [ ] Install EF Core packages (SqlServer, Tools, Design)
- [ ] Create AppDbContext with entity configurations
- [ ] Create initial migration
- [ ] Seed reference data (positions, NFL teams)" \
  -F milestone=$M1 -f "labels[]=pbi" -f "labels[]=phase-1" -f "labels[]=backend" -f "labels[]=data" --jq '.number' > /dev/null
sleep 0.5

gh api repos/$REPO/issues -f title="[PBI-005] Configure MongoDB Atlas connection" \
  -f body="## PBI-005: Configure MongoDB Atlas connection

**Feature:** F1.2 Database Setup

### Tasks
- [ ] Create free MongoDB Atlas cluster (512MB free tier)
- [ ] Install MongoDB.Driver NuGet package
- [ ] Create MongoDbContext wrapper class
- [ ] Implement generic repository pattern for Mongo collections
- [ ] Write connection health check endpoint" \
  -F milestone=$M1 -f "labels[]=pbi" -f "labels[]=phase-1" -f "labels[]=backend" -f "labels[]=data" --jq '.number' > /dev/null
sleep 0.5

gh api repos/$REPO/issues -f title="[PBI-006] Implement SQL repository pattern" \
  -f body="## PBI-006: Implement SQL repository pattern

**Feature:** F1.2 Database Setup

### Tasks
- [ ] Create IRepository<T> interface in FF.Application
- [ ] Implement EF Core repository in FF.Infrastructure
- [ ] Add Unit of Work pattern
- [ ] Write repository unit tests" \
  -F milestone=$M1 -f "labels[]=pbi" -f "labels[]=phase-1" -f "labels[]=backend" --jq '.number' > /dev/null
sleep 0.5

gh api repos/$REPO/issues -f title="[FEATURE] F1.3: CQRS & MediatR Pipeline" \
  -f body="## Feature: CQRS & MediatR Pipeline

Wire up MediatR with pipeline behaviors and scaffold Hangfire for background jobs.

**Epic:** E1 — Platform Foundation
**PBIs:** PBI-007, PBI-008" \
  -F milestone=$M1 -f "labels[]=feature" -f "labels[]=phase-1" -f "labels[]=backend" --jq '.number' > /dev/null
sleep 0.5

gh api repos/$REPO/issues -f title="[PBI-007] Configure MediatR with pipeline behaviors" \
  -f body="## PBI-007: Configure MediatR with pipeline behaviors

**Feature:** F1.3 CQRS & MediatR Pipeline

### Tasks
- [ ] Install MediatR NuGet package
- [ ] Add LoggingBehavior pipeline behavior
- [ ] Add ValidationBehavior with FluentValidation
- [ ] Add PerformanceBehavior (warn on queries > 500ms)" \
  -F milestone=$M1 -f "labels[]=pbi" -f "labels[]=phase-1" -f "labels[]=backend" --jq '.number' > /dev/null
sleep 0.5

gh api repos/$REPO/issues -f title="[PBI-008] Scaffold Hangfire background jobs" \
  -f body="## PBI-008: Scaffold Hangfire background jobs

**Feature:** F1.3 CQRS & MediatR Pipeline

### Tasks
- [ ] Install Hangfire with SQL Server storage
- [ ] Configure Hangfire dashboard (local access only)
- [ ] Create IBackgroundJobService abstraction
- [ ] Write sample health-check recurring job" \
  -F milestone=$M1 -f "labels[]=pbi" -f "labels[]=phase-1" -f "labels[]=backend" -f "labels[]=infra" --jq '.number' > /dev/null
sleep 0.5

gh api repos/$REPO/issues -f title="[FEATURE] F1.4: API & Auth Scaffolding" \
  -f body="## Feature: API & Auth Scaffolding

Build the ASP.NET Core Web API base with Swagger, global error handling, and JWT authentication.

**Epic:** E1 — Platform Foundation
**PBIs:** PBI-009, PBI-010" \
  -F milestone=$M1 -f "labels[]=feature" -f "labels[]=phase-1" -f "labels[]=backend" --jq '.number' > /dev/null
sleep 0.5

gh api repos/$REPO/issues -f title="[PBI-009] Build ASP.NET Core API base" \
  -f body="## PBI-009: Build ASP.NET Core API base

**Feature:** F1.4 API & Auth Scaffolding

### Tasks
- [ ] Configure Swagger / OpenAPI (Swashbuckle)
- [ ] Add global exception handling middleware
- [ ] Add API versioning
- [ ] Configure CORS policy for Blazor WASM origin" \
  -F milestone=$M1 -f "labels[]=pbi" -f "labels[]=phase-1" -f "labels[]=backend" --jq '.number' > /dev/null
sleep 0.5

gh api repos/$REPO/issues -f title="[PBI-010] Implement JWT authentication" \
  -f body="## PBI-010: Implement JWT authentication

**Feature:** F1.4 API & Auth Scaffolding

### Tasks
- [ ] Set up ASP.NET Core Identity with SQL Server
- [ ] Implement register / login / refresh token endpoints
- [ ] Add JWT bearer middleware
- [ ] Write auth integration tests" \
  -F milestone=$M1 -f "labels[]=pbi" -f "labels[]=phase-1" -f "labels[]=backend" --jq '.number' > /dev/null
sleep 0.5

gh api repos/$REPO/issues -f title="[FEATURE] F1.5: Blazor Frontend Shell" \
  -f body="## Feature: Blazor Frontend Shell

Create the Blazor WASM project with MudBlazor layout shell and JWT-aware HTTP client.

**Epic:** E1 — Platform Foundation
**PBIs:** PBI-011, PBI-012" \
  -F milestone=$M1 -f "labels[]=feature" -f "labels[]=phase-1" -f "labels[]=frontend" --jq '.number' > /dev/null
sleep 0.5

gh api repos/$REPO/issues -f title="[PBI-011] Create Blazor WASM project with MudBlazor" \
  -f body="## PBI-011: Create Blazor WASM project with MudBlazor

**Feature:** F1.5 Blazor Frontend Shell

### Tasks
- [ ] Scaffold Blazor WASM hosted project (FF.Web)
- [ ] Install and configure MudBlazor
- [ ] Create AppBar, NavMenu drawer, and MainLayout shell
- [ ] Configure HttpClient with JWT Authorization header injection" \
  -F milestone=$M1 -f "labels[]=pbi" -f "labels[]=phase-1" -f "labels[]=frontend" --jq '.number' > /dev/null
sleep 0.5

gh api repos/$REPO/issues -f title="[PBI-012] Implement login/auth UI" \
  -f body="## PBI-012: Implement login/auth UI

**Feature:** F1.5 Blazor Frontend Shell

### Tasks
- [ ] Build login and register Blazor pages
- [ ] Implement custom AuthenticationStateProvider
- [ ] Persist JWT in memory (not localStorage)
- [ ] Add route guards for protected pages using [Authorize]" \
  -F milestone=$M1 -f "labels[]=pbi" -f "labels[]=phase-1" -f "labels[]=frontend" --jq '.number' > /dev/null
sleep 0.5

gh api repos/$REPO/issues -f title="[FEATURE] F1.6: CI/CD Pipeline" \
  -f body="## Feature: CI/CD Pipeline

GitHub Actions pipeline for automated build and test on every pull request.

**Epic:** E1 — Platform Foundation
**PBIs:** PBI-013" \
  -F milestone=$M1 -f "labels[]=feature" -f "labels[]=phase-1" -f "labels[]=infra" --jq '.number' > /dev/null
sleep 0.5

gh api repos/$REPO/issues -f title="[PBI-013] GitHub Actions build and test pipeline" \
  -f body="## PBI-013: GitHub Actions build and test pipeline

**Feature:** F1.6 CI/CD Pipeline

### Tasks
- [ ] Create .github/workflows/build.yml
- [ ] Run dotnet build and xUnit tests on every PR
- [ ] Generate code coverage report
- [ ] Add branch protection rules on main" \
  -F milestone=$M1 -f "labels[]=pbi" -f "labels[]=phase-1" -f "labels[]=infra" --jq '.number' > /dev/null
sleep 0.5

echo "Phase 1 issues done."

# ==============================================================================
# EPIC 2 — Data Ingestion
# ==============================================================================
echo ""
echo "=== EPIC 2: Data Ingestion ==="

gh api repos/$REPO/issues -f title="[EPIC] E2: Data Ingestion & Integration" \
  -f body="## Epic: Data Ingestion & Integration

Build all external data pipelines — Sleeper API, NFL.com stats, and historical data loading.

### Features
- F2.1 Sleeper API Integration
- F2.2 NFL Stats Ingestion
- F2.3 Data Quality & Monitoring

**Phase:** 2" \
  -F milestone=$M2 -f "labels[]=epic" -f "labels[]=phase-2" -f "labels[]=data" --jq '.number' > /dev/null
sleep 0.5

gh api repos/$REPO/issues -f title="[FEATURE] F2.1: Sleeper API Integration" \
  -f body="## Feature: Sleeper API Integration

Build typed Sleeper API client, ingest league data and sync player universe.

**PBIs:** PBI-014, PBI-015, PBI-016" \
  -F milestone=$M2 -f "labels[]=feature" -f "labels[]=phase-2" -f "labels[]=data" -f "labels[]=backend" --jq '.number' > /dev/null
sleep 0.5

gh api repos/$REPO/issues -f title="[PBI-014] Build Sleeper API client" \
  -f body="## PBI-014: Build Sleeper API client

**Feature:** F2.1 Sleeper API Integration

### Tasks
- [ ] Map all required Sleeper API endpoints (players, leagues, rosters, transactions)
- [ ] Implement typed Refit interface
- [ ] Add Polly retry policy (exponential backoff)
- [ ] Write contract tests against Sleeper API" \
  -F milestone=$M2 -f "labels[]=pbi" -f "labels[]=phase-2" -f "labels[]=data" -f "labels[]=backend" --jq '.number' > /dev/null
sleep 0.5

gh api repos/$REPO/issues -f title="[PBI-015] Ingest Sleeper league data" \
  -f body="## PBI-015: Ingest Sleeper league data

**Feature:** F2.1 Sleeper API Integration

### Tasks
- [ ] Import league settings and scoring configuration
- [ ] Import current rosters and ownership per team
- [ ] Import full transaction history (trades, adds, drops, waivers)
- [ ] Store all to SQL Server via EF Core" \
  -F milestone=$M2 -f "labels[]=pbi" -f "labels[]=phase-2" -f "labels[]=data" -f "labels[]=backend" --jq '.number' > /dev/null
sleep 0.5

gh api repos/$REPO/issues -f title="[PBI-016] Sync Sleeper player universe" \
  -f body="## PBI-016: Sync Sleeper player universe

**Feature:** F2.1 Sleeper API Integration

### Tasks
- [ ] Pull full NFL player list from Sleeper /players/nfl endpoint
- [ ] Map to Player domain entity
- [ ] Schedule weekly delta sync Hangfire job
- [ ] Handle player status changes (IR, suspended, retired)" \
  -F milestone=$M2 -f "labels[]=pbi" -f "labels[]=phase-2" -f "labels[]=data" -f "labels[]=backend" --jq '.number' > /dev/null
sleep 0.5

gh api repos/$REPO/issues -f title="[FEATURE] F2.2: NFL Stats Ingestion" \
  -f body="## Feature: NFL Stats Ingestion

Build NFL stats client, load historical game logs into MongoDB, and schedule weekly syncs.

**PBIs:** PBI-017, PBI-018, PBI-019" \
  -F milestone=$M2 -f "labels[]=feature" -f "labels[]=phase-2" -f "labels[]=data" -f "labels[]=backend" --jq '.number' > /dev/null
sleep 0.5

gh api repos/$REPO/issues -f title="[PBI-017] Build NFL.com stats client" \
  -f body="## PBI-017: Build NFL.com stats client

**Feature:** F2.2 NFL Stats Ingestion

### Tasks
- [ ] Identify NFL.com / nfl-verse data endpoints
- [ ] Implement HttpClient with rate limiting (Polly)
- [ ] Parse game log response into PlayerGameLog documents
- [ ] Store to MongoDB PlayerGameLogs collection" \
  -F milestone=$M2 -f "labels[]=pbi" -f "labels[]=phase-2" -f "labels[]=data" -f "labels[]=backend" --jq '.number' > /dev/null
sleep 0.5

gh api repos/$REPO/issues -f title="[PBI-018] Build historical stats loader (CSV pipeline)" \
  -f body="## PBI-018: Build historical stats loader

**Feature:** F2.2 NFL Stats Ingestion

Since nfl-data-py is Python, we source pre-built CSV exports from nflfastR/pro-football-reference and build a C# import pipeline.

### Tasks
- [ ] Source nflfastR play-by-play CSV exports (2018-present) from GitHub releases
- [ ] Build CSV import pipeline using CsvHelper in C#
- [ ] Normalize stats and upsert into MongoDB PlayerGameLogs
- [ ] Validate data completeness (game count per season/week)" \
  -F milestone=$M2 -f "labels[]=pbi" -f "labels[]=phase-2" -f "labels[]=data" -f "labels[]=backend" --jq '.number' > /dev/null
sleep 0.5

gh api repos/$REPO/issues -f title="[PBI-019] Schedule automated weekly stats sync" \
  -f body="## PBI-019: Schedule automated weekly stats sync

**Feature:** F2.2 NFL Stats Ingestion

### Tasks
- [ ] Create Hangfire recurring job (runs Tuesday post-week)
- [ ] Implement idempotent upsert logic (no duplicates on re-run)
- [ ] Add failure alert notification via MediatR
- [ ] Write integration test for full pipeline run" \
  -F milestone=$M2 -f "labels[]=pbi" -f "labels[]=phase-2" -f "labels[]=data" -f "labels[]=backend" --jq '.number' > /dev/null
sleep 0.5

gh api repos/$REPO/issues -f title="[FEATURE] F2.3: Data Quality & Monitoring" \
  -f body="## Feature: Data Quality & Monitoring

Validate ingested data and expose quality reports in the Blazor dashboard.

**PBIs:** PBI-020" \
  -F milestone=$M2 -f "labels[]=feature" -f "labels[]=phase-2" -f "labels[]=data" --jq '.number' > /dev/null
sleep 0.5

gh api repos/$REPO/issues -f title="[PBI-020] Build data quality validation layer" \
  -f body="## PBI-020: Build data quality validation layer

**Feature:** F2.3 Data Quality & Monitoring

### Tasks
- [ ] Define data quality rules (completeness, stat range checks, missing games)
- [ ] Create DataQualityReport domain object
- [ ] Expose quality report via API endpoint
- [ ] Display quality summary dashboard page in Blazor" \
  -F milestone=$M2 -f "labels[]=pbi" -f "labels[]=phase-2" -f "labels[]=data" -f "labels[]=frontend" --jq '.number' > /dev/null
sleep 0.5

echo "Phase 2 issues done."

# ==============================================================================
# EPIC 3 — Weekly Projection Engine
# ==============================================================================
echo ""
echo "=== EPIC 3: Weekly Projection Engine ==="

gh api repos/$REPO/issues -f title="[EPIC] E3: Weekly Projection & Optimization Engine" \
  -f body="## Epic: Weekly Projection & Optimization Engine

Core analytics engine — regression projections, Monte Carlo simulation, and constrained lineup optimizer.

### Features
- F3.1 Player Projection Model
- F3.2 Monte Carlo Simulation
- F3.3 Lineup Optimizer
- F3.4 Weekly Projection UI

**Phase:** 3" \
  -F milestone=$M3 -f "labels[]=epic" -f "labels[]=phase-3" -f "labels[]=analytics" -f "labels[]=backend" --jq '.number' > /dev/null
sleep 0.5

gh api repos/$REPO/issues -f title="[FEATURE] F3.1: Player Projection Model" \
  -f body="## Feature: Player Projection Model

Aggregate usage metrics and build regression-based weekly projections with matchup adjustments.

**PBIs:** PBI-021, PBI-022, PBI-023" \
  -F milestone=$M3 -f "labels[]=feature" -f "labels[]=phase-3" -f "labels[]=analytics" -f "labels[]=backend" --jq '.number' > /dev/null
sleep 0.5

gh api repos/$REPO/issues -f title="[PBI-021] Build usage metric aggregation pipeline" \
  -f body="## PBI-021: Build usage metric aggregation pipeline

**Feature:** F3.1 Player Projection Model

### Tasks
- [ ] Define usage metrics per position (targets, snap %, air yards, carries, red zone looks)
- [ ] Aggregate rolling weighted averages (3-week, 5-week, season)
- [ ] Store aggregated metrics to MongoDB as projection input documents
- [ ] Unit test metric calculations against known game logs" \
  -F milestone=$M3 -f "labels[]=pbi" -f "labels[]=phase-3" -f "labels[]=analytics" -f "labels[]=backend" --jq '.number' > /dev/null
sleep 0.5

gh api repos/$REPO/issues -f title="[PBI-022] Implement regression-based projection model" \
  -f body="## PBI-022: Implement regression-based projection model

**Feature:** F3.1 Player Projection Model

### Tasks
- [ ] Build weighted regression using MathNet.Numerics
- [ ] Apply matchup adjustment coefficients
- [ ] Apply injury/practice report status adjustments
- [ ] Output point projection + confidence interval per player" \
  -F milestone=$M3 -f "labels[]=pbi" -f "labels[]=phase-3" -f "labels[]=analytics" -f "labels[]=backend" --jq '.number' > /dev/null
sleep 0.5

gh api repos/$REPO/issues -f title="[PBI-023] Build matchup analysis engine" \
  -f body="## PBI-023: Build matchup analysis engine

**Feature:** F3.1 Player Projection Model

### Tasks
- [ ] Calculate opponent defensive rankings by position
- [ ] Compute fantasy points allowed vs position (rolling 4-week and season)
- [ ] Produce matchup difficulty score (0-100 scale)
- [ ] Expose matchup data via CQRS query handler" \
  -F milestone=$M3 -f "labels[]=pbi" -f "labels[]=phase-3" -f "labels[]=analytics" -f "labels[]=backend" --jq '.number' > /dev/null
sleep 0.5

gh api repos/$REPO/issues -f title="[FEATURE] F3.2: Monte Carlo Simulation" \
  -f body="## Feature: Monte Carlo Simulation

Run probabilistic simulations to produce floor/median/ceiling distributions per player.

**PBIs:** PBI-024, PBI-025" \
  -F milestone=$M3 -f "labels[]=feature" -f "labels[]=phase-3" -f "labels[]=analytics" -f "labels[]=backend" --jq '.number' > /dev/null
sleep 0.5

gh api repos/$REPO/issues -f title="[PBI-024] Implement Monte Carlo simulation engine" \
  -f body="## PBI-024: Implement Monte Carlo simulation engine

**Feature:** F3.2 Monte Carlo Simulation

### Tasks
- [ ] Model player performance as probability distribution (Normal/LogNormal)
- [ ] Run 10,000 simulation iterations per player per week
- [ ] Produce floor (10th pct), median (50th pct), ceiling (90th pct) outputs
- [ ] Store SimulationResult documents to MongoDB" \
  -F milestone=$M3 -f "labels[]=pbi" -f "labels[]=phase-3" -f "labels[]=analytics" -f "labels[]=backend" --jq '.number' > /dev/null
sleep 0.5

gh api repos/$REPO/issues -f title="[PBI-025] Build simulation scheduling job" \
  -f body="## PBI-025: Build simulation scheduling job

**Feature:** F3.2 Monte Carlo Simulation

### Tasks
- [ ] Schedule simulation runs via Hangfire (Wednesday and Thursday each week)
- [ ] Implement parallel simulation using Task.WhenAll for performance
- [ ] Add progress tracking visible in Hangfire dashboard
- [ ] Performance test: all starters simulated in under 60 seconds" \
  -F milestone=$M3 -f "labels[]=pbi" -f "labels[]=phase-3" -f "labels[]=analytics" -f "labels[]=backend" --jq '.number' > /dev/null
sleep 0.5

gh api repos/$REPO/issues -f title="[FEATURE] F3.3: Lineup Optimizer" \
  -f body="## Feature: Lineup Optimizer

Constrained optimization engine for both redraft and DFS lineup building.

**PBIs:** PBI-026, PBI-027" \
  -F milestone=$M3 -f "labels[]=feature" -f "labels[]=phase-3" -f "labels[]=analytics" -f "labels[]=backend" --jq '.number' > /dev/null
sleep 0.5

gh api repos/$REPO/issues -f title="[PBI-026] Implement constrained lineup optimizer" \
  -f body="## PBI-026: Implement constrained lineup optimizer

**Feature:** F3.3 Lineup Optimizer

### Tasks
- [ ] Define roster constraint model (slots, position eligibility, flex rules)
- [ ] Integrate Google OR-Tools C# solver
- [ ] Apply salary cap constraints for DFS mode
- [ ] Return optimal lineup with projected score and player breakdown" \
  -F milestone=$M3 -f "labels[]=pbi" -f "labels[]=phase-3" -f "labels[]=analytics" -f "labels[]=backend" --jq '.number' > /dev/null
sleep 0.5

gh api repos/$REPO/issues -f title="[PBI-027] Add risk-adjusted optimization modes" \
  -f body="## PBI-027: Add risk-adjusted optimization modes

**Feature:** F3.3 Lineup Optimizer

### Tasks
- [ ] Implement Safe mode (minimize variance / floor optimization)
- [ ] Implement Ceiling mode (maximize upside / 90th pct optimization)
- [ ] Implement Contrarian DFS mode (penalize high-ownership players)
- [ ] Allow per-player lock / exclude constraint overrides" \
  -F milestone=$M3 -f "labels[]=pbi" -f "labels[]=phase-3" -f "labels[]=analytics" -f "labels[]=backend" --jq '.number' > /dev/null
sleep 0.5

gh api repos/$REPO/issues -f title="[FEATURE] F3.4: Weekly Projection UI" \
  -f body="## Feature: Weekly Projection UI

Blazor pages for viewing projections, simulation outputs, and the lineup optimizer.

**PBIs:** PBI-028, PBI-029" \
  -F milestone=$M3 -f "labels[]=feature" -f "labels[]=phase-3" -f "labels[]=frontend" --jq '.number' > /dev/null
sleep 0.5

gh api repos/$REPO/issues -f title="[PBI-028] Build weekly projections dashboard" \
  -f body="## PBI-028: Build weekly projections dashboard

**Feature:** F3.4 Weekly Projection UI

### Tasks
- [ ] Projection data table with sort/filter by position, team, matchup
- [ ] Floor/median/ceiling visualization per player (bar or range chart)
- [ ] Color-coded matchup difficulty indicators
- [ ] Export projections to CSV" \
  -F milestone=$M3 -f "labels[]=pbi" -f "labels[]=phase-3" -f "labels[]=frontend" --jq '.number' > /dev/null
sleep 0.5

gh api repos/$REPO/issues -f title="[PBI-029] Build lineup optimizer UI" \
  -f body="## PBI-029: Build lineup optimizer UI

**Feature:** F3.4 Weekly Projection UI

### Tasks
- [ ] Roster slot builder UI with position dropdowns
- [ ] Optimizer mode toggle (Safe / Ceiling / Contrarian)
- [ ] Display optimized lineup with projected total and player scores
- [ ] Allow manual lock/exclude of players before optimizing" \
  -F milestone=$M3 -f "labels[]=pbi" -f "labels[]=phase-3" -f "labels[]=frontend" --jq '.number' > /dev/null
sleep 0.5

echo "Phase 3 issues done."

# ==============================================================================
# EPIC 4 — Dynasty Valuation Engine
# ==============================================================================
echo ""
echo "=== EPIC 4: Dynasty Valuation Engine ==="

gh api repos/$REPO/issues -f title="[EPIC] E4: Dynasty Valuation Engine" \
  -f body="## Epic: Dynasty Valuation Engine

Long-term player value modeling — aging curves, breakout detection, career simulations, and trade valuation.

### Features
- F4.1 Aging Curve Modeling
- F4.2 Breakout Probability Classifier
- F4.3 Trade Value Model
- F4.4 Dynasty UI

**Phase:** 4" \
  -F milestone=$M4 -f "labels[]=epic" -f "labels[]=phase-4" -f "labels[]=analytics" -f "labels[]=backend" --jq '.number' > /dev/null
sleep 0.5

gh api repos/$REPO/issues -f title="[FEATURE] F4.1: Aging Curve Modeling" \
  -f body="## Feature: Aging Curve Modeling

Position-based aging curves and multi-year Monte Carlo career simulations.

**PBIs:** PBI-030, PBI-031" \
  -F milestone=$M4 -f "labels[]=feature" -f "labels[]=phase-4" -f "labels[]=analytics" -f "labels[]=backend" --jq '.number' > /dev/null
sleep 0.5

gh api repos/$REPO/issues -f title="[PBI-030] Build position-based aging curve model" \
  -f body="## PBI-030: Build position-based aging curve model

**Feature:** F4.1 Aging Curve Modeling

### Tasks
- [ ] Source historical aging data by position from game log history
- [ ] Fit polynomial aging curves using MathNet.Numerics curve fitting
- [ ] Apply age-based value decay multipliers to dynasty projections
- [ ] Store curve coefficients as configuration documents in MongoDB" \
  -F milestone=$M4 -f "labels[]=pbi" -f "labels[]=phase-4" -f "labels[]=analytics" -f "labels[]=backend" --jq '.number' > /dev/null
sleep 0.5

gh api repos/$REPO/issues -f title="[PBI-031] Implement multi-year career simulation" \
  -f body="## PBI-031: Implement multi-year career simulation

**Feature:** F4.1 Aging Curve Modeling

### Tasks
- [ ] Run Monte Carlo over 5-year career trajectories per player
- [ ] Model injury probability by age and position
- [ ] Produce career value distribution (total projected points)
- [ ] Unit test curve and simulation calculations" \
  -F milestone=$M4 -f "labels[]=pbi" -f "labels[]=phase-4" -f "labels[]=analytics" -f "labels[]=backend" --jq '.number' > /dev/null
sleep 0.5

gh api repos/$REPO/issues -f title="[FEATURE] F4.2: Breakout Probability Classifier" \
  -f body="## Feature: Breakout Probability Classifier

Score players on likelihood of a breakout season using usage growth, draft capital, age, and opportunity signals.

**PBIs:** PBI-032" \
  -F milestone=$M4 -f "labels[]=feature" -f "labels[]=phase-4" -f "labels[]=analytics" -f "labels[]=backend" --jq '.number' > /dev/null
sleep 0.5

gh api repos/$REPO/issues -f title="[PBI-032] Build breakout signal detector" \
  -f body="## PBI-032: Build breakout signal detector

**Feature:** F4.2 Breakout Probability Classifier

### Tasks
- [ ] Define breakout signals (usage growth trend, draft capital, age profile, opportunity score)
- [ ] Score each player on breakout probability (0-100 scale)
- [ ] Classify players: Breakout Candidate / On Curve / Declining / Unknown
- [ ] Store classifications to MongoDB DynastyValuations collection" \
  -F milestone=$M4 -f "labels[]=pbi" -f "labels[]=phase-4" -f "labels[]=analytics" -f "labels[]=backend" --jq '.number' > /dev/null
sleep 0.5

gh api repos/$REPO/issues -f title="[FEATURE] F4.3: Trade Value Model" \
  -f body="## Feature: Trade Value Model

Discounted future value modeling and trade analyzer engine.

**PBIs:** PBI-033, PBI-034" \
  -F milestone=$M4 -f "labels[]=feature" -f "labels[]=phase-4" -f "labels[]=analytics" -f "labels[]=backend" --jq '.number' > /dev/null
sleep 0.5

gh api repos/$REPO/issues -f title="[PBI-033] Implement discounted future value model" \
  -f body="## PBI-033: Implement discounted future value model

**Feature:** F4.3 Trade Value Model

### Tasks
- [ ] Define discount rate by position and age
- [ ] Calculate net present value of projected future production
- [ ] Normalize to trade value scale (0-100)
- [ ] Benchmark values against FantasyCalc / KTC for reasonableness check" \
  -F milestone=$M4 -f "labels[]=pbi" -f "labels[]=phase-4" -f "labels[]=analytics" -f "labels[]=backend" --jq '.number' > /dev/null
sleep 0.5

gh api repos/$REPO/issues -f title="[PBI-034] Build trade analyzer engine" \
  -f body="## PBI-034: Build trade analyzer engine

**Feature:** F4.3 Trade Value Model

### Tasks
- [ ] Accept trade offer input (players on each side)
- [ ] Calculate aggregate DFV per side of the trade
- [ ] Return trade grade (A/B/C/D/F) and win/lose/even recommendation
- [ ] Store trade analysis history to MongoDB" \
  -F milestone=$M4 -f "labels[]=pbi" -f "labels[]=phase-4" -f "labels[]=analytics" -f "labels[]=backend" --jq '.number' > /dev/null
sleep 0.5

gh api repos/$REPO/issues -f title="[FEATURE] F4.4: Dynasty UI" \
  -f body="## Feature: Dynasty UI

Blazor pages for dynasty rankings, player detail views, and the trade analyzer.

**PBIs:** PBI-035, PBI-036" \
  -F milestone=$M4 -f "labels[]=feature" -f "labels[]=phase-4" -f "labels[]=frontend" --jq '.number' > /dev/null
sleep 0.5

gh api repos/$REPO/issues -f title="[PBI-035] Build dynasty player rankings page" \
  -f body="## PBI-035: Build dynasty player rankings page

**Feature:** F4.4 Dynasty UI

### Tasks
- [ ] Sortable rankings table (value, age, position, team)
- [ ] Breakout candidate badge indicators
- [ ] Player detail modal with career projection chart (ApexCharts)
- [ ] Filter by position / team / age range" \
  -F milestone=$M4 -f "labels[]=pbi" -f "labels[]=phase-4" -f "labels[]=frontend" --jq '.number' > /dev/null
sleep 0.5

gh api repos/$REPO/issues -f title="[PBI-036] Build trade analyzer UI" \
  -f body="## PBI-036: Build trade analyzer UI

**Feature:** F4.4 Dynasty UI

### Tasks
- [ ] Player search and add to trade side A / side B slots
- [ ] Side-by-side DFV comparison with letter grade
- [ ] Trade history log view
- [ ] Copy trade summary to clipboard (share)" \
  -F milestone=$M4 -f "labels[]=pbi" -f "labels[]=phase-4" -f "labels[]=frontend" --jq '.number' > /dev/null
sleep 0.5

echo "Phase 4 issues done."

# ==============================================================================
# EPIC 5 — DFS Lineup Builder
# ==============================================================================
echo ""
echo "=== EPIC 5: DFS Lineup Builder ==="

gh api repos/$REPO/issues -f title="[EPIC] E5: DFS Lineup Builder" \
  -f body="## Epic: DFS Lineup Builder

DraftKings / FanDuel contest integration with salary-constrained optimization and ownership projections.

### Features
- F5.1 DFS Contest Setup
- F5.2 DFS Ownership Projections
- F5.3 DFS UI

**Phase:** 5" \
  -F milestone=$M5 -f "labels[]=epic" -f "labels[]=phase-5" -f "labels[]=analytics" -f "labels[]=backend" --jq '.number' > /dev/null
sleep 0.5

gh api repos/$REPO/issues -f title="[FEATURE] F5.1: DFS Contest Setup" \
  -f body="## Feature: DFS Contest Setup

Import DFS slates and wire salary constraints into the optimizer.

**PBIs:** PBI-037, PBI-038" \
  -F milestone=$M5 -f "labels[]=feature" -f "labels[]=phase-5" -f "labels[]=data" -f "labels[]=backend" --jq '.number' > /dev/null
sleep 0.5

gh api repos/$REPO/issues -f title="[PBI-037] Build DFS slate importer" \
  -f body="## PBI-037: Build DFS slate importer

**Feature:** F5.1 DFS Contest Setup

### Tasks
- [ ] Parse DraftKings and FanDuel CSV slate export format
- [ ] Map DFS player names to internal Player IDs (fuzzy match)
- [ ] Store slate to DfsContests SQL table
- [ ] Handle multi-position flex slot eligibility" \
  -F milestone=$M5 -f "labels[]=pbi" -f "labels[]=phase-5" -f "labels[]=data" -f "labels[]=backend" --jq '.number' > /dev/null
sleep 0.5

gh api repos/$REPO/issues -f title="[PBI-038] Integrate salary constraints into optimizer" \
  -f body="## PBI-038: Integrate salary constraints into optimizer

**Feature:** F5.1 DFS Contest Setup

### Tasks
- [ ] Add salary cap constraint to OR-Tools optimization model
- [ ] Support both DraftKings and FanDuel roster configurations
- [ ] Validate lineup legality before returning result
- [ ] Unit test salary constraint enforcement" \
  -F milestone=$M5 -f "labels[]=pbi" -f "labels[]=phase-5" -f "labels[]=analytics" -f "labels[]=backend" --jq '.number' > /dev/null
sleep 0.5

gh api repos/$REPO/issues -f title="[FEATURE] F5.2: DFS Ownership Projections" \
  -f body="## Feature: DFS Ownership Projections

Model projected ownership percentages and generate multiple differentiated lineups.

**PBIs:** PBI-039, PBI-040" \
  -F milestone=$M5 -f "labels[]=feature" -f "labels[]=phase-5" -f "labels[]=analytics" -f "labels[]=backend" --jq '.number' > /dev/null
sleep 0.5

gh api repos/$REPO/issues -f title="[PBI-039] Build ownership projection model" \
  -f body="## PBI-039: Build ownership projection model

**Feature:** F5.2 DFS Ownership Projections

### Tasks
- [ ] Model ownership % based on salary, matchup quality, and recent popularity
- [ ] Flag high-ownership chalk plays (>25% projected ownership)
- [ ] Compute leverage score (projected value vs projected ownership)
- [ ] Store ownership projections to MongoDB" \
  -F milestone=$M5 -f "labels[]=pbi" -f "labels[]=phase-5" -f "labels[]=analytics" -f "labels[]=backend" --jq '.number' > /dev/null
sleep 0.5

gh api repos/$REPO/issues -f title="[PBI-040] Build multi-lineup generator" \
  -f body="## PBI-040: Build multi-lineup generator

**Feature:** F5.2 DFS Ownership Projections

### Tasks
- [ ] Generate N unique lineups with configurable player exposure limits
- [ ] Enforce max exposure % caps per player across lineup set
- [ ] Rank lineups by ceiling score vs chalk exposure balance
- [ ] Export full lineup set to DK/FD bulk upload CSV format" \
  -F milestone=$M5 -f "labels[]=pbi" -f "labels[]=phase-5" -f "labels[]=analytics" -f "labels[]=backend" --jq '.number' > /dev/null
sleep 0.5

gh api repos/$REPO/issues -f title="[FEATURE] F5.3: DFS UI" \
  -f body="## Feature: DFS UI

Blazor DFS lineup builder with slate selection, player pool, and contest export.

**PBIs:** PBI-041" \
  -F milestone=$M5 -f "labels[]=feature" -f "labels[]=phase-5" -f "labels[]=frontend" --jq '.number' > /dev/null
sleep 0.5

gh api repos/$REPO/issues -f title="[PBI-041] Build DFS lineup builder UI" \
  -f body="## PBI-041: Build DFS lineup builder UI

**Feature:** F5.3 DFS UI

### Tasks
- [ ] Slate selector and slate stats summary display
- [ ] Player pool table with salary, projection, and ownership columns
- [ ] Interactive lineup builder with position slot validation
- [ ] Export button generating DK/FD contest upload CSV" \
  -F milestone=$M5 -f "labels[]=pbi" -f "labels[]=phase-5" -f "labels[]=frontend" --jq '.number' > /dev/null
sleep 0.5

echo "Phase 5 issues done."

# ==============================================================================
# EPIC 6 — Polish & Hardening
# ==============================================================================
echo ""
echo "=== EPIC 6: Polish & Hardening ==="

gh api repos/$REPO/issues -f title="[EPIC] E6: Reporting, Alerts & Platform Polish" \
  -f body="## Epic: Reporting, Alerts & Platform Polish

Cross-cutting reporting, injury alerts, waiver recommendations, caching, and production hardening.

### Features
- F6.1 Alerts & Notifications
- F6.2 Performance & Hardening

**Phase:** 6" \
  -F milestone=$M6 -f "labels[]=epic" -f "labels[]=phase-6" -f "labels[]=backend" -f "labels[]=infra" --jq '.number' > /dev/null
sleep 0.5

gh api repos/$REPO/issues -f title="[FEATURE] F6.1: Alerts & Notifications" \
  -f body="## Feature: Alerts & Notifications

Real-time injury alerts via SignalR and waiver wire recommendation engine.

**PBIs:** PBI-042, PBI-043" \
  -F milestone=$M6 -f "labels[]=feature" -f "labels[]=phase-6" -f "labels[]=backend" -f "labels[]=frontend" --jq '.number' > /dev/null
sleep 0.5

gh api repos/$REPO/issues -f title="[PBI-042] Build injury alert monitoring job" \
  -f body="## PBI-042: Build injury alert monitoring job

**Feature:** F6.1 Alerts & Notifications

### Tasks
- [ ] Poll practice report / injury status data on schedule
- [ ] Detect player status changes (Q/D/Out/IR)
- [ ] Trigger alert domain event via MediatR notification
- [ ] Push real-time in-app notification via SignalR" \
  -F milestone=$M6 -f "labels[]=pbi" -f "labels[]=phase-6" -f "labels[]=backend" --jq '.number' > /dev/null
sleep 0.5

gh api repos/$REPO/issues -f title="[PBI-043] Build waiver wire recommendation engine" \
  -f body="## PBI-043: Build waiver wire recommendation engine

**Feature:** F6.1 Alerts & Notifications

### Tasks
- [ ] Score available (unowned) players by projected value over replacement (VORP)
- [ ] Surface top waiver adds ranked by league scoring format
- [ ] Display on dedicated Waiver Wire page in Blazor
- [ ] Filter by position and minimum projected points threshold" \
  -F milestone=$M6 -f "labels[]=pbi" -f "labels[]=phase-6" -f "labels[]=analytics" -f "labels[]=frontend" --jq '.number' > /dev/null
sleep 0.5

gh api repos/$REPO/issues -f title="[FEATURE] F6.2: Performance & Hardening" \
  -f body="## Feature: Performance & Hardening

Caching layer, architecture tests, and production readiness improvements.

**PBIs:** PBI-044, PBI-045" \
  -F milestone=$M6 -f "labels[]=feature" -f "labels[]=phase-6" -f "labels[]=backend" -f "labels[]=infra" --jq '.number' > /dev/null
sleep 0.5

gh api repos/$REPO/issues -f title="[PBI-044] Add caching layer" \
  -f body="## PBI-044: Add caching layer

**Feature:** F6.2 Performance & Hardening

### Tasks
- [ ] Implement IMemoryCache for hot projection and ranking data
- [ ] Add Docker Redis option for future horizontal scale
- [ ] Cache projection queries with sliding expiration (15 min)
- [ ] Benchmark query response times before and after caching" \
  -F milestone=$M6 -f "labels[]=pbi" -f "labels[]=phase-6" -f "labels[]=backend" -f "labels[]=infra" --jq '.number' > /dev/null
sleep 0.5

gh api repos/$REPO/issues -f title="[PBI-045] Write architecture tests" \
  -f body="## PBI-045: Write architecture tests

**Feature:** F6.2 Performance & Hardening

### Tasks
- [ ] Install NetArchTest NuGet package
- [ ] Assert FF.Domain has zero references to FF.Infrastructure
- [ ] Assert FF.Application has zero references to EF Core
- [ ] Add architecture test project to GitHub Actions CI pipeline" \
  -F milestone=$M6 -f "labels[]=pbi" -f "labels[]=phase-6" -f "labels[]=backend" -f "labels[]=infra" --jq '.number' > /dev/null
sleep 0.5

echo "Phase 6 issues done."
echo ""
echo "=============================================="
echo " All 45 issues created successfully!"
echo ""
echo " Next steps:"
echo " 1. Visit: https://github.com/paulmrice-72/FantasyFootball2026/issues"
echo " 2. Create a Project board at: https://github.com/paulmrice-72?tab=projects"
echo " 3. Add columns: Backlog | Up Next | In Progress | In Review | Done"
echo " 4. Add all issues to the board"
echo " 5. Move Phase 1 PBIs to Up Next"
echo "=============================================="
