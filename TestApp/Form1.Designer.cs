namespace TestApp
{
    partial class Form1
    {
        /// <summary>
        /// Required designer variable.
        /// </summary>
        private System.ComponentModel.IContainer components = null;

        /// <summary>
        /// Clean up any resources being used.
        /// </summary>
        /// <param name="disposing">true if managed resources should be disposed; otherwise, false.</param>
        protected override void Dispose(bool disposing)
        {
            if (disposing && (components != null))
            {
                components.Dispose();
            }
            base.Dispose(disposing);
        }

        #region Windows Form Designer generated code

        /// <summary>
        /// Required method for Designer support - do not modify
        /// the contents of this method with the code editor.
        /// </summary>
        private void InitializeComponent()
        {
            buttonConvert = new System.Windows.Forms.Button();
            buttonPlay = new System.Windows.Forms.Button();
            labelConvertedFile = new System.Windows.Forms.Label();
            checkBoxDeleteOnExit = new System.Windows.Forms.CheckBox();
            SuspendLayout();
            // 
            // buttonConvert
            // 
            buttonConvert.Location = new System.Drawing.Point(16, 18);
            buttonConvert.Margin = new System.Windows.Forms.Padding(4, 5, 4, 5);
            buttonConvert.Name = "buttonConvert";
            buttonConvert.Size = new System.Drawing.Size(120, 35);
            buttonConvert.TabIndex = 0;
            buttonConvert.Text = "&Convert...";
            buttonConvert.UseVisualStyleBackColor = true;
            buttonConvert.Click += buttonConvert_Click;
            // 
            // buttonPlay
            // 
            buttonPlay.Enabled = false;
            buttonPlay.Location = new System.Drawing.Point(144, 18);
            buttonPlay.Margin = new System.Windows.Forms.Padding(4, 5, 4, 5);
            buttonPlay.Name = "buttonPlay";
            buttonPlay.Size = new System.Drawing.Size(120, 35);
            buttonPlay.TabIndex = 1;
            buttonPlay.Text = "&Play";
            buttonPlay.UseVisualStyleBackColor = true;
            buttonPlay.Click += buttonPlay_Click;
            // 
            // labelConvertedFile
            // 
            labelConvertedFile.AutoEllipsis = true;
            labelConvertedFile.Location = new System.Drawing.Point(16, 74);
            labelConvertedFile.Margin = new System.Windows.Forms.Padding(4, 0, 4, 0);
            labelConvertedFile.Name = "labelConvertedFile";
            labelConvertedFile.Size = new System.Drawing.Size(357, 108);
            labelConvertedFile.TabIndex = 2;
            labelConvertedFile.Text = "No file converted yet";
            // 
            // checkBoxDeleteOnExit
            // 
            checkBoxDeleteOnExit.AutoSize = true;
            checkBoxDeleteOnExit.Checked = true;
            checkBoxDeleteOnExit.CheckState = System.Windows.Forms.CheckState.Checked;
            checkBoxDeleteOnExit.Location = new System.Drawing.Point(16, 363);
            checkBoxDeleteOnExit.Margin = new System.Windows.Forms.Padding(4, 5, 4, 5);
            checkBoxDeleteOnExit.Name = "checkBoxDeleteOnExit";
            checkBoxDeleteOnExit.Size = new System.Drawing.Size(225, 24);
            checkBoxDeleteOnExit.TabIndex = 3;
            checkBoxDeleteOnExit.Text = "&Delete converted files on exit";
            checkBoxDeleteOnExit.UseVisualStyleBackColor = true;
            // 
            // Form1
            // 
            AutoScaleDimensions = new System.Drawing.SizeF(8F, 20F);
            AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            ClientSize = new System.Drawing.Size(389, 409);
            Controls.Add(checkBoxDeleteOnExit);
            Controls.Add(labelConvertedFile);
            Controls.Add(buttonPlay);
            Controls.Add(buttonConvert);
            Margin = new System.Windows.Forms.Padding(4, 5, 4, 5);
            Name = "Form1";
            Text = "NLayer Test App";
            ResumeLayout(false);
            PerformLayout();

        }

        #endregion

        private System.Windows.Forms.Button buttonConvert;
        private System.Windows.Forms.Button buttonPlay;
        private System.Windows.Forms.Label labelConvertedFile;
        private System.Windows.Forms.CheckBox checkBoxDeleteOnExit;
    }
}
