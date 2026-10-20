using HtmlAgilityPack;
using MISA.ASP.ClientApp.Models;
using MISA.ASP.ClientApp.Models.Enums;
using MISA.ASP.ClientApp.Models.EtaxCrawler;
using MISA.ASP.ClientApp.Models.Exceptions;
using MISA.ASP.ClientApp.Utils.Clients;
using MISA.ASP.ClientApp.Utils.DesignPattern;
using MISA.ASP.ClientApp.Utils.FileHandler;
using MISA.ASP.ClientApp.Utils.Logging;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Configuration;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Xml;

namespace MISA.ASP.ClientApp.BL
{
    public class ETaxCrawler
    {
        private readonly static string ETAX_URL = "https://thuedientu.gdt.gov.vn";

        private int _profileID { get; set; }
        private int _customerID { get; set; }
        private string _taxcode { get; set; }
        private string _username { get; set; }
        private string _password { get; set; }
        private string _sessionID { get; set; }
        private string _processorID { get; set; }
        private CookieContainer _cookieContainer { get; set; }
        private HttpClient _client { get; set; }
        private HttpClientHandler _handler { get; set; }
        CancellationToken _stoppingToken { get; set; }

        public ETaxCrawler(int profileID, int customerID, string taxcode, string username, string password, CancellationToken stoppingToken)
        {
            _profileID = profileID;
            _customerID = customerID;
            _taxcode = taxcode;
            _username = username;
            _password = password;
            _cookieContainer = new CookieContainer();
            _handler = new HttpClientHandler() { CookieContainer = _cookieContainer };
            _client = new HttpClient(_handler) { BaseAddress = new Uri(ETAX_URL) };

            _client.DefaultRequestHeaders.Add("Accept-Encoding", "gzip, deflate, br");
            _client.DefaultRequestHeaders.Add("Accept-Language", "vi-VN,vi;q=0.9,fr-FR;q=0.8,fr;q=0.7,en-US;q=0.6,en;q=0.5");
            //_client.DefaultRequestHeaders.Add("Connection", "keep-alive");
            _client.DefaultRequestHeaders.Add("Host", "thuedientu.gdt.gov.vn");
            _client.DefaultRequestHeaders.Add("sec-ch-ua", "\"Google Chrome\";v=\"105\", \"Not)A;Brand\";v=\"8\", \"Chromium\";v=\"105\"");
            _client.DefaultRequestHeaders.Add("sec-ch-ua-mobile", "?0");
            _client.DefaultRequestHeaders.Add("sec-ch-ua-platform", "Windows");
            _client.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/105.0.0.0 Safari/537.36");

            _stoppingToken = stoppingToken;
        }

        #region Các bước vượt qua thủ tục đăng nhập và các page chung
        private async Task Step1_GotoJumpPage()
        {
            HttpResponseMessage responseMessage = null;

            try
            {
                LogUtil.LogTrace("Step1_GotoJumpPage.Start");
                responseMessage = await _client.GetAsync("/");
                responseMessage.EnsureSuccessStatusCode();

                var html = await responseMessage.Content.ReadAsStringAsync();
                string pattern = @"&dse_sessionId=(.*?)&";
                foreach (Match match in Regex.Matches(html, pattern))
                {
                    if (match.Success && match.Groups.Count > 0)
                    {
                        _sessionID = match.Groups[1].Value;
                    }
                }

                if(String.IsNullOrWhiteSpace(_sessionID))
                {
                    throw new NotFoundSessionIdException();
                }
                LogUtil.LogTrace("Step1_GotoJumpPage.End");
            }
            catch (Exception ex)
            {
                LogUtil.LogError(ex, responseMessage);
                throw ex;
            }
        }

        private async Task Step2_GotoIndexPage()
        {
            HttpResponseMessage responseMessage = null;

            try
            {
                LogUtil.LogTrace("Step2_GotoIndexPage.Start");
                responseMessage = await _client.GetAsync($"/etaxnnt/Request?" +
                    $"&dse_sessionId={_sessionID}" +
                    $"&dse_applicationId=-1" +
                    $"&dse_pageId=1" +
                    $"&dse_operationName=corpJumpProc" +
                    $"&dse_errorPage=error_page.jsp" +
                    $"&dse_processorState=initial" +
                    $"&dse_nextEventName=start" +
                    $"&toOpName=corpIndexProc");

                responseMessage.EnsureSuccessStatusCode();
                LogUtil.LogTrace("Step2_GotoIndexPage.End");
            }
            catch (Exception ex)
            {
                LogUtil.LogError(ex, responseMessage);
                throw ex;
            }
        }

        private async Task Step3_GotoHomePage()
        {
            HttpResponseMessage responseMessage = null;

            try
            {
                LogUtil.LogTrace("Step3_GotoHomePage.Start");
                responseMessage = await _client.GetAsync($"/etaxnnt/Request?" +
                    $"&dse_sessionId={_sessionID}" +
                    $"&dse_applicationId=-1" +
                    $"&dse_pageId=3" +
                    $"&dse_operationName=corpIndexProc" +
                    $"&dse_errorPage=error_page.jsp" +
                    $"&dse_processorState=initial" +
                    $"&dse_nextEventName=home");

                responseMessage.EnsureSuccessStatusCode();
                LogUtil.LogTrace("Step3_GotoHomePage.End");
            }
            catch (Exception ex)
            {
                LogUtil.LogError(ex, responseMessage);
                throw ex;
            }
        }

        private async Task Step4_GotoLoginPage()
        {
            HttpResponseMessage responseMessage = null;

            try
            {
                LogUtil.LogTrace("Step4_GotoLoginPage.Start");
                responseMessage = await _client.GetAsync($"/etaxnnt/Request?" +
                    $"&dse_sessionId={_sessionID}" +
                    $"&dse_applicationId=-1" +
                    $"&dse_pageId=4" +
                    $"&dse_operationName=corpIndexProc" +
                    $"&dse_errorPage=error_page.jsp" +
                    $"&dse_processorState=initial" +
                    $"&dse_nextEventName=login");

                responseMessage.EnsureSuccessStatusCode();
                LogUtil.LogTrace("Step4_GotoLoginPage.End");
            }
            catch (Exception ex)
            {
                LogUtil.LogError(ex, responseMessage);
                throw ex;
            }
        }

        private async Task<CaptchaResult> Step5_ResolveCaptcha()
        {
            var captcha = string.Empty;
            HttpResponseMessage responseMessage = null;
            CaptchaResult captchaV2Result = new CaptchaResult();
            try
            {
                LogUtil.LogTrace("Step5_ResolveCaptcha.Start");
                responseMessage = await _client.GetAsync($"/etaxnnt/servlet/ImageServlet");
                responseMessage.EnsureSuccessStatusCode();
                var captchaResolver = new MisaCaptchaClient();
                var stream = await responseMessage.Content.ReadAsStreamAsync();
                captcha = await captchaResolver.Decaptcha(new CaptchaRequest
                {
                    Stream = stream,
                });
                if (String.IsNullOrWhiteSpace(captcha))
                {
                    throw new UnResolvedCaptchaException();
                }
                captchaV2Result.ByteArray = FileUtil.ConvertStreamToByteArray(stream);
                captchaV2Result.Result = captcha;
                LogUtil.LogTrace($"Step5_ResolveCaptcha.Captcha: {captcha}");
                LogUtil.LogTrace("Step5_ResolveCaptcha.End");
            }
            catch (Exception ex)
            {
                LogUtil.LogError(ex, responseMessage);
                throw ex;
            }

            return captchaV2Result;
        }

