﻿using System;
using System.Drawing;
using System.Threading;
using System.Diagnostics;
using System.Drawing.Text;
using System.ComponentModel;
using System.Windows.Forms;
using System.Drawing.Drawing2D;
using System.Text.RegularExpressions;
using SwitchWinClock.utils;
using System.IO;
using System.Linq;
using SwitchTimeZones;

namespace SwitchWinClock
{
    public partial class DisplayForm : Form
    {
#if DEBUG
        const string mutexExt = "Debug";
#else
        const string mutexExt = "Release";
#endif

        delegate void VoidDelegate();
        private static bool m_closingForm = false;

        private readonly Font ButtonFont = new Font("Arial Black", 10F, FontStyle.Regular, GraphicsUnit.Pixel);
        private readonly SCConfig config = new SCConfig();
        private readonly Screen[] m_allScreens = Screen.AllScreens;
        private static SLog Log = null;

        /// <summary>
        /// allow diff in location. Reason being, all fonts are not blocked in size.
        /// e.g 72px, Regular, "Berlin Sans FB" using "hh:mm:ss tt" can jump as much as 29px at times.
        /// If set to center or right of screen and the fonts aren't blocked, this causes the time to jump every second.
        /// TODO: Future make this more dynamic based on single character Font Max size when set and to set Form width.
        /// </summary>
        private static int FontAllowDiff { get; set; } = 30;
        /// <summary>
        /// TODO: Future make this more dynamic based on single character Font Max size when set and to set Form width.
        /// </summary>
        private static int FormAllowDiff { get; set; } = 20;

        private int m_waitTimer = 60000;  //default: 1 min

