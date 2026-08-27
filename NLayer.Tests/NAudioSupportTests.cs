using System;
using System.IO;
using System.Runtime.InteropServices;
using NAudio.Wave;
using NLayer.NAudioSupport;
using Xunit;

namespace NLayer.Tests
{
    /// <summary>
    /// Covers the NLayer.NAudioSupport bridge against NAudio 3: the
    /// <see cref="IMp3FrameDecompressor"/> implementation NAudio drives frame by
    /// frame, and the <see cref="WaveStream"/> wrapper around <see cref="MpegFile"/>.
    ///
    /// NAudio 3 moved <see cref="IWaveProvider"/> and friends to <c>Span&lt;T&gt;</c>
    /// signatures but kept the byte[] <c>DecompressFrame</c> overload on the
    /// interface, so both entry points have to keep working.
    /// </summary>
    public class NAudioSupportTests
    {
        private static Mp3Frame FirstFrame(byte[] mp3)
            => Mp3Frame.LoadFromStream(new MemoryStream(mp3));

        [Fact]
        public void Decompressor_output_format_is_ieee_float()
        {
            var frame = FirstFrame(SilentMp3.Create(2));
            var sourceFormat = new Mp3WaveFormat(frame.SampleRate, frame.ChannelMode == ChannelMode.Mono ? 1 : 2,
                frame.FrameLength, frame.BitRate);

            using var decompressor = new Mp3FrameDecompressor(sourceFormat);

            Assert.Equal(WaveFormatEncoding.IeeeFloat, decompressor.OutputFormat.Encoding);
            Assert.Equal(SilentMp3.SampleRate, decompressor.OutputFormat.SampleRate);
            Assert.Equal(SilentMp3.Channels, decompressor.OutputFormat.Channels);
        }

        [Fact]
        public void Decompressor_downmix_modes_produce_mono_output()
        {
            var frame = FirstFrame(SilentMp3.Create(2));
            var sourceFormat = new Mp3WaveFormat(frame.SampleRate, 2, frame.FrameLength, frame.BitRate);

            using var decompressor = new Mp3FrameDecompressor(sourceFormat, StereoMode.DownmixToMono);

            Assert.Equal(1, decompressor.OutputFormat.Channels);
            Assert.Equal(StereoMode.DownmixToMono, decompressor.StereoMode);
        }

        [Fact]
        public void Decompressor_decodes_a_frame_via_the_byte_array_overload()
        {
            var frame = FirstFrame(SilentMp3.Create(2));
            var sourceFormat = new Mp3WaveFormat(frame.SampleRate, 2, frame.FrameLength, frame.BitRate);
            using var decompressor = new Mp3FrameDecompressor(sourceFormat);

            // Room for one full frame of stereo float PCM.
            var dest = new byte[SilentMp3.SamplesPerFrame * SilentMp3.Channels * sizeof(float)];
            var written = decompressor.DecompressFrame(frame, dest, 0);

            Assert.Equal(dest.Length, written);
        }

        [Fact]
        public void Mp3FileReaderBase_decodes_a_stream_through_NLayer()
        {
            const int frameCount = 12;
            using var reader = new Mp3FileReaderBase(
                new MemoryStream(SilentMp3.Create(frameCount)),
                waveFormat => new Mp3FrameDecompressor(waveFormat));

            Assert.Equal(WaveFormatEncoding.IeeeFloat, reader.WaveFormat.Encoding);
            Assert.Equal(SilentMp3.SampleRate, reader.WaveFormat.SampleRate);
            Assert.Equal(SilentMp3.Channels, reader.WaveFormat.Channels);

            var buffer = new byte[4096];
            long total = 0;
            int read;
            while ((read = reader.Read(buffer, 0, buffer.Length)) > 0)
            {
                total += read;
            }

            // Decoders drop the first synthesis frame while priming, so allow a
            // one-frame tolerance against the theoretical maximum.
            var maxBytes = (long)frameCount * SilentMp3.SamplesPerFrame * SilentMp3.Channels * sizeof(float);
            Assert.InRange(total, 1, maxBytes);
        }

