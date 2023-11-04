﻿using SwitchWinClock.utils;
using System;
using System.Drawing;
using System.Windows.Forms;

namespace SwitchWinClock
{
    public partial class InstanceNameForm : Form
    {
        private bool Drag { get; set; }
        private Point StartPoint { get; set; } = Point.Empty;

        public InstanceNameForm()
        {
            InitializeComponent();
            foreach(TimeZoneSelection zone in SCConfig.GetTimeZones())
                this.TimeZoneComboBox.Items.Add(zone.LocalName);
        }

        public string InstanceName { get; set; }
        public string TimeZone { get; set; }

        private void BtnOK_Click(object sender, EventArgs e)
        {
            if(string.IsNullOrWhiteSpace(this.TxtInstanceName.Text))
            {
                MessageBox.Show("Provide an instance name or Cancel to exit.", "Missing Name", MessageBoxButtons.OK, MessageBoxIcon.Exclamation);
                return;
            }
            DialogResult = DialogResult.OK;
            this.InstanceName = this.TxtInstanceName.Text.Trim();

            TimeZoneSelection tzFound = SCConfig.GetTimeZones().Find(f => f.LocalName == this.TimeZoneComboBox.Text) ?? Global.CurrentTimeZone();

            this.TimeZone = tzFound.Id;
            this.Close();
        }

        private void BtnCancel_Click(object sender, EventArgs e)
        {
            DialogResult = DialogResult.Cancel;
            this.InstanceName = string.Empty;
            this.TimeZone = string.Empty;
            this.Close();
        }

        private void InstanceNameForm_Load(object sender, EventArgs e)
        {
            if (!string.IsNullOrWhiteSpace(this.InstanceName))
                this.TxtInstanceName.Text = this.InstanceName;
            if (!string.IsNullOrWhiteSpace(this.TimeZone))
                this.TimeZoneComboBox.Text = this.TimeZone;
        }

        private void Control_MouseDown(object sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left)
            {
                this.StartPoint = e.Location;
                this.Drag = true;
            }
        }

        private void Control_MouseUp(object sender, MouseEventArgs e)
        {
            this.Drag = false;  //shouldn't be set.
        }

        private void Control_MouseMove(object sender, MouseEventArgs e)
        {
            if (this.Drag)
            {
                // if we should be dragging it, we need to figure out some movement
                Point p1 = new Point(e.X, e.Y);
                Point p2 = this.PointToScreen(p1);
                Point p3 = new Point(p2.X - this.StartPoint.X,
                                     p2.Y - this.StartPoint.Y);

                this.Location = p3;
            }
        }
    }
}
