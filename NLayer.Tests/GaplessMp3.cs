using System;
using System.IO;
using System.Text;

namespace NLayer.Tests
{
    /// <summary>
    /// Extends <see cref="SilentMp3"/> with the gapless-playback metadata that the
    /// decoder uses to trim encoder delay and end padding: either a LAME
    /// <c>Info</c>/<c>Xing</c> header frame, or an <c>iTunSMPB</c> ID3v2 <c>TXXX</c>
    /// frame. Everything is synthesised in memory so no binary fixtures are needed.
    ///
    /// The values produced here were cross-checked against real LAME 3.100 output:
    /// the Xing frame-count field counts the audio frames only (it excludes the
    /// header frame itself), so a stream of N audio frames declares N here.
    /// </summary>
    internal static class GaplessMp3
    {
        // MPEG-1 stereo: the Xing/Info magic sits 36 bytes into the frame
        // (4-byte header + 32-byte side info); the flags follow at +4 and, when
        // the "frames present" bit is set, the frame count at +8.
        private const int XingOffset = 36;

        /// <summary>
        /// A stream of <paramref name="audioFrames"/> silent frames preceded by a
        /// LAME <c>Info</c> header frame declaring the given encoder delay/padding.
        /// </summary>
        public static byte[] WithLameTag(int audioFrames, int delay, int padding)
        {
            return Concat(BuildInfoFrame(audioFrames, delay, padding), SilentMp3.Create(audioFrames));
        }

        /// <summary>
        /// A stream of <paramref name="audioFrames"/> silent frames preceded by an
        /// ID3v2.3 tag carrying an <c>iTunSMPB</c> <c>TXXX</c> frame with the given
        /// encoder delay/padding.
        /// </summary>
        public static byte[] WithITunSmpb(int audioFrames, int delay, int padding)
        {
            // iTunes value layout: " <priming?> <delay> <padding> <sampleCount> ..."
            // all hex; the decoder reads field 1 (delay) and field 2 (padding).
            var value = string.Format(
                " {0:X8} {1:X8} {2:X8} {3:X16}",
                0, delay, padding, (long)audioFrames * SilentMp3.SamplesPerFrame);

            // TXXX content: encoding byte (0 = Latin-1), description, NUL, value.
            using var content = new MemoryStream();
            content.WriteByte(0);
            WriteAscii(content, "iTunSMPB");
            content.WriteByte(0);
            WriteAscii(content, value);
            var contentBytes = content.ToArray();

            using var frame = new MemoryStream();
            WriteAscii(frame, "TXXX");
            WriteUInt32BE(frame, contentBytes.Length); // v2.3 size is a plain 32-bit int
            frame.WriteByte(0); frame.WriteByte(0);     // frame flags
            frame.Write(contentBytes, 0, contentBytes.Length);
            var body = frame.ToArray();

            using var tag = new MemoryStream();
            WriteAscii(tag, "ID3");
            tag.WriteByte(3); tag.WriteByte(0); tag.WriteByte(0); // v2.3.0, no tag flags
            WriteSyncSafe(tag, body.Length);
            tag.Write(body, 0, body.Length);

            return Concat(tag.ToArray(), SilentMp3.Create(audioFrames));
        }

        private static byte[] BuildInfoFrame(int audioFrames, int delay, int padding)
        {
            // Start from a real silent frame so the MPEG header and frame length
            // exactly match SilentMp3, then overlay the Info + LAME tag in the
            // (otherwise zeroed) side-info/main-data region.
            var frame = SilentMp3.Create(1);

            frame[XingOffset + 0] = (byte)'I';
            frame[XingOffset + 1] = (byte)'n';
            frame[XingOffset + 2] = (byte)'f';
            frame[XingOffset + 3] = (byte)'o';

            // Flags: only the "frame count present" bit (0x1).
            var flagsOffset = XingOffset + 4;
            frame[flagsOffset + 3] = 0x01;

            var countOffset = flagsOffset + 4;
            frame[countOffset + 0] = (byte)(audioFrames >> 24);
            frame[countOffset + 1] = (byte)(audioFrames >> 16);
            frame[countOffset + 2] = (byte)(audioFrames >> 8);
            frame[countOffset + 3] = (byte)audioFrames;

            // LAME tag immediately follows the single Xing field.
            var lame = countOffset + 4;
            var id = Encoding.ASCII.GetBytes("LAME3.100"); // 9-byte identifier
            Array.Copy(id, 0, frame, lame, id.Length);
            frame[lame + 9] = 0x00; // info tag revision 0 (high nibble)

            // Encoder delay (high 12 bits) and end padding (low 12 bits) packed
            // across bytes 21..23 of the LAME tag.
            frame[lame + 21] = (byte)(delay >> 4);
            frame[lame + 22] = (byte)(((delay & 0x0F) << 4) | ((padding >> 8) & 0x0F));
            frame[lame + 23] = (byte)(padding & 0xFF);
            return frame;
        }

        private static void WriteAscii(Stream s, string text)
        {
            var bytes = Encoding.ASCII.GetBytes(text);
            s.Write(bytes, 0, bytes.Length);
        }

        private static void WriteUInt32BE(Stream s, int value)
        {
            s.WriteByte((byte)(value >> 24));
            s.WriteByte((byte)(value >> 16));
            s.WriteByte((byte)(value >> 8));
            s.WriteByte((byte)value);
        }

        private static void WriteSyncSafe(Stream s, int value)
        {
            s.WriteByte((byte)((value >> 21) & 0x7F));
            s.WriteByte((byte)((value >> 14) & 0x7F));
            s.WriteByte((byte)((value >> 7) & 0x7F));
            s.WriteByte((byte)(value & 0x7F));
        }

        private static byte[] Concat(byte[] first, byte[] second)
        {
            var result = new byte[first.Length + second.Length];
            Buffer.BlockCopy(first, 0, result, 0, first.Length);
            Buffer.BlockCopy(second, 0, result, first.Length, second.Length);
            return result;
        }
    }
}
