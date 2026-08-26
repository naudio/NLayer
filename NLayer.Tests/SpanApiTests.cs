using System;
using System.IO;
using Xunit;

namespace NLayer.Tests
{
    /// <summary>
    /// The <c>Span&lt;T&gt;</c> overloads on <see cref="MpegFile"/> exist so callers
    /// (notably NLayer.NAudioSupport on NAudio 3) can decode without bouncing through
    /// a pooled <c>byte[]</c>. They share the decode loop with the array overloads, so
    /// what these tests pin down is that both paths deliver the same bytes.
    ///
    /// The overloads are only compiled for the net8.0 target - netstandard2.0 has no
    /// <c>Span&lt;T&gt;</c> without taking a System.Memory dependency - and this test
    /// project runs against that asset.
    /// </summary>
    public class SpanApiTests
    {
        // Layer II tone rather than Layer III silence: comparing two buffers of zeros
        // would pass whatever the copy did.
        private static byte[] ToneStream() => LayerIIMp3.CreateTone(8);

        [Fact]
        public void ReadSamples_span_of_bytes_matches_the_byte_array_overload()
        {
            using var viaArray = new MpegFile(new MemoryStream(ToneStream()));
            using var viaSpan = new MpegFile(new MemoryStream(ToneStream()));

            var arrayBuffer = new byte[1024];
            var spanBuffer = new byte[1024];

            int readFromArray, total = 0;
            while ((readFromArray = viaArray.ReadSamples(arrayBuffer, 0, arrayBuffer.Length)) > 0)
            {
                var readFromSpan = viaSpan.ReadSamples(spanBuffer.AsSpan());

                Assert.Equal(readFromArray, readFromSpan);
                Assert.Equal(arrayBuffer.AsSpan(0, readFromArray).ToArray(), spanBuffer.AsSpan(0, readFromSpan).ToArray());
                total += readFromArray;
            }

            Assert.True(total > 0, "Expected the tone fixture to decode to something");
            Assert.Equal(0, viaSpan.ReadSamples(spanBuffer.AsSpan()));
        }

        [Fact]
        public void ReadSamples_span_of_floats_matches_the_float_array_overload()
        {
            using var viaArray = new MpegFile(new MemoryStream(ToneStream()));
            using var viaSpan = new MpegFile(new MemoryStream(ToneStream()));

            var arrayBuffer = new float[256];
            var spanBuffer = new float[256];

            int readFromArray, total = 0;
            while ((readFromArray = viaArray.ReadSamples(arrayBuffer, 0, arrayBuffer.Length)) > 0)
            {
                var readFromSpan = viaSpan.ReadSamples(spanBuffer.AsSpan());

                Assert.Equal(readFromArray, readFromSpan);
                Assert.Equal(arrayBuffer.AsSpan(0, readFromArray).ToArray(), spanBuffer.AsSpan(0, readFromSpan).ToArray());
                total += readFromArray;
            }

            Assert.True(total > 0, "Expected the tone fixture to decode to something");
        }

        [Fact]
        public void ReadSamples_span_reads_whole_samples_only()
        {
            using var file = new MpegFile(new MemoryStream(ToneStream()));

            // 10 bytes is two whole floats plus two spare, matching the rounding the
            // byte[] overload has always done.
            var buffer = new byte[10];
            var read = file.ReadSamples(buffer.AsSpan());

            Assert.Equal(8, read);
            Assert.Equal(0, buffer[8]);
            Assert.Equal(0, buffer[9]);
        }

        [Fact]
        public void ReadSamples_span_shorter_than_one_sample_reads_nothing()
        {
            using var file = new MpegFile(new MemoryStream(ToneStream()));

            Assert.Equal(0, file.ReadSamples(new byte[3].AsSpan()));
        }

        [Fact]
        public void ReadSamples_span_respects_seeking()
        {
            using var file = new MpegFile(new MemoryStream(ToneStream()));

            var first = new byte[512];
            var read = file.ReadSamples(first.AsSpan());
            Assert.True(read > 0);

            file.Position = 0;

            var second = new byte[512];
            Assert.Equal(read, file.ReadSamples(second.AsSpan()));
        }
    }
}