        public DisplayForm()
        {
            InitializeComponent();
            //preload for cache
            SCConfig.GetTimeZones();
            //SetInstanceName(false);
            //setup logging
            Log = new SLog(SMsgType.Debug);
            //if this is a new instance, ask for a name and timezone.
            if (config.InstanceName.Equals(Global.DefaultInstanceName))
            {
                if (!SetInstanceName(true))
                {
                    this.Close();
                    return;
                }
            }

            this.SetupMenuChecks();
            this.SetCheckTextDepth(config.TextBorderDepth);
            this.SetDateFormatMenus();
            this.SetFormLocation();
            this.SetWinAlignCheckDefault();
        }
        private bool SetInstanceName(bool isNew)
        {
            TrueTimeZone tzFound = TimeZoneSearch.SearchById(config.TimeZone) ?? Global.CurrentTimeZone();

            using (InstanceNameForm frm = new InstanceNameForm())
            {
                //if not default, meaning this is a rename
                if (!config.InstanceName.Equals(Global.DefaultInstanceName))
                {
                    frm.InstanceName = config.InstanceName;
                    frm.TimeZone = tzFound.DisplayName;
                }

                //load instance name and timezone.
                DialogResult dr = frm.ShowDialog(this);
                if (dr == DialogResult.Cancel)
                {
                    if (isNew)
                    {
                        //we have some possible cleanup, 
                        if (File.Exists(Global.ConfigFileName))
                            File.Delete(Global.ConfigFileName);
                    }
                        
                    return false;
                }
                else if (dr == DialogResult.OK)
                {
                    config.InstanceName = frm.InstanceName;
                    config.TimeZone = frm.TimeZone;
                }
            }

            return true;
        }
        private AutoResetEvent[] SWCEvents { get; set; } = null;    //could be shut down event or new message event
        private bool Drag { get; set; }
        private Point StartPoint { get; set; } = Point.Empty;
        private StringFormat GetTextAlignment()
        {
            StringFormat stringFormat;

            switch (config.TextAlignment)
            {
                case ContentAlignment.TopCenter:
                    stringFormat = new StringFormat()
                    {
                        LineAlignment = StringAlignment.Near,
                        Alignment = StringAlignment.Center
                    };
                    break;
                case ContentAlignment.BottomCenter:
                    stringFormat = new StringFormat()
                    {
                        LineAlignment = StringAlignment.Far,
                        Alignment = StringAlignment.Center
                    };
                    break;
                case ContentAlignment.TopLeft:
                    stringFormat = new StringFormat()
                    {
                        LineAlignment = StringAlignment.Near,
                        Alignment = StringAlignment.Near
                    };
                    break;
                case ContentAlignment.BottomLeft:
                    stringFormat = new StringFormat()
                    {
                        LineAlignment = StringAlignment.Far,
                        Alignment = StringAlignment.Near
                    };
                    break;
                case ContentAlignment.TopRight:
                    stringFormat = new StringFormat()
                    {
                        LineAlignment = StringAlignment.Near,
                        Alignment = StringAlignment.Far
                    };
                    break;
                case ContentAlignment.BottomRight:
                    stringFormat = new StringFormat()
                    {
                        LineAlignment = StringAlignment.Far,
                        Alignment = StringAlignment.Far
                    };
                    break;
                default:
                    stringFormat = new StringFormat()
                    {
                        Alignment = StringAlignment.Center,
                        LineAlignment = StringAlignment.Center
                    };
                    break;
            }

            return stringFormat;
        }
        private void DrawForm(Graphics g, Size sz)
        {
            Rectangle rect = new Rectangle() { X = 0, Y = 0, Width = sz.Width, Height = sz.Height };

            if (config.BackColor.A != 0)            // if not clear
                g.FillRectangle(new SolidBrush(config.BackColor), rect);
            if (config.FormBorderColor.A != 0)      // if not clear
                g.DrawRectangle(new Pen(config.FormBorderColor, 2), rect);
        }
        private void DrawCloseButton(Graphics g, StringFormat sFormat)
        {
            int buttonSize = 15;
            Rectangle rect = new Rectangle() { Height = buttonSize, Width = buttonSize, X = this.ClientSize.Width - (buttonSize + 5), Y = 5 };

            g.FillRectangle(Brushes.Red, rect);
            g.DrawString("X", ButtonFont, Brushes.White, rect, sFormat);
            g.DrawRectangle(new Pen(Brushes.Gray, 2), rect);
        }
        private void SetFormLocation()
        {
            if (config.ManualWinAlignment)
            {
                if (config.FormLocation.X == 0)
                    this.Left = (Screen.GetWorkingArea(this).Width / 2) - (this.Width / 2);     //center screen
                else
                    this.Left = config.FormLocation.X;

                if (config.FormLocation.Y == 0)
                    this.Top = (Screen.GetWorkingArea(this).Height / 2) - (this.Height / 2);    //center screen
                else
                    this.Top = config.FormLocation.Y;
            }
            else
            {
                int lToBe, lDiff;
                var thisScreen = m_allScreens[m_allScreens.Length >= config.DeviceNumber ? config.DeviceNumber : 0].Bounds;

                switch (config.WinAlignment)
                {
                    case ContentAlignment.TopLeft:
                        this.Left = thisScreen.Left;
                        this.Top = thisScreen.Top;
                        break;
                    case ContentAlignment.TopCenter:
                        lToBe = ((thisScreen.Left + thisScreen.Width) - (thisScreen.Width / 2)) - (this.Width / 2);
                        lDiff = this.Left > lToBe ? this.Left - lToBe : lToBe - this.Left;

                        if (lDiff > FontAllowDiff)
                            this.Left = lToBe;

                        this.Top = thisScreen.Top;
                        break;
                    case ContentAlignment.TopRight:
                        lToBe = (thisScreen.Left + thisScreen.Width) - (this.Width);
                        lDiff = this.Left > lToBe ? this.Left - lToBe : lToBe - this.Left;

                        if (lDiff > FontAllowDiff)
                            this.Left = lToBe;

                        this.Top = thisScreen.Top;
                        break;
                    case ContentAlignment.MiddleLeft:
                        this.Left = thisScreen.Left;
                        this.Top = ((thisScreen.Top + thisScreen.Height) - (thisScreen.Height / 2)) - (this.Height / 2);
                        break;
                    case ContentAlignment.MiddleCenter:
                        lToBe = ((thisScreen.Left + thisScreen.Width) - (thisScreen.Width / 2)) - (this.Width / 2);
                        lDiff = this.Left > lToBe ? this.Left - lToBe : lToBe - this.Left;

                        if (lDiff > FontAllowDiff)
                            this.Left = lToBe;

                        this.Top = ((thisScreen.Top + thisScreen.Height) - (thisScreen.Height / 2)) - (this.Height / 2);
                        break;
                    case ContentAlignment.MiddleRight:
                        lToBe = (thisScreen.Left + thisScreen.Width) - (this.Width);
                        lDiff = this.Left > lToBe ? this.Left - lToBe : lToBe - this.Left;

                        if (lDiff > FontAllowDiff)
                            this.Left = lToBe;

                        this.Top = ((thisScreen.Top + thisScreen.Height) - (thisScreen.Height / 2)) - (this.Height / 2);
                        break;
                    case ContentAlignment.BottomLeft:
                        this.Left = thisScreen.Left;
                        this.Top = (thisScreen.Top+ thisScreen.Height) - (this.Height);
                        break;
                    case ContentAlignment.BottomCenter:
                        lToBe = ((thisScreen.Left + thisScreen.Width) - (thisScreen.Width / 2)) - (this.Width / 2);
                        lDiff = this.Left > lToBe ? this.Left - lToBe : lToBe - this.Left;

                        if (lDiff > FontAllowDiff)
                            this.Left = lToBe;

                        this.Top = (thisScreen.Top + thisScreen.Height) - (this.Height);
                        break;
                    case ContentAlignment.BottomRight:
                        lToBe = (thisScreen.Left + thisScreen.Width) - (this.Width);
                        lDiff = this.Left > lToBe ? this.Left - lToBe : lToBe - this.Left;

                        if (lDiff > FontAllowDiff)
                            this.Left = lToBe;

                        this.Top = (thisScreen.Top + thisScreen.Height) - (this.Height);
                        break;
                }
            }
        }
        /// <summary>
        /// Thread safe refresh
        /// </summary>
        private void RefreshForm()
        {
            if (m_closingForm)
                return;

            if (InvokeRequired)
            {
                var d = new VoidDelegate(RefreshForm);
                if (!Disposing && !IsDisposed)
                {
                    try { Invoke(d); }
                    catch (ObjectDisposedException ex) { Debug.WriteLine(ex.Message); }
                    catch (Exception ex)
                    {
                        MessageBox.Show(ex.Message, $"Exception Occured", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    }
                }
            }
            else if (!Disposing && !IsDisposed)
            {
                if(!this.Drag)
                    this.SetFormLocation();

                config.ImAlive();

                this.Invalidate();
            }
        }
        private void StartCounter()
        {
            int iEvent = EventTypes.EVENT_TIMEOUT;

            SetWaitTimer();

            while (iEvent != EventTypes.EVENT_SHUTDOWN)
            {
                RefreshForm();
                iEvent = WaitHandle.WaitAny(SWCEvents, m_waitTimer);
            }
        }
        /// <summary>
        /// Attempting to get the to the closes millisecond to the next second as possible based on format.
        /// </summary>
        private void SetWaitTimer()
        {
            if (SWCEvents == null)
                return;

            string date = DateTime.Now.AddSeconds(1).ToString("yyyy-MM-dd HH:mm:ss");
            if(!DateTime.TryParse(date, out DateTime futreDate))
                futreDate = DateTime.Now;

            if (config.DateFormat?.IndexOf("fff") > -1)
                m_waitTimer = 1;
            else if (config.DateFormat?.IndexOf("ff") > -1)
                m_waitTimer = 10;
            else if (config.DateFormat?.IndexOf("f") > -1)
                m_waitTimer = 100;
            else
                m_waitTimer = 1000;

            TimeSpan diff = futreDate.Subtract(DateTime.Now);
            //wait to set on exact millisecond
            WaitHandle.WaitAny(SWCEvents, diff);
            //refresh ui.
            RefreshForm();
            //make sure, what ever might be on hold, resets based on new waitTimer
            SWCEvents[EventTypes.EVENT_MANUAL].Set();
        }
        private void SetupMenuChecks()
        {
            if (config.ClockStyle == Clock_Style.Depth_Shadowed)
            {
                this.StyleDeptMenuItem.Checked = true;
                this.StyleShadowMenuItem.Checked = true;
            }
            else if (config.ClockStyle == Clock_Style.Depth)
                this.StyleDeptMenuItem.Checked = true;
            else if (config.ClockStyle == Clock_Style.Shadowed)
                this.StyleShadowMenuItem.Checked = true;
            else
                this.StyleBorderMenuItem.Checked = true;

            this.TextDepthpMenuItem.Text = this.StyleBorderMenuItem.Checked ? MenuText.TextBorderText : MenuText.TextDepthText;
            this.FontBorderColorMenuItem.Visible = this.StyleBorderMenuItem.Checked;
            this.BackColorTransparentMenuItem.Checked = config.BackColor.A == 0;
            this.BorderColorTransparentMenuItem.Checked = config.FormBorderColor.A == 0;
            this.ForeColorTransparentMenuItem.Checked = config.ForeColor.A == 0;
            this.TextBorderTransparentMenuItem.Checked = config.TextBorderColor.A == 0;

            this.AlwaysOnTopMenuItem.Checked = config.AlwaysOnTop;
            this.TopMost = config.AlwaysOnTop;
        }
        private void SetDateFormatMenus(ToolStripMenuItem menuItem = null)
        {
            if (menuItem == null)
            {
                bool found = false;
                string format = config.DateFormat;

                foreach (ToolStripMenuItem tsi in DateFormattingMenuItem.DropDownItems)
                {
                    if (format == tsi.Text)
                    {
                        tsi.Checked = true;
                        found = true;                        
                    }
                    else
                        tsi.Checked = false;
                }

                if(!found)
                    CustomDateTextBox.Text = format;
            }
            else
            {
                foreach (ToolStripMenuItem tsi in DateFormattingMenuItem.DropDownItems)
                {
                    if (menuItem.Text == tsi.Text)
                        tsi.Checked = true;
                    else
                        tsi.Checked = false;
                }
                config.DateFormat = menuItem.Text;
            }

            SetWaitTimer();
        }

        private void Menu_MouseLeave(object sender, EventArgs e)
        {
            Log.WriteLine("Refreshing Form");
            RefreshForm();
            //SettingsContextMenu.Hide();
        }
        private void RefreshInstancesMenu()
        {
            AvailableInstanceMenuItem.DropDownItems.Clear();

            string appName = $"{About.AppTitle.Replace(" ", "")}{mutexExt}";
            for (int id = 1; id <= 10; id++)
            {
                FileInfo fi = new FileInfo($"SWClock{id:00}.config");
                if (!Global.AppID.Equals(id) && fi.Exists && fi.LastWriteTimeUtc<DateTime.UtcNow.AddSeconds(-Global.MaxImAliveSeconds))
                {
                    var allLines = File.ReadAllLines(fi.FullName);

                    //pull InstanceName, if column is missing, get DateFormat only from others file.
                    var formats = allLines.Where(w=>w.IndexOf($"{ColNames.InstanceName}\":") >-1).ToArray();
                    if(formats.Length == 0)
                        formats = allLines.Where(w => w.IndexOf($"{ColNames.DateFormat}\":") > -1).ToArray();

                    if (formats.Length > 0)
                    {
                        // "DateFormat": "dddd, MMM dd, hh:mm:ss tt",
                        // (0)'<blank>', (1) 'DateFormat', (2)': ', (3)'dddd, MMM dd, hh:mm:ss tt', (4)','

                        string[] splitter = formats[0].Split('\"');
                        string format = splitter[splitter.Length - 2];

                        var tsmi = new ToolStripMenuItem(format)
                        {
                            Tag = id.ToString("00")
                        };
                        tsmi.Click += new EventHandler(AvaiInstance_Click);
                        AvailableInstanceMenuItem.DropDownItems.Add(tsmi);
                    }
                }
            }

            if (AvailableInstanceMenuItem.DropDownItems.Count == 0)
                AvailableInstanceMenuItem.Visible = false;
            else
                AvailableInstanceMenuItem.Visible = true;
        }
        private void Form_Load(object sender, EventArgs e)
        {
            Log.WriteLine("Starting application form.");
            RefreshInstancesMenu();
            SWCEvents = new AutoResetEvent[]
            {
                new AutoResetEvent(false),  //EVENT_SHUTDOWN
                new AutoResetEvent(false),  //EVENT_MANUAL
            };

            Log.WriteLine("Starting Counter Thread.");
            Thread thread = new Thread(() => { StartCounter(); });
            thread.Start();
        }
        private void Form_Paint(object sender, PaintEventArgs e)
        {
            //e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;   //commented, or it shows the pink form background around the border.
            // When text is rendered directly
            e.Graphics.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
            // The composition properties are useful when drawing on a composited surface
            // It has no effect when drawing on a Control's plain surface
            e.Graphics.CompositingMode = CompositingMode.SourceOver;
            e.Graphics.CompositingQuality = CompositingQuality.HighQuality;

            Graphics g = e.Graphics;
            int addSize;

            Color fColor = config.ForeColor;
            int depth = config.TextBorderDepth;
            int dblPad = (depth * 2);

            Font ft = config.Font;
            switch (ft.Unit)
            {
                case GraphicsUnit.Point:
                    addSize = 15;
                    break;
                case GraphicsUnit.Millimeter:
                    addSize = 20;
                    break;
                case GraphicsUnit.Display:
                case GraphicsUnit.Document:
                    addSize = 15;
                    break;
                default:
                    addSize = 0;
                    break;
            }

            string dtFormat = config.DateFormat;
            if (dtFormat.Contains("z"))
            {
                TimeSpan tzOffSet = config.InstanceTimeZone.IsDaylightSavingTime ? config.InstanceTimeZone.DSTUtcOffset : config.InstanceTimeZone.BaseUtcOffset;
                string addminus = tzOffSet.Hours < 0 ? "-" : "+";

                if (dtFormat.Contains("zzzz"))
                    dtFormat = dtFormat.Replace("zzzz", $"{addminus}{tzOffSet:hh\\:mm}");
                if (dtFormat.Contains("zzz"))
                    dtFormat = dtFormat.Replace("zzz", $"{addminus}{tzOffSet:h\\:mm}");
                if (dtFormat.Contains("zz"))
                    dtFormat = dtFormat.Replace("zz", $"{addminus}{tzOffSet:hh}");
                if (dtFormat.Contains("z"))
                    dtFormat = dtFormat.Replace("z", $"{addminus}{tzOffSet:hh}");  //single h without mm fails

            }
            string dt = config.InstanceTime.ToString(dtFormat);
            SizeF textSize = e.Graphics.MeasureString(dt, ft);
            Size sz = new Size(this.ClientSize.Width - dblPad, this.ClientSize.Height - dblPad);

            int newHeight = ((int)textSize.Height);
            int newWidth = (sz.Width + dblPad);

            int sugWidth = (int)Math.Round(textSize.Width) + addSize + dblPad;
            int sugHeight = (int)Math.Round(textSize.Height) + addSize + dblPad;

            if (sugHeight > newHeight)
                newHeight = sugHeight;

            if (sugWidth > newHeight)
                newWidth = sugWidth;

            //in attempt to not allow the form jump around because of size of font characters, this gives a little allowances.
            //int wDiff = newWidth > this.ClientSize.Width ? newWidth - this.ClientSize.Width : this.ClientSize.Width - newWidth;
            //int hDiff = newHeight > this.ClientSize.Height ? newHeight - this.ClientSize.Height : this.ClientSize.Height - Height;
            //if (wDiff > FormAllowDiff || hDiff > FormAllowDiff)
            {
                this.ClientSize = new Size(newWidth, newHeight);
                sz = new Size(this.ClientSize.Width, this.ClientSize.Height);
            }

            StringFormat sFormat = new StringFormat()
            {
                LineAlignment = StringAlignment.Center,
                Alignment = StringAlignment.Center
            }; //GetTextAlignment();

            //int advColor = (config.ForeColor.R + config.ForeColor.G + config.ForeColor.B) / 3;
            //int tColor = advColor < 128 ? advColor + 128 : advColor - 128;
            int tColor = 230;   //TODO: working on how I want this dynamically set.
            int bColor = tColor > 128 ? tColor - 128 : tColor;
            tColor = tColor > 128 ? tColor : 255 - tColor;

            DrawForm(g, this.ClientSize);

            if (config.ClockStyle == Clock_Style.Border)
            {
                using (FontObject fontObject = new FontObject(dt, ft))
                {
                    fontObject.Outlined = true;
                    fontObject.Outline = new Pen(config.TextBorderColor, depth);
                    fontObject.FillColor = config.ForeColor;

                    CanvasPaint(e, fontObject);
                };
            }
            else
            {
                //########################################
                //NOTE: The depth and shadow was in the same loop.  This caused paint issues with some fonts.  Splitting them up, resolved that issue.
                //########################################
                if (config.ClockStyle == Clock_Style.Shadowed || config.ClockStyle == Clock_Style.Depth_Shadowed)
                {
                    for (int i = 1; i <= depth; i++)
                        g.DrawString(dt, ft, new SolidBrush(Color.FromArgb(255, bColor, bColor, bColor)), new Rectangle(i, i, sz.Width, sz.Height), sFormat);
                }

                if (config.ClockStyle == Clock_Style.Depth || config.ClockStyle == Clock_Style.Depth_Shadowed)
                {
                    for (int i = 1; i <= depth; i++)
                        g.DrawString(dt, ft, new SolidBrush(Color.FromArgb(255, tColor, tColor, tColor)), new Rectangle(-i, -i, sz.Width, sz.Height), sFormat);
                }

                //########################################
                //NOTE: This will be removed once the above has been running for a while.
                //########################################
                //for (int i = 1; i <= depth; i++)
                //{
                //    if (config.ClockStyle == Clock_Style.Depth)
                //        g.DrawString(dt, ft, new SolidBrush(Color.FromArgb(255, tColor, tColor, tColor)), new Rectangle(-i, -i, sz.Width, sz.Height), sFormat);
                //    else if (config.ClockStyle == Clock_Style.Shadowed)
                //        g.DrawString(dt, ft, new SolidBrush(Color.FromArgb(255, bColor, bColor, bColor)), new Rectangle(i, i, sz.Width, sz.Height), sFormat);
                //    else
                //    {
                //        g.DrawString(dt, ft, new SolidBrush(Color.FromArgb(255, bColor, bColor, bColor)), new Rectangle(i, i, sz.Width, sz.Height), sFormat);
                //        g.DrawString(dt, ft, new SolidBrush(Color.FromArgb(255, tColor, tColor, tColor)), new Rectangle(-i, -i, sz.Width, sz.Height), sFormat);
                //    }
                //}

                g.DrawString(dt, ft, new SolidBrush(fColor), new Rectangle(0, 0, sz.Width, sz.Height), sFormat);
            }
        }
        private void CanvasPaint(PaintEventArgs e, FontObject fontObject)
        {
            using (var path = new GraphicsPath())
            using (var format = new StringFormat())
            {
                format.Alignment = StringAlignment.Center; //As selected
                format.LineAlignment = StringAlignment.Center; //As selected

                //float f = e.Graphics.DpiY * fontObject.SizeInPixels / 72f;
                //fontObject.SizeInEms
                path.AddString(fontObject.Text, fontObject.FontFamily, (int)fontObject.FontStyle, fontObject.SizeInPoints, this.ClientRectangle, format);

                if (fontObject.Outlined)
                    e.Graphics.DrawPath(fontObject.Outline, path);

                using (var brush = new SolidBrush(fontObject.FillColor))
                    e.Graphics.FillPath(brush, path);
            }
        }
        private void Form_MouseMove(object sender, MouseEventArgs e)
        {
            if (this.Drag)
            {
                // if we should be dragging it, we need to figure out some movement
                Point p1 = new Point(e.X, e.Y);
                Point p2 = this.PointToScreen(p1);
                Point p3 = new Point(p2.X - this.StartPoint.X,
                                     p2.Y - this.StartPoint.Y);
                
                config.FormLocation = p3;
                this.Location = p3;
            }
        }
        private void Form_MouseClick(object sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Right)
            {
                Point pt = new Point(e.X + this.Left, e.Y + this.Top);
                SettingsContextMenu.Show(pt);
            }
        }
        private void Form_MouseDown(object sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left)
            {
                this.StartPoint = e.Location;
                this.Drag = true;
            }
        }
        private void Form_MouseUp(object sender, MouseEventArgs e)
        {
            if (this.Drag && e.Button == MouseButtons.Left)
            {
                for (int id = 0; id < m_allScreens.Length; id++)
                {
                    if (this.Left > m_allScreens[id].Bounds.Left &&
                        this.Left < m_allScreens[id].Bounds.Right)
                    {
                        if(config.DeviceNumber != id)   //only update if different than existing.
                            config.DeviceNumber = id;   //update is sent to file.

                        break;
                    }
                }
            }

            if (this.Drag) {
                this.Drag = false;  //shouldn't be set.
                RefreshForm();
            }
        }
        private void Form_Closing(object sender, FormClosingEventArgs e)
        {
            m_closingForm = true;
            Log.WriteLine("Application Closing down.");
            SWCEvents?[EventTypes.EVENT_SHUTDOWN].Set();
        }
        private void ExitMenuItem_Click(object sender, EventArgs e)
        {
            this.Close();
        }
        private void FontSetupMenuItem_Click(object sender, EventArgs e)
        {
            using (FontDialog fontDialog = new FontDialog())
            {
                fontDialog.ShowApply = true;
                fontDialog.Apply += FontApply;
                fontDialog.ShowColor = true;
                fontDialog.Font = config.Font;
                fontDialog.Color = config.ForeColor;

                if (fontDialog.ShowDialog() == DialogResult.OK)
                {
                    FontApply(fontDialog, e);
                    RefreshForm();
                }
            }
        }
        private void AvaiInstance_Click(object sender, EventArgs e)
        {
            var tsi = sender as ToolStripItem;
            string tag = tsi.Tag as string;

            Global.RunApp(file: About.AppPath, args: tag);
        }
        private void FontApply(object sender, EventArgs e)
        {
            var fd = sender as FontDialog;
            config.Font = fd.Font;
            config.ForeColor = fd.Color;
            RefreshForm();
        }
        private void TextDepthp_Click(object sender, EventArgs e)
        {
            ToolStripMenuItem menuItem = ((ToolStripMenuItem)sender);
            SetCheckTextDepth(menuItem);

            string name = Regex.Replace(menuItem.Name, "[^0-9]", "");
            if (int.TryParse(name, out int iVal))
                config.TextBorderDepth = iVal;
        }
        private void ColorTransparentMenuItem_Click(object sender, EventArgs e)
        {
            ToolStripMenuItem menuItem = ((ToolStripMenuItem)sender);
            int isTransparent = menuItem.Checked ? 0 : 255;

            switch(menuItem.Name)
            {
                case "BackColorTransparentMenuItem":
                    config.BackColor = Color.FromArgb(isTransparent, config.BackColor.R, config.BackColor.G, config.BackColor.B);
                    break;
                case "BorderColorTransparentMenuItem":
                    config.FormBorderColor = Color.FromArgb(isTransparent, config.FormBorderColor.R, config.FormBorderColor.G, config.FormBorderColor.B);
                    break;
                case "ForeColorTransparentMenuItem":
                    config.ForeColor = Color.FromArgb(isTransparent, config.ForeColor.R, config.ForeColor.G, config.ForeColor.B);
                    break;
                case "TextBorderTransparentMenuItem":
                    config.TextBorderColor = Color.FromArgb(isTransparent, config.TextBorderColor.R, config.TextBorderColor.G, config.TextBorderColor.B);
                    break;
            }
        }
        private void SetCheckTextDepth(ToolStripMenuItem menuItem)
        {
            foreach (ToolStripMenuItem tsi in TextDepthpMenuItem.DropDownItems)
            {
                if (menuItem.Name == tsi.Name)
                    tsi.Checked = true;
                else
                    tsi.Checked = false;
            }
        }
        private void SetCheckTextDepth(int menuItem)
        {
            foreach (ToolStripMenuItem tsi in TextDepthpMenuItem.DropDownItems)
            {
                string name = Regex.Replace(tsi.Name, "[^0-9]", "");
                if (int.TryParse(name, out int iVal) && iVal == menuItem)
                {
                    tsi.Checked = true;
                    break;
                }
            }
        }
        private void BackColorSetMenuItem_Click(object sender, EventArgs e)
        {
            Color defColor = config.BackColor;
            using (ColorDialog cd = new ColorDialog())
            {
                cd.FullOpen = true;
                cd.Color = config.BackColor;
                if (cd.ShowDialog() == DialogResult.OK)
                {
                    config.BackColor = cd.Color;
                    BackColorTransparentMenuItem.Checked = false;
                }
                else
                    config.BackColor = defColor;
            }
        }
        private bool PickColor(Color color, out Color retColor)
        {
            bool retVal = false;
            retColor = color;
            using (ColorDialog cd = new ColorDialog())
            {
                cd.FullOpen = true;
                cd.Color = color;
                if (cd.ShowDialog(this) == DialogResult.OK)
                {
                    retColor = cd.Color;
                    retVal = true;
                }
            }

            return retVal;
        }
        private void BorderColorSetMenuItem_Click(object sender, EventArgs e)
        {
            if(PickColor(config.FormBorderColor, out Color selColor))
            {
                config.FormBorderColor = selColor;
                BorderColorTransparentMenuItem.Checked = false;
            }
        }
        private void CustomDateTextBox_KeyUp(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Enter)
                config.DateFormat = CustomDateTextBox.Text.Trim();
            if (e.KeyCode == Keys.Escape || e.KeyCode == Keys.Enter)
                SetDateFormatMenus();
            
