using System;
using System.Windows.Forms;
using NAudio.Wave;
using System.IO;
using System.Diagnostics;
using NLayer.NAudioSupport;

namespace TestApp
{
    public partial class Form1 : Form
    {
        public Form1()
        {
            InitializeComponent();
        }

        private void buttonOpen_Click(object sender, EventArgs e)
        {
            var ofd = new OpenFileDialog();
            ofd.Filter = "MP3 Files|*.mp3";
            if (ofd.ShowDialog() == DialogResult.OK)
            {
                using (var stream = new Mp3FileReader(ofd.FileName, waveFormat => new Mp3FrameDecompressor(waveFormat)))
                {
                    string fileName = Path.GetFileNameWithoutExtension(ofd.FileName) + ".wav";
                    fileName = Path.Combine(Path.GetTempPath(), fileName);
                    WaveFileWriter.CreateWaveFile(fileName, stream);
                    var p = new Process
                    {
                        StartInfo = new ProcessStartInfo(fileName)
                        {
                            UseShellExecute = true
                        }
                    };
                    p.Start();
                }
            }
        }
    }
}
