using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Windows.Forms;
using NAudio.Wave;
using System.IO;
using System.Diagnostics;

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
            OpenFileDialog ofd = new OpenFileDialog();
            ofd.Filter = "MP3 Files|*.mp3";
            if (ofd.ShowDialog() == DialogResult.OK)
            {
                using (var stream = new NAudio.Wave.Mp3FileReader(ofd.FileName, new Mp3FileReader.FrameDecompressorBuilder(waveFormat => new NLayer.NAudioSupport.Mp3FrameDecompressor(waveFormat))))
                {
                    string fileName = Path.GetFileNameWithoutExtension(ofd.FileName) + ".wav";
                    fileName = Path.Combine(Path.GetTempPath(), fileName);
                    WaveFileWriter.CreateWaveFile(fileName, stream);
                    Process.Start(fileName);
                }
            }
        }
    }
}