        private async Task Step6_PostLoginForm(CaptchaResult captcha)
        {
            HttpResponseMessage responseMessage = null;

            try
            {
                LogUtil.LogTrace("Step6_PostLoginForm.Start");
                _client.DefaultRequestHeaders.Clear();
                _client.DefaultRequestHeaders.Add("Accept", "text/html,application/xhtml+xml,application/xml;q=0.9,image/avif,image/webp,image/apng,*/*;q=0.8,application/signed-exchange;v=b3;q=0.9");
                _client.DefaultRequestHeaders.Add("Accept-Encoding", "gzip, deflate, br");
                _client.DefaultRequestHeaders.Add("Accept-Language", "vi-VN,vi;q=0.9,fr-FR;q=0.8,fr;q=0.7,en-US;q=0.6,en;q=0.5");
                _client.DefaultRequestHeaders.Add("Connection", "keep-alive");
                _client.DefaultRequestHeaders.Add("Host", "thuedientu.gdt.gov.vn");
                _client.DefaultRequestHeaders.Add("Referer", "https://thuedientu.gdt.gov.vn/etaxnnt/Request");
                _client.DefaultRequestHeaders.Add("sec-ch-ua", "\"Google Chrome\";v=\"105\", \"Not)A;Brand\";v=\"8\", \"Chromium\";v=\"105\"");
                _client.DefaultRequestHeaders.Add("sec-ch-ua-mobile", "?0");
                _client.DefaultRequestHeaders.Add("sec-ch-ua-platform", "Windows");
                _client.DefaultRequestHeaders.Add("Sec-Fetch-Dest", "document");
                _client.DefaultRequestHeaders.Add("Sec-Fetch-Mode", "navigate");
                _client.DefaultRequestHeaders.Add("Sec-Fetch-Site", "same-origin");
                _client.DefaultRequestHeaders.Add("Upgrade-Insecure-Requests", "1");
                _client.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/105.0.0.0 Safari/537.36");

                var content = new FormUrlEncodedContent(new[]
                {
                    new KeyValuePair<string, string>("dse_sessionId", _sessionID),
                    new KeyValuePair<string, string>("dse_applicationId", "-1"),
                    new KeyValuePair<string, string>("dse_pageId", "5"),
                    new KeyValuePair<string, string>("dse_operationName", "corpUserLoginProc"),
                    new KeyValuePair<string, string>("dse_errorPage", "error_page.jsp"),
                    new KeyValuePair<string, string>("dse_processorState", "initial"),
                    new KeyValuePair<string, string>("dse_nextEventName", "start"),
                    new KeyValuePair<string, string>("showVerifyCode", "show"),
                    new KeyValuePair<string, string>("_userName", _username),
                    new KeyValuePair<string, string>("_password", _password),
                    new KeyValuePair<string, string>("login_type", "01"),
                    new KeyValuePair<string, string>("_verifyCode", captcha.Result),

                });

                responseMessage = await _client.PostAsync("/etaxnnt/Request", content);
                responseMessage.EnsureSuccessStatusCode();
                var html = await responseMessage.Content.ReadAsStringAsync();

                if (html.Contains("Mã xác thực không chính xác"))
                {
                    throw new InvalidCaptchaException();
                }
                else if (html.Contains("Tên đăng nhập hoặc mật khẩu của bạn không chính xác"))
                {
                    throw new InvalidIdentityException();
                }
                LogUtil.LogTrace("Step6_PostLoginForm.End");
            }
            catch (Exception ex)
            {
                LogUtil.LogError(ex, responseMessage);
                throw ex;
            }
        }

        private async Task Step7_GotoMainPage()
        {
            HttpResponseMessage responseMessage = null;

            try
            {
                LogUtil.LogTrace("Step7_GotoMainPage.Start");
                responseMessage = await _client.GetAsync($"/etaxnnt/Request?" +
                    $"&dse_sessionId={_sessionID}" +
                    $"&dse_applicationId=-1" +
                    $"&dse_pageId=6" +
                    $"&dse_operationName=corporateHomeProc" +
                    $"&dse_errorPage=error_page.jsp" +
                    $"&dse_processorState=initial" +
                    $"&dse_nextEventName=start");

                responseMessage.EnsureSuccessStatusCode();
                var html = await responseMessage.Content.ReadAsStringAsync();

                if(html.Contains("Để bảo đảm an toàn vui lòng chọn 'Đồng ý' để thay đổi mật khẩu"))
                {
                    throw new PasswordExpiredException();
                }

                var taxcode = string.Empty;
                string pattern = "Mã số thuế:<\\/span>\r\n                <strong class=\\\"text_den\\\">(.*?)<\\/strong>";
                foreach (Match match in Regex.Matches(html, pattern))
                {
                    if (match.Success && match.Groups.Count > 0)
                    {
                        taxcode = match.Groups[1].Value;
                    }
                }

                if (!string.IsNullOrWhiteSpace(taxcode))
                {
                    if(taxcode != _taxcode)
                    {
                        throw new InvalidTaxCodeException();
                    }
                }
                else
                {
                    // Nếu không đọc được mã số thuế đồng nghĩa với việc không nhận diện được MainPage và đăng nhập đã thất bại
                    throw new UnRecognizedMainPageException();
                }


                LogUtil.LogTrace("Step7_GotoMainPage.End");
            }
            catch (Exception ex)
            {
                LogUtil.LogError(ex, responseMessage);
                throw ex;
            }
        }

        public async Task SignIn()
        {
            await Step1_GotoJumpPage();
            await Task.Delay(1000, _stoppingToken);
            await Step2_GotoIndexPage();
            await Task.Delay(1000, _stoppingToken);
            await Step3_GotoHomePage();
            await Task.Delay(1000, _stoppingToken);
            await Step4_GotoLoginPage();
            await Task.Delay(1000, _stoppingToken);
            var captcha = await Step5_ResolveCaptcha();
            await Step6_PostLoginForm(captcha);
            await Task.Delay(1000, _stoppingToken);
            await Step7_GotoMainPage();
            await Task.Delay(3000, _stoppingToken);
        }
        #endregion

        #region Các bước Đồng bộ tờ khai đã đăng ký
        private async Task<HtmlDocument> Step9_GotoDangKyToKhaiPage()
        {
            HtmlDocument doc = new HtmlDocument();
            HttpResponseMessage responseMessage = null;

            try
            {
                LogUtil.LogTrace("Step9_GotoDangKyToKhaiPage.Start");
                responseMessage = await _client.GetAsync($"/etaxnnt/Request?" +
                        $"&dse_sessionId={_sessionID}" +
                        $"&dse_applicationId=-1" +
                        $"&dse_pageId=7" +
                        $"&dse_operationName=corpDKyTKhaiProc" +
                        $"&dse_processorState=initial" +
                        $"&dse_nextEventName=start");

                responseMessage.EnsureSuccessStatusCode();
                doc.LoadHtml(await responseMessage.Content.ReadAsStringAsync());
                LogUtil.LogTrace("Step9_GotoDangKyToKhaiPage.End");
            }
            catch (Exception ex)
            {
                LogUtil.LogError(ex, responseMessage);
                LogUtil.LogError("Step9_GotoDangKyToKhaiPage.LoadDoc: " + JsonConvert.SerializeObject(doc));
                throw ex;
            }

            return doc;
        }

        private List<TaxDeclarationRegistered> Step10_ExtractTaxDecRegistered(HtmlDocument doc)
        {
            var lstTaxDecRegistered = new List<TaxDeclarationRegistered>();

            try
            {
                LogUtil.LogTrace("Step10_ExtractTaxDecRegistered.Start");
                var lstTrEl = doc.DocumentNode.SelectNodes("//*[@id='dkyTkhaiForm']/div[1]/table/tr");
                LogUtil.LogTrace("Step10_ExtractTaxDecRegistered.SelectNodes.lstTrEl");
                if (lstTrEl != null && lstTrEl.Count > 0)
                {
                    LogUtil.LogTrace("Step10_ExtractTaxDecRegistered.ExtractNodes.lstTrEl");
                    for (int i = 1; i < lstTrEl.Count; i++)
                    {
                        try
                        {
                            var trEl = lstTrEl[i];
                            if (trEl.ChildNodes.Count > 4)
                            {
                                LogUtil.LogTrace($"Step10_ExtractTaxDecRegistered.ExtractNodes.lstTrEl:{i}.ChildNodes.Count:{trEl.ChildNodes.Count}");
                                var childNodes = trEl.ChildNodes.Descendants().ToList();
                                lstTaxDecRegistered.Add(new TaxDeclarationRegistered()
                                {
                                    Code = childNodes[4].GetAttributeValue("value", "").Trim(),
                                    Name = childNodes[1].InnerText.Trim(),
                                    TaxPeriodType = childNodes[2].InnerText.Trim(),
                                    BeginTaxPeriod = childNodes[3].InnerText.Trim()
                                });
                            }
                        }
                        catch (Exception ex)
                        {
                            LogUtil.LogError(ex, "Step10_ExtractTaxDecRegistered.ExtractNodes.lstTrEl Exception");
                        }
                    }
                }
                LogUtil.LogTrace("Step10_ExtractTaxDecRegistered.End");
            }
            catch (Exception ex)
            {
                LogUtil.LogError(ex);
                throw ex;
            }

            return lstTaxDecRegistered;
        }
        #endregion

