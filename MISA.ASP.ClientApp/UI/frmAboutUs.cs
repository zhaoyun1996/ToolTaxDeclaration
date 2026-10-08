using Microsoft.Win32;
using MISA.ASP.ClientApp.BL;
using MISA.ASP.ClientApp.Models;
using MISA.ASP.ClientApp.UI.TaxDeclaration;
using MISA.ASP.ClientApp.Utils.Clients;
using MISA.ASP.ClientApp.Utils.FileHandler;
using MISA.ASP.ClientApp.Utils.Logging;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Configuration;
using System.Data;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace MISA.ASP.ClientApp.UI
{
    public partial class frmAboutUs : Form
    {
        public frmAboutUs()
        {
            InitializeComponent();
        }

        

        public async Task HandleUri(Uri uri)
        {
            try
            {
                if (InvokeRequired)
                {
                    BeginInvoke(new Func<Uri, Task>(HandleUri), uri);
                }
                else
                {
                    if(!Program.InProcessing)
                    {
                        if( uri != null && !String.IsNullOrEmpty(uri.ToString()))
                        {
                            LogUtil.LogInfo($"Start Handle Uri: {uri}");
                            var lstParam = uri.ToString().Split(':');
                            var actionIDKey = lstParam[1];
                            var aspClient = new ASPClient();

                            var versionInfor = await aspClient.GetLastestVersion();
                            if(false)
                            {
                                var fNewVersion = new frmNewVersion(versionInfor.LinkDownload);
                                fNewVersion.ShowDialog();
                            }
                            else
                            {
                                var actionInfor = await aspClient.GetActionInfor(actionIDKey);
                                actionInfor.ID = actionIDKey;
                                LogUtil.LogInfo($"GetActionInfor: {JsonConvert.SerializeObject(actionInfor)}");

                                for (int i = Application.OpenForms.Count - 1; i >= 0; i--)
                                {
                                    if (Application.OpenForms[i].Name != "frmAboutUs")
                                        Application.OpenForms[i].Close();
                                }

                                switch (actionInfor.ActionType)
                                {
                                    case Models.Enums.ActionType.SyncTaxDecRegistered:
                                        frmTaxDecRegistered frmTaxDecRegistered = new frmTaxDecRegistered();
                                        frmTaxDecRegistered.ShowForm(actionIDKey, actionInfor.Data);
                                        break;
                                    case Models.Enums.ActionType.SyncTaxDecSubmitted:
                                    case Models.Enums.ActionType.SyncDvcDecSubmitted:
                                        frmTaxDecSubmitted frmTaxDecSubmitted = new frmTaxDecSubmitted();
                                        frmTaxDecSubmitted.ShowForm(actionIDKey, actionInfor.Data);
                                        break;
                                    case Models.Enums.ActionType.CheckETaxAccount:
                                        frmCheckETaxAccount frmCheckETaxAccount = new frmCheckETaxAccount();
                                        frmCheckETaxAccount.ShowForm(actionIDKey, actionInfor.Data);
                                        break;
                                    case Models.Enums.ActionType.SyncPaymentRequest:
                                        frmPaymentRequest frmPaymentRequest = new frmPaymentRequest();
                                        frmPaymentRequest.ShowForm(actionIDKey, actionInfor.Data);
                                        break;
                                    case Models.Enums.ActionType.OpenITaxViewer:
                                        var eTaxViewer = new ETaxViewer();
                                        _ = eTaxViewer.OpenFile(actionInfor.Data);
                                        break;
                                    default:
                                        break;
                                }
                            }

                            this.WindowState = FormWindowState.Minimized;
                        }
                    }
                    else
                    {
                        MessageBox.Show("Không thể thực hiện do một tác vụ khác đang trong quá trình xử lý");
                    }
                }
            }
            catch (Exception ex)
            {
                LogUtil.LogError(ex);
            }
        }

        private void frmAboutUs_FormClosing(object sender, FormClosingEventArgs e)
        {
            if(e.CloseReason == CloseReason.UserClosing)
            {
                e.Cancel = true;
                this.WindowState = FormWindowState.Minimized;
            }
        }

        private void frmAboutUs_Resize(object sender, EventArgs e)
        {
            if(this.WindowState == FormWindowState.Minimized)
            {
                Hide();
                notifyIcon1.Visible = true;
            }
        }

        private void notifyIcon1_MouseDoubleClick(object sender, MouseEventArgs e)
        {
            Show();
            this.WindowState = FormWindowState.Normal;
            notifyIcon1.Visible = false;
        }

        private void notifyIcon1_MouseClick(object sender, MouseEventArgs e)
        {
            contextMenuStrip1.Show(Control.MousePosition);
        }

        private void contextMenuStrip1_ItemClicked(object sender, ToolStripItemClickedEventArgs e)
        {
            switch (e.ClickedItem.Text)
            {
                case "Quit":
                    Application.Exit();
                    break;
                default:
                    break;
            }
        }
    }
}
