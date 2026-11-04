using System.IO;
using Xunit;

namespace NLayer.Tests
{
    /// <summary>
    /// Regression tests for issue #59: when a frame's main data fills the bit
    /// reservoir's 8192-byte ring exactly to its last byte, the write pointer
    /// wraps to -1 — the same value that used to mean "empty / just reset". The
    /// next frame was then treated as the start of the stream: if it borrowed
    /// bytes from earlier frames (main_data_begin &gt; 0) it was dropped, and if
    /// it didn't, it was decoded from a stale read position.
    /// </summary>
    public class BitReservoirWrapTests
    {
        // MPEG-1 Layer III, 48 kHz, 320 kbps, stereo, no CRC, no padding.
        //   0xFF        sync (11111111)
        //   0xFB        sync(111) version=MPEG1(11) layer=III(01) protection=off(1)
        //   0xE4        bitrate=320k(1110) samplerate=48k(01) padding=0 private=0
        //   0x00        channel=stereo(00) modeExt(00) copyright=0 original=0 emphasis(00)
        private static readonly byte[] Header = { 0xFF, 0xFB, 0xE4, 0x00 };

        // Frame length = 144 * 320000 / 48000 = 960 bytes exactly, so 48 kHz never pads.
        // Main data per frame = 960 - 4 (header) - 32 (stereo side info) = 924 bytes.
        // 924 * 2048 = 231 * 8192, so the ring wraps exactly onto its end after 2048 frames.
        private const int FrameLength = 960;
        private const int FramesUntilExactWrap = 2048;
        private const int SamplesPerFrame = 1152;
        private const int Channels = 2;

        /// <summary>
        /// Builds silent frames (part2_3_length = 0 everywhere) whose side info
        /// sets main_data_begin. The first frame must start at 0 because there is
        /// nothing in the reservoir yet.
        /// </summary>
        private static byte[] Create(int frameCount, int mainDataBegin)
        {
            var data = new byte[FrameLength * frameCount];
            for (var i = 0; i < frameCount; i++)
            {
                var offset = i * FrameLength;
                System.Array.Copy(Header, 0, data, offset, Header.Length);

                // main_data_begin is the first 9 bits of the side info.
                var mdb = i == 0 ? 0 : mainDataBegin;
                data[offset + 4] = (byte)(mdb >> 1);
                data[offset + 5] = (byte)((mdb & 1) << 7);
            }
            return data;
        }

        private static long CountSamples(byte[] mp3)
        {
            using var file = new MpegFile(new MemoryStream(mp3));
            var buffer = new float[SamplesPerFrame * Channels * 4];
            long total = 0;
            int read;
            while ((read = file.ReadSamples(buffer, 0, buffer.Length)) > 0)
            {
                total += read;
            }
            return total;
        }

        [Theory]
        [InlineData(0)]
        [InlineData(100)]
        [InlineData(511)] // largest main_data_begin MPEG-1 can express
        public void Frame_after_exact_ring_wrap_is_not_dropped(int mainDataBegin)
        {
            // Two exact wraps (after frames 2048 and 4096), plus a few frames either side.
            const int frameCount = FramesUntilExactWrap * 2 + 3;

            var total = CountSamples(Create(frameCount, mainDataBegin));

            Assert.Equal((long)frameCount * SamplesPerFrame * Channels, total);
        }

        [Fact]
        public void Seeking_after_exact_ring_wrap_still_decodes_following_frames()
        {
            const int frameCount = FramesUntilExactWrap + 10;
            using var file = new MpegFile(new MemoryStream(Create(frameCount, 100)));

            // Decode past the wrap, then seek back to the start (which resets the reservoir)
            // and make sure every frame is still produced.
            var buffer = new float[SamplesPerFrame * Channels * 4];
            while (file.ReadSamples(buffer, 0, buffer.Length) > 0) { }

            file.Time = System.TimeSpan.Zero;
            long total = 0;
            int read;
            while ((read = file.ReadSamples(buffer, 0, buffer.Length)) > 0)
            {
                total += read;
            }

            Assert.Equal((long)frameCount * SamplesPerFrame * Channels, total);
        }
    }
}
