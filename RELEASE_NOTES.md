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

<!-- Add notes for the next release here as PRs land. Rename this heading to
     `### 3.0.0 (DD MMM YYYY)` before tagging v3.0.0. -->

- **`NLayer.NAudioSupport` now targets NAudio 3** (`NAudio.Core` 3.0.1). NAudio 3
  dropped .NET Framework and `netstandard2.0`, so this package is now `net9.0`
  only. Consumers on NAudio 2, .NET Framework, Unity or Mono should stay on
  `NLayer.NAudioSupport` 2.x — mixing 3.x with an NAudio 2 install resolves
  `NAudio.Core` up to 3.x and fails at runtime rather than at compile time.
- `NLayer` itself is unchanged and still targets `netstandard2.0` and `net8.0`,
  so the decoder remains available on every platform it already supported. The
  major version bump only reflects the lockstep versioning of the two packages.
- Added tests covering the NAudio bridge (`Mp3FrameDecompressor`,
  `ManagedMpegStream` and `Mp3FileReaderBase` end to end) against NAudio 3.

### 2.0.1 (6 Jul 2026)

- Fixed Layer II decoding of grouped samples (closes #55). Three consecutive
  samples of a low-allocation subband are packed into one codeword; the decoder
  split it with the wrong radix (5/9/17 instead of 3/5/9), and two bit-allocation
  table entries named the wrong quantization class. Together these distorted
  Layer II audio — most audibly on low-bitrate MPEG-2 (LSF) streams — with loss of
  bass and added high-frequency noise. Thanks @tokula for the fix in #56.
- Added regression tests covering the grouped-sample radix and the corrected
  bit-allocation tables.

### 2.0.0 (28 Jun 2026)

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
