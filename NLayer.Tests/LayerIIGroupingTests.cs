using System;
using System.Collections.Generic;
using System.IO;
using Xunit;

namespace NLayer.Tests
{
    /// <summary>
    /// Regression tests for issue #55 (fixed in PR #56), which had two parts:
    ///
    /// 1. Grouping radix: Layer II packs three consecutive samples of a
    ///    low-allocation subband into a single codeword using a radix equal to the
    ///    number of quantization levels (3, 5 or 9). <c>ReadSamples</c> computed the
    ///    level count one power of two too large, giving radices of 5/9/17. That
    ///    both mis-splits the codeword and lets sample indices exceed the quantizer's
    ///    valid range, which the dequantiser reconstructs as loud, wrong values.
    ///
    /// 2. Allocation tables: two entries in <c>_allocLookupTable</c> named the wrong
    ///    quantization class - table 5 (low-rate MPEG-1 and LSF) mapped its top
    ///    3-bit class to 511 levels instead of 127, and table 7 (LSF) mapped its top
    ///    2-bit class to 7 levels instead of a 9-level group. A wrong class reads the
    ///    wrong number of bits per sample and desyncs the rest of the subband.
    ///
    /// The fixtures below are synthesised in-memory (no binary asset committed),
    /// following the same approach as <see cref="SilentMp3"/>.
    /// </summary>
    public class LayerIIGroupingTests
    {
        // MSB-first bit writer for assembling a frame by hand.
        private sealed class BitWriter
        {
            private readonly List<byte> _bytes = new List<byte>();
            private int _cur, _nbits;

            public void Write(int value, int bits)
            {
                for (var i = bits - 1; i >= 0; i--)
                {
                    _cur = (_cur << 1) | ((value >> i) & 1);
                    if (++_nbits == 8) { _bytes.Add((byte)_cur); _cur = 0; _nbits = 0; }
                }
            }

            public byte[] ToArray(int totalLength)
            {
                if (_nbits > 0) { _bytes.Add((byte)(_cur << (8 - _nbits))); _cur = 0; _nbits = 0; }
                var a = new byte[totalLength];
                Array.Copy(_bytes.ToArray(), a, Math.Min(_bytes.Count, totalLength));
                return a;
            }
        }

        // Builds one MPEG-1 Layer II frame: 32 kbps, 44.1 kHz, mono, no CRC.
        //
        // At this bit rate the decoder selects the "low-rate, 8 subband" allocation
        // layout. We activate only subband 0 with allocation index 1, which maps to
        // the grouped -5 entry (3 quantization levels, a 5-bit codeword). Every
        // other subband is left at allocation 0 (silence), so the whole frame is
        // driven by a single grouped codeword repeated across all 12 subsamples.
        //
        // With scalefactor index 3 (denormal multiplier 1.0) and codeword 26 the
        // three grouped samples are (2, 2, 2) - the top level of a 3-level
        // quantizer - so every reconstructed subband-0 value is +2/3 and the frame
        // decodes to a near-constant tone of magnitude ~0.667.
        //
        // The off-by-one radix (5) instead of 3 mis-splits 26 into (1, 0, 1) and
        // drops the RMS to ~0.385, so the two behaviours are clearly separable.
        private const int FrameLength = 104;   // 144 * 32000 / 44100
        private const int SamplesPerFrame = 1152;

        private static byte[] BuildFrame(int groupedCodeword, int scalefacIndex)
        {
            var bw = new BitWriter();

            // Header FF FD 10 C0: sync, MPEG-1, Layer II, no CRC, 32 kbps, 44.1 kHz, mono.
            bw.Write(0xFF, 8);
            bw.Write(0xFD, 8);
            bw.Write(0x10, 8);
            bw.Write(0xC0, 8);

            // Allocations: subband 0 uses a 4-bit field (index 1 -> grouped -5);
            // subband 1 also 4-bit (0); subbands 2..7 use 3-bit fields (all 0).
            bw.Write(1, 4);
            bw.Write(0, 4);
            for (var sb = 2; sb < 8; sb++) bw.Write(0, 3);

            // Scalefactor selection for the single active subband: 2 => one scalefactor.
            bw.Write(2, 2);

            // The one 6-bit scalefactor (index 3 => multiplier 1.0).
            bw.Write(scalefacIndex, 6);

            // 12 subsamples of subband 0, each a 5-bit grouped codeword.
            for (var ss = 0; ss < 12; ss++) bw.Write(groupedCodeword, 5);

            return bw.ToArray(FrameLength);
        }

        private static byte[] BuildStream(int groupedCodeword, int scalefacIndex, int frameCount)
        {
            var frame = BuildFrame(groupedCodeword, scalefacIndex);
            return Repeat(frame, frameCount);
        }

