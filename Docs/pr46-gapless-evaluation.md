# PR #46 evaluation — gapless playback (encoder delay/padding) & issue #9

_Evaluation date: 2026-06-26. Tested against PR head `73e21d4` and `master` `044759e`._

## TL;DR

- **PR #46 is correct and safe.** It re-enables LAME-tag parsing and adds iTunSMPB
  parsing, wires encoder delay/padding through to `MpegFile`, and trims them on
  decode. Verified bit-exact against real LAME files; **no regression** on files
  without metadata (byte-identical PCM to `master`).
- **The real value of the PR is the *automatic* detection** of encoder delay/padding
  from LAME (`Info`/`Xing` + `LAME`) and iTunSMPB tags. A large fraction of real-world
  MP3s — anything LAME-encoded, anything from iTunes/AAC pipelines — carry one of these,
  and for those files NLayer now produces sample-accurate gapless output with **zero
  user effort**. This alone justifies the change.
- **The PR does *not* auto-fix the original issue #9 file** — and it cannot. The
  attached `title.mp3` contains **no gapless metadata of any kind** (no Xing/Info,
  no LAME tag, no iTunSMPB). For such a file the exact original length is
  **fundamentally unrecoverable by automated means**: the reporter's 1,152 / 1,563
  values came from painstaking trial-and-error and are not present anywhere in the file.
- **The manual `EncoderDelay`/`EncoderPadding` override is a niche escape hatch, not a
  general solution.** It only helps a caller who *already knows* the exact values — which,
  for a metadata-less file, requires the same manual analysis the reporter did. Its one
  defensible use is a **batch pipeline** where every file comes from the same encoder/preset
  (measure once, apply to all — which is the reporter's actual situation). For an arbitrary
  file from an unknown source it provides no usable answer. The honest fix for that class of
  file is to **re-encode through a gapless-aware encoder** (LAME, etc.) that writes the tag,
  after which auto-detection just works.

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

**Conclusion:** Nothing was missed in the PR's parsing — there is genuinely nothing in the
file to parse. The exact original length cannot be recovered programmatically. Two practical
responses exist, neither of which is a generic auto-fix:

1. **Re-encode through a gapless-aware encoder** (recommended for most users). Running the
   file through LAME produces an `Info`/`LAME` tag, after which the PR's auto-detection
   trims it correctly with no further work.
2. **Manual override**, *only* if you already know the values (e.g. a batch of loops all
   exported from the same Adobe Audition preset — measure 1,152 / 1,563 once, apply to all):
   ```csharp
   var mp = new MpegFile("title.mp3");
   mp.EncoderDelay = 1152;
   mp.EncoderPadding = 1563;   // now decodes to exactly 1,181,541 samples
   ```
   This was verified to produce exactly 1,181,541 samples and to survive seeking. It is not a
   solution for an arbitrary metadata-less file, because there is no way to know the numbers.

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

## 4. Proposed plan — extend the test suite for gapless

Master now has an `NLayer.Tests` xUnit (net8.0) project (added in #48) with a clever
`SilentMp3` helper that synthesises a valid bare MPEG-1 L3 bitstream **in memory** — no
binary fixtures. The gapless tests should build on that project and prefer the same
in-memory / generated approach where possible.

### Test data — prefer generated/crafted over committed binaries
The delay/padding tags can be produced without checking in audio:
- **iTunSMPB**: the tag is plain text in an ID3v2 `TXXX` frame; it can be **prepended in
  code** to a `SilentMp3`-style stream (exactly as this evaluation crafted `title_itunsmpb`),
  so no binary asset is needed.
- **LAME `Info`/`Xing` + `LAME` tag**: the header layout is well-defined; a minimal valid
  Info frame with known delay/padding can also be built in code.
- A couple of **real LAME-encoded fixtures** (sub-second tones, ~8–17 KB) are still worth
  committing as a belt-and-braces check that parsing matches what LAME actually writes,
  with a `tools/generate-assets.sh` documenting how they were produced. This avoids a hard
  CI dependency on LAME while keeping one real-world anchor.

### Tests to add (focused on the *valuable* behaviour — auto-detection)
- **LAME tag parse** → `MpegFile` reports the embedded delay/padding and trims to the exact
  source length (the headline capability).
- **iTunSMPB parse** → delay/padding read from the `TXXX` frame; covers Latin-1/UTF-16+BOM.
- **No-metadata regression** → delay/padding default to 0 and decoded output is unchanged
  vs. the pre-PR behaviour (guards against silent breakage).
- **Edge case** → encoder delay that exactly fills one frame decodes without hanging.
- **Manual override (minor)** → setting the values trims accordingly; included for coverage,
  but documented as the niche escape hatch it is, not the issue #9 resolution.

### Open scope decisions (for you)
1. Go ahead and add the gapless tests to `NLayer.Tests`, or stop at this evaluation?
2. Generated/crafted test data (recommended) vs. also committing a couple of real LAME fixtures?
3. The PR itself: keep as-is and just add tests, or also push small hardening
   (clamp negative `Length`/`Duration`; honest XML-doc on the override explaining it needs
   known values and pointing metadata-less users at re-encoding)?
