# NLayer Release Notes

Release notes are organised newest-first. The release workflow extracts the
section matching the version being shipped:

- **Final releases** (triggered by pushing a `vX.Y.Z` tag) use the
  `### X.Y.Z (date)` section and fail if it is missing — rename
  `### Unreleased` to the versioned heading before tagging.
- **Pre-releases** (manual workflow dispatch) use the `### Unreleased`
  section if present; otherwise the package ships with empty release notes.

<!-- Lines wrapped in HTML comments are stripped before the notes reach NuGet
     and the GitHub Release, so use them for contributor-facing reminders. -->

### Unreleased

- Modernised build: dropped `netstandard1.3`; packages now target
  `netstandard2.0` and `net8.0`.
- Unified `NLayer` and `NLayer.NAudioSupport` on a single version (2.0.0).
- Reproducible, deterministic builds with SourceLink, embedded sources,
  symbol packages (`.snupkg`) and an SPDX SBOM in each package.
- Packages now carry the `RepositoryUrl` and commit metadata (closes #31).
- Assemblies are now strong-named (closes #39).
- Packages now use the shared NAudio-family icon.
- Added GitHub Actions CI (build + test on every PR) and an automated
  release workflow (pre-release via dispatch, final release via `v*` tag)
  publishing to NuGet via trusted publishing (OIDC).
- Added an initial `NLayer.Tests` project.
- `NLayer.NAudioSupport` continues to target NAudio 2; NAudio 3 support
  will follow once the NAudio 3 API stabilises.