        [Fact]
        public void Mp3FileReaderBase_can_reposition()
        {
            using var reader = new Mp3FileReaderBase(
                new MemoryStream(SilentMp3.Create(20)),
                waveFormat => new Mp3FrameDecompressor(waveFormat));

            var buffer = new byte[4096];
            Assert.True(reader.Read(buffer, 0, buffer.Length) > 0);

            reader.Position = 0;
            Assert.Equal(0, reader.Position);
            Assert.True(reader.Read(buffer, 0, buffer.Length) > 0);
        }

        [Fact]
        public void ManagedMpegStream_reports_the_decoded_wave_format()
        {
            using var stream = new ManagedMpegStream(new MemoryStream(SilentMp3.Create(4)), closeOnDispose: true);

            Assert.Equal(WaveFormatEncoding.IeeeFloat, stream.WaveFormat.Encoding);
            Assert.Equal(SilentMp3.SampleRate, stream.WaveFormat.SampleRate);
            Assert.Equal(SilentMp3.Channels, stream.WaveFormat.Channels);
            Assert.Equal(StereoMode.Both, stream.StereoMode);
        }

        [Fact]
        public void ManagedMpegStream_single_channel_mode_produces_mono_format()
        {
            using var stream = new ManagedMpegStream(new MemoryStream(SilentMp3.Create(4)), closeOnDispose: true,
                stereoMode: StereoMode.LeftOnly);

            Assert.Equal(1, stream.WaveFormat.Channels);
            Assert.Equal(StereoMode.LeftOnly, stream.StereoMode);
        }

        [Fact]
        public void ManagedMpegStream_reads_to_the_end_of_the_stream()
        {
            const int frameCount = 12;
            using var stream = new ManagedMpegStream(new MemoryStream(SilentMp3.Create(frameCount)), closeOnDispose: true);

            var buffer = new byte[4096];
            long total = 0;
            int read;
            while ((read = stream.Read(buffer, 0, buffer.Length)) > 0)
            {
                total += read;
            }

            var maxBytes = (long)frameCount * SilentMp3.SamplesPerFrame * SilentMp3.Channels * sizeof(float);
            Assert.InRange(total, 1, maxBytes);
        }

        [Fact]
        public void ManagedMpegStream_decodes_layer_ii_audio()
        {
            // Layer II as well as Layer III: NAudio's Mp3Frame parser and NLayer's
            // decoder have to agree on more than the common case.
            using var stream = new ManagedMpegStream(new MemoryStream(LayerIIMp3.CreateTone(6)), closeOnDispose: true);

            Assert.Equal(LayerIIMp3.SampleRate, stream.WaveFormat.SampleRate);
            Assert.Equal(LayerIIMp3.Channels, stream.WaveFormat.Channels);

            var buffer = new byte[LayerIIMp3.SamplesPerFrame * sizeof(float)];
            var read = stream.Read(buffer, 0, buffer.Length);

            Assert.True(read > 0, "Expected at least one frame of Layer II audio");
            Assert.True(Rms(buffer, read) > 0.1, "Expected the Layer II tone fixture to decode to audible audio");
        }

        [Fact]
        public void Decompressor_span_overload_matches_the_byte_array_overload()
        {
            // Layer II tone rather than Layer III silence: comparing two buffers of
            // zeros would pass whatever the copy did. Two decompressors, because the
            // decoder carries state from frame to frame.
            var mp3 = LayerIIMp3.CreateTone(8);
            var sourceFormat = new Mp3WaveFormat(LayerIIMp3.SampleRate, LayerIIMp3.Channels, LayerIIMp3.FrameLength, 32000);

            using var viaArray = new Mp3FrameDecompressor(sourceFormat);
            // Typed as the interface, because that is how NAudio calls it - and it is the
            // interface dispatch that decides whether the pooled default kicks in.
            using IMp3FrameDecompressor viaSpan = new Mp3FrameDecompressor(sourceFormat);

            using var arrayStream = new MemoryStream(mp3);
            using var spanStream = new MemoryStream(mp3);

            var arrayBuffer = new byte[LayerIIMp3.SamplesPerFrame * 2 * sizeof(float)];
            var spanBuffer = new byte[arrayBuffer.Length];

            var frames = 0;
            Mp3Frame frame;
            while ((frame = Mp3Frame.LoadFromStream(arrayStream)) != null)
            {
                var spanFrame = Mp3Frame.LoadFromStream(spanStream);
                Assert.NotNull(spanFrame);

                var writtenToArray = viaArray.DecompressFrame(frame, arrayBuffer, 0);
                var writtenToSpan = viaSpan.DecompressFrame(spanFrame, spanBuffer.AsSpan());

                Assert.Equal(writtenToArray, writtenToSpan);
                Assert.Equal(arrayBuffer.AsSpan(0, writtenToArray).ToArray(), spanBuffer.AsSpan(0, writtenToSpan).ToArray());
                frames++;
            }

            Assert.True(frames > 1, $"Expected several frames from the fixture, got {frames}");
            Assert.True(Rms(arrayBuffer, arrayBuffer.Length) > 0.1, "Expected the fixture to decode to audible audio");
        }