        #region Các bước Tra cứu tờ khai
        private async Task Step9_GotoTraCuuToKhaiPage()
        {
            HttpResponseMessage responseMessage = null;
            try
            {
                LogUtil.LogTrace("Step9_GotoTraCuuToKhaiPage.Start");
                responseMessage = await _client.GetAsync($"/etaxnnt/Request?" +
                        $"&dse_sessionId={_sessionID}" +
                        $"&dse_applicationId=-1" +
                        $"&dse_pageId=7" +
                        $"&dse_operationName=traCuuToKhaiProc" +
                        $"&dse_processorState=initial" +
                        $"&dse_nextEventName=start");

                responseMessage.EnsureSuccessStatusCode();
                var html = await responseMessage.Content.ReadAsStringAsync();
                string pattern = @"&dse_processorId=(.*?)&";
                foreach (Match match in Regex.Matches(html, pattern))
                {
                    if (match.Success && match.Groups.Count > 0)
                    {
                        _processorID = match.Groups[1].Value;
                    }
                }

                if(String.IsNullOrEmpty(_processorID))
                {
                    throw new NotFoundProcessIdException();
                }

                LogUtil.LogTrace("Step9_GotoTraCuuToKhaiPage.End");
            }
            catch (Exception ex)
            {
                LogUtil.LogError(ex, responseMessage);
                throw ex;
            }
        }

        private async Task<HtmlDocument> Step10_ClickButtonTraCuu(string fromDate, string toDate)
        {
            HtmlDocument doc = new HtmlDocument();
            HttpResponseMessage responseMessage = null;
            try
            {
                LogUtil.LogTrace("Step10_ClickButtonTraCuu.Start");
                var content = new FormUrlEncodedContent(new[]
                   {
                    new KeyValuePair<string, string>("dse_sessionId", _sessionID),
                    new KeyValuePair<string, string>("dse_applicationId", "-1"),
                    new KeyValuePair<string, string>("dse_operationName", "traCuuToKhaiProc"),
                    new KeyValuePair<string, string>("dse_pageId", "10"),
                    new KeyValuePair<string, string>("dse_processorState", "viewTraCuuTkhai"),
                    new KeyValuePair<string, string>("dse_processorId", _processorID),
                    new KeyValuePair<string, string>("dse_errorPage", "error_page.jsp"),
                    new KeyValuePair<string, string>("dse_nextEventName", "query"),
                    new KeyValuePair<string, string>("pn", "1"),
                    new KeyValuePair<string, string>("maTKhai", "00"),
                    new KeyValuePair<string, string>("tenTKhai", ""),
                    new KeyValuePair<string, string>("kieuKy", ""),
                    new KeyValuePair<string, string>("ma_gd", ""),
                    new KeyValuePair<string, string>("qryFromDate", fromDate),
                    new KeyValuePair<string, string>("qryToDate", toDate),
                });

                responseMessage = await _client.PostAsync("/etaxnnt/Request", content);
                responseMessage.EnsureSuccessStatusCode();
                doc.LoadHtml(await responseMessage.Content.ReadAsStringAsync());
                LogUtil.LogTrace("Step10_ClickButtonTraCuu.End");
            }
            catch (Exception ex)
            {
                LogUtil.LogError(ex, responseMessage);
                throw ex;
            }
            return doc;
        }

        private async Task<List<TaxDeclarationSubmitted>> Step11_ExtractTaxDecSubmitted(HtmlDocument doc)
        {
            var lstTaxDecSubmitted = new List<TaxDeclarationSubmitted>();

            try
            {
                LogUtil.LogTrace("Step11_ExtractTaxDecSubmitted.Start");

                // Extract html lấy 1 số thông số cơ bản
                var currentPage = 1;
                var nodeMaxPage = doc.DocumentNode.SelectSingleNode("//*[@id='currAcc']/b[1]");
                if (nodeMaxPage != null)
                {
                    var maxPage = int.Parse(nodeMaxPage.InnerText);

                    LogUtil.LogTrace("Step11_ExtractTaxDecSubmitted.maxPage");

                    // Tạo folder để chứa file theo customerID
                    string folderPath = $"{FileUtil.BASE_PATH}/OutputFiles/{_profileID}/{_customerID}";
                    Directory.CreateDirectory(folderPath);
                    LogUtil.LogTrace("Step11_ExtractTaxDecSubmitted.folderPath");

                    // Extract luôn các tờ khai ở page 1
                    await ExtractTaxDecSubmitted(doc, lstTaxDecSubmitted);
                    LogUtil.LogTrace("Step11_ExtractTaxDecSubmitted.ExtractTaxDecSubmitted.Page.1");

                    // Extract các tờ khai ở các page còn lại nếu có
                    while (currentPage < maxPage)
                    {
                        currentPage += 1;
                        LogUtil.LogTrace($"Step11_ExtractTaxDecSubmitted.ExtractTaxDecSubmitted.Page.{currentPage}");
                        await Step12_ExtractTaxDecSubmitted_NextPage(currentPage, lstTaxDecSubmitted);
                    }
                }
                else
                {
                    LogUtil.LogError("Không đọc được html tổng số page của trang tra cứu tờ khai");
                }
                LogUtil.LogTrace("Step11_ExtractTaxDecSubmitted.End");
            }
            catch (Exception ex)
            {
                throw ex;
            }

            return lstTaxDecSubmitted;
        }

        private async Task Step12_ExtractTaxDecSubmitted_NextPage(int page, List<TaxDeclarationSubmitted> lstTaxDecSubmitted)
        {
            try
            {
                var result = await _client.GetAsync($"/etaxnnt/Request" +
                        $"?dse_sessionId={_sessionID}" +
                        $"&dse_applicationId=-1" +
                        $"&dse_operationName=traCuuToKhaiProc" +
                        $"&dse_pageId=12" +
                        $"&dse_processorState=viewTraCuuTkhai" +
                        $"&dse_processorId={_processorID}" +
                        $"&dse_errorPage=error_page.jsp" +
                        $"&dse_nextEventName=query" +
                        $"&&pn={page}");

                result.EnsureSuccessStatusCode();

                HtmlDocument doc = new HtmlDocument();
                doc.LoadHtml(await result.Content.ReadAsStringAsync());
                await ExtractTaxDecSubmitted(doc, lstTaxDecSubmitted);
            }
            catch (Exception ex)
            {
                throw ex;
            }
        }

