using MISA.ASP.ClientApp.BL;
using MISA.ASP.ClientApp.Models.ActionInput.SynTaxDec;
using MISA.ASP.ClientApp.Models.ActionOutput.SyncTaxDec;
using MISA.ASP.ClientApp.Models.EtaxCrawler;
using MISA.ASP.ClientApp.Utils.Clients;
using MISA.ASP.ClientApp.Utils.Logging;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Configuration;
using System.Data;
using System.IO;
using MISA.ASP.ClientApp.UI.Common;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using MISA.ASP.ClientApp.Models.Enums;
using MISA.ASP.ClientApp.Models.Exceptions;
using MISA.ASP.ClientApp.Utils.FileHandler;
using System.Linq;

namespace MISA.ASP.ClientApp.UI.TaxDeclaration
{
    public partial class frmTaxDecSubmitted : Form
    {
        private ASPClient _aspClient { get; set; }

        public frmTaxDecSubmitted()
        {
            InitializeComponent();
            Program.InProcessing = true;
            this.TopMost = true;
            _aspClient = new ASPClient();
        }

        public void ShowForm(string actionID, string data)
        {
            if (InvokeRequired)
            {
                BeginInvoke(new Action<string, string>(ShowForm), actionID, data);
            }
            else
            {
                //this.Show();
                UpdateProgress(5);
                Task.Factory.StartNew(() =>
                {
                    _  = SyncTaxDecSubmitted(actionID, data);
                });
            }
        }

        private void frmTaxDecSubmitted_FormClosing(object sender, FormClosingEventArgs e)
        {
            Program.InProcessing = false;
        }

        public async Task SyncTaxDecSubmitted(string actionID, string data)
        {
            try
            {
                var output = new SyncTaxDecSubmittedOutput() { ActionID = actionID };
                var syncInput = JsonConvert.DeserializeObject<SyncTaxDecSubmittedInput>(data);
                UpdateProgress(10);
                if (syncInput != null && syncInput.Details != null && syncInput.Details.Count > 0)
                {
                    bool isFromDvc = syncInput.SyncFromType == EnumSyncFromType.Dvc;
                    //isFromDvc = true;
                    //detail.TaxCode = "0316966632";
                    //detail.Username = "0316966632-QL";
                    //detail.Password = "Td@1234";
                    var isLocalMode = bool.Parse(ConfigurationManager.AppSettings["IsLocalMode"]);
                    foreach (var detail in syncInput.Details)
                    {
                        var iOutputDetail = new SyncTaxDecSubmittedOutputDetail() { CustomerID = detail.CustomerID };
                        try
                        {
                            if (!isLocalMode)
                            {
                                if (isFromDvc)
                                {
                                    var publicServiceCrawler = new PublicServiceCrawler(syncInput.ProfileID, detail.CustomerID, detail.TaxCode, detail.Username, detail.Password, detail.AccountType, CancellationToken.None);
                                    var taxDecSubmitted = await publicServiceCrawler.GetTaxDecSubmitted(syncInput.FromDate, syncInput.ToDate);
                                    iOutputDetail.TaxDeclarationSubmitteds = taxDecSubmitted;
                                }
                                else
                                {
                                    var crawler = new ETaxCrawler(syncInput.ProfileID, detail.CustomerID, detail.TaxCode, detail.Username, detail.Password, CancellationToken.None);
                                    var taxDecSubmitted = await crawler.GetTaxDecSubmitted(syncInput.FromDate, syncInput.ToDate);
                                    iOutputDetail.TaxDeclarationSubmitteds = taxDecSubmitted;
                                }
                            }
                            else
                            {
                                using (var r = new StreamReader($"{Path.Combine(AppDomain.CurrentDomain.BaseDirectory, AppDomain.CurrentDomain.RelativeSearchPath ?? "")}"))
                                {
                                    string json = r.ReadToEnd();
                                    List<TaxDeclarationSubmitted> items = JsonConvert.DeserializeObject<List<TaxDeclarationSubmitted>>(json);
                                    iOutputDetail.TaxDeclarationSubmitteds = items;
                                }
                            }
                        }
                        catch (InvalidCaptchaException ex)
                        {
                            iOutputDetail.CrawlErrorType = CrawlErrorType.InvalidCaptcha;
                            LogUtil.LogError(ex);
                        }
                        catch (InvalidIdentityException ex)
                        {
                            iOutputDetail.CrawlErrorType = CrawlErrorType.InvalidIdentity;
                            LogUtil.LogError(ex);
                        }
                        catch (PasswordExpiredException ex)
                        {
                            iOutputDetail.CrawlErrorType = CrawlErrorType.PasswordExpired;
                            LogUtil.LogError(ex);
                        }
                        catch (InvalidTaxCodeException ex)
                        {
                            iOutputDetail.CrawlErrorType = CrawlErrorType.InvalidTaxCode;
                            LogUtil.LogError(ex);
                        }
                        catch (Exception ex)
                        {
                            iOutputDetail.CrawlErrorType = CrawlErrorType.Other;
                            LogUtil.LogError(ex);
                        }
                        finally
                        {
                            AppendProgress(70 / syncInput.Details.Count);
                            output.Details.Add(iOutputDetail);
                        }
                    }
                }

                if(output.Details.Count > 0)
                {
                    _aspClient.SetAuthentication(syncInput.AccessToken);
                    await _aspClient.InsertTaxDecSubmitted(syncInput.ProfileID, output);
                    UpdateProgress(80);
                    await UploadFileToServer(syncInput.ProfileID, output.Details);
                }
                
                UpdateProgress(100);
                ShowSuccessForm();
            }
            catch (Exception ex)
            {
                LogUtil.LogError(ex);
                ShowErrorForm();
            }
            finally
            {
                Program.InProcessing = false;
            }
        }

