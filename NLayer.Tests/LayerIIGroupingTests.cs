using System;
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
        // The frame builders live in LayerIIMp3 so other tests can reuse the same
        // hand-assembled Layer II fixtures. The comments below record what each
        // fixture is for in the context of this regression.
        //
        // BuildGroupedFrame activates only subband 0 at allocation index 1, which maps
        // to the grouped -5 entry (3 quantization levels, a 5-bit codeword), so the
        // whole frame is driven by a single grouped codeword repeated across all 12
        // subsamples. With scalefactor index 3 (denormal multiplier 1.0) and codeword
        // 26 the three grouped samples are (2, 2, 2) - the top level of a 3-level
        // quantizer - so every reconstructed subband-0 value is +2/3 and the frame
        // decodes to a near-constant tone of magnitude ~0.667.
        //
        // The off-by-one radix (5) instead of 3 mis-splits 26 into (1, 0, 1) and
        // drops the RMS to ~0.385, so the two behaviours are clearly separable.
        //
        // BuildLinearFrame instead drives subband 2 (which uses allocation table 5) at
        // allocation index 7 - the highest 3-bit class. With the corrected table that
        // class is 127 levels: a linear, non-grouped 7-bit sample. The regressed table
        // named 511 levels (9 bits), so the same bytes are read 9 bits at a time and
        // mis-decoded.
        private const int SamplesPerFrame = LayerIIMp3.SamplesPerFrame;

        private static byte[] BuildStream(int groupedCodeword, int scalefacIndex, int frameCount)
            => LayerIIMp3.Repeat(LayerIIMp3.BuildGroupedFrame(groupedCodeword, scalefacIndex), frameCount);

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
            var (rms, peak, count) = Decode(LayerIIMp3.Repeat(LayerIIMp3.BuildLinearFrame(sampleValue: 64, scalefacIndex: 3), frameCount: 6));

            Assert.True(count >= SamplesPerFrame, $"Expected at least one decoded frame, got {count} samples");
            Assert.True(rms < 0.1, $"Expected near-silence (~0.016) but RMS was {rms}; the pre-#56 table gives ~0.82");
            Assert.True(peak < 0.1, $"Expected near-silence but peak was {peak}; the pre-#56 table peaks ~1.5");
        }
    }
}