        /// <summary>
        /// Trích xuất dữ liệu từ giao diện màn hình kết quả tra cứu tờ khai
        /// </summary>
        /// <param name="sessionID">ID phiên đăng nhập của thuedientu</param>
        /// <param name="cookieContainer">giả lập Cookie</param>
        /// <param name="processorID">ID phiên xử lý</param>
        /// <param name="taxcode">mã số thuế</param>
        /// <param name="doc">Html Document</param>
        /// <param name="prevTransactionID">mã TransactionID của tờ khai phía trên (0 nếu là tờ khai đầu tiên trong danh sách)</param>
        /// <returns></returns>
        private async Task ExtractTaxDecSubmitted(HtmlDocument doc, List<TaxDeclarationSubmitted> lstTaxDecSubmitted)
        {
            try
            {
                LogUtil.LogTrace("ExtractTaxDecSubmitted.Start");
                // Đọc các dòng tr, bỏ qua dòng đầu tiên là header
                var lstTrEl = doc.DocumentNode.SelectNodes("//*[@id='allResultTableBody']/tr");
                LogUtil.LogTrace("ExtractTaxDecSubmitted.SelectNodes.lstTrEl");
                if (lstTrEl != null && lstTrEl.Count > 0)
                {
                    LogUtil.LogTrace($"ExtractTaxDecSubmitted.ExtractNodes.lstTrEl.Count:{lstTrEl.Count}");
                    for (int i = 0; i < lstTrEl.Count; i++)
                    {
                        try
                        {
                            // Đọc các cột td để lấy dữ liệu
                            var trEl = lstTrEl[i];
                            if (trEl.ChildNodes.Count > 0)
                            {
                                LogUtil.LogTrace($"ExtractTaxDecSubmitted.ExtractNodes.lstTrEl:{i}:{JsonConvert.SerializeObject(trEl.ChildNodes.Count)}");
                                var childNodes = trEl.Elements("td").ToList();
                                var iTaxDec = new TaxDeclarationSubmitted()
                                {
                                    TaxCode = _taxcode,
                                    Order = childNodes[0].InnerText.Trim(),
                                    TransactionID = childNodes[1].InnerText.Trim(),
                                    Name = childNodes[2].InnerText.Trim(),
                                    TaxPeriod = childNodes[3].InnerText.Trim(),
                                    DeclarationType = childNodes[4].InnerText.Trim(),
                                    SubmitTimes = childNodes[5].InnerText.Trim(),
                                    AdditionalTimes = childNodes[6].InnerText.Trim(),
                                    SubmitDate = childNodes[7].InnerText.Trim(),
                                    TaxAgencyName = childNodes[9].InnerText.Trim(),
                                    State = childNodes[10].InnerText.Trim(),

                                };
                                LogUtil.LogTrace($"ExtractTaxDecSubmitted.ExtractNodes.lstTrEl.{i}.iTaxDec");
                                // Một số record có mối quan hệ dạng cha-con (có cột STT ví dụ: 3.1) phải tính toán để lưu lại giá trị ở field ParentID
                                if (!string.IsNullOrWhiteSpace(iTaxDec.Order) && iTaxDec.Order.IndexOf(".") > -1)
                                {
                                    var parentOrder = iTaxDec.Order.Split('.')[0];
                                    if (!string.IsNullOrWhiteSpace(parentOrder))
                                    {
                                        var parentTaxDec = lstTaxDecSubmitted.FirstOrDefault(_ => _.Order == parentOrder);
                                        if (parentTaxDec != null)
                                        {
                                            iTaxDec.ParentTransactionID = parentTaxDec.TransactionID;
                                        }
                                    }
                                }


                                LogUtil.LogTrace($"ExtractTaxDecSubmitted.ExtractNodes.lstTrEl.{i}.aTag");
                                // Nếu có thẻ a ẩn ở cột thứ 2 thì Tải file transaction về
                                var aTag = childNodes[2].Element("a");
                                if (aTag != null)
                                {
                                    // Một số record dạng con sẽ không có transactionID ở cột thứ 2, nên cần đọc transactionID ẩn ở trong thẻ a
                                    var hiddenTransactionID = aTag.GetAttributeValue("onclick", "");
                                    if (string.IsNullOrWhiteSpace(iTaxDec.TransactionID) && !string.IsNullOrWhiteSpace(hiddenTransactionID))
                                    {
                                        iTaxDec.IsHideTransactionID = true;
                                        string pattern = @"downloadBke\('(.*?)'\)";
                                        foreach (Match match in Regex.Matches(hiddenTransactionID, pattern))
                                        {
                                            if (match.Success && match.Groups.Count > 0)
                                            {
                                                hiddenTransactionID = match.Groups[1].Value;
                                                // Lúc này transactionID sẽ null nên cần gán lại bằng giá trị của hiddenTransactionID
                                                iTaxDec.TransactionID = hiddenTransactionID;
                                                iTaxDec.HideTransactionID = hiddenTransactionID;
                                            }
                                        }
                                    }
                                }
                                else
                                {
                                    iTaxDec.IsHideDownloadLink = true;
                                }

                                LogUtil.LogTrace($"ExtractTaxDecSubmitted.ExtractNodes.lstTrEl.{i}.aTag.End");
                                if (!string.IsNullOrWhiteSpace(iTaxDec.TransactionID))
                                {
                                    LogUtil.LogTrace($"ExtractTaxDecSubmitted.ExtractNodes.lstTrEl.{i}.folderPath");
                                    // Tạo folder để chứa file theo TransactionID
                                    string folderPath = $"{FileUtil.BASE_PATH}/OutputFiles/{_profileID}/{_customerID}/{iTaxDec.TransactionID}";
                                    Directory.CreateDirectory(folderPath);

                                    if (!iTaxDec.IsHideDownloadLink)
                                    {
                                        await Step13_DownloadTransactionFile(iTaxDec);
                                    }
                                    LogUtil.LogTrace($"ExtractTaxDecSubmitted.Step13_DownloadTransactionFile.lstTrEl.{i}.End");

                                    iTaxDec.IsHideNotificationLink = string.IsNullOrWhiteSpace(childNodes[11].InnerText.Trim());
                                    if (!iTaxDec.IsHideNotificationLink)
                                    {
                                        // Lấy danh sách thông báo
                                        await Step14_GetThongBaoPageData(iTaxDec);
                                    }
                                    LogUtil.LogTrace($"ExtractTaxDecSubmitted.Step14_GetThongBaoPageData.lstTrEl.{i}.End");
                                }

                                // add TaxDec vào collection kết quả
                                lstTaxDecSubmitted.Add(iTaxDec);
                            }
                        }
                        catch (Exception ex)
                        {
                            LogUtil.LogError(ex);
                        }
                    }
                }
                LogUtil.LogTrace("ExtractTaxDecSubmitted.End");
            }
            catch (Exception ex)
            {
                LogUtil.LogError(ex);
            }
        }

        private async Task Step13_DownloadTransactionFile(TaxDeclarationSubmitted iTaxDec)
        {
            try
            {
                LogUtil.LogTrace($"Step13_DownloadTransactionFile.Start");
                string folderPath = $"{FileUtil.BASE_PATH}/OutputFiles/{_profileID}/{_customerID}/{iTaxDec.TransactionID}";
                //var files = Directory.GetFiles(folderPath, $"ETAX{iTaxDec.TransactionID}.*");
                //if (files.Length < 1) // Kiểm tra chưa tồn tại file thì đi tải mới
                //{

                //}

                var result = await _client.GetAsync($"/etaxnnt/Request?" +
                            $"dse_sessionId={_sessionID}" +
                            $"&dse_applicationId=-1" +
                            $"&dse_operationName=traCuuToKhaiProc" +
                            $"&dse_pageId=11" +
                            $"&dse_processorState=viewTraCuuTkhai" +
                            $"&dse_processorId={_processorID}" +
                            $"&dse_nextEventName={(iTaxDec.IsHideTransactionID ? "downBke" : "downTkhai")}" +
                            $"&messageId={iTaxDec.TransactionID}");
                result.EnsureSuccessStatusCode();
                LogUtil.LogTrace($"Step13_DownloadTransactionFile.GetAsync");
                iTaxDec.FileName = result.Content.Headers.ContentDisposition.FileName;
                using (var fs = new FileStream($"{folderPath}\\{iTaxDec.FileName}", FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None))
                {
                    await result.Content.CopyToAsync(fs);
                }
                LogUtil.LogTrace($"Step13_DownloadTransactionFile.SaveFile");
                if (Path.GetExtension(iTaxDec.FileName) == ".xml")
                {
                    LogUtil.LogTrace($"Step13_DownloadTransactionFile.ExtractXml.Start");
                    // Đọc file để trích xuất 1 số thông tin như: Số tiền khấu trừ, số tiền còn nợ, ...
                    XmlDocument doc = new XmlDocument();
                    var xmlString = await result.Content.ReadAsStringAsync();
                    LogUtil.LogTrace($"Step13_DownloadTransactionFile.ExtractXml.LoadXml: {xmlString}");
                    doc.LoadXml(xmlString);
                    
                    XmlNamespaceManager ns = new XmlNamespaceManager(doc.NameTable);
                    ns.AddNamespace("msbld", "http://kekhaithue.gdt.gov.vn/TKhaiThue");
                    LogUtil.LogTrace("Step13_DownloadTransactionFile.ExtractXml.taxDecCode");
                    var taxDecCode = doc.SelectSingleNode("//msbld:maTKhai", ns).InnerText;
                    iTaxDec.Code = taxDecCode;
                    switch (taxDecCode)
                    {
                        case "01":
                            iTaxDec.DebitAmount = doc.SelectSingleNode("//msbld:ct40", ns).InnerText;
                            iTaxDec.CreditAmount = doc.SelectSingleNode("//msbld:ct43", ns).InnerText;
                            break;
                        case "03":
                            iTaxDec.DebitAmount = doc.SelectSingleNode("//msbld:ctG", ns).InnerText;
                            break;
                        case "394":
                            iTaxDec.DebitAmount = doc.SelectSingleNode("//msbld:ct32", ns).InnerText;
                            break;
                        case "395":
                            break;
                        case "842":
                            iTaxDec.DebitAmount = doc.SelectSingleNode("//msbld:ct40", ns).InnerText;
                            iTaxDec.CreditAmount = doc.SelectSingleNode("//msbld:ct43", ns).InnerText;
                            break;
                        case "864":
                            iTaxDec.DebitAmount = doc.SelectSingleNode("//msbld:ct29", ns).InnerText;
                            break;
                        case "892":
                            iTaxDec.DebitAmount = doc.SelectSingleNode("//msbld:ctI", ns).InnerText;
                            break;
                        case "953":
                            break;
                        default:
                            break;
                    }

                    LogUtil.LogTrace("Step13_DownloadTransactionFile.ExtractXml.TaxAgencyCode");
                    iTaxDec.TaxAgencyCode = doc.SelectSingleNode("//msbld:maCQTNoiNop", ns).InnerText;
                    LogUtil.LogTrace($"Step13_DownloadTransactionFile.ExtractXml.End");
                }

                LogUtil.LogTrace($"Step13_DownloadTransactionFile.End");
            }
            catch (Exception ex)
            {
                LogUtil.LogError(ex);
            }
        }

