using System;
#if NET8_0_OR_GREATER
using System.Runtime.InteropServices;
#endif

namespace NLayer
{
    public class MpegFrameDecoder
    {
        Decoder.LayerIDecoder _layerIDecoder;
        Decoder.LayerIIDecoder _layerIIDecoder;
        Decoder.LayerIIIDecoder _layerIIIDecoder;

        float[] _eqFactors;

        // channel buffers for getting data out of the decoders...
        // we do it this way so the stereo interleaving code is in one place: DecodeFrameImpl(...)
        // if we ever add support for multi-channel, we'll have to add a pass after the initial
        //  stereo decode (since multi-channel basically uses the stereo channels as a reference)
        float[] _ch0, _ch1;

        public MpegFrameDecoder()
        {
            _ch0 = new float[1152];
            _ch1 = new float[1152];
        }

        /// <summary>
        /// Set the equalizer.
        /// </summary>
        /// <param name="eq">The equalizer, represented by an array of 32 adjustments in dB.</param>
        public void SetEQ(float[] eq)
        {
            if (eq != null)
            {
                var factors = new float[32];
                for (int i = 0; i < eq.Length; i++)
                {
                    // convert from dB -> scaling
                    factors[i] = (float)Math.Pow(2, eq[i] / 6);
                }
                _eqFactors = factors;
            }
            else
            {
                _eqFactors = null;
            }
        }

        /// <summary>
        /// Stereo mode used in decoding.
        /// </summary>
        public StereoMode StereoMode { get; set; }

        /// <summary>
        /// Decode the Mpeg frame into provided buffer. Do exactly the same as <see cref="DecodeFrame(IMpegFrame, float[], int)"/>
        /// except that the data is written in type as byte array, while still representing single-precision float (in local endian).
        /// </summary>
        /// <param name="frame">The Mpeg frame to be decoded.</param>
        /// <param name="dest">Destination buffer. Decoded PCM (single-precision floating point array) will be written into it.</param>
        /// <param name="destOffset">Writing offset on the destination buffer.</param>
        /// <returns></returns>
        public int DecodeFrame(IMpegFrame frame, byte[] dest, int destOffset)
        {
            if (frame == null) throw new ArgumentNullException("frame");
            if (dest == null) throw new ArgumentNullException("dest");
            if (destOffset % 4 != 0) throw new ArgumentException("Must be an even multiple of 4", "destOffset");

            var bufferAvailable = (dest.Length - destOffset) / 4;
            if (bufferAvailable < RequiredSampleCount(frame))
            {
                throw new ArgumentException("Buffer not large enough!  Must be big enough to hold the frame's entire output.  This is up to 9,216 bytes.", "dest");
            }

            return DecodeFrameImpl(frame, dest, destOffset / 4) * 4;
        }

        /// <summary>
        /// Decode the Mpeg frame into provided buffer.
        /// Result varies with different <see cref="StereoMode"/>:
        /// <list type="bullet">
        /// <item>
        /// <description>For <see cref="NLayer.StereoMode.Both"/>, sample data on both two channels will occur in turn (left first).</description>
        /// </item>
        /// <item>
        /// <description>For <see cref="NLayer.StereoMode.LeftOnly"/> and <see cref="NLayer.StereoMode.RightOnly"/>, only data on
        /// specified channel will occur.</description>
        /// </item>
        /// <item>
        /// <description>For <see cref="NLayer.StereoMode.DownmixToMono"/>, two channels will be down-mixed into single channel.</description>
        /// </item>
        /// </list>
        /// </summary>
        /// <param name="frame">The Mpeg frame to be decoded.</param>
        /// <param name="dest">Destination buffer. Decoded PCM (single-precision floating point array) will be written into it.</param>
        /// <param name="destOffset">Writing offset on the destination buffer.</param>
        /// <returns></returns>
        public int DecodeFrame(IMpegFrame frame, float[] dest, int destOffset)
        {
            if (frame == null) throw new ArgumentNullException("frame");
            if (dest == null) throw new ArgumentNullException("dest");

            if (dest.Length - destOffset < RequiredSampleCount(frame))
            {
                throw new ArgumentException("Buffer not large enough!  Must be big enough to hold the frame's entire output.  This is up to 2,304 elements.", "dest");
            }

            return DecodeFrameImpl(frame, dest, destOffset);
        }

        // Decodes the frame into _ch0/_ch1 and returns the per-channel sample count
        // (0 if the frame's layer isn't supported). Shared by every output path so
        // they differ only in how the channel buffers are copied out.
        int DecodeToChannels(IMpegFrame frame)
        {
            frame.Reset();

            Decoder.LayerDecoderBase curDecoder = null;
            switch (frame.Layer)
            {
                case MpegLayer.LayerI:
                    if (_layerIDecoder == null)
                    {
                        _layerIDecoder = new Decoder.LayerIDecoder();
                    }
                    curDecoder = _layerIDecoder;
                    break;
                case MpegLayer.LayerII:
                    if (_layerIIDecoder == null)
                    {
                        _layerIIDecoder = new Decoder.LayerIIDecoder();
                    }
                    curDecoder = _layerIIDecoder;
                    break;
                case MpegLayer.LayerIII:
                    if (_layerIIIDecoder == null)
                    {
                        _layerIIIDecoder = new Decoder.LayerIIIDecoder();
                    }
                    curDecoder = _layerIIIDecoder;
                    break;
            }

            if (curDecoder == null) return 0;

            curDecoder.SetEQ(_eqFactors);
            curDecoder.StereoMode = StereoMode;

            return curDecoder.DecodeFrame(frame, _ch0, _ch1);
        }

