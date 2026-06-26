namespace NLayer.NAudioSupport
{
    public class Mp3FrameDecompressor : NAudio.Wave.IMp3FrameDecompressor
    {
        MpegFrameDecoder _decoder;
        Mp3FrameWrapper _frame;

        public Mp3FrameDecompressor(NAudio.Wave.WaveFormat waveFormat)
            : this(waveFormat, StereoMode.Both)
        {
        }

        /// <param name="waveFormat">The source wave format (assumed to be calculated from the first frame already).</param>
        /// <param name="stereoMode">
        /// How stereo content is decoded. The single-channel modes
        /// (<see cref="NLayer.StereoMode.LeftOnly"/>, <see cref="NLayer.StereoMode.RightOnly"/>
        /// and <see cref="NLayer.StereoMode.DownmixToMono"/>) produce a mono
        /// <see cref="OutputFormat"/>, so the mode must be supplied here (where the output
        /// format is decided) rather than set afterwards.
        /// </param>
        public Mp3FrameDecompressor(NAudio.Wave.WaveFormat waveFormat, StereoMode stereoMode)
        {
            _decoder = new MpegFrameDecoder { StereoMode = stereoMode };
            _frame = new Mp3FrameWrapper();

            var channels = stereoMode == StereoMode.Both ? waveFormat.Channels : 1;
            OutputFormat = NAudio.Wave.WaveFormat.CreateIeeeFloatWaveFormat(waveFormat.SampleRate, channels);
        }

        public int DecompressFrame(NAudio.Wave.Mp3Frame frame, byte[] dest, int destOffset)
        {
            _frame.WrappedFrame = frame;
            return _decoder.DecodeFrame(_frame, dest, destOffset);
        }

        public NAudio.Wave.WaveFormat OutputFormat { get; private set; }

        public void SetEQ(float[] eq)
        {
            _decoder.SetEQ(eq);
        }

        public StereoMode StereoMode
        {
            get { return _decoder.StereoMode; }
        }

        public void Reset()
        {
            _decoder.Reset();
        }

        public void Dispose()
        {
            // no-op, since we don't have anything to do here...
        }
    }
}