        private async Task Step14_GetThongBaoPageData(TaxDeclarationSubmitted iTaxDec)
        {
            try
            {
                LogUtil.LogTrace("Step14_GetThongBaoPageData.Start");
                var lstTaxDecNotification = new List<TaxDecNotification>();

                var result = await _client.GetAsync($"/etaxnnt/Request" +
                         $"?dse_sessionId={_sessionID}" +
                         $"&dse_applicationId=-1" +
                         $"&dse_operationName=traCuuToKhaiProc" +
                         $"&dse_pageId=14" +
                         $"&dse_processorState=viewTraCuuTkhai" +
                         $"&dse_processorId={_processorID}" +
                         $"&dse_nextEventName=viewTBao" +
                         $"&ctMaGDich={iTaxDec.TransactionID}");

                result.EnsureSuccessStatusCode();

                HtmlDocument doc = new HtmlDocument();
                LogUtil.LogTrace("Step14_GetThongBaoPageData.LoadDoc");
                doc.LoadHtml(await result.Content.ReadAsStringAsync());

                LogUtil.LogTrace("Step14_GetThongBaoPageData.maxPage");
                // Extract html lấy 1 số thông số cơ bản
                var currentPage = 1;
                var maxPage = int.Parse(doc.DocumentNode.SelectSingleNode("//*[@id='currAcc']/b[1]").InnerText);

                LogUtil.LogTrace("Step14_GetThongBaoPageData.Step16_ExtractThongBaoData.1.Start");
                // Extract luôn các thông báo ở page 1
                var firstPageNoti = await Step16_ExtractThongBaoData(doc, iTaxDec);
                lstTaxDecNotification.AddRange(firstPageNoti);

                // Extract các tờ khai ở các page còn lại nếu có
                while (currentPage < maxPage)
                {
                    currentPage += 1;
                    LogUtil.LogTrace($"Step14_GetThongBaoPageData.Step16_ExtractThongBaoData.{currentPage}.Start");
                    var iPageTaxDec = await Step15_GetThongBaoPageDataPaging(currentPage, iTaxDec);
                    lstTaxDecNotification.AddRange(iPageTaxDec);
                }

                iTaxDec.Notifications = lstTaxDecNotification;
                LogUtil.LogTrace("Step14_GetThongBaoPageData.End");
            }
            catch (Exception ex)
            {
                LogUtil.LogError(ex);
            }
        }

        private async Task<List<TaxDecNotification>> Step15_GetThongBaoPageDataPaging(int currentPage, TaxDeclarationSubmitted iTaxDec)
        {
            var lstTaxDecNotification = new List<TaxDecNotification>();

            try
            {
                LogUtil.LogTrace("Step15_GetThongBaoPageDataPaging.Start");
                var result = await _client.GetAsync($"/etaxnnt/Request" +
                        $"?dse_sessionId={_sessionID}" +
                        $"&dse_applicationId=-1" +
                        $"&dse_operationName=traCuuToKhaiProc" +
                        $"&dse_pageId=14" +
                        $"&dse_processorState=viewTraCuuTkhai" +
                        $"&dse_processorId={_processorID}" +
                        $"&dse_nextEventName=viewTBao" +
                        $"&ctMaGDich={iTaxDec.TransactionID}" +
                        $"&&pn={currentPage}");

                result.EnsureSuccessStatusCode();

                HtmlDocument doc = new HtmlDocument();
                LogUtil.LogTrace("Step15_GetThongBaoPageDataPaging.LoadDoc");
                doc.LoadHtml(await result.Content.ReadAsStringAsync());
                lstTaxDecNotification = await Step16_ExtractThongBaoData(doc, iTaxDec);
                LogUtil.LogTrace("Step15_GetThongBaoPageDataPaging.End");
            }
            catch (Exception ex)
            {
                LogUtil.LogError(ex);
            }

            return lstTaxDecNotification;
        }

        private async Task<List<TaxDecNotification>> Step16_ExtractThongBaoData(HtmlDocument doc, TaxDeclarationSubmitted iTaxDec)
        {
            var lstTaxDecNotification = new List<TaxDecNotification>();

            try
            {
                LogUtil.LogTrace("Step16_ExtractThongBaoData.Start");
                // Đọc các dòng tr
                var lstTrEl = doc.DocumentNode.SelectNodes("//*[@id='tbl_tracuu']/tbody/tr");
                LogUtil.LogTrace("Step16_ExtractThongBaoData.lstTrEl");
                if (lstTrEl != null && lstTrEl.Count > 0)
                {
                    for (int i = 0; i < lstTrEl.Count; i++)
                    {
                        LogUtil.LogTrace($"Step16_ExtractThongBaoData.lstTrEl.{i}");
                        try
                        {
                            // Đọc các cột td để lấy dữ liệu
                            var trEl = lstTrEl[i];
                            if (trEl.ChildNodes.Count > 0)
                            {
                                LogUtil.LogTrace($"Step16_ExtractThongBaoData.lstTrEl.{i}.ChildNodes.Count:{trEl.ChildNodes.Count}");
                                var childNodes = trEl.Elements("td").ToList();
                                var iTaxDecNoti = new TaxDecNotification()
                                {
                                    Name = childNodes[1].InnerText.Trim(),
                                    Message = childNodes[2].InnerText.Trim(),
                                    SendDate = childNodes[3].InnerText.Trim(),
                                    SendBy = childNodes[4].InnerText.Trim()
                                };

                                LogUtil.LogTrace($"Step16_ExtractThongBaoData.lstTrEl.{i}.aTag");
                                // Nếu có thẻ a ẩn ở cột thứ 2 thì Tải file notification về
                                var aTag = childNodes[1].Element("a");
                                if (aTag != null)
                                {
                                    LogUtil.LogTrace($"Step16_ExtractThongBaoData.lstTrEl.{i}.hiddenNotificationID");
                                    var hiddenNotificationID = aTag.GetAttributeValue("onclick", "");
                                    if (!string.IsNullOrWhiteSpace(hiddenNotificationID))
                                    {
                                        string pattern = @"downloadFile\('(.*?)'\)";
                                        foreach (Match match in Regex.Matches(hiddenNotificationID, pattern))
                                        {
                                            if (match.Success && match.Groups.Count > 0)
                                            {
                                                hiddenNotificationID = match.Groups[1].Value;
                                                // Lúc này notificationID sẽ null nên cần gán lại bằng giá trị của hiddenTransactionID
                                                iTaxDecNoti.NotificationID = hiddenNotificationID;
                                                LogUtil.LogTrace($"Step16_ExtractThongBaoData.lstTrEl.{i}.hiddenNotificationID.Set");
                                            }
                                        }
                                    }

                                    await Step17_DownloadNotificationFile(iTaxDec, iTaxDecNoti);
                                }

                                lstTaxDecNotification.Add(iTaxDecNoti);
                            }
                        }
                        catch (Exception ex)
                        {
                            LogUtil.LogError(ex);
                        }
                    }
                }
                LogUtil.LogTrace("Step16_ExtractThongBaoData.End");
            }
            catch (Exception ex)
            {
                LogUtil.LogError(ex);
            }

            return lstTaxDecNotification;
        }

