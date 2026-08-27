using System;
using System.Collections.Generic;

namespace NLayer.Tests
{
    /// <summary>
    /// Builds MPEG-1 Layer II frames by hand, so tests have a fixture that decodes
    /// to audible (non-silent) audio without committing a binary asset. Companion
    /// to <see cref="SilentMp3"/>, which covers Layer III.
    ///
    /// Every frame here is 32 kbps, 44.1 kHz, mono, no CRC. At that bit rate the
    /// decoder selects the "low-rate, 8 subband" allocation layout.
    /// </summary>
    internal static class LayerIIMp3
    {
        public const int SampleRate = 44100;
        public const int Channels = 1;
        public const int SamplesPerFrame = 1152;
        public const int FrameLength = 104;   // 144 * 32000 / 44100

        /// <summary>
        /// Scalefactor index 3 is the denormal multiplier 1.0 - the natural choice
        /// when a fixture wants the quantized values reproduced as-is.
        /// </summary>
        public const int UnityScalefactorIndex = 3;

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

        // Header FF FD 10 C0: sync, MPEG-1, Layer II, no CRC, 32 kbps, 44.1 kHz, mono.
        private static void WriteHeader(BitWriter bw)
        {
            bw.Write(0xFF, 8);
            bw.Write(0xFD, 8);
            bw.Write(0x10, 8);
            bw.Write(0xC0, 8);
        }

        /// <summary>
        /// One frame driven by a single grouped codeword. Subband 0 uses allocation
        /// index 1, which maps to the grouped -5 entry (3 quantization levels packed
        /// into a 5-bit codeword); every other subband is left at allocation 0
        /// (silence), so the whole frame is that one codeword repeated across all 12
        /// subsamples.
        /// </summary>
        public static byte[] BuildGroupedFrame(int groupedCodeword, int scalefacIndex)
        {
            var bw = new BitWriter();
            WriteHeader(bw);

            // Allocations: subband 0 uses a 4-bit field (index 1 -> grouped -5);
            // subband 1 also 4-bit (0); subbands 2..7 use 3-bit fields (all 0).
            bw.Write(1, 4);
            bw.Write(0, 4);
            for (var sb = 2; sb < 8; sb++) bw.Write(0, 3);

            // Scalefactor selection for the single active subband: 2 => one scalefactor.
            bw.Write(2, 2);

            // The one 6-bit scalefactor.
            bw.Write(scalefacIndex, 6);

            // 12 subsamples of subband 0, each a 5-bit grouped codeword.
            for (var ss = 0; ss < 12; ss++) bw.Write(groupedCodeword, 5);

            return bw.ToArray(FrameLength);
        }

        /// <summary>
        /// Same header/layout as <see cref="BuildGroupedFrame"/>, but drives subband 2
        /// (which uses allocation table 5) at allocation index 7 - the highest 3-bit
        /// class, a linear, non-grouped 7-bit sample at 127 quantization levels.
        /// </summary>
        public static byte[] BuildLinearFrame(int sampleValue, int scalefacIndex)
        {
            var bw = new BitWriter();
            WriteHeader(bw);

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

        public static byte[] Repeat(byte[] frame, int frameCount)
        {
            var data = new byte[frame.Length * frameCount];
            for (var i = 0; i < frameCount; i++) Array.Copy(frame, 0, data, i * frame.Length, frame.Length);
            return data;
        }

        /// <summary>
        /// A stream that decodes to a near-constant tone of magnitude ~0.667: codeword
        /// 26 splits (radix 3) into the samples (2, 2, 2), the top level of a 3-level
        /// quantizer, at unity scalefactor.
        /// </summary>
        public static byte[] CreateTone(int frameCount)
            => Repeat(BuildGroupedFrame(groupedCodeword: 26, scalefacIndex: UnityScalefactorIndex), frameCount);
    }
}
