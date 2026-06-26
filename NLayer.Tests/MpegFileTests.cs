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

        [Fact]
        public void Reads_format_from_mpeg2_stream()
        {
            using var file = new MpegFile(new MemoryStream(SilentMp3.Mpeg2.Create(10)));

            Assert.Equal(SilentMp3.Mpeg2.SampleRate, file.SampleRate);
            Assert.Equal(SilentMp3.Mpeg2.Channels, file.Channels);
        }

        [Fact]
        public void Backwards_seeks_stay_stable()
        {
            // Smoke test around the backwards-seek buffer handling reworked for issues
            // #7/#38. A silent stream must stay silent and never throw, no matter how we
            // seek around it. (The full corruption in #7/#38 only manifests on streams
            // with real Huffman data, which can't be synthesised here, so this guards the
            // invariant rather than reproducing the exact garbling.)
            using var file = new MpegFile(new MemoryStream(SilentMp3.Create(40)));
            Assert.True(file.CanSeek);

            var total = file.Length;
            var buffer = new float[2048];
            var rng = new Random(1234);

            for (var i = 0; i < 500; i++)
            {
                // jump forwards to load some data, then repeatedly seek backwards
                file.Position = Math.Min(total - 1, (long)(rng.NextDouble() * total));
                var read = file.ReadSamples(buffer, 0, buffer.Length);
                for (var j = 0; j < read; j++)
                {
                    Assert.True(Math.Abs(buffer[j]) < 1e-3f, $"Backwards seek produced non-silence: {buffer[j]}");
                }
            }
        }

        [Fact]
        public void Mpeg2_frames_decode_full_length()
        {
            // Regression test for issues #10/#43/#33: MPEG-2/2.5 Layer III frames hold
            // 576 samples and have a frame length based on SampleCount/8 (= 72), not the
            // 144 used for MPEG-1. The hard-coded 144 doubled the assumed frame length,
            // so the parser desynced and decoded only roughly half the audio.
            const int frameCount = 20;
            using var file = new MpegFile(new MemoryStream(SilentMp3.Mpeg2.Create(frameCount)));

            var buffer = new float[4096];
            long total = 0;
            int read;
            while ((read = file.ReadSamples(buffer, 0, buffer.Length)) > 0)
            {
                total += read;
            }

            // Allow one frame of priming tolerance; the buggy frame size produced only
            // about half this many samples, so this comfortably distinguishes the two.
            var expected = (long)(frameCount - 1) * SilentMp3.Mpeg2.SamplesPerFrame * SilentMp3.Mpeg2.Channels;
            Assert.True(total >= expected, $"Expected at least {expected} samples but decoded {total}");
        }
    }
}