        private async Task Step17_DownloadNotificationFile(TaxDeclarationSubmitted iTaxDec, TaxDecNotification iTaxDecNoti)
        {
            try
            {
                LogUtil.LogTrace("Step17_DownloadNotificationFile.Start");
                string folderPath = $"{FileUtil.BASE_PATH}/OutputFiles/{_profileID}/{_customerID}/{iTaxDec.TransactionID}";
                //var files = Directory.GetFiles(folderPath, $"ETAX{iTaxDecNoti.NotificationID}.*");
                //if (files.Length < 1) // Kiểm tra chưa tồn tại file thì đi tải mới
                //{

                //}

                var content = new FormUrlEncodedContent(new[]
                       {
                        new KeyValuePair<string, string>("dse_sessionId", _sessionID),
                        new KeyValuePair<string, string>("dse_applicationId", "-1"),
                        new KeyValuePair<string, string>("dse_pageId", "13"),
                        new KeyValuePair<string, string>("dse_operationName", "traCuuToKhaiProc"),
                        new KeyValuePair<string, string>("dse_errorPage", "error_page.jsp"),
                        new KeyValuePair<string, string>("dse_processorState", "dsTBaoViewPage"),
                        new KeyValuePair<string, string>("dse_processorId", _processorID),
                        new KeyValuePair<string, string>("dse_nextEventName", "download"),
                        new KeyValuePair<string, string>("pn", "1"),
                        new KeyValuePair<string, string>("messageId", iTaxDecNoti.NotificationID),

                    });

                var result = await _client.PostAsync("/etaxnnt/Request", content);
                result.EnsureSuccessStatusCode();
                LogUtil.LogTrace("Step17_DownloadNotificationFile.PostAsync");

                iTaxDecNoti.FileName = result.Content.Headers.ContentDisposition.FileName;
                using (var fs = new FileStream($"{folderPath}\\{iTaxDecNoti.FileName}", FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None))
                {
                    await result.Content.CopyToAsync(fs);
                }

                LogUtil.LogTrace("Step17_DownloadNotificationFile.SaveFile");
                if (Path.GetExtension(iTaxDecNoti.FileName) == ".xml")
                {
                    LogUtil.LogTrace("Step17_DownloadNotificationFile.ExtractXml");
                    // Đọc file để trích xuất 1 số thông tin như: Mã thông báo, Trạng thái chấp thuận, ...
                    XmlDocument doc = new XmlDocument();
                    var xmlString = await result.Content.ReadAsStringAsync();
                    doc.LoadXml(xmlString);
                    LogUtil.LogTrace("Step17_DownloadNotificationFile.LoadDoc");
                    XmlNamespaceManager ns = new XmlNamespaceManager(doc.NameTable);
                    ns.AddNamespace("msbld", "http://kekhaithue.gdt.gov.vn/TBaoThue");

                    var notiCode = doc.SelectSingleNode("//msbld:maTBao", ns).InnerText;
                    LogUtil.LogTrace("Step17_DownloadNotificationFile.notiCode");
                    iTaxDecNoti.Code = notiCode;
                    iTaxDecNoti.XMLTrangThai = doc.SelectSingleNode("//msbld:trangThai", ns).InnerText;
                    iTaxDecNoti.XMLTenTBao = doc.SelectSingleNode("//msbld:tenTBao", ns).InnerText;
                    LogUtil.LogTrace("Step17_DownloadNotificationFile.XMLTrangThai");
                }
                LogUtil.LogTrace("Step17_DownloadNotificationFile.End");
            }
            catch (Exception ex)
            {
                LogUtil.LogError(ex);
            }
        }
        #endregion

        #region Các bước tra cứu Giấy nộp tiền
        private async Task Step9_GotoTraCuuGiayNopTienPage()
        {
            HttpResponseMessage responseMessage = null;

            try
            {
                LogUtil.LogTrace("Step9_GotoTraCuuGiayNopTienPage.Start");
                responseMessage = await _client.GetAsync($"/etaxnnt/Request?" +
                        $"&dse_sessionId={_sessionID}" +
                        $"&dse_applicationId=-1" +
                        $"&dse_pageId=7" +
                        $"&dse_operationName=corpQueryTaxProc" +
                        $"&dse_processorState=initial" +
                        $"&dse_nextEventName=start");

                responseMessage.EnsureSuccessStatusCode();
                var html = await responseMessage.Content.ReadAsStringAsync();
                string pattern = @"name=""dse_processorId"" value=""(.*?)""";
                foreach (Match match in Regex.Matches(html, pattern))
                {
                    if (match.Success && match.Groups.Count > 0)
                    {
                        _processorID = match.Groups[1].Value;
                    }
                }

                if (String.IsNullOrEmpty(_processorID))
                {
                    throw new NotFoundProcessIdException();
                }
                LogUtil.LogTrace("Step9_GotoTraCuuGiayNopTienPage.End");
            }
            catch (Exception ex)
            {
                LogUtil.LogError(ex, responseMessage);
                throw ex;
            }
        }

        private async Task<HtmlDocument> Step10_ClickButtonTraCuuGiayNopTien(string fromDate, string toDate, string typeTax = "01")
        {
            HtmlDocument doc = new HtmlDocument();
            HttpResponseMessage responseMessage = null;
            try
            {
                LogUtil.LogTrace($"Step10_ClickButtonTraCuuGiayNopTien.Start.type_tax={typeTax}");
                var content = new FormUrlEncodedContent(new[]
                {
                    new KeyValuePair<string, string>("dse_sessionId", _sessionID),
                    new KeyValuePair<string, string>("dse_applicationId", "-1"),
                    new KeyValuePair<string, string>("dse_operationName", "corpQueryTaxProc"),
                    new KeyValuePair<string, string>("dse_pageId", "10"),
                    new KeyValuePair<string, string>("dse_processorState", "viewQueryPage"),
                    new KeyValuePair<string, string>("dse_processorId", _processorID),
                    new KeyValuePair<string, string>("dse_errorPage", "/etax/query_tax_information.jsp"),
                    new KeyValuePair<string, string>("dse_nextEventName", "query"),
                    new KeyValuePair<string, string>("pn", "1"),
                    new KeyValuePair<string, string>("sct", ""),
                    new KeyValuePair<string, string>("ctuId", ""),
                    new KeyValuePair<string, string>("soGnt", ""),
                    new KeyValuePair<string, string>("idBke", ""),
                    new KeyValuePair<string, string>("type_tax", typeTax),
                    new KeyValuePair<string, string>("ma_giao_dich", ""),
                    new KeyValuePair<string, string>("so_ctu_nh", ""),
                    new KeyValuePair<string, string>("so_gnt", ""),
                    new KeyValuePair<string, string>("ngay_lap_tu_ngay", fromDate),
                    new KeyValuePair<string, string>("ngay_lap_den_ngay", toDate),
                    new KeyValuePair<string, string>("ngay_gui_tu_ngay", ""),
                    new KeyValuePair<string, string>("ngay_gui_den_ngay", ""),
                    new KeyValuePair<string, string>("ngay_nop_tu_ngay", typeTax == "02" ? fromDate : ""),
                    new KeyValuePair<string, string>("ngay_nop_den_ngay", typeTax == "02" ? toDate : ""),
                    new KeyValuePair<string, string>("ma_nhang", ""),
                    new KeyValuePair<string, string>("so_tk", ""),
                    new KeyValuePair<string, string>("nguyen_te", ""),
                    new KeyValuePair<string, string>("tong_tien_nt_tu", ""),
                    new KeyValuePair<string, string>("tong_tien_nt_den", ""),
                    new KeyValuePair<string, string>("trang_thai", ""),
                });

                responseMessage = await _client.PostAsync("/etaxnnt/Request", content);
                responseMessage.EnsureSuccessStatusCode();
                doc.LoadHtml(await responseMessage.Content.ReadAsStringAsync());
                LogUtil.LogTrace($"Step10_ClickButtonTraCuuGiayNopTien.End.type_tax={typeTax}");
            }
            catch (Exception ex)
            {
                LogUtil.LogError(ex, responseMessage);
                throw ex;
            }
            return doc;
        }

        private async Task<List<PaymentRequest>> Step11_ExtractPaymentRequest(string fromDate, string toDate, HtmlDocument doc)
        {
            var lstPaymentRequest = new List<PaymentRequest>();

            try
            {
                LogUtil.LogTrace("Step11_ExtractPaymentRequest.Start");

                // Extract html lấy 1 số thông số cơ bản
                var currentPage = 1;
                var maxPage = int.Parse(doc.DocumentNode.SelectSingleNode("//*[@id='currAcc']/b[1]").InnerText);
                LogUtil.LogTrace("Step11_ExtractPaymentRequest.maxPage");

                // Tạo folder để chứa file theo profileID và customerID
                string folderPath = $"{FileUtil.BASE_PATH}/OutputFiles/{_profileID}/{_customerID}/PaymentRequest";
                Directory.CreateDirectory(folderPath);
                LogUtil.LogTrace("Step11_ExtractPaymentRequest.folderPath");

                // Extract luôn các giấy nộp tiền ở page 1
                await ExtractRequestPayment(fromDate, toDate, doc, lstPaymentRequest);
                LogUtil.LogTrace("Step11_ExtractPaymentRequest.ExtractTaxDecSubmitted.Page.1");

                // Extract các tờ khai ở các page còn lại nếu có
                while (currentPage < maxPage)
                {
                    currentPage += 1;
                    LogUtil.LogTrace($"Step11_ExtractPaymentRequest.ExtractTaxDecSubmitted.Page.{currentPage}");
                    await Step12_ExtractPaymentRequest_NextPage(fromDate, toDate, currentPage, lstPaymentRequest);
                }
                LogUtil.LogTrace("Step11_ExtractPaymentRequest.End");
            }
            catch (Exception ex)
            {
                throw ex;
            }

            return lstPaymentRequest;
        }