        private async Task UploadFileToServer(int profileID, List<SyncTaxDecSubmittedOutputDetail> details)
        {
            if (details == null || details.Count == 0) return;

            foreach (var iOutput in details)
            {
                if (iOutput.TaxDeclarationSubmitteds == null) continue;

                foreach (var iTaxDec in iOutput.TaxDeclarationSubmitteds)
                {
                    var basePath = $"{FileUtil.BASE_PATH}/OutputFiles/{profileID}/{iOutput.CustomerID}/{iTaxDec.TransactionID}";
                    if (!Directory.Exists(basePath)) continue;

                    // ---------------------------------------------------------------
                    // Create sub‑folders: Declarations, Documents, Notifications
                    // ---------------------------------------------------------------
                    var declFolder = Path.Combine(basePath, "Declarations");
                    var docFolder = Path.Combine(basePath, "Documents");
                    var notiFolder = Path.Combine(basePath, "Notifications");
                    Directory.CreateDirectory(declFolder);
                    Directory.CreateDirectory(docFolder);
                    Directory.CreateDirectory(notiFolder);

                    // ---------------------------------------------------------------
                    // Move existing files into their proper folder
                    // ---------------------------------------------------------------
                    var allFiles = Directory.GetFiles(basePath);
                    // Build a quick lookup for notification file names
                    var notiNames = new HashSet<string>(
                        iTaxDec.Notifications?.Select(n => n.FileName ?? string.Empty) ?? Enumerable.Empty<string>(),
                        StringComparer.OrdinalIgnoreCase);

                    foreach (var filePath in allFiles)
                    {
                        var fileName = Path.GetFileName(filePath);
                        // Main declaration file (iTaxDec.FileName) stays in Declarations
                        string targetFolder;
                        if (!string.IsNullOrEmpty(iTaxDec.FileName) &&
                            fileName.Equals(iTaxDec.FileName, StringComparison.OrdinalIgnoreCase))
                        {
                            targetFolder = declFolder;
                        }
                        // Notification files go to Notifications
                        else if (notiNames.Contains(fileName))
                        {
                            targetFolder = notiFolder;
                        }
                        // Everything else is treated as a document (report, annex, pdf …)
                        else
                        {
                            targetFolder = docFolder;
                        }

                        var destPath = Path.Combine(targetFolder, fileName);
                        if (!File.Exists(destPath))
                        {
                            File.Move(filePath, destPath);
                        }
                    }

                    // ---------------------------------------------------------------
                    // Upload files from each folder
                    // ---------------------------------------------------------------
                    // 1️⃣ Upload main declaration (if exists)
                    if (!string.IsNullOrEmpty(iTaxDec.FileName))
                    {
                        try
                        {
                            await _aspClient.UploadTaxDecFileToServer(
                                profileID,
                                FileTypeEnum.TaxDeclaration,
                                declFolder,
                                iTaxDec.FileName,
                                iOutput.CustomerID,
                                iTaxDec.DisplayTransactionID);
                        }
                        catch (Exception exDecl)
                        {
                            LogUtil.LogError(exDecl);
                        }
                    }

                    // 2️⃣ Upload all document files (PDF, annexes, other XML not the main declaration)
                    foreach (var docPath in Directory.GetFiles(docFolder))
                    {
                        var docName = Path.GetFileName(docPath);

                        try
                        {
                            await _aspClient.UploadTaxDecFileToServer(
                                profileID,
                                FileTypeEnum.TaxDeclaration,
                                docFolder,
                                docName,
                                iOutput.CustomerID,
                                iTaxDec.DisplayTransactionID);
                        }
                        catch (Exception exDoc)
                        {
                            LogUtil.LogError(exDoc);
                        }
                    }

                    // 3️⃣ Upload notification files
                    foreach (var notiPath in Directory.GetFiles(notiFolder))
                    {
                        var notiName = Path.GetFileName(notiPath);
                        try
                        {
                            await _aspClient.UploadTaxDecFileToServer(
                                profileID,
                                FileTypeEnum.TaxNotification,
                                notiFolder,
                                notiName,
                                iOutput.CustomerID,
                                iTaxDec.DisplayTransactionID);
                        }
                        catch (Exception exNoti)
                        {
                            LogUtil.LogError(exNoti);
                        }
                    }
                }
            }
        }

        #region Thao tác với Form Control
        private void UpdateProgress(int value)
        {
            if (InvokeRequired)
            {
                BeginInvoke(new Action<int>(UpdateProgress), value);
            }
            else
            {
                progressBar1.Value = value;
            }
        }

        private void AppendProgress(int value)
        {
            if (InvokeRequired)
            {
                BeginInvoke(new Action<int>(AppendProgress), value);
            }
            else
            {
                progressBar1.Value = Math.Min(100, progressBar1.Value + value);
            }
        }

        private void ShowSuccessForm()
        {
            if (InvokeRequired)
            {
                BeginInvoke(new Action(ShowSuccessForm));
            }
            else
            {
                var frmSuccess = new frmSuccess("Đã hoàn thành đồng bộ tờ khai");
                frmSuccess.ShowDialog();
            }
        }

        private void ShowErrorForm()
        {
            if (InvokeRequired)
            {
                BeginInvoke(new Action(ShowErrorForm));
            }
            else
            {
                var frmError = new frmError("Đã có lỗi xảy ra, vui lòng thực hiện đồng bộ lại");
                frmError.ShowDialog();
            }
        }
        #endregion
    }
}
