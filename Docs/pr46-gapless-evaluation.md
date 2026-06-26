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
- **The manual `EncoderDelay`/`EncoderPadding` override has been dropped** (see §5). It
  only helped a caller who *already knew* the exact values — which, for a metadata-less file,
  requires the same manual analysis the reporter did. It provided no usable answer for an
  arbitrary file from an unknown source, so it has been removed pending a real request. The
  honest fix for a metadata-less file is to **re-encode through a gapless-aware encoder**
  (LAME, etc.) that writes the tag, after which auto-detection just works.

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
file to parse. The exact original length cannot be recovered programmatically. The practical
fix is to **re-encode through a gapless-aware encoder**: running the file through LAME
produces an `Info`/`LAME` tag, after which auto-detection trims it correctly with no further
work. (During evaluation, manually feeding the reporter's 1,152 / 1,563 values through the
since-removed override produced exactly 1,181,541 samples and survived seeking — confirming
those *are* the right values, but also that knowing them at all required the reporter's
manual analysis. See §5 for why the override was dropped.)

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

## 4. Test suite — added to `NLayer.Tests`

Master's `NLayer.Tests` xUnit (net8.0) project (added in #48) already had a `SilentMp3`
helper that synthesises a valid bare MPEG-1 L3 bitstream **in memory** — no binary fixtures.
The gapless tests build on that, the same way: a new `GaplessMp3` helper prepends a LAME
`Info` header frame or an `iTunSMPB` ID3v2 `TXXX` tag to a `SilentMp3` body, all in code.

The generator was cross-checked against real LAME 3.100 output to get the conventions right
(notably: the Xing *Frames* field counts the audio frames only, excluding the header frame —
verified against an actual `lame -b 128 --cbr` file). With deterministic silence the expected
sample counts are exact.

`GaplessTests` (6 tests, all passing — full suite 11/11):
- **`Lame_tag_trims_encoder_delay_and_padding`** — the headline capability: trims to
  `frames·1152 − delay − padding`.
- **`ITunSmpb_tag_trims_encoder_delay_and_padding`** — same via the ID3v2 `TXXX` path.
- **`Stream_without_gapless_metadata_is_not_trimmed`** — regression guard: no tag ⇒ no trim.
- **`Lame_tag_with_zero_delay_and_padding_is_a_no_op`** — a present-but-zero tag changes nothing.
- **`Trimmed_length_matches_decoded_samples`** — `MpegFile.Length` agrees with what is decoded.
- **`Encoder_delay_filling_an_entire_frame_does_not_hang`** — regression for the one-frame
  delay infinite-loop, guarded with a timeout.

No binary assets were committed — everything is generated, matching the repo's existing
convention. The real LAME/iTunSMPB sample files used during evaluation live in the session
scratchpad and a `tools/` generator script could be added later if a real-world anchor fixture
is ever wanted.

## 5. Change made: manual override removed

Per maintainer decision, the public `MpegFile.EncoderDelay` / `EncoderPadding` **setter**
properties from the original PR have been removed. Rationale: they only help a caller who
already knows the exact delay/padding, which a metadata-less file does not provide — so the
"escape hatch" had no realistic general use. The internal auto-detection plumbing
(`VBRInfo` / `MpegStreamReader` / `ID3Frame`) is unchanged; the values are still detected and
applied automatically. No read-only getters were added either: exposing the detected values is
cheap to add later if requested, but removing public API later would be a breaking change, so
the surface is kept minimal for now. Nothing else in the repo referenced the removed
properties.
