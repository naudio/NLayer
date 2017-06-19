using System;

namespace NLayer
{
    public class MpegFile : IDisposable
    {
        System.IO.Stream _stream;
        bool _closeStream, _eofFound;

        Decoder.MpegStreamReader _reader;
        MpegFrameDecoder _decoder;

        object _seekLock = new object();
        long _position;

        public MpegFile(string fileName)
        {
            Init(System.IO.File.Open(fileName, System.IO.FileMode.Open, System.IO.FileAccess.Read, System.IO.FileShare.Read), true);
        }

        public MpegFile(System.IO.Stream stream)
        {
            Init(stream, false);
        }

        void Init(System.IO.Stream stream, bool closeStream)
        {
            _stream = stream;
            _closeStream = closeStream;

            _reader = new Decoder.MpegStreamReader(_stream);

            _decoder = new MpegFrameDecoder();
        }

        public void Dispose()
        {
            if (_closeStream)
            {
                _stream.Dispose();
                _closeStream = false;
            }
        }

        public int SampleRate { get { return _reader.SampleRate; } }
        public int Channels { get { return _reader.Channels; } }

        public bool CanSeek { get { return _reader.CanSeek; } }

        public long Length { get { return _reader.SampleCount * _reader.Channels * sizeof(float); } }

        public TimeSpan Duration
        {
            get
            {
                var len = _reader.SampleCount;
                if (len == -1) return TimeSpan.Zero;
                return TimeSpan.FromSeconds((double)len / _reader.SampleRate);
            }
        }

        public long Position
        {
            get { return _position; }
            set
            {
                if (!_reader.CanSeek) throw new InvalidOperationException("Cannot Seek!");
                if (value < 0L) throw new ArgumentOutOfRangeException("value");

                // we're thinking in 4-byte samples, pcmStep interleaved...  adjust accordingly
                var samples = value / sizeof(float) / _reader.Channels;
                var sampleOffset = 0;

                // seek to the frame preceding the one we want (unless we're seeking to the first frame)
                if (samples >= _reader.FirstFrameSampleCount)
                {
                    sampleOffset = _reader.FirstFrameSampleCount;
                    samples -= sampleOffset;
                }

                lock (_seekLock)
                {
                    // seek the stream
                    var newPos = _reader.SeekTo(samples);
                    if (newPos == -1) throw new ArgumentOutOfRangeException("value");

                    _decoder.Reset();

                    // if we have a sample offset, decode the next frame
                    if (sampleOffset != 0)
                    {
                        _decoder.DecodeFrame(_reader.NextFrame(), _readBuf, 0); // throw away a frame (but allow the decoder to resync)
                        newPos += sampleOffset;
                    }

                    _position = newPos * sizeof(float) * _reader.Channels;
                    _eofFound = false;

                    // clear the decoder & buffer
                    _readBufOfs = _readBufLen = 0;
                }
            }
        }

        public TimeSpan Time
        {
            get { return TimeSpan.FromSeconds((double)_position / sizeof(float) / _reader.Channels / _reader.SampleRate); }
            set { Position = (long)(value.TotalSeconds * _reader.SampleRate * _reader.Channels * sizeof(float)); }
        }

        public void SetEQ(float[] eq)
        {
            _decoder.SetEQ(eq);
        }

        public StereoMode StereoMode
        {
            get { return _decoder.StereoMode; }
            set { _decoder.StereoMode = value; }
        }

        public int ReadSamples(byte[] buffer, int index, int count)
        {
            if (index < 0 || index + count > buffer.Length) throw new ArgumentOutOfRangeException("index");

            // make sure we're asking for an even number of samples
            count -= (count % sizeof(float));

            return ReadSamplesImpl(buffer, index, count);
        }

        public int ReadSamples(float[] buffer, int index, int count)
        {
            if (index < 0 || index + count > buffer.Length) throw new ArgumentOutOfRangeException("index");

            // ReadSampleImpl "thinks" in bytes, so adjust accordingly
            return ReadSamplesImpl(buffer, index * sizeof(float), count * sizeof(float)) / sizeof(float);
        }

        float[] _readBuf = new float[1152 * 2];
        int _readBufLen, _readBufOfs;

        int ReadSamplesImpl(Array buffer, int index, int count)
        {
            var cnt = 0;

            // lock around the entire read operation so seeking doesn't bork our buffers as we decode
            lock (_seekLock)
            {
                while (count > 0)
                {
                    if (_readBufLen > _readBufOfs)
                    {
                        // we have bytes in the buffer, so copy them first
                        var temp = _readBufLen - _readBufOfs;
                        if (temp > count) temp = count;
                        Buffer.BlockCopy(_readBuf, _readBufOfs, buffer, index, temp);

                        // now update our counters...
                        cnt += temp;

                        count -= temp;
                        index += temp;

                        _position += temp;
                        _readBufOfs += temp;

                        // finally, mark the buffer as empty if we've read everything in it
                        if (_readBufOfs == _readBufLen)
                        {
                            _readBufLen = 0;
                        }
                    }

                    // if the buffer is empty, try to fill it
                    //  NB: If we've already satisfied the read request, we'll still try to fill the buffer.
                    //      This ensures there's data in the pipe on the next call
                    if (_readBufLen == 0)
                    {
                        if (_eofFound)
                        {
                            break;
                        }

                        // decode the next frame (update _readBufXXX)
                        var frame = _reader.NextFrame();
                        if (frame == null)
                        {
                            _eofFound = true;
                            break;
                        }

                        try
                        {
                            _readBufLen = _decoder.DecodeFrame(frame, _readBuf, 0) * sizeof(float);
                            _readBufOfs = 0;
                        }
                        catch (System.IO.InvalidDataException)
                        {
                            // bad frame...  try again...
                            _decoder.Reset();
                            _readBufOfs = _readBufLen = 0;
                            continue;
                        }
                        catch (System.IO.EndOfStreamException)
                        {
                            // no more frames
                            _eofFound = true;
                            break;
                        }
                        finally
                        {
                            frame.ClearBuffer();
                        }
                    }
                }
            }
            return cnt;
        }
    }
}
