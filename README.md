# NLayer

NLayer is a fully managed MP3 to WAV decoder. The code was originally based 
on [JavaLayer](http://www.javazoom.net/javalayer/javalayer.html) (v1.0.1), 
which has been ported to C#.

Was previously hosted at [nlayer.codeplex.com](http://nlayer.codeplex.com/). 
Please see the history there for full details of contributors.

## Usage

To use NLayer for decoding MP3, first reference NLayer.

```cs
using NLayer;
```

Then create an `MpegFile`, pass a file name or a stream to the constructor, and use `ReadSamples` for decoding the content:

```cs
// samples per second times channel count
const int samplesCount = 44100;
var fileName = "myMp3File.mp3";
var mpegFile = new MpegFile(filename);
float[] samples = new float[samplesCount];
int readCount = mpegFile.ReadSamples(samples, 0, samplesCount);
```

More information could be found in code documents.

## Use with NAudio

NLayer is capable of using in conjunction with [NAudio](https://github.com/naudio/NAudio/)
for file conversion and real-time playback.

You need to reference NAudio, NLayer and NLayer.NAudioSupport first.

```cs
using NAudio.Wave;
using NLayer.NAudioSupport;
```

Then create an `Mp3FileReader`, passing in a `FrameDecompressorBuilder` that uses the `Mp3FrameDecompressor` from NLayer.NAudioSupport:

```cs
var fileName = "myMp3File.mp3";
var builder = new Mp3FileReader.FrameDecompressorBuilder(wf => new Mp3FrameDecompressor(wf));
var reader = new Mp3FileReaderBase(fileName, builder);
// play or process the file, e.g.:
waveOut.Init(reader);
waveOut.Play();
```
