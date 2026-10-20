using Microsoft.Win32;
using MISA.ASP.ClientApp.UI;
using MISA.ASP.ClientApp.Utils.Logging;
using MISA.ASP.ClientApp.Utils.UriHandler;
using System;
using System.Collections.Generic;
using System.Configuration;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace MISA.ASP.ClientApp
{
    internal static class Program
    {
        internal static frmAboutUs MainForm { get; private set; }
        internal static bool InProcessing{ get; set; }

        /// <summary>
        /// The main entry point for the application.
        /// </summary>
        [STAThread]
        static void Main(string[] args)
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            if (ConfigurationManager.AppSettings["Env"] == "Dev")
            {
                var applicationPath = @"F:\Project\ToolTaxDeclaration\MISA.ASP.ClientApp\bin\Debug\MISA.ASP.ClientApp.exe";
                var KeyTest = Registry.CurrentUser.OpenSubKey("Software", true).OpenSubKey("Classes", true);
                RegistryKey key = KeyTest.CreateSubKey("MisaASPLauncher");
                key.SetValue("URL Protocol", "MisaASPLauncherDev");
                key.CreateSubKey(@"shell\open\command").SetValue("", "\"" + applicationPath + "\" \"%1\"");
            }

            Uri uri = null;
            if(args != null && args.Length > 0)
            {
                try
                {
                    uri = new Uri(args[0].Trim());
                }
                catch (UriFormatException ex)
                {
                    LogUtil.LogError(ex);
                }
            }

            IUriHandler handler = UriHandler.GetHandler();
            if(handler != null)
            {
                handler.HandleUri(uri);
            }
            else
            {
                UriHandler.Register();
                MainForm = new frmAboutUs();

                if(uri != null)
                {
                    MainForm.Shown += (o, e) => new UriHandler().HandleUri(uri);
                }

                Application.Run(MainForm);
            }
        }
    }
}
