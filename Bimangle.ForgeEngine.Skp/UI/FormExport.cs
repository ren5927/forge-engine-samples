﻿using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using Bimangle.ForgeEngine.Skp.Config;
using Bimangle.ForgeEngine.Skp.Core;
using Bimangle.ForgeEngine.Skp.Georeferncing;
using Bimangle.ForgeEngine.Skp.Toolset;
using Bimangle.ForgeEngine.Skp.UI.Controls;
using Bimangle.ForgeEngine.Skp.Utility;
using CommandLine;

namespace Bimangle.ForgeEngine.Skp.UI
{
    partial class FormExport : Form, IExportForm
    {
#pragma warning disable 414
        private bool _IsClosing;
#pragma warning restore 414

        private IExportControl _Exporter;
        private Options _Options;

        public FormExport()
        {
            InitializeComponent();
        }

        public FormExport(AppConfig config, string target)
            : this()
        {

            tabList.TabPages.Clear();

            var exporters = new List<IExportControl>();

            void AddPage(Control control, IExportControl exporter)
            {
                var tab = new TabPage();
                tabList.Controls.Add(tab);
                tab.Text = exporter.Title;
                tab.Name = $@"tabPage{exporter.Title}";
                tab.UseVisualStyleBackColor = true;
                tab.ImageKey = exporter.Icon;
                tab.Controls.Add(control);
                tab.Tag = exporter;
                control.Dock = DockStyle.Fill;

                if (exporter.Icon == target)
                {
                    _Exporter = exporter;
                }

                exporters.Add(exporter);
            }

#if !EXPRESS
            #region 增加 SvfZip 导出
            {
                var control = new ExportSvfzip();
                var exporter = (IExportControl)control;
                AddPage(control, exporter);
            }
            #endregion
#endif

            #region 增加 glTF/glb 导出
            {
                var control = new ExportGltf();
                var exporter = (IExportControl)control;
                AddPage(control, exporter);
            }
#endregion

#region 增加 3D Tiles 导出
            {
                var control = new ExportCesium3DTiles();
                var exporter = (IExportControl)control;
                AddPage(control, exporter);
            }
#endregion

            if (_Exporter == null) _Exporter = exporters.First();

            foreach (var exporter in exporters)
            {
                exporter.Init(this, config);
            }

            foreach (TabPage tab in tabList.TabPages)
            {
                if (tab.Tag == _Exporter)
                {
                    tabList.SelectTab(tab);
                    break;
                }
            }
        }

        private void FormExport_Load(object sender, EventArgs e)
        {
            Icon = Properties.Resources.app;

            Text = $@"{PackageInfo.ProductName} - Samples v{PackageInfo.ProductVersion}";

            txtInputFile.Text = Properties.Settings.Default.OptionsInputFilePath;
            txtInputFile.EnableFilePathDrop();

            InitToolset();
        }

        private void InitToolset()
        {
            var tsmiQuickPreview = new ToolStripMenuItem();
            tsmiQuickPreview.Text = Strings.PreviewAppName;
            tsmiQuickPreview.Click += (sender, e) =>
            {
                var cmd = new CommandToolsetQuickPreview();
                cmd.Execute(this);
            };
            tsmiToolset.DropDownItems.Add(tsmiQuickPreview);
            tsmiToolset.DropDownItems.Add(new ToolStripSeparator());

            var tsmiPickPositionFromMap = new ToolStripMenuItem();
            tsmiPickPositionFromMap.Text = GeoStrings.ToolsetPickPositionFromMap;
            tsmiPickPositionFromMap.Click += (sender, e) =>
            {
                var cmd = new CommandToolsetPickPositionFromMap();
                cmd.Execute(this);
            };

            var tsmiCreateProj = new ToolStripMenuItem();
            tsmiCreateProj.Text = GeoStrings.ToolsetCreateProj;
            tsmiCreateProj.Click += (sender, e) =>
            {
                var cmd = new CommandToolsetCreateProj();
                cmd.Execute(this, txtInputFile.Text.Trim());
            };

            tsmiToolset.DropDownItems.Add(tsmiPickPositionFromMap);
            tsmiToolset.DropDownItems.Add(tsmiCreateProj);
            tsmiToolset.DropDownItems.Add(new ToolStripSeparator());

            var tsmiCheckEngineLogs = new ToolStripMenuItem();
            tsmiCheckEngineLogs.Text = Strings.ToolsetCheckEngineLogs;
            tsmiCheckEngineLogs.Click += (sender, e) =>
            {
                var cmd = new CommandToolsetCheckEngineLogs();
                cmd.Execute(this);
            };
            tsmiToolset.DropDownItems.Add(tsmiCheckEngineLogs);
        }