        // True when the decoded output is a single channel: either the source is mono,
        // or the caller asked for one channel (LeftOnly / RightOnly / DownmixToMono).
        // In every one of those cases the layer decoder has already placed the single
        // channel of output in _ch0.
        bool IsSingleChannel(IMpegFrame frame)
            => frame.ChannelMode == MpegChannelMode.Mono || StereoMode != StereoMode.Both;

        int DecodeFrameImpl(IMpegFrame frame, Array dest, int destOffset)
        {
            var cnt = DecodeToChannels(frame);
            if (cnt > 0)
            {
                if (IsSingleChannel(frame))
                {
                    // Emit just that one channel rather than interleaving the (stale) _ch1.
                    Buffer.BlockCopy(_ch0, 0, dest, destOffset * sizeof(float), cnt * sizeof(float));
                }
                else if (dest is float[] floatDest)
                {
                    // Full stereo into a float[] - the dominant path (MpegFile's internal
                    // buffer and the DecodeFrame(float[]) overload). Interleave by direct
                    // element assignment instead of a four-byte Buffer.BlockCopy per sample
                    // (which cost ~2,300 method calls per frame).
                    for (int i = 0; i < cnt; i++)
                    {
                        floatDest[destOffset++] = _ch0[i];
                        floatDest[destOffset++] = _ch1[i];
                    }
                    cnt *= 2;
                }
                else
                {
                    // Full stereo into a byte[] (the DecodeFrame(byte[]) overload): we don't
                    // have a float view of dest, so copy each sample's raw bytes.
                    for (int i = 0; i < cnt; i++)
                    {
                        Buffer.BlockCopy(_ch0, i * sizeof(float), dest, destOffset * sizeof(float), sizeof(float));
                        ++destOffset;
                        Buffer.BlockCopy(_ch1, i * sizeof(float), dest, destOffset * sizeof(float), sizeof(float));
                        ++destOffset;
                    }
                    cnt *= 2;
                }

                return cnt;
            }

            return 0;
        }

#if NET8_0_OR_GREATER
        /// <summary>
        /// Decode the Mpeg frame into the provided span. Does exactly the same as
        /// <see cref="DecodeFrame(IMpegFrame, Span{float})"/> except that the data is written as
        /// bytes, while still representing single-precision float (in local endian).
        /// </summary>
        /// <param name="frame">The Mpeg frame to be decoded.</param>
        /// <param name="dest">Destination span. Decoded PCM (single-precision floating point) will be written into it.</param>
        /// <returns>The number of bytes written.</returns>
        public int DecodeFrame(IMpegFrame frame, Span<byte> dest)
        {
            if (frame == null) throw new ArgumentNullException(nameof(frame));

            if (dest.Length / sizeof(float) < RequiredSampleCount(frame))
            {
                throw new ArgumentException("Buffer not large enough!  Must be big enough to hold the frame's entire output.  This is up to 9,216 bytes.", nameof(dest));
            }

            return DecodeFrameImpl(frame, MemoryMarshal.Cast<byte, float>(dest)) * sizeof(float);
        }

        /// <summary>
        /// Decode the Mpeg frame into the provided span.
        /// Result varies with <see cref="StereoMode"/> exactly as for
        /// <see cref="DecodeFrame(IMpegFrame, float[], int)"/>.
        /// </summary>
        /// <param name="frame">The Mpeg frame to be decoded.</param>
        /// <param name="dest">Destination span. Decoded PCM (single-precision floating point) will be written into it.</param>
        /// <returns>The number of samples written.</returns>
        public int DecodeFrame(IMpegFrame frame, Span<float> dest)
        {
            if (frame == null) throw new ArgumentNullException(nameof(frame));

            if (dest.Length < RequiredSampleCount(frame))
            {
                throw new ArgumentException("Buffer not large enough!  Must be big enough to hold the frame's entire output.  This is up to 2,304 elements.", nameof(dest));
            }

            return DecodeFrameImpl(frame, dest);
        }

        int DecodeFrameImpl(IMpegFrame frame, Span<float> dest)
        {
            var cnt = DecodeToChannels(frame);
            if (cnt == 0) return 0;

            if (IsSingleChannel(frame))
            {
                // Emit just that one channel rather than interleaving the (stale) _ch1.
                _ch0.AsSpan(0, cnt).CopyTo(dest);
                return cnt;
            }

            for (int i = 0, j = 0; i < cnt; i++)
            {
                dest[j++] = _ch0[i];
                dest[j++] = _ch1[i];
            }
            return cnt * 2;
        }
#endif

        // The worst-case output of a frame, in samples. Deliberately ignores the
        // single-channel StereoModes: a stereo frame decoded to mono needs only half
        // this, but callers sizing a buffer from the frame header alone shouldn't have
        // to know that.
        static int RequiredSampleCount(IMpegFrame frame)
            => (frame.ChannelMode == MpegChannelMode.Mono ? 1 : 2) * frame.SampleCount;

        /// <summary>
        /// Reset the decoder.
        /// </summary>
        public void Reset()
        {
            // the synthesis filters need to be cleared
            if (_layerIDecoder != null)
            {
                _layerIDecoder.ResetForSeek();
            }
            if (_layerIIDecoder != null)
            {
                _layerIIDecoder.ResetForSeek();
            }
            if (_layerIIIDecoder != null)
            {
                _layerIIIDecoder.ResetForSeek();
            }
        }
    }
}