        [Fact]
        public void Decompressor_implements_the_span_overload_rather_than_inheriting_the_default()
        {
            // NAudio 3 declares DecompressFrame(Mp3Frame, Span<byte>) as a default
            // interface method that rents a pooled byte[] and routes to the byte[]
            // overload. That fallback is invisible at runtime, so if the signature here
            // ever drifts the only symptom is a silent per-frame rent and copy. Assert
            // on the interface map instead.
            var map = typeof(Mp3FrameDecompressor).GetInterfaceMap(typeof(IMp3FrameDecompressor));
            var index = Array.FindIndex(map.InterfaceMethods, m =>
                m.Name == nameof(IMp3FrameDecompressor.DecompressFrame) &&
                m.GetParameters().Length == 2 &&
                m.GetParameters()[1].ParameterType == typeof(Span<byte>));

            Assert.True(index >= 0, "IMp3FrameDecompressor no longer declares a Span<byte> DecompressFrame overload");
            Assert.Equal(typeof(Mp3FrameDecompressor), map.TargetMethods[index].DeclaringType);
        }

        [Fact]
        public void Decompressor_span_overload_rejects_a_span_that_is_too_small()
        {
            var frame = FirstFrame(SilentMp3.Create(2));
            var sourceFormat = new Mp3WaveFormat(frame.SampleRate, 2, frame.FrameLength, frame.BitRate);
            using IMp3FrameDecompressor decompressor = new Mp3FrameDecompressor(sourceFormat);

            var tooSmall = new byte[64];
            Assert.Throws<ArgumentException>(() => decompressor.DecompressFrame(frame, tooSmall.AsSpan()));
        }

        [Fact]
        public void ManagedMpegStream_span_read_matches_the_byte_array_read()
        {
            using var viaArray = new ManagedMpegStream(new MemoryStream(LayerIIMp3.CreateTone(8)), closeOnDispose: true);
            using var viaSpan = new ManagedMpegStream(new MemoryStream(LayerIIMp3.CreateTone(8)), closeOnDispose: true);

            var arrayBuffer = new byte[1024];
            var spanBuffer = new byte[1024];

            int readFromArray, total = 0;
            while ((readFromArray = viaArray.Read(arrayBuffer, 0, arrayBuffer.Length)) > 0)
            {
                var readFromSpan = viaSpan.Read(spanBuffer.AsSpan());

                Assert.Equal(readFromArray, readFromSpan);
                Assert.Equal(arrayBuffer.AsSpan(0, readFromArray).ToArray(), spanBuffer.AsSpan(0, readFromSpan).ToArray());
                total += readFromArray;
            }

            Assert.True(total > 0, "Expected the tone fixture to decode to something");
        }

        private static double Rms(byte[] buffer, int byteCount)
        {
            var samples = MemoryMarshal.Cast<byte, float>(buffer.AsSpan(0, byteCount));
            double sumSq = 0;
            foreach (var sample in samples)
            {
                sumSq += (double)sample * sample;
            }
            return samples.Length == 0 ? 0 : Math.Sqrt(sumSq / samples.Length);
        }
    }
}
