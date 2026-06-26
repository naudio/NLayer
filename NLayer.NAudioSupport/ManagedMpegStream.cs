using System;
using NAudio.Wave;
using System.IO;

namespace NLayer.NAudioSupport
{
    public class ManagedMpegStream : WaveStream, IDisposable
    {
        Stream _source;
        WaveFormat _waveFormat;
        MpegFile _fileDecoder;
        bool _closeOnDispose;

        public ManagedMpegStream(string fileName, StereoMode stereoMode = StereoMode.Both)
            : this(File.OpenRead(fileName), true, stereoMode)
        {

        }

        public ManagedMpegStream(Stream source, StereoMode stereoMode = StereoMode.Both)
            : this(source, false, stereoMode)
        {
        }

        /// <param name="stereoMode">
        /// How stereo content is decoded. The single-channel modes
        /// (<see cref="NLayer.StereoMode.LeftOnly"/>, <see cref="NLayer.StereoMode.RightOnly"/>
        /// and <see cref="NLayer.StereoMode.DownmixToMono"/>) produce a mono
        /// <see cref="WaveFormat"/>, so the mode must be supplied here (where the wave format
        /// is decided) rather than set afterwards.
        /// </param>
        public ManagedMpegStream(Stream source, bool closeOnDispose, StereoMode stereoMode = StereoMode.Both)
        {
            this._source = source;
            this._closeOnDispose = closeOnDispose;
            this._fileDecoder = new MpegFile(this._source) { StereoMode = stereoMode };
            this._waveFormat = WaveFormat.CreateIeeeFloatWaveFormat(this._fileDecoder.SampleRate, this._fileDecoder.Channels);
        }

        public override WaveFormat WaveFormat
        {
            get { return _waveFormat; }
        }

        public void SetEQ(float[] eq)
        {
            _fileDecoder.SetEQ(eq);
        }

        public StereoMode StereoMode
        {
            get { return _fileDecoder.StereoMode; }
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            return this._fileDecoder.ReadSamples(buffer, offset, count);
        }

        protected override void Dispose(bool disposing)
        {
            if (_source != null)
            {
                if (_closeOnDispose)
                {
                    _source.Dispose();
                }
                _source = null;
            }
            base.Dispose(disposing);
        }

        public override long Length
        {
            get { return this._fileDecoder.Length; }
        }

        public override long Position
        {
            get
            {
                return this._fileDecoder.Position;
            }
            set
            {
                this._fileDecoder.Position = value;
            }
        }
    }
}
