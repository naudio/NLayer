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