        private async Task Step12_ExtractPaymentRequest_NextPage(string fromDate, string toDate, int page, List<PaymentRequest> lstPaymentRequest)
        {
            try
            {
                var result = await _client.GetAsync($"/etaxnnt/Request" +
                        $"?dse_sessionId={_sessionID}" +
                        $"&dse_applicationId=-1" +
                        $"&dse_operationName=corpQueryTaxProc" +
                        $"&dse_pageId=12" +
                        $"&dse_processorState=viewQueryPage" +
                        $"&dse_processorId={_processorID}" +
                        $"&dse_errorPage=error_page.jsp" +
                        $"&dse_nextEventName=query" +
                        $"&&pn={page}");

                result.EnsureSuccessStatusCode();

                HtmlDocument doc = new HtmlDocument();
                doc.LoadHtml(await result.Content.ReadAsStringAsync());
                await ExtractRequestPayment(fromDate, toDate, doc, lstPaymentRequest);
            }
            catch (Exception ex)
            {
                throw ex;
            }
        }

        /// <summary>
        /// Trích xuất dữ liệu từ giao diện màn hình kết quả tra cứu giấy nộp tiền
        /// </summary>
        /// <param name="sessionID">ID phiên đăng nhập của thuedientu</param>
        /// <param name="cookieContainer">giả lập Cookie</param>
        /// <param name="processorID">ID phiên xử lý</param>
        /// <param name="taxcode">mã số thuế</param>
        /// <param name="doc">Html Document</param>
        /// <param name="prevTransactionID">mã TransactionID của tờ khai phía trên (0 nếu là tờ khai đầu tiên trong danh sách)</param>
        /// <returns></returns>
        private async Task ExtractRequestPayment(string fromDate, string toDate, HtmlDocument doc, List<PaymentRequest> lstPaymentRequest)
        {
            try
            {
                LogUtil.LogTrace("ExtractRequestPayment.Start");
                var columnMapping = GetColumnMapping(doc, "//*[@id='data_content_onday']");
                // Đọc các dòng tr trong tbody
                var lstTrEl = doc.DocumentNode.SelectNodes("//*[@id='allResultTableBody']/tr");
                LogUtil.LogTrace("ExtractRequestPayment.SelectNodes.lstTrEl");
                if (lstTrEl != null && lstTrEl.Count > 0)
                {
                    LogUtil.LogTrace($"ExtractRequestPayment.ExtractNodes.lstTrEl.Count:{lstTrEl.Count}");
                    for (int i = 0; i < lstTrEl.Count; i++)
                    {
                        try
                        {
                            // Đọc các cột td để lấy dữ liệu
                            var trEl = lstTrEl[i];
                            var childNodes = trEl.Elements("td").ToList();
                            if (childNodes.Count < 5)
                            {
                                continue;
                            }

                            LogUtil.LogTrace($"ExtractRequestPayment.ExtractNodes.lstTrEl:{i}:{JsonConvert.SerializeObject(childNodes.Count)}");
                            var iPaymentRequest = new PaymentRequest()
                            {
                                TransactionID = GetCellValue(childNodes, columnMapping, "Số tham chiếu / Mã giao dịch", childNodes.ElementAtOrDefault(1)?.InnerText.Trim()),
                                PaymentRequestID = GetCellValue(childNodes, columnMapping, "Số giấy nộp tiền", childNodes.ElementAtOrDefault(4)?.InnerText.Trim()),
                                Amount = GetCellValue(childNodes, columnMapping, "Số tiền", childNodes.ElementAtOrDefault(5)?.InnerText.Trim()),
                                AmountType = GetCellValue(childNodes, columnMapping, "Loại tiền", childNodes.ElementAtOrDefault(6)?.InnerText.Trim()),
                                StateTitle = GetCellValue(childNodes, columnMapping, "Trạng thái", childNodes.ElementAtOrDefault(7)?.InnerText.Trim()),
                                RefNo = GetCellValue(childNodes, columnMapping, "Số chứng từ", childNodes.ElementAtOrDefault(8)?.InnerText.Trim()),
                                CreatedDate = GetCellValue(childNodes, columnMapping, "Ngày lập GNT", childNodes.ElementAtOrDefault(9)?.InnerText.Trim()),
                                SentDate = GetCellValue(childNodes, columnMapping, "Ngày gửi GNT", childNodes.ElementAtOrDefault(10)?.InnerText.Trim()),
                                TaxSubmittedDate = GetCellValue(childNodes, columnMapping, "Ngày nộp thuế", childNodes.ElementAtOrDefault(11)?.InnerText.Trim()),
                                BankName = GetCellValue(childNodes, columnMapping, "Ngân hàng", childNodes.ElementAtOrDefault(16)?.InnerText.Trim()),
                                BankAccount = GetCellValue(childNodes, columnMapping, "Tài khoản ngân hàng", childNodes.ElementAtOrDefault(17)?.InnerText.Trim()),
                            };
                            LogUtil.LogTrace($"ExtractRequestPayment.ExtractNodes.lstTrEl.{i}.iTaxDec");

                            LogUtil.LogTrace($"ExtractRequestPayment.ExtractNodes.lstTrEl.{i}.aTag");
                            // Lấy ô chứa nút tải chứng từ (thường ở cột cuối)
                            var nameCell = GetCellNode(childNodes, columnMapping, "#", childNodes.LastOrDefault());
                            var aTags = nameCell?.Descendants("a");
                            if (aTags != null)
                            {
                                foreach (var aTag in aTags)
                                {
                                    var downloadAttr = (aTag.GetAttributeValue("href", "") + " " + aTag.GetAttributeValue("onclick", "")).Trim();
                                    string pattern = @"downloadGNT\(['""]?(.*?)['""]?\)";
                                    var match = Regex.Match(downloadAttr, pattern);
                                    if (match.Success && match.Groups.Count > 1)
                                    {
                                        iPaymentRequest.HiddenPaymentRequestID = match.Groups[1].Value.Trim('\'', '"', ' ');
                                        break;
                                    }
                                }
                            }

                            if (string.IsNullOrWhiteSpace(iPaymentRequest.HiddenPaymentRequestID))
                            {
                                iPaymentRequest.IsHideDownloadLink = true;
                            }

                            LogUtil.LogTrace($"ExtractRequestPayment.ExtractNodes.lstTrEl.{i}.aTag.End");

                            if (!string.IsNullOrWhiteSpace(iPaymentRequest.HiddenPaymentRequestID))
                            {
                                if (!iPaymentRequest.IsHideDownloadLink)
                                {
                                    await Step13_DownloadPaymentRequestFile(fromDate, toDate, iPaymentRequest);
                                }
                                LogUtil.LogTrace($"ExtractRequestPayment.Step13_DownloadPaymentRequestFile.lstTrEl.{i}.End");
                            }

                            // add PaymentRequest vào collection kết quả
                            lstPaymentRequest.Add(iPaymentRequest);
                        }
                        catch (Exception ex)
                        {
                            LogUtil.LogError(ex);
                        }
                    }
                }
                LogUtil.LogTrace("ExtractRequestPayment.End");
            }
            catch (Exception ex)
            {
                LogUtil.LogError(ex);
            }
        }

