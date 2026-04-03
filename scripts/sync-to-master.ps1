# sync-to-master.ps1
# Run before every develop -> master PR to keep histories linear
# Usage: .\scripts\sync-to-master.ps1

Write-Host "Syncing develop onto master..." -ForegroundColor Cyan

git checkout develop
git pull origin develop
git fetch origin
git rebase origin/master

if ($LASTEXITCODE -ne 0) {
    Write-Host "Rebase had conflicts - resolve them then run: git rebase --continue" -ForegroundColor Red
    exit 1
}

Write-Host "Rebase clean. Disabling branch protection in GitHub before force push..." -ForegroundColor Yellow
Write-Host "Go to: https://github.com/paulmrice-72/FantasyFootball2026/settings/branches" -ForegroundColor Yellow
Write-Host "Uncheck 'Require a pull request' and 'Do not allow force pushes' on develop, save." -ForegroundColor Yellow
Read-Host "Press Enter when protection is disabled"

git push origin develop --force-with-lease

if ($LASTEXITCODE -eq 0) {
    Write-Host "Pushed. Re-enable branch protection now, then open PR develop -> master." -ForegroundColor Green
    Start-Process "https://github.com/paulmrice-72/FantasyFootball2026/settings/branches"
} else {
    Write-Host "Push failed - check output above." -ForegroundColor Red
}