        private void FormExportSvfzip_Shown(object sender, EventArgs e)
        {
#if !DEBUG
            ThreadPool.QueueUserWorkItem(CheckVersion);
#endif
        }

        private void FormExportSvfzip_FormClosing(object sender, FormClosingEventArgs e)
        {
            _IsClosing = true;
        }

        private void tabList_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (tabList.SelectedTab.Tag is IExportControl exporter)
            {
                _Exporter = exporter;
                _Exporter.RefreshCommand();
            }
        }


        #region Implementation of IExportForm

        public void RefreshCommand(Options options)
        {
            var arguments = GeneralCommandArguments(options);
            if (arguments == null)
            {
                btnRun.Enabled = false;
            }
            else
            {
                btnRun.Enabled = true;
            }

            _Options = options;
        }

        public string GetInputFilePath()
        {
            return txtInputFile.Text;
        }

        #endregion

        private void tsmiResetOptions_Click(object sender, EventArgs e)
        {
            // txtInputFile.Text = string.Empty;

            _Exporter.Reset();
            _Exporter.RefreshCommand();
        }

        private void btnRun_Click(object sender, EventArgs e)
        {
            var arguments = GeneralCommandArguments(_Options);
            if (arguments == null) return;

            var homePath = App.GetHomePath();
            if (App.CheckHomeFolder(homePath) == false &&
                ShowConfirm(Strings.HomeFolderIsInvalid) == false)
            {
                return;
            }

            using (var session = LicenseConfig.Create())
            {
                if (session.IsValid == false)
                {
                    LicenseConfig.ShowDialog(session, this);
                    return;
                }

                using (var progress = new ProgressExHelper(this, Strings.MessageExporting))
                {
                    var cancellationToken = progress.GetCancellationToken();

                    try
                    {
                        var sw = Stopwatch.StartNew();
                        var job = new Job();
                        var ret = job.Run(_Options, new LogProgress(progress), cancellationToken);
                        if (cancellationToken.IsCancellationRequested) return;

                        if (ret != 0)
                        {
                            ShowMessage($@"Error Code: {ret}");
                            return;
                        }

                        sw.Stop();
                        var ts = sw.Elapsed;
                        var duration = new TimeSpan(ts.Days, ts.Hours, ts.Minutes, ts.Seconds); //去掉毫秒部分
                        ShowMessage(string.Format(Strings.MessageExportSuccess, duration));
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show(ex.ToString(), Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
                    }
                }
            }
        }

        private void btnBrowseInputFile_Click(object sender, EventArgs e)
        {
            dlgSelectFile.FileName = txtInputFile.Text;

            if (dlgSelectFile.ShowDialog(this) == DialogResult.OK)
            {
                txtInputFile.Text = dlgSelectFile.FileName;
            }
        }

        private void txtInputFile_TextChanged(object sender, EventArgs e)
        {
            var s = txtInputFile.Text.Trim();
            if (string.IsNullOrWhiteSpace(s))
            {
                lblInputFilePrompt.ForeColor = SystemColors.ControlText;
                lblInputFilePrompt.Text = StringsUI.SelectInputFileFirst;
            }
            else
            {
                if (File.Exists(s) == false)
                {
                    lblInputFilePrompt.ForeColor = Color.Red;
                    lblInputFilePrompt.Text = StringsUI.InputFileNotExist;
                }
                else
                {
                    lblInputFilePrompt.ForeColor = Color.Green;
                    lblInputFilePrompt.Text = StringsUI.InputFileValid;
                }
            }

            _Exporter.RefreshCommand();

            Properties.Settings.Default.OptionsInputFilePath = txtInputFile.Text;
            Properties.Settings.Default.Save();
        }

        private string GeneralCommandArguments(Options options)
        {
            if (options == null) return null;
            if (string.IsNullOrEmpty(txtInputFile.Text) ||
                File.Exists(txtInputFile.Text) == false)
            {
                return null;
            }

            options.InputFilePath = txtInputFile.Text;

            return Parser.Default.FormatCommandLine(options);
        }

        private void ShowMessage(string s)
        {
            MessageBox.Show(this, s, Text, MessageBoxButtons.OK, MessageBoxIcon.Information, MessageBoxDefaultButton.Button1);
        }

        private bool ShowConfirm(string s)
        {
            var r = MessageBox.Show(this, s, Text, MessageBoxButtons.OKCancel, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2);
            return r == DialogResult.OK;
        }

        private void tsmiLicense_Click(object sender, EventArgs e)
        {
            LicenseConfig.ShowDialog(this);
        }
    }
}
