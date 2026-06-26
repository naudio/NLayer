using System;
using System.IO;
using System.Threading.Tasks;
using Xunit;

namespace NLayer.Tests
{
    /// <summary>
    /// Covers the encoder delay / end-padding trimming sourced from a LAME
    /// <c>Info</c>/<c>Xing</c> header or an <c>iTunSMPB</c> ID3v2 tag. The bitstreams
    /// are synthesised by <see cref="GaplessMp3"/>; the expected sample counts are
    /// exact because the input is fully deterministic silence.
    /// </summary>
    public class GaplessTests
    {
        private const int Frames = 20;
        private const int Delay = 576;
        private const int Padding = 1404;

        private static long RawSamplesPerChannel => (long)Frames * SilentMp3.SamplesPerFrame;

        private static long DecodeSamplesPerChannel(byte[] data)
        {
            using var file = new MpegFile(new MemoryStream(data));
            var buffer = new float[8192];
            long total = 0;
            int read;
            while ((read = file.ReadSamples(buffer, 0, buffer.Length)) > 0)
            {
                total += read;
            }
            return total / file.Channels;
        }

        [Fact]
        public void Stream_without_gapless_metadata_is_not_trimmed()
        {
            var samples = DecodeSamplesPerChannel(SilentMp3.Create(Frames));
            Assert.Equal(RawSamplesPerChannel, samples);
        }

        [Fact]
        public void Lame_tag_trims_encoder_delay_and_padding()
        {
            var samples = DecodeSamplesPerChannel(GaplessMp3.WithLameTag(Frames, Delay, Padding));
            Assert.Equal(RawSamplesPerChannel - Delay - Padding, samples);
        }

        [Fact]
        public void Lame_tag_with_zero_delay_and_padding_is_a_no_op()
        {
            var samples = DecodeSamplesPerChannel(GaplessMp3.WithLameTag(Frames, 0, 0));
            Assert.Equal(RawSamplesPerChannel, samples);
        }

        [Fact]
        public void ITunSmpb_tag_trims_encoder_delay_and_padding()
        {
            var samples = DecodeSamplesPerChannel(GaplessMp3.WithITunSmpb(Frames, Delay, Padding));
            Assert.Equal(RawSamplesPerChannel - Delay - Padding, samples);
        }

        [Fact]
        public void Trimmed_length_matches_decoded_samples()
        {
            var data = GaplessMp3.WithLameTag(Frames, Delay, Padding);
            using var file = new MpegFile(new MemoryStream(data));
            var expectedSamples = RawSamplesPerChannel - Delay - Padding;
            Assert.Equal(expectedSamples * file.Channels * sizeof(float), file.Length);
        }

        [Fact]
        public async Task Encoder_delay_filling_an_entire_frame_does_not_hang()
        {
            // Regression guard: the delay-skip path previously could loop forever
            // when the encoder delay exactly consumed one decoded frame, because
            // the emptied buffer was not flagged for refill.
            const int oneFrameDelay = SilentMp3.SamplesPerFrame;
            var decode = Task.Run(() =>
                DecodeSamplesPerChannel(GaplessMp3.WithLameTag(Frames, oneFrameDelay, Padding)));

            var finished = await Task.WhenAny(decode, Task.Delay(TimeSpan.FromSeconds(10)));
            Assert.True(finished == decode,
                "Decoding hung on an encoder delay that fills exactly one frame");
            Assert.Equal(RawSamplesPerChannel - oneFrameDelay - Padding, await decode);
        }
    }
}
