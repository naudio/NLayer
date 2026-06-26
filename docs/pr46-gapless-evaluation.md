# PR #46 evaluation — gapless playback (encoder delay/padding) & issue #9

_Evaluation date: 2026-06-26. Tested against PR head `73e21d4` and `master` `044759e`._

## TL;DR

- **PR #46 is correct and safe.** It re-enables LAME-tag parsing and adds iTunSMPB
  parsing, wires encoder delay/padding through to `MpegFile`, and trims them on
  decode. Verified bit-exact against real LAME files; **no regression** on files
  without metadata (byte-identical PCM to `master`).
- **The PR does *not* auto-fix the original issue #9 file** — and it cannot. The
  attached `title.mp3` contains **no gapless metadata of any kind** (no Xing/Info,
  no LAME tag, no iTunSMPB). The reporter's 1,152 / 1,563 values were found by
  trial-and-error and are not recoverable from the file by any standard means.
- The genuinely useful outcomes of this PR are (a) **automatic** trimming for the
  large fraction of real-world files that *do* carry LAME or iTunSMPB tags, and
  (b) the **manual `EncoderDelay` / `EncoderPadding` override API**, which is the
  only thing that actually fixes the issue #9 file.

## 1. Is the PR safe? — Yes

Built a net8.0 harness referencing the PR's `NLayer.csproj` and `master`'s, and
decoded real files through both.

| File | Source of metadata | master samples/ch | PR samples/ch | Notes |
|---|---|---|---|---|
| `tone_lame_cbr.mp3` | LAME Info tag (delay 576, pad 1404) | 46,080 (raw) | **44,100** | == exact source length |
| `tone_lame_vbr.mp3` | LAME Xing tag (delay 576, pad 1404) | 46,080 (raw) | **44,100** | == exact source length |
| `title_original.mp3` (issue #9) | none | 1,184,256 | 1,184,256 | unchanged; PCM **byte-identical** to master |
| `title_itunsmpb.mp3` (crafted) | iTunSMPB ID3v2 TXXX | — | **1,181,541** | matches Adobe Audition |
| `title_original.mp3` + manual override 1152/1563 | API setter | — | **1,181,541** | matches Adobe Audition |

Safety findings:
- **No regression.** For a file with no metadata, PR output is bit-for-bit identical
  to master (delay/padding default to 0; trim is a no-op).
- **Trim is non-destructive.** For the LAME CBR file, the PR's PCM equals master's PCM
  with exactly the first 576 samples/ch skipped and the last 1,404 samples/ch dropped —
  verified byte-for-byte. No corruption, just range removal.
- **Infinite-loop edge case fixed.** The "encoder delay exactly fills one frame"
  (1,152-sample delay) case completes without hanging (the PR's `_readBufLen` reset).
- **Seeking is robust.** Seeks followed by full reads return the correct trimmed total;
  no crashes/hangs across multiple seek points.

Minor notes (not blockers):
- `MpegStreamReader.EncoderDelay/Padding` prefer `_vbrInfo` only when `> 0`, then fall
  back to ID3. A file with both a LAME tag *and* an iTunSMPB tag where one value is 0
  could mix sources. Pathological; not seen in practice.
- The eager ID3 parse (`ParseEagerIfNeeded()` in the `MpegStreamReader` ctor) is a
  **workaround** for a pre-existing unreliable backward-seek in `ReadBuffer`, not a root
  fix. It works, but the underlying `ReadBuffer` seek fragility remains.
- `Length`/`Duration` subtract delay+padding without clamping; a pathological file where
  delay+padding > sample count could report a negative logical length.

## 2. Issue #9 — the mystery, solved

The reporter saw NLayer decode 1,184,256 samples vs Adobe Audition's 1,181,541
(2,715 extra = one 1,152 frame of delay + 1,563 of padding).

Analysis of the actual attached `title.mp3`:
- ID3v2.3 tag (6,318 bytes) at the start, ID3v1 trailer at the end.
- Audio: **1,028 frames × 1,152 = 1,184,256 samples**, CBR 192 kbps / 48 kHz.
- **No Xing/Info frame, no LAME tag, no iTunSMPB tag** — none.

So NLayer decodes the file **100% correctly**; it returns every encoded sample. The
"extra" 2,715 samples are genuine encoder delay + end padding that **no metadata in the
file describes**. There is nothing to parse. The values 1,152 / 1,563 cannot be derived
programmatically from this file — Adobe Audition's MP3 encoder simply didn't write a
gapless header, and Audition only loops it seamlessly because *it* knows its own
encoder's delay.

**Conclusion:** Nothing was missed in the PR's parsing. The only fix for *this* file is
the manual override, which the PR provides:
```csharp
var mp = new MpegFile("title.mp3");
mp.EncoderDelay = 1152;
mp.EncoderPadding = 1563;   // now decodes to exactly 1,181,541 samples
```
This was verified to produce exactly 1,181,541 samples and to survive seeking.

## 3. Real MP3s — what was tested

- **LAME 3.100** installed locally; encoded a 1.0 s 440 Hz stereo tone to CBR and VBR.
  Both carry real LAME tags (delay 576 / pad 1404) and decode to the exact 44,100-sample
  source length under the PR. ✅
- **iTunSMPB**: iTunes can't run here (Linux; Apple hosts are also blocked by the egress
  policy), so a spec-accurate `iTunSMPB` `TXXX` frame was injected into the issue #9
  audio. The PR auto-detected delay 1152 / pad 1563 → 1,181,541. ✅ (A genuine
  iTunes-encoded sample would be a nice-to-have to confirm BOM/encoding handling against
  a real-world tag, but the format is matched.)
- **No-metadata path**: the original issue #9 file exercises the fallback (delay/pad 0).

## 4. Proposed plan — test assets + first test suite

The repo currently has **zero unit/integration tests**. This work is an ideal seed.

### Assets (committed, tiny, deterministic)
A small `NLayer.Tests/Assets/` set, each a fraction of a second so they stay small:
- `tone_lame_cbr.mp3` — LAME Info tag (CBR)
- `tone_lame_vbr.mp3` — LAME Xing tag (VBR)
- `tone_itunsmpb.mp3` — iTunes-style gapless tag
- `tone_plain.mp3` — no metadata (drives the manual-override test)
- `issue9_title.mp3` — the real regression file (optionally length-trimmed to shrink it)

Recommend **committing the binaries** (offline, deterministic) over generating with LAME
at test time (LAME isn't available on all CI runners). A `tools/generate-assets.sh` script
documents exactly how each asset was produced for reproducibility.

### Test project `NLayer.Tests` (xUnit, net8.0)
- LAME tag parse: asserts delay/padding extracted from the Info/Xing+LAME header.
- iTunSMPB parse: asserts delay/padding from the ID3v2 `TXXX` frame.
- Integration decode: each asset decodes to the expected sample count.
- Issue #9 manual override: assert exactly 1,181,541 after setting 1152/1563.
- Regression: a no-metadata file decodes to its raw count and is unchanged.
- (Optional) a tiny pure-decode correctness test on a known tone (RMS / frequency check).

### Open scope decisions (for you)
1. Stop after this evaluation, or go ahead and build the test suite + assets?
2. If building: commit binary assets (recommended) vs generate-at-test-time?
3. The PR itself: keep as-is and just add tests, or also push small hardening
   (clamp negative length; brief XML-doc note that issue #9-style files need the
   manual override)?
