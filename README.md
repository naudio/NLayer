NLayer is a fully managed MP3 to WAV decoder. The code was originally based 
on [JavaLayer](http://www.javazoom.net/javalayer/javalayer.html) (v1.0.1), 
which has been ported to C#. Use in conjunction with [NAudio](https://github.com/naudio/NAudio/)
 for file conversion and real-time playback.

Was previously hosted at [nlayer.codeplex.com](http://nlayer.codeplex.com/). 
Please see the history there for full details of contributors.

How to use it with NAudio:

You need to reference NAudio, NLayer and NLayer.NAudioSupport

```cs
using NAudio.Wave;
using NLayer.NAudioSupport;
```

Then create an `Mp3FileReader`, passing in a `FrameDecompressorBuilder` that uses the `Mp3FrameDecompressor` from NLayer.NAudioSupport

```cs
var fileName = "myMp3File.mp3";
var builder = new Mp3FileReader.FrameDecompressorBuilder(wf => new Mp3FrameDecompressor(wf));
var reader = new Mp3FileReader(fileName, builder);
// play or process the file, e.g.:
waveOut.Init(reader);
waveOut.Play();

```