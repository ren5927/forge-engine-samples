﻿using System;
using System.Diagnostics;
using System.IO;
using System.Windows.Forms;
using Bimangle.ForgeEngine.Skp.Core;
using Bimangle.ForgeEngine.Skp.Utility;

namespace Bimangle.ForgeEngine.Skp.Toolset
{
    class CommandToolsetCheckEngineLogs
    {
        public void Execute(Form parentForm)
        {
            try
            {
                var logsPath = Path.Combine(
                    App.GetHomePath(),
                    @"Logs");
                if (Directory.Exists(logsPath) == false)
                {
                    Directory.CreateDirectory(logsPath);
                }
                Process.Start(logsPath);
            }
            catch (Exception ex)
            {
                parentForm.ShowMessageBox(ex.ToString());

                Trace.WriteLine(ex.ToString());
            }
        }
    }
}
