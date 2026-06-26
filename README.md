# NLayer

[![build](https://github.com/naudio/NLayer/actions/workflows/build.yml/badge.svg)](https://github.com/naudio/NLayer/actions/workflows/build.yml)
[![NuGet](https://img.shields.io/nuget/v/NLayer.svg)](https://www.nuget.org/packages/NLayer/)

NLayer is a fully managed, MIT-licensed MP3 to WAV decoder implemented in C# based on the MPEG specifications.

NLayer targets `netstandard2.0` and `net8.0`, so it runs on .NET Framework 4.6.1+, .NET 8+, Unity, and Mono.

## Usage

To use NLayer for decoding MP3, first reference NLayer.

```cs
using NLayer;
```

Then create an `MpegFile`, pass a file name or a stream to the constructor, and use `ReadSamples` for decoding the content:

```cs
// samples per second times channel count
const int samplesCount = 44100;
var fileName = "myMp3File.mp3";
var mpegFile = new MpegFile(filename);
float[] samples = new float[samplesCount];
int readCount = mpegFile.ReadSamples(samples, 0, samplesCount);
```

More information could be found in code documents.

## Use with NAudio

NLayer is capable of using in conjunction with [NAudio](https://github.com/naudio/NAudio/)
for file conversion and real-time playback.

You need to reference NAudio, NLayer and NLayer.NAudioSupport first.

```cs
using NAudio.Wave;
using NLayer.NAudioSupport;
```

Then create an `Mp3FileReader`, passing in a `FrameDecompressorBuilder` that uses the `Mp3FrameDecompressor` from NLayer.NAudioSupport:

```cs
var fileName = "myMp3File.mp3";
var builder = new Mp3FileReader.FrameDecompressorBuilder(wf => new Mp3FrameDecompressor(wf));
var reader = new Mp3FileReaderBase(fileName, builder);
// play or process the file, e.g.:
waveOut.Init(reader);
waveOut.Play();
```

> `NLayer.NAudioSupport` currently targets NAudio 2. NAudio 3 support (which
> carries breaking `Span<T>` API changes) will follow once the NAudio 3 API
> stabilises.

## Building

```sh
dotnet build NLayer.sln -c Release
dotnet test NLayer.sln -c Release
```

Every push and pull request is built and tested by the `build` GitHub Actions
workflow.

Assemblies are strong-named with `NLayerStrongNameKey.snk` (checked into the
repo root). Compiler warnings are treated as errors; the broader .NET
code-analysis (CA) analyzers are not enabled — turning them on surfaces ~110
mostly-mechanical style/perf suggestions that can be addressed incrementally.

## Releasing

Versioning is centralised in `Directory.Build.props` (`<VersionPrefix>`) and
shared by both the `NLayer` and `NLayer.NAudioSupport` packages. Release notes
live in `RELEASE_NOTES.md`. Packages are published to NuGet by the `release`
workflow, which uses NuGet trusted publishing (OIDC) — no API key is stored in
the repository.

- **Pre-release:** run the `release` workflow manually (Actions → release →
  Run workflow) from `master`. It publishes `<VersionPrefix>-preview.<run>`,
  or pass a `milestone` such as `rc.1` for `<VersionPrefix>-rc.1`.
- **Final release:** bump `<VersionPrefix>`, rename the `### Unreleased`
  heading in `RELEASE_NOTES.md` to `### <version> (DD MMM YYYY)`, commit, then
  push a matching `v<version>` tag (e.g. `v2.0.0`). The workflow packs, pushes
  to NuGet, and creates a GitHub Release.
