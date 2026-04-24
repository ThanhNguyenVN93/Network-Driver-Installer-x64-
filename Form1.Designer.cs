namespace frmnetx64
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

        private void InitializeComponent()
        {
            this.components = new System.ComponentModel.Container();
            System.ComponentModel.ComponentResourceManager resources = new System.ComponentModel.ComponentResourceManager(typeof(Form1));
            this.timer1 = new System.Windows.Forms.Timer(this.components);
            this.groupBox1 = new System.Windows.Forms.GroupBox();
            this.lblos = new System.Windows.Forms.Label();
            this.groupBox2 = new System.Windows.Forms.GroupBox();
            this.groupBox4 = new System.Windows.Forms.GroupBox();
            this.btnlan = new System.Windows.Forms.Button();
            this.btnwifi = new System.Windows.Forms.Button();
            this.lblnettus = new System.Windows.Forms.Label();
            this.groupBox5 = new System.Windows.Forms.GroupBox();
            this.pbStatus = new System.Windows.Forms.ProgressBar();
            this.rtbLog = new System.Windows.Forms.RichTextBox();
            this.groupBox3 = new System.Windows.Forms.GroupBox();
            this.llbdonate = new System.Windows.Forms.LinkLabel();
            this.btnDevMgr = new System.Windows.Forms.Button();
            this.llbthanks = new System.Windows.Forms.LinkLabel();
            this.groupBox1.SuspendLayout();
            this.groupBox2.SuspendLayout();
            this.groupBox4.SuspendLayout();
            this.groupBox5.SuspendLayout();
            this.groupBox3.SuspendLayout();
            this.SuspendLayout();
            // 
            // timer1
            // 
            this.timer1.Enabled = true;
            this.timer1.Interval = 1000;
            this.timer1.Tick += new System.EventHandler(this.timer1_Tick);
            // 
            // groupBox1
            // 
            this.groupBox1.Controls.Add(this.lblos);
            this.groupBox1.Location = new System.Drawing.Point(1, 2);
            this.groupBox1.Name = "groupBox1";
            this.groupBox1.Size = new System.Drawing.Size(484, 69);
            this.groupBox1.TabIndex = 0;
            this.groupBox1.TabStop = false;
            this.groupBox1.Text = "System Information";
            // 
            // lblos
            // 
            this.lblos.Font = new System.Drawing.Font("Segoe UI", 11.25F, System.Drawing.FontStyle.Bold);
            this.lblos.Location = new System.Drawing.Point(6, 16);
            this.lblos.Name = "lblos";
            this.lblos.Size = new System.Drawing.Size(472, 42);
            this.lblos.TabIndex = 0;
            this.lblos.Text = "Detecting OS...";
            this.lblos.TextAlign = System.Drawing.ContentAlignment.MiddleCenter;
            // 
            // groupBox2
            // 
            this.groupBox2.Controls.Add(this.groupBox4);
            this.groupBox2.Controls.Add(this.groupBox5);
            this.groupBox2.Location = new System.Drawing.Point(1, 75);
            this.groupBox2.Name = "groupBox2";
            this.groupBox2.Size = new System.Drawing.Size(484, 270);
            this.groupBox2.TabIndex = 1;
            this.groupBox2.TabStop = false;
            this.groupBox2.Text = "Driver Installation";
            // 
            // groupBox4
            // 
            this.groupBox4.Controls.Add(this.btnlan);
            this.groupBox4.Controls.Add(this.btnwifi);
            this.groupBox4.Controls.Add(this.lblnettus);
            this.groupBox4.Location = new System.Drawing.Point(10, 19);
            this.groupBox4.Name = "groupBox4";
            this.groupBox4.Size = new System.Drawing.Size(460, 100);
            this.groupBox4.TabIndex = 0;
            this.groupBox4.TabStop = false;
            this.groupBox4.Text = "Network Drivers";
            // 
            // btnlan
            // 
            this.btnlan.Font = new System.Drawing.Font("Segoe UI", 9.75F, System.Drawing.FontStyle.Bold);
            this.btnlan.Location = new System.Drawing.Point(6, 18);
            this.btnlan.Name = "btnlan";
            this.btnlan.Size = new System.Drawing.Size(90, 72);
            this.btnlan.TabIndex = 0;
            this.btnlan.Text = "Install\r\nLAN";
            this.btnlan.UseVisualStyleBackColor = true;
            this.btnlan.Click += new System.EventHandler(this.btnlan_Click);
            // 
            // btnwifi
            // 
            this.btnwifi.Font = new System.Drawing.Font("Segoe UI", 9.75F, System.Drawing.FontStyle.Bold);
            this.btnwifi.Location = new System.Drawing.Point(364, 18);
            this.btnwifi.Name = "btnwifi";
            this.btnwifi.Size = new System.Drawing.Size(90, 72);
            this.btnwifi.TabIndex = 1;
            this.btnwifi.Text = "Install\r\nWi-Fi";
            this.btnwifi.UseVisualStyleBackColor = true;
            this.btnwifi.Click += new System.EventHandler(this.btnwifi_Click);
            // 
            // lblnettus
            // 
            this.lblnettus.Font = new System.Drawing.Font("Segoe UI", 9F);
            this.lblnettus.Location = new System.Drawing.Point(102, 18);
            this.lblnettus.Name = "lblnettus";
            this.lblnettus.Size = new System.Drawing.Size(256, 72);
            this.lblnettus.TabIndex = 2;
            this.lblnettus.Text = "Initializing...";
            this.lblnettus.TextAlign = System.Drawing.ContentAlignment.MiddleCenter;
            // 
            // groupBox5
            // 
            this.groupBox5.Controls.Add(this.pbStatus);
            this.groupBox5.Controls.Add(this.rtbLog);
            this.groupBox5.Location = new System.Drawing.Point(10, 125);
            this.groupBox5.Name = "groupBox5";
            this.groupBox5.Size = new System.Drawing.Size(460, 137);
            this.groupBox5.TabIndex = 3;
            this.groupBox5.TabStop = false;
            this.groupBox5.Text = "Progress & Log";
            // 
            // pbStatus
            // 
            this.pbStatus.Location = new System.Drawing.Point(6, 19);
            this.pbStatus.Name = "pbStatus";
            this.pbStatus.Size = new System.Drawing.Size(448, 20);
            this.pbStatus.Step = 1;
            this.pbStatus.TabIndex = 0;
            // 
            // rtbLog
            // 
            this.rtbLog.BackColor = System.Drawing.Color.Black;
            this.rtbLog.BorderStyle = System.Windows.Forms.BorderStyle.None;
            this.rtbLog.Font = new System.Drawing.Font("Consolas", 9F);
            this.rtbLog.ForeColor = System.Drawing.Color.Lime;
            this.rtbLog.Location = new System.Drawing.Point(6, 45);
            this.rtbLog.Name = "rtbLog";
            this.rtbLog.ReadOnly = true;
            this.rtbLog.ScrollBars = System.Windows.Forms.RichTextBoxScrollBars.Vertical;
            this.rtbLog.Size = new System.Drawing.Size(448, 86);
            this.rtbLog.TabIndex = 1;
            this.rtbLog.Text = "";
            // 
            // groupBox3
            // 
            this.groupBox3.Controls.Add(this.llbdonate);
            this.groupBox3.Controls.Add(this.btnDevMgr);
            this.groupBox3.Controls.Add(this.llbthanks);
            this.groupBox3.Location = new System.Drawing.Point(1, 349);
            this.groupBox3.Name = "groupBox3";
            this.groupBox3.Size = new System.Drawing.Size(484, 98);
            this.groupBox3.TabIndex = 2;
            this.groupBox3.TabStop = false;
            this.groupBox3.Text = "Tools";
            // 
            // llbdonate
            // 
            this.llbdonate.AutoSize = true;
            this.llbdonate.Location = new System.Drawing.Point(426, 67);
            this.llbdonate.Name = "llbdonate";
            this.llbdonate.Size = new System.Drawing.Size(45, 13);
            this.llbdonate.TabIndex = 2;
            this.llbdonate.TabStop = true;
            this.llbdonate.Text = "Donate";
            this.llbdonate.LinkClicked += new System.Windows.Forms.LinkLabelLinkClickedEventHandler(this.llbdonate_LinkClicked);
            // 
            // btnDevMgr
            // 
            this.btnDevMgr.Font = new System.Drawing.Font("Segoe UI", 9F, System.Drawing.FontStyle.Bold);
            this.btnDevMgr.Location = new System.Drawing.Point(6, 19);
            this.btnDevMgr.Name = "btnDevMgr";
            this.btnDevMgr.Size = new System.Drawing.Size(155, 32);
            this.btnDevMgr.TabIndex = 1;
            this.btnDevMgr.Text = "Open Device Manager";
            this.btnDevMgr.UseVisualStyleBackColor = true;
            this.btnDevMgr.Click += new System.EventHandler(this.btnDevMgr_Click);
            // 
            // llbthanks
            // 
            this.llbthanks.AutoSize = true;
            this.llbthanks.Font = new System.Drawing.Font("Segoe UI", 9F, System.Drawing.FontStyle.Bold);
            this.llbthanks.Location = new System.Drawing.Point(6, 65);
            this.llbthanks.Name = "llbthanks";
            this.llbthanks.Size = new System.Drawing.Size(119, 15);
            this.llbthanks.TabIndex = 0;
            this.llbthanks.TabStop = true;
            this.llbthanks.Text = "Thanks For Products";
            this.llbthanks.LinkClicked += new System.Windows.Forms.LinkLabelLinkClickedEventHandler(this.llbthanks_LinkClicked);
            // 
            // Form1
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.ClientSize = new System.Drawing.Size(484, 453);
            this.Controls.Add(this.groupBox1);
            this.Controls.Add(this.groupBox2);
            this.Controls.Add(this.groupBox3);
            this.Font = new System.Drawing.Font("Segoe UI", 8.25F);
            this.FormBorderStyle = System.Windows.Forms.FormBorderStyle.FixedSingle;
            this.Icon = ((System.Drawing.Icon)(resources.GetObject("$this.Icon")));
            this.MaximizeBox = false;
            this.MaximumSize = new System.Drawing.Size(500, 492);
            this.MinimizeBox = false;
            this.MinimumSize = new System.Drawing.Size(500, 492);
            this.Name = "Form1";
            this.StartPosition = System.Windows.Forms.FormStartPosition.CenterScreen;
            this.Text = "Network Driver Installer (x64)";
            this.Load += new System.EventHandler(this.Form1_Load);
            this.groupBox1.ResumeLayout(false);
            this.groupBox2.ResumeLayout(false);
            this.groupBox4.ResumeLayout(false);
            this.groupBox5.ResumeLayout(false);
            this.groupBox3.ResumeLayout(false);
            this.groupBox3.PerformLayout();
            this.ResumeLayout(false);

        }

        #endregion

        public System.Windows.Forms.Timer timer1;
        private System.Windows.Forms.GroupBox groupBox1;
        private System.Windows.Forms.Label lblos;
        private System.Windows.Forms.GroupBox groupBox2;
        private System.Windows.Forms.GroupBox groupBox4;
        private System.Windows.Forms.Button btnlan;
        private System.Windows.Forms.Button btnwifi;
        private System.Windows.Forms.Label lblnettus;
        private System.Windows.Forms.GroupBox groupBox5;
        private System.Windows.Forms.ProgressBar pbStatus;
        private System.Windows.Forms.RichTextBox rtbLog;
        private System.Windows.Forms.GroupBox groupBox3;
        private System.Windows.Forms.Button btnDevMgr;
        private System.Windows.Forms.LinkLabel llbthanks;
        private System.Windows.Forms.LinkLabel llbdonate;
    }
}

