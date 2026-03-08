# GitHub Branch Protection Rules
## FantasyCombine.AI — Setup Reference

Branch protection cannot be defined in a YAML file — it must be configured in the GitHub UI
or via the GitHub CLI. Follow these steps exactly after pushing your first successful workflow run.

---

## Step 1 — Push the workflow and get a green build first

Branch protection rules that require status checks only work *after* the check name has
appeared at least once in GitHub. Push `PMR_PBI-013` to `develop` via PR so the
`CI - Build and Test / build-and-test` check appears in GitHub's system.

---

## Step 2 — Protect the `develop` branch

1. Go to: **GitHub → Your Repo → Settings → Branches**
2. Click **Add branch protection rule**
3. **Branch name pattern:** `develop`
4. Enable the following:

| Setting | Value |
|---|---|
| Require a pull request before merging | ✅ ON |
| Required approvals | `0` (solo developer — you can't approve your own PR, keep at 0) |
| Dismiss stale PR approvals when new commits are pushed | ✅ ON |
| Require status checks to pass before merging | ✅ ON |
| Require branches to be up to date before merging | ✅ ON |
| **Required status check** | `build-and-test` ← exact name from build.yml `jobs:` key |
| Require conversation resolution before merging | ✅ ON |
| Do not allow bypassing the above settings | ❌ OFF (you need override ability as solo dev) |
| Restrict who can push to matching branches | ❌ OFF (solo developer) |

5. Click **Create**

---

## Step 3 — Protect the `main` branch

1. Click **Add branch protection rule** again
2. **Branch name pattern:** `main`
3. Enable the following:

| Setting | Value |
|---|---|
| Require a pull request before merging | ✅ ON |
| Required approvals | `0` |
| Require status checks to pass before merging | ✅ ON |
| Require branches to be up to date before merging | ✅ ON |
| **Required status check** | `build-and-test` |
| Require linear history | ✅ ON (enforces squash/rebase, cleaner history) |
| Do not allow bypassing the above settings | ❌ OFF |

4. Click **Create**

---

## Step 4 — Verify the workflow file location

The `build.yml` file must be at this exact path in your repo:

```
.github/
  workflows/
    build.yml
```

Not inside a subfolder. GitHub Actions **only** looks in `.github/workflows/`.

---

## Step 5 — Add Coverlet to FF.Tests.csproj

The coverage collection requires Coverlet. Add these two packages to `FF.Tests.csproj`
if not already present:

```xml
<PackageReference Include="coverlet.collector" Version="6.0.2">
  <IncludeAssets>runtime; build; native; contentfiles; analyzers; buildtransitive</IncludeAssets>
  <PrivateAssets>all</PrivateAssets>
</PackageReference>
<PackageReference Include="coverlet.msbuild" Version="6.0.2">
  <PrivateAssets>all</PrivateAssets>
  <IncludeAssets>runtime; build; native; contentfiles; analyzers; buildtransitive</IncludeAssets>
</PackageReference>
```

---

## Step 6 — Handle the `--locked-mode` flag

The `build.yml` uses `dotnet restore --locked-mode` which requires `packages.lock.json` files.

**To generate them locally:**

```bash
# Run this once from your solution root on PAULMRICE
dotnet restore --use-lock-file

# This creates packages.lock.json in each project folder
# Commit ALL of them to Git
git add **/packages.lock.json
git commit -m "chore: add NuGet lock files for CI reproducibility"
```

If you prefer to skip locked mode for now, edit `build.yml` and change:
```yaml
- name: Restore dependencies
  run: dotnet restore --locked-mode
```
to:
```yaml
- name: Restore dependencies
  run: dotnet restore
```

---

## Workflow Run URL

Once pushed, monitor runs at:
`https://github.com/<your-username>/<your-repo>/actions`

---

## Expected First Run Output

```
✅ Checkout repository
✅ Setup .NET 9.0.x
✅ Cache NuGet packages
✅ Restore dependencies
✅ Build (Release)
✅ Run tests with coverage
✅ Publish test results
✅ Generate coverage report
✅ Upload coverage report artifact
```
