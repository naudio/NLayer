using System;
using System.Collections.Generic;
using System.Windows.Forms;
using NAudio.Wave;
using System.IO;
using System.Diagnostics;
using NLayer.NAudioSupport;

namespace TestApp
{
    public partial class Form1 : Form
    {
        private static readonly string OutputFolder = Path.Combine(Path.GetTempPath(), "NLayer");

        private readonly List<string> convertedFiles = new List<string>();
        private string lastConvertedFile;

        public Form1()
        {
            InitializeComponent();
        }

        private void buttonConvert_Click(object sender, EventArgs e)
        {
            var ofd = new OpenFileDialog();
            ofd.Filter = "MP3 Files|*.mp3";
            if (ofd.ShowDialog() == DialogResult.OK)
            {
                Directory.CreateDirectory(OutputFolder);
                string fileName = Path.GetFileNameWithoutExtension(ofd.FileName) + ".wav";
                fileName = Path.Combine(OutputFolder, fileName);
                // Mp3FileReaderBase is the full class, but you have to pass in a decoder
                using (var stream = new Mp3FileReaderBase(ofd.FileName, waveFormat => new Mp3FrameDecompressor(waveFormat)))
                {
                    WaveFileWriter.CreateWaveFile(fileName, stream);
                }
                lastConvertedFile = fileName;
                if (!convertedFiles.Contains(fileName))
                {
                    convertedFiles.Add(fileName);
                }
                labelConvertedFile.Text = "Converted to: " + fileName;
                buttonPlay.Enabled = true;
            }
        }

        private void buttonPlay_Click(object sender, EventArgs e)
        {
            if (lastConvertedFile == null) return;
            var p = new Process
            {
                StartInfo = new ProcessStartInfo(lastConvertedFile)
                {
                    UseShellExecute = true
                }
            };
            p.Start();
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            base.OnFormClosed(e);
            if (checkBoxDeleteOnExit.Checked)
            {
                foreach (var file in convertedFiles)
                {
                    try
                    {
                        File.Delete(file);
                    }
                    catch (IOException)
                    {
                        // most likely still open in the player, so leave it behind
                    }
                    catch (UnauthorizedAccessException)
                    {
                    }
                }
                try
                {
                    // only succeeds if we managed to delete everything we put in there
                    Directory.Delete(OutputFolder);
                }
                catch (IOException)
                {
                }
                catch (UnauthorizedAccessException)
                {
                }
            }
            convertedFiles.Clear();
        }
    }
}
