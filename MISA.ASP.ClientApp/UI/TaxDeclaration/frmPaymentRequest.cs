using MISA.ASP.ClientApp.BL;
using MISA.ASP.ClientApp.Models.ActionInput.SynTaxDec;
using MISA.ASP.ClientApp.Models.ActionOutput.SyncTaxDec;
using MISA.ASP.ClientApp.Models.Enums;
using MISA.ASP.ClientApp.Models.EtaxCrawler;
using MISA.ASP.ClientApp.Models.Exceptions;
using MISA.ASP.ClientApp.Utils.Clients;
using MISA.ASP.ClientApp.Utils.Logging;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Configuration;
using System.Data;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using MISA.ASP.ClientApp.UI.Common;
using MISA.ASP.ClientApp.Utils.FileHandler;

namespace MISA.ASP.ClientApp.UI.TaxDeclaration
{
    public partial class frmPaymentRequest : Form
    {
        private ASPClient _aspClient { get; set; }
        public frmPaymentRequest()
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
                    _ = SyncPaymentRequest(actionID, data);
                });
            }
        }

        private void frmPaymentRequest_FormClosing(object sender, FormClosingEventArgs e)
        {
            Program.InProcessing = false;
        }

        public async Task SyncPaymentRequest(string actionID, string data)
        {
            try
            {
                var output = new SyncPaymentRequestOutput() { ActionID = actionID };
                var syncInput = JsonConvert.DeserializeObject<SyncPaymentRequestInput>(data);
                UpdateProgress(10);
                if (syncInput != null && syncInput.Details != null && syncInput.Details.Count > 0)
                {
                    var isLocalMode = bool.Parse(ConfigurationManager.AppSettings["IsLocalMode"]);
                    foreach (var detail in syncInput.Details)
                    {
                        var iOutputDetail = new SyncPaymentRequestOutputDetail() { CustomerID = detail.CustomerID };
                        try
                        {
                            if (!isLocalMode)
                            {
                                //detail.TaxCode = "0318585992";
                                //detail.Username = "0318585992-ql";
                                //detail.Password = "12345678@Abc";
                                var crawler = new ETaxCrawler(syncInput.ProfileID, detail.CustomerID, detail.TaxCode, detail.Username, detail.Password, CancellationToken.None);
                                var lstPaymentRequest = await crawler.GetPaymentRequest(detail, syncInput.FromDate, syncInput.ToDate);
                                iOutputDetail.PaymentRequests = lstPaymentRequest;
                            }
                            else
                            {
                                using (var r = new StreamReader($"{Path.Combine(AppDomain.CurrentDomain.BaseDirectory, AppDomain.CurrentDomain.RelativeSearchPath ?? "")}"))
                                {
                                    string json = r.ReadToEnd();
                                    List<PaymentRequest> items = JsonConvert.DeserializeObject<List<PaymentRequest>>(json);
                                    iOutputDetail.PaymentRequests = items;
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
                            AppendProgress(Math.Max(1, 70 / syncInput.Details.Count));
                            output.Details.Add(iOutputDetail);
                        }
                    }
                }

                if (output.Details.Count > 0)
                {
                    _aspClient.SetAuthentication(syncInput.AccessToken);
                    //await _aspClient.InsertPaymentRequest(syncInput.ProfileID, output);
                    UpdateProgress(80);
                    //await UploadFileToServer(syncInput.ProfileID, output.Details);
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

        private async Task UploadFileToServer(int profileID, List<SyncPaymentRequestOutputDetail> details)
        {
            if (details != null && details.Count > 0)
            {
                foreach (var iOutput in details)
                {
                    try
                    {
                        if (iOutput.PaymentRequests != null && iOutput.PaymentRequests.Count > 0)
                        {
                            foreach (var iPaymentRequest in iOutput.PaymentRequests)
                            {
                                try
                                {
                                    var localFolderPath = $"{FileUtil.BASE_PATH}/OutputFiles/{profileID}/{iOutput.CustomerID}/PaymentRequest";
                                    if (!String.IsNullOrEmpty(iPaymentRequest.FileName))
                                    {
                                        await _aspClient.UploadPaymentRequestFileToServer(
                                            profileID,
                                            FileTypeEnum.TaxDeclaration,
                                            localFolderPath,
                                            iPaymentRequest.FileName,
                                            iOutput.CustomerID
                                        );
                                        await Task.Delay(200);
                                    }
                                }
                                catch (Exception ex)
                                {
                                    LogUtil.LogError(ex);
                                    // Không throw ex khi 1 tờ khai upload lỗi
                                }
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        LogUtil.LogError(ex);
                        // Không throw ex khi 1 khách hàng lỗi
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
                var frmSuccess = new frmSuccess("Đã hoàn thành tra cứu giấy nộp tiền");
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
                var frmError = new frmError("Đã có lỗi xảy ra, vui lòng thực hiện tra cứu lại");
                frmError.ShowDialog();
            }
        }
        #endregion
    }
}