            RefreshForm();
        }
        private void DateFormatItem_Click(object sender, EventArgs e)
        {
            ToolStripMenuItem menuItem = ((ToolStripMenuItem)sender);
            menuItem.Checked = !menuItem.Checked;
            SetDateFormatMenus(menuItem);
            RefreshForm();
        }
        private void ForeColorSetMenuItem_Click(object sender, EventArgs e)
        {
            if (PickColor(config.ForeColor, out Color selColor))
            {
                config.ForeColor = selColor;
                ForeColorTransparentMenuItem.Checked = false;
            }
        }
        private void StyleDeptMenuItem_Click(object sender, EventArgs e)
        {
            if (StyleDeptMenuItem.Checked || this.StyleShadowMenuItem.Checked)
            {
                this.TextDepthpMenuItem.Text = MenuText.TextDepthText;
                if (this.StyleShadowMenuItem.Checked && StyleDeptMenuItem.Checked)
                    config.ClockStyle = Clock_Style.Depth_Shadowed;
                else if (this.StyleDeptMenuItem.Checked)
                    config.ClockStyle = Clock_Style.Depth;
                else
                    config.ClockStyle = Clock_Style.Shadowed;

                this.StyleBorderMenuItem.Checked = false;
                this.FontBorderColorMenuItem.Visible = false;
            }
            else
                this.StyleDeptMenuItem.Checked = true;      //reject unchecking, since nothing is checked.
        }
        private void StyleShadowMenuItem_Click(object sender, EventArgs e)
        {
            if (StyleDeptMenuItem.Checked || this.StyleShadowMenuItem.Checked)
            {
                this.TextDepthpMenuItem.Text = MenuText.TextDepthText;
                if (this.StyleShadowMenuItem.Checked && StyleDeptMenuItem.Checked)
                    config.ClockStyle = Clock_Style.Depth_Shadowed;
                else if (this.StyleShadowMenuItem.Checked)
                    config.ClockStyle = Clock_Style.Shadowed;
                else
                    config.ClockStyle = Clock_Style.Depth;

                this.StyleBorderMenuItem.Checked = false;
                this.FontBorderColorMenuItem.Visible = false;
            }
            else
                this.StyleShadowMenuItem.Checked = true;   //reject unchecking, since nothing is checked.
        }
        private void StyleBorderMenuItem_Click(object sender, EventArgs e)
        {
            this.TextDepthpMenuItem.Text = MenuText.TextBorderText;
            config.ClockStyle = Clock_Style.Border;
            StyleDeptMenuItem.Checked = false;
            StyleShadowMenuItem.Checked = false;
            if (!StyleBorderMenuItem.Checked)
                StyleBorderMenuItem.Checked = true;
            FontBorderColorMenuItem.Visible = true;
        }
        private void TextBorderSetColorMenuItem_Click(object sender, EventArgs e)
        {
            if (PickColor(config.TextBorderColor, out Color selColor))
            {
                config.TextBorderColor = selColor;
                TextBorderTransparentMenuItem.Checked = false;
            }
        }
        private void NewInstanceMenuItem_Click(object sender, EventArgs e)
        {
            bool foundNew = false;
            for (int id = 1; id <= Global.MaxImAliveSeconds; id++)
            {
                if (!Global.AppID.Equals(id) && !File.Exists($"SWClock{id:00}.config"))
                {
                    foundNew = true;
                    Global.RunApp(file: About.AppPath, args: id.ToString());
                    break;
                }
            }

            if (!foundNew)
                MessageBox.Show($"No new instances available.  {Global.MaxImAliveSeconds} is the max instances count. Please delete some to create new or change existing.");
        }
        private void DeleteInstanceMenuItem_Click(object sender, EventArgs e)
        {
            if(File.Exists(Global.ConfigFileName) 
                && MessageBox.Show(this, 
                        "Are you sure you want to delete this clock instance?\n\nNote: No other instance of this clock will be changed.", 
                        "Delete an Instance", 
                        MessageBoxButtons.YesNo, 
                        MessageBoxIcon.Question) == DialogResult.Yes) 
            {
                File.Delete(Global.ConfigFileName);
                this.Close();
            }
        }
        private void WinAlignMenuItems_Click(object sender, EventArgs e)
        {
            var winAlignSel = sender as ToolStripMenuItem;
            var menuItemName = ((ToolStripMenuItem)sender).Name.Replace("WinAlign", "").Replace("MenuItem", "");

            if (!winAlignSel.Checked)
                winAlignSel.Checked = true; //don't uncheck if they click it twice.

            if (menuItemName.Equals("Manual"))
                config.ManualWinAlignment = true;
            else
                config.ManualWinAlignment = false;

            foreach (ToolStripMenuItem tsmi in this.WinAlignmentMenuItem.DropDownItems)
            {
                if(tsmi.Checked && !winAlignSel.Name.Equals(tsmi.Name))
                    tsmi.Checked = false;
                else if(winAlignSel.Name.Equals(tsmi.Name))
                {
                    if (Enum.TryParse(menuItemName, out ContentAlignment winAlign))
                    {
                        Log.WriteLine($"WinAlignment changing: From: {config.WinAlignment} To: {winAlign}");
                        config.WinAlignment = winAlign;
                    }
                }
            }
        }
        private void SetWinAlignCheckDefault()
        {
            string winAlign2Check = config.WinAlignment.ToString();
            if (config.ManualWinAlignment)
                winAlign2Check = "Manual";

            foreach (ToolStripMenuItem tsmi in this.WinAlignmentMenuItem.DropDownItems)
            {
                if (tsmi.Name.Contains(winAlign2Check))
                    tsmi.Checked = true;
                else
                    tsmi.Checked = false;
            }
        }
        private void AlwaysOnTopMenuItem_Click(object sender, EventArgs e)
        {
            this.TopMost = this.AlwaysOnTopMenuItem.Checked;
            config.AlwaysOnTop = this.TopMost;
        }
        private void StyleCounterMenuItem_Click(object sender, EventArgs e)
        {
            //TODO
        }
        private void SettingsContextMenu_Opening(object sender, CancelEventArgs e)
        {
            RefreshInstancesMenu();
        }
        private void RenameInstanceMenuItem_Click(object sender, EventArgs e)
        {
            SetInstanceName(false);
        }
    }
}