        // Same header/layout as BuildFrame, but drives subband 2 (which uses
        // allocation table 5) at allocation index 7 - the highest 3-bit class. With
        // the corrected table that class is 127 levels: a linear, non-grouped 7-bit
        // sample. The regressed table named 511 levels (9 bits), so the same bytes
        // are read 9 bits at a time and mis-decoded.
        private static byte[] BuildLinearFrame(int sampleValue, int scalefacIndex)
        {
            var bw = new BitWriter();
            bw.Write(0xFF, 8); bw.Write(0xFD, 8); bw.Write(0x10, 8); bw.Write(0xC0, 8);

            bw.Write(0, 4);                                   // subband 0 (table 4) = 0
            bw.Write(0, 4);                                   // subband 1 (table 4) = 0
            bw.Write(7, 3);                                   // subband 2 (table 5) = index 7
            for (var sb = 3; sb < 8; sb++) bw.Write(0, 3);    // subbands 3..7 = 0

            bw.Write(2, 2);                                   // scfsi for subband 2: one scalefactor
            bw.Write(scalefacIndex, 6);

            // subband 2 samples: non-grouped 7-bit values, 3 granules per subsample.
            for (var ss = 0; ss < 12; ss++)
                for (var gr = 0; gr < 3; gr++)
                    bw.Write(sampleValue, 7);

            return bw.ToArray(FrameLength);
        }

        private static byte[] Repeat(byte[] frame, int frameCount)
        {
            var data = new byte[frame.Length * frameCount];
            for (var i = 0; i < frameCount; i++) Array.Copy(frame, 0, data, i * frame.Length, frame.Length);
            return data;
        }

        private static (double rms, double peak, long count) Decode(byte[] stream)
        {
            using var file = new MpegFile(new MemoryStream(stream));
            var buffer = new float[4096];
            double sumSq = 0, peak = 0;
            long count = 0;
            int read;
            while ((read = file.ReadSamples(buffer, 0, buffer.Length)) > 0)
            {
                for (var i = 0; i < read; i++)
                {
                    sumSq += (double)buffer[i] * buffer[i];
                    var a = Math.Abs(buffer[i]);
                    if (a > peak) peak = a;
                    count++;
                }
            }
            return (Math.Sqrt(sumSq / Math.Max(1, count)), peak, count);
        }

        [Fact]
        public void Grouped_codeword_uses_correct_radix()
        {
            // Codeword 26 with the correct radix (3) is (2, 2, 2): the top level of a
            // 3-level quantizer, reconstructed as a constant +2/3. The frame therefore
            // decodes to a near-constant signal of magnitude ~0.667.
            //
            // The buggy radix (5) splits 26 into (1, 0, 1) and produces ~0.385 RMS, so
            // this band cleanly distinguishes the fixed decoder from the regressed one.
            var (rms, peak, count) = Decode(BuildStream(groupedCodeword: 26, scalefacIndex: 3, frameCount: 6));

            Assert.True(count >= SamplesPerFrame, $"Expected at least one decoded frame, got {count} samples");
            Assert.InRange(rms, 0.60, 0.72);   // correct ~0.667; the #55 radix bug gives ~0.385
            Assert.InRange(peak, 0.80, 1.0);   // correct ~0.916; the bug peaks near ~0.694
        }

        [Fact]
        public void Grouped_samples_stay_within_quantizer_range()
        {
            // Independently of the exact reconstruction, a correctly split 3-level
            // codeword can never drive a subband value beyond the quantizer's full
            // scale (|value| <= 2/3 before synthesis). Across every valid codeword the
            // correct decoder peaks at ~1.145 (the polyphase filter adds some
            // overshoot). A decoder using the wrong (larger) radix lets sample indices
            // reach 3 or 4, which the dequantiser reconstructs well past full scale and
            // pushes peaks to ~2.0-2.9. The 1.3 bound sits cleanly between the two.
            for (var codeword = 0; codeword < 27; codeword++)
            {
                var (_, peak, _) = Decode(BuildStream(codeword, scalefacIndex: 3, frameCount: 4));
                Assert.True(peak < 1.3, $"Codeword {codeword} produced out-of-range peak {peak}");
            }
        }

        [Fact]
        public void Allocation_table_uses_correct_bits_per_sample()
        {
            // Sample value 64 is the midpoint of the 7-bit range (1000000b), which a
            // correct 127-level quantizer reconstructs as ~+0.016 - effectively
            // silence. So a frame whose only active subband is quantized at that level
            // decodes to near-silence (RMS ~0.016).
            //
            // The regressed table read this class as 9 bits per sample, so it slices
            // the same repeating byte pattern into different values, desyncs, and
            // produces a loud ~0.82 RMS. The bound below sits far below that.
            var (rms, peak, count) = Decode(Repeat(BuildLinearFrame(sampleValue: 64, scalefacIndex: 3), frameCount: 6));

            Assert.True(count >= SamplesPerFrame, $"Expected at least one decoded frame, got {count} samples");
            Assert.True(rms < 0.1, $"Expected near-silence (~0.016) but RMS was {rms}; the pre-#56 table gives ~0.82");
            Assert.True(peak < 0.1, $"Expected near-silence but peak was {peak}; the pre-#56 table peaks ~1.5");
        }
    }
}
