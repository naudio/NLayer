using System;
using System.IO;
using System.Threading.Tasks;
using Xunit;

namespace NLayer.Tests
{
    /// <summary>
    /// A stream that can't seek has no reliable sample count, so
    /// <see cref="MpegFile"/> has to decode until the source runs dry rather than
    /// stopping at a known logical end. These tests cover that path: reading an
    /// unseekable stream must produce exactly the audio a seekable one does.
    /// </summary>
    public class NonSeekableStreamTests
    {
        /// <summary>
        /// Reads are forwarded, but <see cref="CanSeek"/> is false and every seek
        /// member throws - the shape of a network or pipe stream.
        /// </summary>
        private sealed class NonSeekableStream : Stream
        {
            private readonly MemoryStream _inner;

            public NonSeekableStream(byte[] data) => _inner = new MemoryStream(data);

            public override bool CanRead => true;
            public override bool CanSeek => false;
            public override bool CanWrite => false;
            public override long Length => throw new NotSupportedException();
            public override long Position
            {
                get => _inner.Position;
                set => throw new NotSupportedException();
            }

            public override void Flush() { }
            public override int Read(byte[] buffer, int offset, int count) => _inner.Read(buffer, offset, count);
            public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
            public override void SetLength(long value) => throw new NotSupportedException();
            public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        }

        private const int FrameCount = 8;

        private static byte[] ToneStream() => LayerIIMp3.CreateTone(FrameCount);

        // A read against an unseekable stream used to spin forever rather than return,
        // so run it off-thread: a regression should fail the test, not wedge the run.
        private static async Task<T> WithTimeout<T>(Func<T> read)
        {
            var task = Task.Run(read);
            var completed = await Task.WhenAny(task, Task.Delay(TimeSpan.FromSeconds(10)));
            Assert.True(completed == task, "Reading from a non-seekable stream did not complete.");
            return await task;
        }

        private static float[] ReadAll(MpegFile file)
        {
            var samples = new float[LayerIIMp3.SamplesPerFrame * LayerIIMp3.Channels * FrameCount];
            var total = 0;
            int read;
            while (total < samples.Length && (read = file.ReadSamples(samples, total, samples.Length - total)) > 0)
            {
                total += read;
            }
            return samples[..total];
        }

        [Fact]
        public async Task Reading_a_non_seekable_stream_yields_the_same_samples_as_a_seekable_one()
        {
            using var seekable = new MpegFile(new MemoryStream(ToneStream()));
            var expected = ReadAll(seekable);

            using var nonSeekable = new MpegFile(new NonSeekableStream(ToneStream()));
            var actual = await WithTimeout(() => ReadAll(nonSeekable));

            Assert.Equal(expected, actual);
        }

        [Fact]
        public async Task Reading_a_non_seekable_stream_through_the_span_overload_yields_the_same_samples()
        {
            using var seekable = new MpegFile(new MemoryStream(ToneStream()));
            var expected = ReadAll(seekable);

            using var nonSeekable = new MpegFile(new NonSeekableStream(ToneStream()));
            var actual = await WithTimeout(() =>
            {
                var samples = new float[expected.Length];
                var total = 0;
                int read;
                while (total < samples.Length && (read = nonSeekable.ReadSamples(samples.AsSpan(total))) > 0)
                {
                    total += read;
                }
                return samples[..total];
            });

            Assert.Equal(expected, actual);
        }
    }
}
