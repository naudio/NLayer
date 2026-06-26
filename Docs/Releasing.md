# Releasing NLayer

NLayer is published to NuGet by the **`release`** GitHub Actions workflow
([`.github/workflows/release.yml`](../.github/workflows/release.yml)). There are
two kinds of release:

- **Pre-release** — triggered manually (`workflow_dispatch`). Ships a
  `-preview`/milestone build to NuGet. No GitHub Release is created.
- **Final release** — triggered by pushing a `vX.Y.Z` tag. Ships the final
  build to NuGet **and** creates a GitHub Release.

Both publish two packages in lockstep: **`NLayer`** and
**`NLayer.NAudioSupport`**. There are no long-lived NuGet API keys — publishing
uses [NuGet trusted publishing (OIDC)](#one-time-setup-trusted-publishing).

## How versioning works

The version is centralised in [`Directory.Build.props`](../Directory.Build.props)
as a single `<VersionPrefix>` shared by both packages:

```xml
<VersionPrefix>2.0.0</VersionPrefix>
```

- **Final releases** use `VersionPrefix` verbatim (e.g. `2.0.0`).
- **Pre-releases** append a suffix: `VersionPrefix-<suffix>`, e.g.
  `2.0.0-preview.42` or `2.0.0-rc.1`.

Release notes live in [`RELEASE_NOTES.md`](../RELEASE_NOTES.md). Keep the
`### Unreleased` section up to date as PRs land — it is what pre-releases ship,
and it becomes the final release's notes when you rename it (see below).

---

## Cutting a pre-release

Use this to put a preview on NuGet for testing before committing to a final
version.

1. Go to **Actions → release → Run workflow**.
2. Select the **`main`** branch (the workflow refuses to dispatch from any
   other branch).
3. Optionally fill in **`milestone`**:
   - **Leave blank** → ships `…-preview.<run_number>` (auto-incrementing), e.g.
     `2.0.0-preview.42`.
   - **Set it** (e.g. `alpha.1`, `beta.2`, `rc.1`) → ships `…-<milestone>`,
     e.g. `2.0.0-rc.1`.
4. Run it. The workflow restores, builds, tests, packs both packages with the
   pre-release version, and pushes them (plus symbols) to NuGet.

### …or from the command line

You can trigger the same `workflow_dispatch` with the
[GitHub CLI](https://cli.github.com/) instead of the web UI:

```sh
# Auto preview.<run_number>, e.g. 2.0.0-preview.42
gh workflow run release.yml --ref main

# Named milestone, e.g. 2.0.0-rc.1
gh workflow run release.yml --ref main -f milestone=rc.1
```

`--ref main` is required — the workflow refuses to dispatch from any other
branch. To watch the run you just started:

```sh
gh run watch $(gh run list --workflow=release.yml --limit 1 --json databaseId --jq '.[0].databaseId')
```

The `### Unreleased` section of `RELEASE_NOTES.md` is used for the package notes.
No `git` tag and no GitHub Release are created for pre-releases.

> NuGet hides pre-release packages by default, so consumers only get them if
> they opt in to pre-release in their package manager.

---

## Cutting a final release

1. **Bump the version** in `Directory.Build.props` if needed, e.g.
   `<VersionPrefix>2.1.0</VersionPrefix>`.
2. **Curate the release notes.** In `RELEASE_NOTES.md`, rename the
   `### Unreleased` heading to the version and date:

   ```md
   ### 2.1.0 (15 Jul 2026)
   ```

   The workflow **fails fast** if a final release has no matching
   `### <version> (…)` section, so don't skip this.
3. **Commit** both changes to `main` (via PR).
4. **Tag and push:**

   ```sh
   git tag v2.1.0          # must equal VersionPrefix, prefixed with "v"
   git push origin v2.1.0
   ```

The tag push triggers the workflow, which:

- verifies the tag (`v2.1.0`) matches `<VersionPrefix>` (`2.1.0`) — and fails if
  they differ, so bump the prefix and retag;
- verifies the `### 2.1.0 (…)` notes section exists;
- restores, builds, tests, and packs both packages;
- pushes the packages and symbols to NuGet;
- creates a **GitHub Release** `v2.1.0` with the body taken from the
  `RELEASE_NOTES.md` section.

After release, add a fresh `### Unreleased` section to `RELEASE_NOTES.md` for the
next cycle.

---

## One-time setup: trusted publishing

Publishing uses OIDC trusted publishing, so no API key is stored in the repo.
This must be configured once by a NuGet.org owner of both packages.

1. On **nuget.org**, sign in → **Trusted Publishing** (under account/org
   settings) → create a policy:
   - **Subject:** GitHub Actions
   - **Repository owner:** `naudio`
   - **Repository:** `NLayer`
   - **Workflow file:** `release.yml`
   - **Environment:** leave blank
   A single policy covers all packages owned by that account; add a second
   policy if `NLayer` and `NLayer.NAudioSupport` are owned by different accounts.
2. In the GitHub repo, add a repository **variable** (not a secret):
   **Settings → Secrets and variables → Actions → Variables → New variable**
   - **Name:** `NUGET_USER`
   - **Value:** the NuGet.org account/org username that owns the packages.
3. The workflow already grants the required `permissions: id-token: write`.

**Smoke test:** cut a pre-release (above) and confirm the NuGet login + push
steps succeed before tagging your first final release.

---

## Troubleshooting

| Symptom | Cause / fix |
| --- | --- |
| `Release dispatch must be from main` | You ran the workflow from another branch. Re-run it from `main`. |
| `Tag vX.Y.Z does not match VersionPrefix …` | The tag and `<VersionPrefix>` differ. Bump the prefix (or fix the tag), commit, delete the bad tag, and retag. |
| `RELEASE_NOTES.md has no '### X.Y.Z (...)' section` | Rename `### Unreleased` to `### X.Y.Z (DD MMM YYYY)` before tagging. |
| NuGet login/push fails with an auth error | Trusted publishing isn't configured, or `NUGET_USER` is missing/wrong. See [one-time setup](#one-time-setup-trusted-publishing). |
| A package version already exists on NuGet | Pushes use `--skip-duplicate`, so re-running is safe; bump the version to publish new content. |