        private async Task Step13_DownloadPaymentRequestFile(string fromDate, string toDate, PaymentRequest iPaymentRequest)
        {
            try
            {
                LogUtil.LogTrace($"Step13_DownloadPaymentRequestFile.Start");
                string folderPath = $"{FileUtil.BASE_PATH}/OutputFiles/{_profileID}/{_customerID}/PaymentRequest";
                //var files = Directory.GetFiles(folderPath, $"ETAX{iTaxDec.TransactionID}.*");
                //if (files.Length < 1) // Kiểm tra chưa tồn tại file thì đi tải mới
                //{

                //}

                var content = new FormUrlEncodedContent(new[]
                  {
                    new KeyValuePair<string, string>("dse_sessionId", _sessionID),
                    new KeyValuePair<string, string>("dse_applicationId", "-1"),
                    new KeyValuePair<string, string>("dse_operationName", "corpQueryTaxProc"),
                    new KeyValuePair<string, string>("dse_pageId", "13"),
                    new KeyValuePair<string, string>("dse_processorState", "viewQueryPage"),
                    new KeyValuePair<string, string>("dse_processorId", _processorID),
                    new KeyValuePair<string, string>("dse_errorPage", "/etax/query_tax_information.jsp"),
                    new KeyValuePair<string, string>("dse_nextEventName", "download"),
                    new KeyValuePair<string, string>("pn", "1"),
                    new KeyValuePair<string, string>("sct", ""),
                    new KeyValuePair<string, string>("ctuId", iPaymentRequest.HiddenPaymentRequestID),
                    new KeyValuePair<string, string>("soGnt", ""),
                    new KeyValuePair<string, string>("ma_giao_dich", ""),
                    new KeyValuePair<string, string>("so_ctu_nh", ""),
                    new KeyValuePair<string, string>("so_gnt", ""),
                    new KeyValuePair<string, string>("ngay_lap_tu_ngay", fromDate),
                    new KeyValuePair<string, string>("ngay_lap_den_ngay", toDate),
                    new KeyValuePair<string, string>("ngay_gui_tu_ngay", ""),
                    new KeyValuePair<string, string>("ngay_gui_den_ngay", ""),
                    new KeyValuePair<string, string>("ngay_nop_tu_ngay", ""),
                    new KeyValuePair<string, string>("ngay_nop_den_ngay", ""),
                    new KeyValuePair<string, string>("ma_nhang", ""),
                    new KeyValuePair<string, string>("so_tk", ""),
                    new KeyValuePair<string, string>("nguyen_te", ""),
                    new KeyValuePair<string, string>("tong_tien_nt_tu", ""),
                    new KeyValuePair<string, string>("tong_tien_nt_den", ""),
                    new KeyValuePair<string, string>("trang_thai", ""),
                    new KeyValuePair<string, string>("isReport", "N"),
                    new KeyValuePair<string, string>("type", "pdf"),
                });

                var responseMessage = await _client.PostAsync("/etaxnnt/Request", content);
                responseMessage.EnsureSuccessStatusCode();
                LogUtil.LogTrace($"Step13_DownloadPaymentRequestFile.PostAsync");
                var originFileName = responseMessage.Content.Headers.ContentDisposition.FileName;
                iPaymentRequest.FileName = originFileName.Replace("chungtu", iPaymentRequest.HiddenPaymentRequestID);

                using (var fs = new FileStream($"{folderPath}\\{iPaymentRequest.FileName}", FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None))
                {
                    await responseMessage.Content.CopyToAsync(fs);
                }
                LogUtil.LogTrace($"Step13_DownloadPaymentRequestFile.SaveFile");
                if (Path.GetExtension(iPaymentRequest.FileName) == ".xml")
                {
                    LogUtil.LogTrace($"Step13_DownloadPaymentRequestFile.ExtractXml.Start");
                    // Đọc file để trích xuất 1 số thông tin như: mã ngân hàng, ...
                    XmlDocument doc = new XmlDocument();
                    var xmlString = await responseMessage.Content.ReadAsStringAsync();
                    LogUtil.LogTrace($"Step13_DownloadPaymentRequestFile.ExtractXml.LoadXml: {xmlString}");
                    doc.LoadXml(xmlString);

                    XmlNamespaceManager ns = new XmlNamespaceManager(doc.NameTable);
                    ns.AddNamespace("msbld", "http://kekhaithue.gdt.gov.vn/TKhaiThue");
                    LogUtil.LogTrace("Step13_DownloadPaymentRequestFile.ExtractXml.taxDecCode");
                    var bankCode = doc.SelectSingleNode("//MA_NHANG_NOP").InnerText;
                    iPaymentRequest.BankCode = bankCode;
                    LogUtil.LogTrace($"Step13_DownloadPaymentRequestFile.ExtractXml.End");
                }

                LogUtil.LogTrace($"Step13_DownloadPaymentRequestFile.End");
            }
            catch (Exception ex)
            {
                LogUtil.LogError(ex);
            }
        }
        #endregion

        #region Các hàm public cho phép tầng trên UI gọi 
        /// <summary>
        /// Hàm kiểm tra tính hợp lệ của tài khoản ThueDienTu
        /// </summary>
        public async Task CheckETaxAccount()
        {

            await CommonPattern.Retry(async () =>
            {
                await SignIn();
            });
        }

        /// <summary>
        /// Hàm đồng bộ tờ khai đã đăng ký
        /// </summary>
        public async Task<List<TaxDeclarationRegistered>> GetTaxDecRegistered()
        {

            var doc = await CommonPattern.Retry(async () =>
            {
                await SignIn();
                return await Step9_GotoDangKyToKhaiPage();
            });
            return Step10_ExtractTaxDecRegistered(doc);
        }

        /// <summary>
        /// Hàm tra cứu tờ khai đã nộp
        /// </summary>
        public async Task<List<TaxDeclarationSubmitted>> GetTaxDecSubmitted(string fromDate, string toDate)
        {
            var crawlerSource = ConfigurationManager.AppSettings["CrawlerSource"];
            if (crawlerSource == "PublicService")
            {
                var publicServiceCrawler = new PublicServiceCrawler(_profileID, _customerID, _taxcode, _username, _password, Models.Enums.EnumTaxAccountType.Enterprise, _stoppingToken);
                return await publicServiceCrawler.GetTaxDecSubmitted(fromDate, toDate);
            }

            var doc = await CommonPattern.Retry(async () =>
            {
                await SignIn();
                await Step9_GotoTraCuuToKhaiPage();
                await Task.Delay(1000, _stoppingToken);
                return await Step10_ClickButtonTraCuu(fromDate, toDate);
            });
            await Task.Delay(1000, _stoppingToken);
            return await Step11_ExtractTaxDecSubmitted(doc);
        }

        /// <summary>
        /// Hàm tra cứu giấy nộp tiền
        /// </summary>
        public async Task<List<PaymentRequest>> GetPaymentRequest(string fromDate, string toDate)
        {
            var targetTypeTaxes = CommonConst.UseOldETaxFlow ? new[] { "01", "02" } : new[] { "01" };

            var allPaymentRequests = new List<PaymentRequest>();

            await CommonPattern.Retry(async () =>
            {
                allPaymentRequests.Clear();

                await SignIn();

                foreach (var typeTax in targetTypeTaxes)
                {
                    LogUtil.LogTrace($"ETaxCrawler.GetPaymentRequest.QueryTypeTax.Start.typeTax={typeTax}");

                    await Step9_GotoTraCuuGiayNopTienPage();
                    await Task.Delay(1000, _stoppingToken);

                    var doc = await Step10_ClickButtonTraCuuGiayNopTien(fromDate, toDate, typeTax);
                    await Task.Delay(1000, _stoppingToken);

                    var requests = await Step11_ExtractPaymentRequest(fromDate, toDate, doc);
                    if (requests != null && requests.Count > 0)
                    {
                        allPaymentRequests.AddRange(requests);
                    }

                    LogUtil.LogTrace($"ETaxCrawler.GetPaymentRequest.QueryTypeTax.End.typeTax={typeTax}, count={requests?.Count ?? 0}");
                }
            }, 5);

            return allPaymentRequests;
        }
        #endregion

        public Dictionary<string, int> GetColumnMapping(HtmlDocument doc, string tableSelector)
        {
            var columnMapping = new Dictionary<string, int>();
            var headerRow = doc.DocumentNode.SelectSingleNode($"{tableSelector}/thead/tr")
                ?? doc.DocumentNode.SelectSingleNode($"{tableSelector}/tr[1]");

            if (headerRow != null)
            {
                var headers = headerRow.Elements("th").ToList();
                if (headers.Count == 0)
                    headers = headerRow.Elements("td").ToList(); // fallback nếu dùng td cho header

                for (int i = 0; i < headers.Count; i++)
                {
                    var headerText = NormalizeColumnName(headers[i].InnerText);
                    columnMapping[headerText] = i;
                }
            }

            return columnMapping;
        }

        public string NormalizeColumnName(string columnName)
        {
            if (string.IsNullOrWhiteSpace(columnName))
                return string.Empty;

            return columnName.Trim()
                .ToLower()
                .Replace(" ", "")
                .Replace("\t", "")
                .Replace("\n", "")
                .Replace("\r", "");
        }

        public string GetCellValue(List<HtmlNode> childNodes, Dictionary<string, int> columnMapping, string columnName, string fallBackValue)
        {
            columnName = NormalizeColumnName(columnName);
            var foundKey = columnMapping.FirstOrDefault(col => string.Equals(col.Key, columnName, StringComparison.OrdinalIgnoreCase));
            if (foundKey.Key != null)
            {
                return childNodes[foundKey.Value].InnerText.Trim();
            }
            return fallBackValue;
        }

        public HtmlNode GetCellNode(List<HtmlNode> childNodes, Dictionary<string, int> columnMapping, string columnName, HtmlAgilityPack.HtmlNode fallbackNode)
        {
            columnName = NormalizeColumnName(columnName);
            var foundKey = columnMapping.FirstOrDefault(col => string.Equals(col.Key, columnName, StringComparison.OrdinalIgnoreCase));
            if (foundKey.Key != null)
            {
                return childNodes[foundKey.Value];
            }
            return fallbackNode;
        }
    }
}
