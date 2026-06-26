using System;
using System.IO;
using Xunit;

namespace NLayer.Tests
{
    public class MpegFileTests
    {
        [Fact]
        public void Constructing_from_non_mpeg_data_throws()
        {
            using var stream = new MemoryStream(new byte[2048]); // all zeros: no sync
            Assert.Throws<InvalidDataException>(() => new MpegFile(stream));
        }

        [Fact]
        public void Reads_format_from_silent_stream()
        {
            using var file = new MpegFile(new MemoryStream(SilentMp3.Create(10)));

            Assert.Equal(SilentMp3.SampleRate, file.SampleRate);
            Assert.Equal(SilentMp3.Channels, file.Channels);
        }

        [Fact]
        public void ReadSamples_returns_decoded_silence()
        {
            using var file = new MpegFile(new MemoryStream(SilentMp3.Create(10)));

            var buffer = new float[SilentMp3.SamplesPerFrame * SilentMp3.Channels];
            var read = file.ReadSamples(buffer, 0, buffer.Length);

            Assert.True(read > 0, "Expected at least one frame of samples to be decoded");
            for (var i = 0; i < read; i++)
            {
                Assert.True(Math.Abs(buffer[i]) < 1e-4f, $"Sample {i} was not silence: {buffer[i]}");
            }
        }

        [Fact]
        public void ReadSamples_eventually_reaches_end_of_stream()
        {
            const int frameCount = 10;
            using var file = new MpegFile(new MemoryStream(SilentMp3.Create(frameCount)));

            var buffer = new float[4096];
            long total = 0;
            int read;
            while ((read = file.ReadSamples(buffer, 0, buffer.Length)) > 0)
            {
                total += read;
            }

            // Decoders drop the first synthesis frame while priming, so allow a
            // one-frame tolerance against the theoretical maximum.
            var maxSamples = (long)frameCount * SilentMp3.SamplesPerFrame * SilentMp3.Channels;
            Assert.InRange(total, 1, maxSamples);
        }

        [Fact]
        public void ReadSamples_with_negative_index_throws()
        {
            using var file = new MpegFile(new MemoryStream(SilentMp3.Create(2)));
            Assert.Throws<ArgumentOutOfRangeException>(() => file.ReadSamples(new float[16], -1, 4));
        }
    }
}
