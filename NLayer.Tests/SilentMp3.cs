using System;

namespace NLayer.Tests
{
    /// <summary>
    /// Builds a minimal but valid MPEG-1 Layer III bitstream in memory so the
    /// decoder can be exercised without committing a binary fixture.
    ///
    /// Each frame is a 44.1 kHz, 128 kbps, stereo MPEG-1 Layer III frame whose
    /// side information and main data are all zero. With part2_3_length and
    /// big_values both zero there is no Huffman data to decode, so every frame
    /// decodes to 1152 samples of silence per channel.
    /// </summary>
    internal static class SilentMp3
    {
        public const int SampleRate = 44100;
        public const int Channels = 2;
        public const int SamplesPerFrame = 1152;

        // MPEG-1 Layer III, 44.1 kHz, 128 kbps, stereo, no CRC, no padding.
        //   0xFF        sync (11111111)
        //   0xFB        sync(111) version=MPEG1(11) layer=III(01) protection=off(1)
        //   0x90        bitrate=128k(1001) samplerate=44.1k(00) padding=0 private=0
        //   0x00        channel=stereo(00) modeExt(00) copyright=0 original=0 emphasis(00)
        private static readonly byte[] Header = { 0xFF, 0xFB, 0x90, 0x00 };

        // Frame length (bytes) = 144 * bitrate / samplerate + padding
        //                      = 144 * 128000 / 44100 = 417 (integer truncation).
        private const int FrameLength = 417;

        public static byte[] Create(int frameCount)
        {
            if (frameCount < 1) throw new ArgumentOutOfRangeException(nameof(frameCount));

            var data = new byte[FrameLength * frameCount];
            for (var i = 0; i < frameCount; i++)
            {
                // Only the 4-byte header is non-zero; the side info (32 bytes for
                // MPEG-1 stereo) and main data that follow stay zeroed.
                Array.Copy(Header, 0, data, i * FrameLength, Header.Length);
            }
            return data;
        }
    }
}
