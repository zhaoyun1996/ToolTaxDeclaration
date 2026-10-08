using HtmlAgilityPack;
using MISA.ASP.ClientApp.Models;
using MISA.ASP.ClientApp.Models.ActionInput.SynTaxDec;
using MISA.ASP.ClientApp.Models.Enums;
using MISA.ASP.ClientApp.Models.EtaxCrawler;
using MISA.ASP.ClientApp.Models.Exceptions;
using MISA.ASP.ClientApp.Utils.Clients;
using MISA.ASP.ClientApp.Utils.DesignPattern;
using MISA.ASP.ClientApp.Utils.FileHandler;
using MISA.ASP.ClientApp.Utils.Logging;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Configuration;
using System.Globalization;
using System.IO;
using System.IO.Compression;
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
    public class PublicServiceCrawler
    {
        private static string PUBLIC_SERVICE_URL => ConfigurationManager.AppSettings["PublicService_BaseUrl"] ?? "https://dichvucong.gdt.gov.vn";
        private static string ETAX_URL => ConfigurationManager.AppSettings["ETax_BaseUrl"] ?? "https://thuedientu.gdt.gov.vn";

        private int _profileID { get; set; }
        private int _customerID { get; set; }
        private string _taxcode { get; set; }
        private string _username { get; set; }
        private string _password { get; set; }
        private string _csrfToken { get; set; }
        private string _etaxSessionId { get; set; }
        private string _processorID { get; set; }
        private string _queryPageId { get; set; }
        public string EtaxSessionId => _etaxSessionId;
        public string ProcessorID => _processorID;
        public HttpClient EtaxClient => _client;
        private CookieContainer _cookieContainer { get; set; }
        private HttpClient _dvcClient { get; set; }
        private HttpClientHandler _handler { get; set; }
        private CookieContainer _etaxCookieContainer { get; set; }
        private HttpClient _client { get; set; }
        private HttpClientHandler _etaxHandler { get; set; }
        private CancellationToken _stoppingToken { get; set; }
        private EnumTaxAccountType _accountType { get; set; }

        public PublicServiceCrawler(int profileID, int customerID, string taxcode, string username, string password, 
            EnumTaxAccountType accountType, CancellationToken stoppingToken)
        {
            _profileID = profileID;
            _customerID = customerID;
            _taxcode = taxcode;
            _username = username;
            _password = password;
            _cookieContainer = new CookieContainer();
            _handler = new HttpClientHandler()
            {
                CookieContainer = _cookieContainer,
                AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate
            };
            _dvcClient = new HttpClient(_handler) { BaseAddress = new Uri(PUBLIC_SERVICE_URL) };

            _dvcClient.DefaultRequestHeaders.Add("Accept-Language", "vi-VN,vi;q=0.9,en-US;q=0.8,en;q=0.7");
            _dvcClient.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36");

            _stoppingToken = stoppingToken;
            _accountType = accountType;

            // HttpClient riêng cho thuedientu.gdt.gov.vn
            _etaxCookieContainer = new CookieContainer();
            _etaxHandler = new HttpClientHandler()
            {
                CookieContainer = _etaxCookieContainer,
                AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate,
                AllowAutoRedirect = false
            };
            _client = new HttpClient(_etaxHandler) { BaseAddress = new Uri(ETAX_URL) };
            _client.DefaultRequestHeaders.Add("Accept-Language", "en-US,en;q=0.9");
            _client.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/154.0.0.0 Safari/537.36 Edg/154.0.0.0");
        }

        #region Các bước đăng nhập Cổng Dịch vụ công Thuế (Public Service)

        /// <summary>
        /// Bước 1: Khởi tạo phiên qua /tthc/homelogin để thiết lập cookies (JSESSIONID, TS01...)
        /// </summary>
        private async Task Step1_GotoHomeLogin()
        {
            HttpResponseMessage responseMessage = null;
            try
            {
                LogUtil.LogTrace("PublicServiceCrawler.Step1_GotoHomeLogin.Start");
                responseMessage = await _dvcClient.GetAsync("/tthc/homelogin", _stoppingToken);
                responseMessage.EnsureSuccessStatusCode();
                LogUtil.LogTrace("PublicServiceCrawler.Step1_GotoHomeLogin.End");
            }
            catch (Exception ex)
            {
                LogUtil.LogError(ex, responseMessage);
                throw;
            }
        }

        /// <summary>
        /// Bước 2: Truy cập trang đăng nhập /tthc/login, trích xuất CSRF token (_csrf)
        /// (Tương ứng với luồng: Đăng nhập bằng tài khoản Thuế điện tử [dangNhapLDAP] -> Chọn Doanh nghiệp [processChonDT('DN')])
        /// </summary>
        private async Task Step2_GotoLoginPage()
        {
            HttpResponseMessage responseMessage = null;
            try
            {
                LogUtil.LogTrace("PublicServiceCrawler.Step2_GotoLoginPage.Start");
                responseMessage = await _dvcClient.GetAsync("/tthc/login", _stoppingToken);
                responseMessage.EnsureSuccessStatusCode();

                var html = await responseMessage.Content.ReadAsStringAsync();

                // Trích xuất _csrf token từ form login
                string pattern = @"name=""_csrf""\s+value=""(.*?)""";
                var match = Regex.Match(html, pattern);
                if (match.Success && match.Groups.Count > 1)
                {
                    _csrfToken = match.Groups[1].Value;
                }

                if (string.IsNullOrWhiteSpace(_csrfToken))
                {
                    // Thử tìm bằng HtmlAgilityPack nếu Regex không match
                    var doc = new HtmlDocument();
                    doc.LoadHtml(html);
                    var csrfNode = doc.DocumentNode.SelectSingleNode("//input[@name='_csrf']");
                    if (csrfNode != null)
                    {
                        _csrfToken = csrfNode.GetAttributeValue("value", "");
                    }
                }

                if (string.IsNullOrWhiteSpace(_csrfToken))
                {
                    throw new Exception("Không tìm thấy CSRF Token (_csrf) trong trang /tthc/login");
                }

                LogUtil.LogTrace($"PublicServiceCrawler.Step2_GotoLoginPage.End, _csrfToken: {_csrfToken}");
            }
            catch (Exception ex)
            {
                LogUtil.LogError(ex, responseMessage);
                throw;
            }
        }

        /// <summary>
        /// Bước 3: Lấy captcha từ endpoint chỉ định (mặc định /tthc/login/getCaptcha) và giải mã qua mô hình Python
        /// </summary>
        //private async Task<CaptchaResult> Step3_ResolveCaptcha(string captchaUrl = "/tthc/login/getCaptcha")
        //{
        //    HttpResponseMessage responseMessage = null;
        //    var captchaResult = new CaptchaResult();

        //    try
        //    {
        //        LogUtil.LogTrace($"PublicServiceCrawler.Step3_ResolveCaptcha.Start, url: {captchaUrl}");
        //        var timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        //        string separator = captchaUrl.Contains("?") ? "&" : "?";
        //        var requestUrl = $"{captchaUrl}{separator}{timestamp}";

        //        var request = new HttpRequestMessage(HttpMethod.Get, requestUrl);
        //        string refererUrl = captchaUrl.Contains("/login") ? $"{PUBLIC_SERVICE_URL}/tthc/login" : $"{PUBLIC_SERVICE_URL}/tthc/tchs";
        //        request.Headers.Add("Referer", refererUrl);
        //        request.Headers.Add("Accept", "image/avif,image/webp,image/apng,image/svg+xml,image/*,*/*;q=0.8");

        //        responseMessage = await _dvcClient.SendAsync(request, _stoppingToken);
        //        responseMessage.EnsureSuccessStatusCode();

        //        var imageBytes = await responseMessage.Content.ReadAsByteArrayAsync();

        //        // Giải mã captcha bằng mô hình cục bộ D:\Project\GetCaptcha\main.py
        //        var captchaText = await ResolveCaptchaWithPython(imageBytes);

        //        if (string.IsNullOrWhiteSpace(captchaText))
        //        {
        //            throw new UnResolvedCaptchaException();
        //        }

        //        captchaResult.ByteArray = imageBytes;
        //        captchaResult.Result = captchaText;
        //        LogUtil.LogTrace($"PublicServiceCrawler.Step3_ResolveCaptcha.Captcha: {captchaText}");
        //    }
        //    catch (Exception ex)
        //    {
        //        LogUtil.LogError(ex, responseMessage);
        //        throw;
        //    }

        //    return captchaResult;
        //}

        private async Task<CaptchaResult> Step3_ResolveCaptcha(string captchaUrl = "/tthc/login/getCaptcha")
        {
            var captcha = string.Empty;
            HttpResponseMessage responseMessage = null;
            var captchaResult = new CaptchaResult();

            try
            {
                LogUtil.LogTrace($"PublicServiceCrawler.Step3_ResolveCaptcha.Start, url: {captchaUrl}");
                var timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

                string separator = captchaUrl.Contains("?") ? "&" : "?";
                var requestUrl = $"{captchaUrl}{separator}{timestamp}";

                var request = new HttpRequestMessage(HttpMethod.Get, requestUrl);
                string refererUrl = captchaUrl.Contains("/login") ? $"{PUBLIC_SERVICE_URL}/tthc/login" : $"{PUBLIC_SERVICE_URL}/tthc/tchs";
                request.Headers.Add("Referer", refererUrl);
                request.Headers.Add("Accept", "image/avif,image/webp,image/apng,image/svg+xml,image/*,*/*;q=0.8");

                responseMessage = await _dvcClient.SendAsync(request, _stoppingToken);
                responseMessage.EnsureSuccessStatusCode();

                var imageBytes = await responseMessage.Content.ReadAsByteArrayAsync();

                responseMessage.EnsureSuccessStatusCode();
                var captchaResolver = new MisaCaptchaClient();
                var stream = await responseMessage.Content.ReadAsStreamAsync();
                captcha = await captchaResolver.Decaptcha(new CaptchaRequest
                {
                    Stream = stream,
                    Source = "dichvucong",
                },
                refererUrl);
                if (String.IsNullOrWhiteSpace(captcha))
                {
                    throw new UnResolvedCaptchaException();
                }
                captchaResult.ByteArray = FileUtil.ConvertStreamToByteArray(stream);
                captchaResult.Result = captcha;

                if (string.IsNullOrWhiteSpace(captcha))
                {
                    throw new UnResolvedCaptchaException();
                }

                LogUtil.LogTrace($"PublicServiceCrawler.Step3_ResolveCaptcha.Captcha: {captcha}");
            }
            catch (Exception ex)
            {
                LogUtil.LogError(ex, responseMessage);
                throw;
            }

            return captchaResult;
        }

        /// <summary>
        /// Giải mã captcha tạm thời bằng D:\Project\GetCaptcha\main.py
        /// Hỗ trợ gọi qua Local FastAPI service (nếu đang chạy ở http://127.0.0.1:8000) hoặc qua Python CLI
        /// </summary>
        private async Task<string> ResolveCaptchaWithPython(byte[] imageBytes)
        {
            // 1. Thử gọi qua Local HTTP API (nếu uvicorn/fastapi đang chạy ở http://127.0.0.1:8000)
            //var localApiUrl = ConfigurationManager.AppSettings["LocalCaptcha_Api_Url"] ?? "http://127.0.0.1:8000/solve_image";
            var localApiUrl = "https://my-captcha-api-z9vy.onrender.com/solve_image";
            try
            {
                using (var httpCli = new HttpClient { Timeout = TimeSpan.FromSeconds(2) })
                {
                    var payload = JsonConvert.SerializeObject(new { image_base64 = Convert.ToBase64String(imageBytes) });
                    var content = new StringContent(payload, Encoding.UTF8, "application/json");
                    var res = await httpCli.PostAsync(localApiUrl, content);
                    if (res.IsSuccessStatusCode)
                    {
                        var json = await res.Content.ReadAsStringAsync();
                        var obj = JObject.Parse(json);
                        if (obj["success"]?.Value<bool>() == true)
                        {
                            var text = obj["text"]?.ToString()?.Trim();
                            if (!string.IsNullOrEmpty(text))
                            {
                                LogUtil.LogTrace($"ResolveCaptcha via HTTP API: {text}");
                                return text;
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                LogUtil.LogTrace($"Local HTTP Captcha API not reachable, fallback to Python CLI: {ex.Message}");
            }

            // 2. Fallback: Gọi trực tiếp Python CLI: python D:\Project\GetCaptcha\main.py <tempFile>
            var scriptPath = ConfigurationManager.AppSettings["LocalCaptcha_Script_Path"] ?? @"D:\Project\GetCaptcha\main.py";
            var tempFile = Path.Combine(Path.GetTempPath(), $"captcha_{Guid.NewGuid():N}.png");

            try
            {
                File.WriteAllBytes(tempFile, imageBytes);

                var psi = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "python",
                    Arguments = $"\"{scriptPath}\" \"{tempFile}\"",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    StandardOutputEncoding = Encoding.UTF8
                };

                using (var process = new System.Diagnostics.Process { StartInfo = psi })
                {
                    process.Start();
                    var stdoutTask = process.StandardOutput.ReadToEndAsync();

                    if (await Task.WhenAny(stdoutTask, Task.Delay(10000)) == stdoutTask)
                    {
                        var stdout = await stdoutTask;
                        var result = stdout?.Trim();
                        LogUtil.LogTrace($"ResolveCaptcha via Python CLI: {result}");
                        return result;
                    }
                    else
                    {
                        try { process.Kill(); } catch { }
                        throw new TimeoutException("Python CLI timeout after 10s");
                    }
                }
            }
            finally
            {
                try
                {
                    if (File.Exists(tempFile))
                    {
                        File.Delete(tempFile);
                    }
                }
                catch { }
            }
        }

        /// <summary>
        /// Bước 4: POST thông tin đăng nhập tới /tthc/loginLDAP
        /// Tham số:
        /// - tenDN: Tên đăng nhập
        /// - matKhau: Mật khẩu mã hóa Base64
        /// - doiTuong: 'DN' (Doanh nghiệp)
        /// - captcha: Mã captcha đã giải mã
        /// </summary>
        private async Task Step4_PostLoginLDAP(CaptchaResult captcha)
        {
            HttpResponseMessage responseMessage = null;

            try
            {
                LogUtil.LogTrace("PublicServiceCrawler.Step4_PostLoginLDAP.Start");

                // Mật khẩu mã hóa Base64 theo UTF-8 tương ứng với btoa(unescape(encodeURIComponent(matKhau))) trong JS
                var base64Password = Convert.ToBase64String(Encoding.UTF8.GetBytes(_password ?? ""));

                var formContent = new FormUrlEncodedContent(new[]
                {
                    new KeyValuePair<string, string>("tenDN", _username),
                    new KeyValuePair<string, string>("matKhau", base64Password),
                    new KeyValuePair<string, string>("doiTuong", _accountType == EnumTaxAccountType.Enterprise ? "DN" : "CN"),
                    new KeyValuePair<string, string>("captcha", captcha.Result)
                });

                var request = new HttpRequestMessage(HttpMethod.Post, "/tthc/loginLDAP")
                {
                    Content = formContent
                };

                request.Headers.Add("X-XSRF-TOKEN", _csrfToken);
                request.Headers.Add("X-Requested-With", "XMLHttpRequest");
                request.Headers.Add("Referer", $"{PUBLIC_SERVICE_URL}/tthc/login");

                responseMessage = await _dvcClient.SendAsync(request, _stoppingToken);
                responseMessage.EnsureSuccessStatusCode();

                var jsonContent = await responseMessage.Content.ReadAsStringAsync();
                LogUtil.LogInfo($"PublicServiceCrawler.Step4_PostLoginLDAP.Response: {jsonContent}");

                var responseObj = JObject.Parse(jsonContent);
                var status = responseObj["status"]?.ToString();
                var desc = responseObj["desc"]?.ToString() ?? "";

                if (status == "200" || status == "201")
                {
                    LogUtil.LogInfo("PublicServiceCrawler.Step4_PostLoginLDAP.Success");
                }
                else if (desc.IndexOf("captcha", StringComparison.OrdinalIgnoreCase) >= 0 || status == "999")
                {
                    throw new InvalidCaptchaException();
                }
                else if (desc.IndexOf("Tên đăng nhập hoặc mật khẩu", StringComparison.OrdinalIgnoreCase) >= 0 ||
                         desc.IndexOf("không đúng", StringComparison.OrdinalIgnoreCase) >= 0 ||
                         desc.IndexOf("chưa đúng", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    throw new InvalidIdentityException();
                }
                else
                {
                    throw new Exception(string.IsNullOrWhiteSpace(desc) ? $"Đăng nhập thất bại với mã lỗi {status}" : desc);
                }

                LogUtil.LogTrace("PublicServiceCrawler.Step4_PostLoginLDAP.End");
            }
            catch (Exception ex)
            {
                LogUtil.LogError(ex, responseMessage);
                throw;
            }
        }

        /// <summary>
        /// Đăng nhập Cổng Dịch vụ công Thuế (Public Service)
        /// </summary>
        public async Task SignIn()
        {
            await CommonPattern.Retry(async () =>
            {
                await Step1_GotoHomeLogin();
                await Task.Delay(500, _stoppingToken);
                await Step2_GotoLoginPage();
                await Task.Delay(500, _stoppingToken);
                var captcha = await Step3_ResolveCaptcha();
                await Task.Delay(500, _stoppingToken);
                await Step4_PostLoginLDAP(captcha);
                await Task.Delay(1000, _stoppingToken);
            }, 5);
        }

        #endregion

        #region Các bước Tra cứu tờ khai đã nộp (Public Service)

        /// <summary>
        /// Bước 9: Đi tới trang danh sách tờ khai (/tthc/tchs)
        /// </summary>
        private async Task<HtmlDocument> Step9_GotoTraCuuToKhaiPage()
        {
            HttpResponseMessage responseMessage = null;
            HtmlDocument doc = new HtmlDocument();
            try
            {
                LogUtil.LogTrace("PublicServiceCrawler.Step9_GotoTraCuuToKhaiPage.Start");
                var request = new HttpRequestMessage(HttpMethod.Get, "/tthc/tchs");
                request.Headers.Add("Referer", $"{PUBLIC_SERVICE_URL}/tthc/login");

                responseMessage = await _dvcClient.SendAsync(request, _stoppingToken);
                responseMessage.EnsureSuccessStatusCode();

                var html = await responseMessage.Content.ReadAsStringAsync();
                doc.LoadHtml(html);

                LogUtil.LogTrace("PublicServiceCrawler.Step9_GotoTraCuuToKhaiPage.End");
            }
            catch (Exception ex)
            {
                LogUtil.LogError(ex, responseMessage);
                throw;
            }

            return doc;
        }

        /// <summary>
        /// Bước 10: Thực hiện tìm kiếm tờ khai đã nộp (gửi request GET tới /tthc/ho-so/search với các tham số từ và đến ngày, captcha)
        /// </summary>
        private async Task<HtmlDocument> Step10_ClickButtonTraCuu(string fromDate, string toDate, CaptchaResult captchaResult)
        {
            HttpResponseMessage responseMessage = null;
            HtmlDocument doc = new HtmlDocument();
            try
            {
                LogUtil.LogTrace("PublicServiceCrawler.Step10_ClickButtonTraCuu.Start");

                var queryParams = new List<KeyValuePair<string, string>>
                {
                    new KeyValuePair<string, string>("loaiThuTuc", ""),
                    new KeyValuePair<string, string>("maTTHC", ""),
                    new KeyValuePair<string, string>("maToKhai", ""),
                    new KeyValuePair<string, string>("maHoSo", ""),
                    new KeyValuePair<string, string>("tuNgay", fromDate ?? ""),
                    new KeyValuePair<string, string>("denNgay", toDate ?? ""),
                    new KeyValuePair<string, string>("scope_tdt1", "SELF"),
                    new KeyValuePair<string, string>("mstUyQuyen_tdt1", ""),
                    new KeyValuePair<string, string>("captcha", captchaResult.Result),
                    new KeyValuePair<string, string>("page", "1"),
                    new KeyValuePair<string, string>("size", "10")
                };

                var queryString = string.Join("&", queryParams.Select(p => $"{Uri.EscapeDataString(p.Key)}={Uri.EscapeDataString(p.Value)}"));
                var requestUrl = $"/tthc/ho-so/search?{queryString}";

                var request = new HttpRequestMessage(HttpMethod.Get, requestUrl);
                request.Headers.Add("Referer", $"{PUBLIC_SERVICE_URL}/tthc/tchs");
                request.Headers.Add("HX-Request", "true");
                request.Headers.Add("HX-Target", "table-container");
                request.Headers.Add("X-Requested-With", "XMLHttpRequest");

                responseMessage = await _dvcClient.SendAsync(request, _stoppingToken);
                responseMessage.EnsureSuccessStatusCode();

                var html = await responseMessage.Content.ReadAsStringAsync();

                if (html.Contains("Mã xác nhận") && (html.Contains("không chính xác") || html.Contains("không đúng") || html.Contains("chưa đúng")))
                {
                    throw new InvalidCaptchaException();
                }

                doc.LoadHtml(html);
                LogUtil.LogTrace("PublicServiceCrawler.Step10_ClickButtonTraCuu.End");
            }
            catch (Exception ex)
            {
                LogUtil.LogError(ex, responseMessage);
                throw;
            }
            return doc;
        }

        /// <summary>
        /// Bước 11: Trích xuất danh sách tờ khai đã nộp từ HTML kết quả và chuyển trang nếu có nhiều trang
        /// </summary>
        private async Task<List<TaxDeclarationSubmitted>> Step11_ExtractTaxDecSubmitted(HtmlDocument doc, string fromDate, string toDate, CaptchaResult captchaResult)
        {
            var lstTaxDecSubmitted = new List<TaxDeclarationSubmitted>();

            try
            {
                LogUtil.LogTrace("PublicServiceCrawler.Step11_ExtractTaxDecSubmitted.Start");

                var nodeTotalPage = doc.DocumentNode.SelectSingleNode("//*[@id='totalPage']");
                int maxPage = 1;
                if (nodeTotalPage != null)
                {
                    int.TryParse(nodeTotalPage.InnerText.Trim(), out maxPage);
                }
                if (maxPage < 1) maxPage = 1;

                LogUtil.LogTrace($"PublicServiceCrawler.Step11_ExtractTaxDecSubmitted.maxPage: {maxPage}");

                string folderPath = $"{FileUtil.BASE_PATH}/OutputFiles/{_profileID}/{_customerID}";
                Directory.CreateDirectory(folderPath);

                // Extract trang 1
                await ExtractTaxDecSubmitted(doc, lstTaxDecSubmitted);
                LogUtil.LogTrace("PublicServiceCrawler.Step11_ExtractTaxDecSubmitted.Page.1");

                // Extract các trang tiếp theo nếu có
                for (int currentPage = 2; currentPage <= maxPage; currentPage++)
                {
                    LogUtil.LogTrace($"PublicServiceCrawler.Step11_ExtractTaxDecSubmitted.Page.{currentPage}");
                    await Step12_ExtractTaxDecSubmitted_NextPage(currentPage, fromDate, toDate, captchaResult, lstTaxDecSubmitted);
                }

                LogUtil.LogTrace("PublicServiceCrawler.Step11_ExtractTaxDecSubmitted.End");
            }
            catch (Exception ex)
            {
                LogUtil.LogError(ex);
                throw;
            }

            return lstTaxDecSubmitted;
        }

        /// <summary>
        /// Bước 12: Trích xuất dữ liệu tờ khai ở các trang tiếp theo
        /// </summary>
        private async Task Step12_ExtractTaxDecSubmitted_NextPage(int page, string fromDate, string toDate, CaptchaResult captchaResult, List<TaxDeclarationSubmitted> lstTaxDecSubmitted)
        {
            HttpResponseMessage responseMessage = null;
            try
            {
                LogUtil.LogTrace($"PublicServiceCrawler.Step12_ExtractTaxDecSubmitted_NextPage.Start.Page:{page}");

                var queryParams = new List<KeyValuePair<string, string>>
                {
                    new KeyValuePair<string, string>("loaiThuTuc", ""),
                    new KeyValuePair<string, string>("maTTHC", ""),
                    new KeyValuePair<string, string>("maToKhai", ""),
                    new KeyValuePair<string, string>("maHoSo", ""),
                    new KeyValuePair<string, string>("tuNgay", fromDate ?? ""),
                    new KeyValuePair<string, string>("denNgay", toDate ?? ""),
                    new KeyValuePair<string, string>("scope_tdt1", "SELF"),
                    new KeyValuePair<string, string>("mstUyQuyen_tdt1", ""),
                    new KeyValuePair<string, string>("captcha", captchaResult.Result),
                    new KeyValuePair<string, string>("page", page.ToString()),
                    new KeyValuePair<string, string>("size", "10")
                };

                var queryString = string.Join("&", queryParams.Select(p => $"{Uri.EscapeDataString(p.Key)}={Uri.EscapeDataString(p.Value)}"));
                var requestUrl = $"/tthc/ho-so/search?{queryString}";

                var request = new HttpRequestMessage(HttpMethod.Get, requestUrl);
                request.Headers.Add("Referer", $"{PUBLIC_SERVICE_URL}/tthc/tchs");
                request.Headers.Add("HX-Request", "true");
                request.Headers.Add("HX-Target", "table-container");
                request.Headers.Add("X-Requested-With", "XMLHttpRequest");

                responseMessage = await _dvcClient.SendAsync(request, _stoppingToken);
                responseMessage.EnsureSuccessStatusCode();

                var html = await responseMessage.Content.ReadAsStringAsync();

                if (html.Contains("Mã xác nhận") && (html.Contains("không chính xác") || html.Contains("không đúng") || html.Contains("chưa đúng")))
                {
                    throw new InvalidCaptchaException();
                }

                HtmlDocument doc = new HtmlDocument();
                doc.LoadHtml(html);

                await ExtractTaxDecSubmitted(doc, lstTaxDecSubmitted);
                LogUtil.LogTrace($"PublicServiceCrawler.Step12_ExtractTaxDecSubmitted_NextPage.End.Page:{page}");
            }
            catch (Exception ex)
            {
                LogUtil.LogError(ex, responseMessage);
                throw;
            }
        }

        /// <summary>
        /// Trích xuất dữ liệu từ giao diện màn hình kết quả tra cứu tờ khai
        /// </summary>
        private async Task ExtractTaxDecSubmitted(HtmlDocument doc, List<TaxDeclarationSubmitted> lstTaxDecSubmitted)
        {
            try
            {
                LogUtil.LogTrace("PublicServiceCrawler.ExtractTaxDecSubmitted.Start");
                var lstTrEl = doc.DocumentNode.SelectNodes("//table//tbody/tr") ?? doc.DocumentNode.SelectNodes("//tbody/tr");

                if (lstTrEl != null && lstTrEl.Count > 0)
                {
                    LogUtil.LogTrace($"PublicServiceCrawler.ExtractTaxDecSubmitted.Count: {lstTrEl.Count}");
                    for (int i = 0; i < lstTrEl.Count; i++)
                    {
                        try
                        {
                            var trEl = lstTrEl[i];
                            var childNodes = trEl.Elements("td").ToList();
                            if (childNodes.Count >= 12)
                            {
                                var orderText = childNodes[0].InnerText.Trim();
                                var transactionIdText = childNodes[2].InnerText.Trim();
                                var procedureName = childNodes[3].InnerText.Trim();
                                var nameText = childNodes[4].InnerText.Trim();
                                var taxPeriodText = childNodes[5].InnerText.Trim();
                                var declarationTypeText = childNodes[6].InnerText.Trim();
                                var additionalTimesText = childNodes[7].InnerText.Trim();
                                var submitTimesText = childNodes[8].InnerText.Trim();
                                var taxAgencyNameText = childNodes[9].InnerText.Trim();
                                var submitDateText = childNodes[10].InnerText.Trim();
                                var stateText = childNodes[11].InnerText.Trim();

                                string codeText = "";
                                if (!string.IsNullOrEmpty(nameText))
                                {
                                    var parts = nameText.Split(new[] { " - " }, StringSplitOptions.RemoveEmptyEntries);
                                    if (parts.Length > 0)
                                    {
                                        codeText = parts[0].Trim();
                                    }
                                }

                                string taxAgencyCodeText = "";
                                if (!string.IsNullOrEmpty(taxAgencyNameText))
                                {
                                    var parts = taxAgencyNameText.Split(new[] { " - " }, StringSplitOptions.RemoveEmptyEntries);
                                    if (parts.Length > 0)
                                    {
                                        taxAgencyCodeText = parts[0].Trim();
                                    }
                                }

                                var iTaxDec = new TaxDeclarationSubmitted()
                                {
                                    TaxCode = _taxcode,
                                    Order = orderText,
                                    TransactionID = transactionIdText,
                                    Code = codeText,
                                    Name = nameText,
                                    TaxPeriod = taxPeriodText,
                                    DeclarationType = declarationTypeText,
                                    AdditionalTimes = additionalTimesText,
                                    SubmitTimes = submitTimesText,
                                    TaxAgencyCode = taxAgencyCodeText,
                                    TaxAgencyName = taxAgencyNameText,
                                    State = stateText
                                };

                                if (DateTime.TryParseExact(
                                    submitDateText,
                                    "dd/MM/yyyy HH:mm",
                                    CultureInfo.InvariantCulture,
                                    DateTimeStyles.None,
                                    out DateTime dateValue))
                                {
                                    iTaxDec.SubmitDate = dateValue.ToString("dd/MM/yyyy HH:mm:ss");
                                }

                                if (!string.IsNullOrWhiteSpace(iTaxDec.Order) && iTaxDec.Order.Contains("."))
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

                                if (!string.IsNullOrWhiteSpace(iTaxDec.TransactionID))
                                {
                                    await ProcessTransactionFile(iTaxDec);
                                }

                                lstTaxDecSubmitted.Add(iTaxDec);
                            }
                        }
                        catch (Exception ex)
                        {
                            LogUtil.LogError(ex, $"PublicServiceCrawler.ExtractTaxDecSubmitted.RowError, index: {i}");
                        }
                    }
                }
                LogUtil.LogTrace("PublicServiceCrawler.ExtractTaxDecSubmitted.End");
            }
            catch (Exception ex)
            {
                LogUtil.LogError(ex);
            }
            await Task.CompletedTask;
        }

        /// <summary>
        /// Đi tới trang chi tiết hồ sơ tờ khai (/tthc/tchs/files/detail/{maHoSo}?loai=)
        /// </summary>
        private async Task<HtmlDocument> Step13_GetDetailPage(string maHoSo)
        {
            HttpResponseMessage responseMessage = null;
            HtmlDocument doc = new HtmlDocument();
            try
            {
                LogUtil.LogTrace($"PublicServiceCrawler.Step13_GetDetailPage.Start, maHoSo: {maHoSo}");
                var request = new HttpRequestMessage(HttpMethod.Get, $"/tthc/tchs/files/detail/{maHoSo}?loai=");
                request.Headers.Add("Referer", $"{PUBLIC_SERVICE_URL}/tthc/tchs");

                responseMessage = await _dvcClient.SendAsync(request, _stoppingToken);
                responseMessage.EnsureSuccessStatusCode();

                var html = await responseMessage.Content.ReadAsStringAsync();

                var match = Regex.Match(html, @"name=""_csrf""\s+value=""(.*?)""");
                if (match.Success && match.Groups.Count > 1)
                {
                    _csrfToken = match.Groups[1].Value;
                }
                else
                {
                    match = Regex.Match(html, @"id=""csrfToken""\s+value=""(.*?)""");
                    if (match.Success && match.Groups.Count > 1)
                    {
                        _csrfToken = match.Groups[1].Value;
                    }
                }

                doc.LoadHtml(html);
                LogUtil.LogTrace("PublicServiceCrawler.Step13_GetDetailPage.End");
            }
            catch (Exception ex)
            {
                LogUtil.LogError(ex, responseMessage);
                throw;
            }
            return doc;
        }

        /// <summary>
        /// Bước 13: Đọc và tải file tờ khai dựa vào API /tthc/tchs/downloadhoso
        /// Trả về true nếu tải và ghi file thành công (có ít nhất 1 file tờ khai), ngược lại false.
        /// Không còn swallow lỗi: lỗi hợp lệ (XML/Zip sai) được đánh dấu và ghi log cảnh báo.
        /// </summary>
        private async Task<bool> Step13_DownloadTransactionFile(TaxDeclarationSubmitted iTaxDec, string csrfToken = null)
        {
            HttpResponseMessage responseMessage = null;
            var token = string.IsNullOrEmpty(csrfToken) ? _csrfToken : csrfToken;
            bool hasDownloadedFile = false;
            try
            {
                LogUtil.LogTrace($"PublicServiceCrawler.Step13_DownloadTransactionFile.Start, maHoSo: {iTaxDec.TransactionID}");
                string transactionFolder = $"{FileUtil.BASE_PATH}/OutputFiles/{_profileID}/{_customerID}/{iTaxDec.TransactionID}";
                string declarationsFolder = Path.Combine(transactionFolder, "Declarations");
                string documentsFolder = Path.Combine(transactionFolder, "Documents");
                Directory.CreateDirectory(declarationsFolder);
                Directory.CreateDirectory(documentsFolder);

                var payload = JsonConvert.SerializeObject(new { maHoSo = iTaxDec.TransactionID });
                var request = new HttpRequestMessage(HttpMethod.Post, "/tthc/tchs/downloadhoso")
                {
                    Content = new StringContent(payload, Encoding.UTF8, "application/json")
                };

                request.Headers.Add("Referer", $"{PUBLIC_SERVICE_URL}/tthc/tchs/files/detail/{iTaxDec.TransactionID}?loai=");
                if (!string.IsNullOrEmpty(token))
                {
                    request.Headers.Add("X-XSRF-TOKEN", token);
                }

                responseMessage = await _dvcClient.SendAsync(request, _stoppingToken);
                responseMessage.EnsureSuccessStatusCode();

                var jsonStr = await responseMessage.Content.ReadAsStringAsync();
                var jsonObj = JObject.Parse(jsonStr);

                var contentBase64 = jsonObj["content"]?.ToString();
                var fileName = jsonObj["fileName"]?.ToString();

                if (string.IsNullOrEmpty(contentBase64))
                {
                    LogUtil.LogError($"PublicServiceCrawler.Step13_DownloadTransactionFile.EmptyContent, maHoSo: {iTaxDec.TransactionID}");
                    return false;
                }

                byte[] fileBytes = Convert.FromBase64String(contentBase64);

                if (Path.GetExtension(fileName).Equals(".zip", StringComparison.OrdinalIgnoreCase))
                {
                    using (var stream = new MemoryStream(fileBytes))
                    using (var archive = new ZipArchive(stream, ZipArchiveMode.Read))
                    {
                        foreach (var entry in archive.Entries)
                        {
                            if (string.IsNullOrEmpty(entry.Name)) continue;
                            string destFolder;
                            if (entry.Name.EndsWith("_0.xml", StringComparison.OrdinalIgnoreCase))
                            {
                                destFolder = declarationsFolder;
                                if (string.IsNullOrEmpty(iTaxDec.FileName))
                                {
                                    iTaxDec.FileName = entry.Name;
                                }
                            }
                            else
                            {
                                destFolder = documentsFolder;
                            }
                            string destinationPath = Path.Combine(destFolder, entry.FullName);
                            string dirPath = Path.GetDirectoryName(destinationPath);
                            if (!string.IsNullOrEmpty(dirPath) && !Directory.Exists(dirPath))
                            {
                                Directory.CreateDirectory(dirPath);
                            }
                            using (var entryStream = entry.Open())
                            using (var fileStream = File.Create(destinationPath))
                            {
                                entryStream.CopyTo(fileStream);
                            }
                            if (entry.Name.EndsWith("_0.xml", StringComparison.OrdinalIgnoreCase))
                            {
                                hasDownloadedFile = true;
                            }
                        }
                    }
                }
                else
                {
                    string destFolder = Path.GetExtension(fileName).Equals(".xml", StringComparison.OrdinalIgnoreCase) ? declarationsFolder : documentsFolder;
                    iTaxDec.FileName = fileName;
                    string filePath = Path.Combine(destFolder, fileName);
                    File.WriteAllBytes(filePath, fileBytes);
                    LogUtil.LogTrace($"PublicServiceCrawler.Step13_DownloadTransactionFile.SaveFile: {filePath}");
                    if (Path.GetExtension(fileName).Equals(".xml", StringComparison.OrdinalIgnoreCase))
                    {
                        hasDownloadedFile = true;
                    }
                }

                var mainXmlFile = Directory.GetFiles(declarationsFolder, "*_0.xml", SearchOption.AllDirectories).FirstOrDefault();
                if (string.IsNullOrEmpty(mainXmlFile))
                {
                    mainXmlFile = Directory.GetFiles(declarationsFolder, "*.xml", SearchOption.AllDirectories).FirstOrDefault();
                }

                if (!string.IsNullOrEmpty(mainXmlFile))
                {
                    try
                    {
                        if (string.IsNullOrEmpty(iTaxDec.FileName))
                        {
                            iTaxDec.FileName = Path.GetFileName(mainXmlFile);
                        }

                        LogUtil.LogTrace($"PublicServiceCrawler.Step13_DownloadTransactionFile.ExtractXml.Start: {mainXmlFile}");
                        XmlDocument xmlDoc = new XmlDocument();
                        xmlDoc.Load(mainXmlFile);

                        XmlNamespaceManager ns = new XmlNamespaceManager(xmlDoc.NameTable);
                        ns.AddNamespace("msbld", "http://kekhaithue.gdt.gov.vn/TKhaiThue");

                        LogUtil.LogTrace("PublicServiceCrawler.Step13_DownloadTransactionFile.ExtractXml.taxDecCode");
                        var taxDecCodeNode = xmlDoc.SelectSingleNode("//msbld:maTKhai", ns);
                        if (taxDecCodeNode != null)
                        {
                            var taxDecCode = taxDecCodeNode.InnerText;
                            iTaxDec.Code = taxDecCode;
                            switch (taxDecCode)
                            {
                                case "01":
                                    iTaxDec.DebitAmount = xmlDoc.SelectSingleNode("//msbld:ct40", ns)?.InnerText;
                                    iTaxDec.CreditAmount = xmlDoc.SelectSingleNode("//msbld:ct43", ns)?.InnerText;
                                    break;
                                case "03":
                                    iTaxDec.DebitAmount = xmlDoc.SelectSingleNode("//msbld:ctG", ns)?.InnerText;
                                    break;
                                case "394":
                                    iTaxDec.DebitAmount = xmlDoc.SelectSingleNode("//msbld:ct32", ns)?.InnerText;
                                    break;
                                case "395":
                                    break;
                                case "842":
                                    iTaxDec.DebitAmount = xmlDoc.SelectSingleNode("//msbld:ct40", ns)?.InnerText;
                                    iTaxDec.CreditAmount = xmlDoc.SelectSingleNode("//msbld:ct43", ns)?.InnerText;
                                    break;
                                case "864":
                                    iTaxDec.DebitAmount = xmlDoc.SelectSingleNode("//msbld:ct29", ns)?.InnerText;
                                    break;
                                case "892":
                                    iTaxDec.DebitAmount = xmlDoc.SelectSingleNode("//msbld:ctI", ns)?.InnerText;
                                    break;
                                case "953":
                                    break;
                                default:
                                    break;
                            }
                        }

                        LogUtil.LogTrace("PublicServiceCrawler.Step13_DownloadTransactionFile.ExtractXml.TaxAgencyCode");
                        var taxAgencyCodeNode = xmlDoc.SelectSingleNode("//msbld:maCQTNoiNop", ns);
                        if (taxAgencyCodeNode != null)
                        {
                            iTaxDec.TaxAgencyCode = taxAgencyCodeNode.InnerText;
                        }
                        LogUtil.LogTrace($"PublicServiceCrawler.Step13_DownloadTransactionFile.ExtractXml.End");
                    }
                    catch (Exception exXml)
                    {
                        LogUtil.LogError(exXml, $"PublicServiceCrawler.Step13_DownloadTransactionFile.ParseXmlFailed, maHoSo: {iTaxDec.TransactionID}, file: {mainXmlFile}");
                        return false;
                    }
                }

                if (!hasDownloadedFile)
                {
                    LogUtil.LogError($"PublicServiceCrawler.Step13_DownloadTransactionFile.NoXmlFile, maHoSo: {iTaxDec.TransactionID}");
                    return false;
                }

                LogUtil.LogTrace("PublicServiceCrawler.Step13_DownloadTransactionFile.End");
                return true;
            }
            catch (Exception ex)
            {
                LogUtil.LogError(ex, responseMessage);
                return false;
            }
        }

        /// <summary>
        /// Bước 14: Đọc và tải danh sách thông báo từ modalThongBao trong trang chi tiết
        /// </summary>
        private async Task Step14_GetThongBaoPageData(TaxDeclarationSubmitted iTaxDec, HtmlDocument detailDoc)
        {
            try
            {
                LogUtil.LogTrace("PublicServiceCrawler.Step14_GetThongBaoPageData.Start");
                var lstTaxDecNotification = new List<TaxDecNotification>();

                var modalThongBaoNode = detailDoc.DocumentNode.SelectSingleNode("//*[@id='modalThongBao']");
                if (modalThongBaoNode != null)
                {
                    var aNodes = modalThongBaoNode.SelectNodes(".//a[@data-id]") ?? modalThongBaoNode.SelectNodes(".//a[contains(@onclick, 'downloadThongBao')]");
                    if (aNodes != null)
                    {
                        foreach (var aNode in aNodes)
                        {
                            string idTbao = aNode.GetAttributeValue("data-id", "");
                            if (!string.IsNullOrEmpty(idTbao))
                            {
                                var parentRow = aNode.SelectSingleNode("./ancestor::div[contains(@class, 'row')][1]");
                                string title = "";
                                string sendDate = "";

                                if (parentRow != null)
                                {
                                    var titleNode = parentRow.SelectSingleNode(".//div[contains(@class, 'fw-bold')]");
                                    if (titleNode != null)
                                    {
                                        title = titleNode.InnerText.Trim();
                                    }

                                    var col9 = parentRow.SelectSingleNode(".//div[contains(@class, 'col-md-9')]");
                                    if (col9 != null)
                                    {
                                        var divNodes = col9.SelectNodes("./div");
                                        if (divNodes != null && divNodes.Count > 1)
                                        {
                                            sendDate = divNodes[1].InnerText.Trim();
                                        }
                                    }
                                }

                                var iNoti = new TaxDecNotification
                                {
                                    NotificationID = idTbao,
                                    Message = title,
                                };

                                if (DateTime.TryParseExact(
                                    sendDate,
                                    "HH:mm dd/MM/yyyy",
                                    CultureInfo.InvariantCulture,
                                    DateTimeStyles.None,
                                    out DateTime dateValue))
                                {
                                    iNoti.SendDate = dateValue.ToString("dd/MM/yyyy HH:mm:ss");
                                }

                                bool notiOk = false;
                                await CommonPattern.Retry(async () =>
                                {
                                    notiOk = await Step17_DownloadNotificationFile(iTaxDec, iNoti, _csrfToken);
                                    if (!notiOk)
                                    {
                                        throw new Exception("Download notification file failed");
                                    }
                                    await Task.Delay(300, _stoppingToken);
                                }, 3, 2000);
                                if (!notiOk)
                                {
                                    iTaxDec.HasDownloadError = true;
                                    iTaxDec.DownloadErrorMsg = $"Tải thông báo {idTbao} thất bại sau nhiều lần thử";
                                    LogUtil.LogError($"PublicServiceCrawler.Step14_GetThongBaoPageData.DownloadFail, idTbao: {idTbao}");
                                }

                                lstTaxDecNotification.Add(iNoti);
                            }
                        }
                    }
                }

                iTaxDec.Notifications = lstTaxDecNotification;
                LogUtil.LogTrace("PublicServiceCrawler.Step14_GetThongBaoPageData.End");
            }
            catch (Exception ex)
            {
                LogUtil.LogError(ex);
            }
        }

        /// <summary>
        /// Xử lý tải chi tiết, file tờ khai và danh sách thông báo cho 1 hồ sơ.
        /// Có retry riêng cho từng bước; nếu phiên hết hạn (token/session) sẽ re-login và thử lại.
        /// Mọi lỗi được đánh dấu lên iTaxDec (HasDownloadError/DownloadErrorMsg) thay vì bỏ qua im lặng.
        /// </summary>
        private async Task ProcessTransactionFile(TaxDeclarationSubmitted iTaxDec)
        {
            try
            {
                LogUtil.LogTrace($"PublicServiceCrawler.ProcessTransactionFile.Start, maHoSo: {iTaxDec.TransactionID}");
                string folderPath = $"{FileUtil.BASE_PATH}/OutputFiles/{_profileID}/{_customerID}/{iTaxDec.TransactionID}";
                Directory.CreateDirectory(folderPath);

                HtmlDocument detailDoc = null;
                await CommonPattern.Retry(async () =>
                {
                    detailDoc = await Step13_GetDetailPage(iTaxDec.TransactionID);
                    await Task.Delay(300, _stoppingToken);
                }, 3, 1000);

                bool declOk = false;
                await CommonPattern.Retry(async () =>
                {
                    declOk = await Step13_DownloadTransactionFile(iTaxDec);
                    if (!declOk)
                    {
                        // Làm mới CSRF token (lấy lại trang chi tiết) trước khi thử lại, phòng phiên hết hạn
                        try
                        {
                            detailDoc = await Step13_GetDetailPage(iTaxDec.TransactionID);
                        }
                        catch { }
                        throw new Exception("Download declaration file failed");
                    }
                    await Task.Delay(300, _stoppingToken);
                }, 3, 1000);
                if (!declOk)
                {
                    iTaxDec.HasDownloadError = true;
                    iTaxDec.DownloadErrorMsg = "Tải file tờ khai thất bại sau nhiều lần thử";
                    LogUtil.LogError($"PublicServiceCrawler.ProcessTransactionFile.DownloadFail, maHoSo: {iTaxDec.TransactionID}");
                }

                await Step14_GetThongBaoPageData(iTaxDec, detailDoc);
                await Task.Delay(300, _stoppingToken);

                LogUtil.LogTrace($"PublicServiceCrawler.ProcessTransactionFile.End, maHoSo: {iTaxDec.TransactionID}");
            }
            catch (Exception ex)
            {
                iTaxDec.HasDownloadError = true;
                iTaxDec.DownloadErrorMsg = ex.Message;
                LogUtil.LogError(ex, $"PublicServiceCrawler.ProcessTransactionFile.Failed, maHoSo: {iTaxDec.TransactionID}");
            }
        }

        /// <summary>
        /// Bước 17: Tải file thông báo dựa vào API /tthc/tchs/downloadthongbao
        /// File thông báo luôn là dạng .xml, ghi trực tiếp và parse nội dung XML.
        /// Trả về true nếu tải và lưu file thông báo thành công.
        /// </summary>
        private async Task<bool> Step17_DownloadNotificationFile(TaxDeclarationSubmitted iTaxDec, TaxDecNotification iTaxDecNoti, string csrfToken = null)
        {
            HttpResponseMessage responseMessage = null;
            var token = string.IsNullOrEmpty(csrfToken) ? _csrfToken : csrfToken;
            bool saved = false;
            try
            {
                LogUtil.LogTrace($"PublicServiceCrawler.Step17_DownloadNotificationFile.Start, idTbao: {iTaxDecNoti.NotificationID}");
                string transactionFolder = $"{FileUtil.BASE_PATH}/OutputFiles/{_profileID}/{_customerID}/{iTaxDec.TransactionID}";
                string notificationsFolder = Path.Combine(transactionFolder, "Notifications");
                Directory.CreateDirectory(notificationsFolder);

                var payload = JsonConvert.SerializeObject(new
                {
                    idTbao = iTaxDecNoti.NotificationID,
                    loaiTBao = ""
                });

                var request = new HttpRequestMessage(HttpMethod.Post, "/tthc/tchs/downloadthongbao")
                {
                    Content = new StringContent(payload, Encoding.UTF8, "application/json")
                };
                request.Headers.Add("Referer", $"{PUBLIC_SERVICE_URL}/tthc/tchs/files/detail/{iTaxDec.TransactionID}?loai=");
                if (!string.IsNullOrEmpty(token))
                {
                    request.Headers.Add("X-XSRF-TOKEN", token);
                }

                responseMessage = await _dvcClient.SendAsync(request, _stoppingToken);
                responseMessage.EnsureSuccessStatusCode();

                var jsonStr = await responseMessage.Content.ReadAsStringAsync();
                var jsonObj = JObject.Parse(jsonStr);

                var contentBase64 = jsonObj["content"]?.ToString();
                var fileName = jsonObj["fileName"]?.ToString();

                if (!string.IsNullOrEmpty(contentBase64))
                {
                    byte[] fileBytes = Convert.FromBase64String(contentBase64);

                    if (string.IsNullOrEmpty(fileName))
                    {
                        fileName = $"Thong bao_{iTaxDecNoti.NotificationID}.xml";
                    }

                    if (string.IsNullOrEmpty(iTaxDecNoti.FileName))
                    {
                        iTaxDecNoti.FileName = fileName;
                    }

                    string filePath = Path.Combine(notificationsFolder, fileName);
                    File.WriteAllBytes(filePath, fileBytes);
                    LogUtil.LogTrace($"PublicServiceCrawler.Step17_DownloadNotificationFile.SaveFile: {filePath}");
                    ParseNotificationXml(filePath, iTaxDecNoti);
                    saved = true;
                }
                else
                {
                    LogUtil.LogError($"PublicServiceCrawler.Step17_DownloadNotificationFile.EmptyContent, idTbao: {iTaxDecNoti.NotificationID}");
                }

                LogUtil.LogTrace("PublicServiceCrawler.Step17_DownloadNotificationFile.End");
                return saved;
            }
            catch (Exception ex)
            {
                LogUtil.LogError(ex, responseMessage);
                return false;
            }
        }

        /// <summary>
        /// Trích xuất thông tin từ file XML thông báo
        /// </summary>
        private void ParseNotificationXml(string xmlFilePath, TaxDecNotification iTaxDecNoti)
        {
            try
            {
                if (!File.Exists(xmlFilePath)) return;

                LogUtil.LogTrace($"Step17_DownloadNotificationFile.ExtractXml: {xmlFilePath}");
                XmlDocument xmlDoc = new XmlDocument();
                xmlDoc.Load(xmlFilePath);
                LogUtil.LogTrace("Step17_DownloadNotificationFile.LoadDoc");

                XmlNamespaceManager ns = new XmlNamespaceManager(xmlDoc.NameTable);
                ns.AddNamespace("msbld", "http://kekhaithue.gdt.gov.vn/TBaoThue");

                var maTBaoNode = xmlDoc.SelectSingleNode("//msbld:maTBao", ns);
                if (maTBaoNode != null)
                {
                    iTaxDecNoti.Code = maTBaoNode.InnerText;
                    iTaxDecNoti.Name = xmlDoc.SelectSingleNode("//msbld:soTBao", ns)?.InnerText;
                    iTaxDecNoti.XMLTrangThai = xmlDoc.SelectSingleNode("//msbld:trangThai", ns)?.InnerText;
                    iTaxDecNoti.XMLTenTBao = xmlDoc.SelectSingleNode("//msbld:tenTBao", ns)?.InnerText;
                    LogUtil.LogTrace($"Step17_DownloadNotificationFile.notiCode: {iTaxDecNoti.Code}, XMLTrangThai: {iTaxDecNoti.XMLTrangThai}");
                }
            }
            catch (Exception ex)
            {
                LogUtil.LogError(ex);
            }
        }

        #endregion

        #region Các hàm public cho tầng trên UI gọi

        /// <summary>
        /// Hành động kiểm tra tài khoản
        /// </summary>
        public async Task CheckAccount()
        {
            await CommonPattern.Retry(async () =>
            {
                await SignIn();
            }, 5);
        }

        /// <summary>
        /// Hàm tra cứu tờ khai đã nộp
        /// </summary>
        public async Task<List<TaxDeclarationSubmitted>> GetTaxDecSubmitted(string fromDate, string toDate)
        {
            List<TaxDeclarationSubmitted> result = null;

            await CommonPattern.Retry(async () =>
            {
                await SignIn();
                await Step9_GotoTraCuuToKhaiPage();
                await Task.Delay(500, _stoppingToken);

                var captchaResult = await Step3_ResolveCaptcha("/tthc/getCaptcha");
                await Task.Delay(500, _stoppingToken);

                var docResult = await Step10_ClickButtonTraCuu(fromDate, toDate, captchaResult);
                await Task.Delay(500, _stoppingToken);

                result = await Step11_ExtractTaxDecSubmitted(docResult, fromDate, toDate, captchaResult);
            }, 5);

            return result ?? new List<TaxDeclarationSubmitted>();
        }

        /// <summary>
        /// Hàm tra cứu giấy nộp tiền
        /// </summary>
        public async Task<HtmlDocument> GetDocPaymentRequest(SyncPaymentRequestInputDetail detail, string fromDate, string toDate)
        {
            return await CommonPattern.Retry(async () =>
            {
                await SignIn();

                await StepGNT1_GotoDichVuKhacPage();
                await Task.Delay(500, _stoppingToken);

                string step2Url = await StepGNT2_RedirectToService();
                await Task.Delay(500, _stoppingToken);

                string step3Url = await StepGNT3_GetEtaxSession(step2Url);
                await Task.Delay(500, _stoppingToken);

                string step4Url = await StepGNT4_CorpJumpProc(step3Url);
                await Task.Delay(500, _stoppingToken);

                string step5Url = await StepGNT5_EstablishSession(step4Url);
                await Task.Delay(500, _stoppingToken);

                var step6Form = await StepGNT6_RequestWithNewSession(step5Url);
                await Task.Delay(500, _stoppingToken);

                var step7Form = await StepGNT7_LoginSSO(step6Form);
                await Task.Delay(500, _stoppingToken);

                await StepGNT8_GotoMainPage(step7Form);
                await Task.Delay(500, _stoppingToken);

                await Step9_GotoTraCuuGiayNopTienPage();
                return await Step10_ClickButtonTraCuuGiayNopTien(fromDate, toDate);
            }, 3);
        }

        #endregion

        #region Các hàm hỗ trợ chuẩn hóa luồng eTax SSO

        /// <summary>
        /// Bóc tách toàn bộ input trong form HTML (dùng cho các form auto-submit trong DSE SSO)
        /// </summary>
        private Dictionary<string, string> ExtractFormInputs(string html, string formId = "mainForm")
        {
            var formValues = new Dictionary<string, string>();
            if (string.IsNullOrWhiteSpace(html)) return formValues;

            var doc = new HtmlAgilityPack.HtmlDocument();
            doc.LoadHtml(html);

            var formNode = doc.DocumentNode.SelectSingleNode($"//form[@id='{formId}']")
                ?? doc.DocumentNode.SelectSingleNode("//form");

            if (formNode != null)
            {
                var inputs = formNode.SelectNodes(".//input");
                if (inputs != null)
                {
                    foreach (var input in inputs)
                    {
                        var name = input.GetAttributeValue("name", null);
                        if (!string.IsNullOrEmpty(name))
                        {
                            var value = input.GetAttributeValue("value", "");
                            formValues[name] = value;
                        }
                    }
                }
            }
            return formValues;
        }

        /// <summary>
        /// Chuẩn hóa đường dẫn tương đối trên thuedientu đảm bảo có context path /etaxnnt/
        /// </summary>
        private string NormalizeEtaxRelativeUrl(string url)
        {
            if (string.IsNullOrWhiteSpace(url)) return url;
            url = url.Trim();
            if (url.StartsWith("http://", StringComparison.OrdinalIgnoreCase) || 
                url.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            {
                return url;
            }

            if (!url.StartsWith("/"))
            {
                url = "/" + url;
            }

            if (!url.StartsWith("/etaxnnt/", StringComparison.OrdinalIgnoreCase) && 
                !url.Equals("/etaxnnt", StringComparison.OrdinalIgnoreCase))
            {
                url = "/etaxnnt" + url;
            }

            return url;
        }

        #endregion

        #region Các bước Tra cứu giấy nộp tiền (thuedientu.gdt.gov.vn)

        /// <summary>
        /// Bước GNT1: Đi tới trang Dịch vụ khác (/tthc/dich-vu-khac)
        /// </summary>
        private async Task StepGNT1_GotoDichVuKhacPage()
        {
            HttpResponseMessage responseMessage = null;
            try
            {
                LogUtil.LogTrace("PublicServiceCrawler.StepGNT1_GotoDichVuKhacPage.Start");

                var request = new HttpRequestMessage(HttpMethod.Get, "/tthc/dich-vu-khac");
                request.Headers.Add("Referer", $"{PUBLIC_SERVICE_URL}/tthc/home");
                request.Headers.Add("Accept", "text/html,application/xhtml+xml,application/xml;q=0.9,image/avif,image/webp,image/apng,*/*;q=0.8,application/signed-exchange;v=b3;q=0.7");

                responseMessage = await _dvcClient.SendAsync(request, _stoppingToken);
                responseMessage.EnsureSuccessStatusCode();

                LogUtil.LogTrace("PublicServiceCrawler.StepGNT1_GotoDichVuKhacPage.End");
            }
            catch (Exception ex)
            {
                LogUtil.LogError(ex, responseMessage);
                throw;
            }
        }

        /// <summary>
        /// Bước GNT2: POST redirect-to-service?module=330410 để lấy URL chuyển hướng sang thuedientu
        /// </summary>
        private async Task<string> StepGNT2_RedirectToService()
        {
            HttpResponseMessage responseMessage = null;
            string redirectUrl = null;
            try
            {
                LogUtil.LogTrace("PublicServiceCrawler.StepGNT2_RedirectToService.Start");

                var request = new HttpRequestMessage(HttpMethod.Post, "/tthc/sso/redirect-to-service?module=330410");
                request.Content = new StringContent("", Encoding.UTF8);
                request.Headers.Add("Referer", $"{PUBLIC_SERVICE_URL}/tthc/dich-vu-khac");
                request.Headers.Add("X-Requested-With", "XMLHttpRequest");
                request.Headers.Add("X-XSRF-TOKEN", _csrfToken);
                request.Headers.Add("Accept", "*/*");

                responseMessage = await _dvcClient.SendAsync(request, _stoppingToken);
                responseMessage.EnsureSuccessStatusCode();

                redirectUrl = await responseMessage.Content.ReadAsStringAsync();
                if (!string.IsNullOrWhiteSpace(redirectUrl))
                {
                    redirectUrl = redirectUrl.Trim().Trim('"');
                    if (redirectUrl.StartsWith("{") && redirectUrl.EndsWith("}"))
                    {
                        try
                        {
                            var jobj = JObject.Parse(redirectUrl);
                            redirectUrl = jobj["url"]?.ToString() ?? jobj["data"]?.ToString() ?? redirectUrl;
                        }
                        catch { }
                    }
                }

                if (string.IsNullOrWhiteSpace(redirectUrl) || !redirectUrl.StartsWith("http", StringComparison.OrdinalIgnoreCase))
                {
                    throw new Exception($"Không lấy được URL chuyển hướng sang thuedientu từ DVC. Phản hồi: {redirectUrl}");
                }

                LogUtil.LogTrace($"PublicServiceCrawler.StepGNT2_RedirectToService.End, redirectUrl: {redirectUrl}");
            }
            catch (Exception ex)
            {
                LogUtil.LogError(ex, responseMessage);
                throw;
            }

            return redirectUrl;
        }

        /// <summary>
        /// Bước GNT3: GET URL redirect trên thuedientu.gdt.gov.vn để lấy JSESSIONID và dse_sessionId
        /// URL dạng: https://thuedientu.gdt.gov.vn/etaxnnt/?vnconnect=SSOTHUE&code=...&module=330410
        /// Response trả về HTML chứa form auto-submit với hidden fields (dse_sessionId, dse_operationName, ...)
        /// JavaScript sẽ tự submit form POST tới /etaxnnt/Request
        /// </summary>
        private async Task<string> StepGNT3_GetEtaxSession(string redirectUrl)
        {
            HttpResponseMessage responseMessage = null;
            var nextUrl = string.Empty;
            try
            {
                LogUtil.LogTrace($"PublicServiceCrawler.StepGNT3_GetEtaxSession.Start, url: {redirectUrl}");

                // Cần gọi trực tiếp URL đầy đủ (khác domain) nên dùng etaxClient
                var request = new HttpRequestMessage(HttpMethod.Get, redirectUrl);
                request.Headers.Add("Accept", "text/html,application/xhtml+xml,application/xml;q=0.9,image/avif,image/webp,image/apng,*/*;q=0.8,application/signed-exchange;v=b3;q=0.7");
                request.Headers.Add("Referer", $"{PUBLIC_SERVICE_URL}/");

                responseMessage = await _client.SendAsync(request, _stoppingToken);
                if (responseMessage.Headers.Location != null)
                {
                    nextUrl = responseMessage.Headers.Location.ToString();
                }
                else
                {
                    // Fallback: nếu server trả về HTML script redirect
                    var html = await responseMessage.Content.ReadAsStringAsync();
                    var match = Regex.Match(html, @"window\.location\.href\s*=\s*'([^']+)'");
                    if (match.Success)
                    {
                        nextUrl = match.Groups[1].Value;
                    }
                }

                if (string.IsNullOrWhiteSpace(nextUrl))
                {
                    throw new Exception("Không lấy được URL chuyển hướng sang thuedientu");
                }

                nextUrl = NormalizeEtaxRelativeUrl(nextUrl);
                LogUtil.LogTrace($"PublicServiceCrawler.StepGNT3_GetEtaxSession.End, nextUrl: {nextUrl}");
            }
            catch (Exception ex)
            {
                LogUtil.LogError(ex, responseMessage);
                throw;
            }

            return nextUrl;
        }

        /// <summary>
        /// Bước GNT4: POST form data từ StepGNT3 tới /etaxnnt/Request
        /// Mô phỏng hành vi auto-submit form: document.getElementById("mainForm").submit()
        /// Form chứa: dse_sessionId, dse_applicationId, dse_pageId, dse_operationName, 
        /// dse_errorPage, dse_processorState, dse_nextEventName
        /// </summary>
        private async Task<string> StepGNT4_CorpJumpProc(string corpJumpUrl)
        {
            HttpResponseMessage responseMessage = null;
            var nextUrl = string.Empty;
            try
            {
                LogUtil.LogTrace($"PublicServiceCrawler.StepGNT4_CorpJumpProc.Start, url: {corpJumpUrl}");

                var request = new HttpRequestMessage(HttpMethod.Get, corpJumpUrl);
                request.Headers.Add("Accept", "text/html,application/xhtml+xml,application/xml;q=0.9,*/*;q=0.8");
                request.Headers.Add("Referer", $"{PUBLIC_SERVICE_URL}/");

                responseMessage = await _client.SendAsync(request, _stoppingToken);
                if (responseMessage.Headers.Location != null)
                {
                    nextUrl = responseMessage.Headers.Location.ToString();
                }
                else
                {
                    // Fallback: nếu eTax trả về mã script redirect thay vì header 302
                    var html = await responseMessage.Content.ReadAsStringAsync();
                    var match = Regex.Match(html, @"window\.location\.href\s*=\s*'([^']+)'");
                    if (match.Success)
                    {
                        nextUrl = match.Groups[1].Value;
                    }
                }

                if (string.IsNullOrWhiteSpace(nextUrl))
                {
                    throw new Exception("Không nhận được URL chuyển hướng từ corpJumpProc trên thuedientu");
                }

                nextUrl = NormalizeEtaxRelativeUrl(nextUrl);
                LogUtil.LogTrace($"PublicServiceCrawler.StepGNT4_CorpJumpProc.End, nextUrl: {nextUrl}");
            }
            catch (Exception ex)
            {
                LogUtil.LogError(ex, responseMessage);
                throw;
            }

            return nextUrl;
        }

        /// <summary>
        /// Bước GNT5: GET EstablishSession để thiết lập session mới trên thuedientu
        /// </summary>
        public async Task<string> StepGNT5_EstablishSession(string requestUrl)
        {
            HttpResponseMessage responseMessage = null;
            var nextUrl = string.Empty;
            try
            {
                LogUtil.LogTrace("PublicServiceCrawler.StepGNT5_EstablishSession.Start");

                var request = new HttpRequestMessage(HttpMethod.Get, requestUrl);
                request.Headers.Add("Accept", "text/html,application/xhtml+xml,application/xml;q=0.9,*/*;q=0.8");
                request.Headers.Add("Referer", $"{PUBLIC_SERVICE_URL}/");

                responseMessage = await _client.SendAsync(request, _stoppingToken);

                // Đọc body HTML ra chuỗi
                string html = await responseMessage.Content.ReadAsStringAsync();

                var doc = new HtmlAgilityPack.HtmlDocument();
                doc.LoadHtml(html);

                var scriptNode = doc.DocumentNode.SelectSingleNode("//script");
                string scriptContent = scriptNode?.InnerText ?? "";

                var match = Regex.Match(scriptContent, @"window\.location\.href\s*=\s*'([^']+)'");

                if (match.Success)
                {
                    nextUrl = match.Groups[1].Value;

                    var sessionMatch = Regex.Match(
                        nextUrl,
                        @"(?:\?|&)dse_sessionId=([^&""']+)",
                        RegexOptions.IgnoreCase
                    );

                    _etaxSessionId = sessionMatch.Success
                        ? Uri.UnescapeDataString(sessionMatch.Groups[1].Value)
                        : null;
                }
                else
                {
                    throw new Exception("Không tìm thấy window.location.href trong response của EstablishSession");
                }

                // Đảm bảo URL luôn có prefix hợp lệ /etaxnnt/ để tránh lỗi 404
                nextUrl = NormalizeEtaxRelativeUrl(nextUrl);
                LogUtil.LogTrace($"PublicServiceCrawler.StepGNT5_EstablishSession.End, sessionId: {_etaxSessionId}, nextUrl: {nextUrl}");
            }
            catch (Exception ex)
            {
                LogUtil.LogError(ex, responseMessage);
                throw;
            }

            return nextUrl;
        }

        /// <summary>
        /// Bước GNT6: GET Request với dse_pageId=1, corpJumpProc, toOpName=ssoTTHC
        /// </summary>
        private async Task<Dictionary<string, string>> StepGNT6_RequestWithNewSession(string requestUrl)
        {
            HttpResponseMessage responseMessage = null;
            Dictionary<string, string> formInputs = null;
            try
            {
                LogUtil.LogTrace("PublicServiceCrawler.StepGNT6_RequestWithNewSession.Start");

                var request = new HttpRequestMessage(HttpMethod.Get, requestUrl);
                request.Headers.Add("Accept", "text/html,application/xhtml+xml,application/xml;q=0.9,*/*;q=0.8");
                request.Headers.Add("Referer", $"{ETAX_URL}/etaxnnt/EstablishSession?&fromOpName=corpJumpProc&fromStateName=initial&fromEventName=start&toOpName=ssoTTHC");

                responseMessage = await _client.SendAsync(request, _stoppingToken);
                responseMessage.EnsureSuccessStatusCode();

                var html = await responseMessage.Content.ReadAsStringAsync();
                formInputs = ExtractFormInputs(html, "mainForm");

                // Cập nhật lại _etaxSessionId nếu trong form có trường dse_sessionId mới
                if (formInputs.TryGetValue("dse_sessionId", out var sid) && !string.IsNullOrWhiteSpace(sid))
                {
                    _etaxSessionId = sid;
                }

                LogUtil.LogTrace($"PublicServiceCrawler.StepGNT6_RequestWithNewSession.End, extracted {formInputs.Count} form inputs");
            }
            catch (Exception ex)
            {
                LogUtil.LogError(ex, responseMessage);
                throw;
            }

            return formInputs ?? new Dictionary<string, string>();
        }

        /// <summary>
        /// Bước GNT7: POST form đăng nhập SSO (corpUserLoginProc, startSSO_TTHC)
        /// Sử dụng chính xác các trường trong form HTML trả về từ Bước GNT6
        /// </summary>
        private async Task<Dictionary<string, string>> StepGNT7_LoginSSO(Dictionary<string, string> formInputs)
        {
            HttpResponseMessage responseMessage = null;
            Dictionary<string, string> nextFormInputs = null;
            try
            {
                LogUtil.LogTrace("PublicServiceCrawler.StepGNT7_LoginSSO.Start");

                IEnumerable<KeyValuePair<string, string>> postData;
                if (formInputs != null && formInputs.Count > 0)
                {
                    postData = formInputs;
                }
                else
                {
                    postData = new[]
                    {
                        new KeyValuePair<string, string>("dse_sessionId", _etaxSessionId),
                        new KeyValuePair<string, string>("dse_applicationId", "-1"),
                        new KeyValuePair<string, string>("dse_pageId", "2"),
                        new KeyValuePair<string, string>("dse_operationName", "corpUserLoginProc"),
                        new KeyValuePair<string, string>("dse_errorPage", "error_page.jsp"),
                        new KeyValuePair<string, string>("dse_processorState", "initial"),
                        new KeyValuePair<string, string>("dse_nextEventName", "startSSO_TTHC")
                    };
                }

                var request = new HttpRequestMessage(HttpMethod.Post, "/etaxnnt/Request")
                {
                    Content = new FormUrlEncodedContent(postData)
                };
                request.Headers.Add("Accept", "text/html,application/xhtml+xml,application/xml;q=0.9,*/*;q=0.8");
                request.Headers.Add("Referer", $"{ETAX_URL}/etaxnnt/Request?&dse_sessionId={_etaxSessionId}&dse_applicationId=-1&dse_pageId=1&dse_operationName=corpJumpProc&dse_errorPage=error_page.jsp&dse_processorState=initial&dse_nextEventName=start&toOpName=ssoTTHC");
                request.Headers.Add("Origin", ETAX_URL);

                responseMessage = await _client.SendAsync(request, _stoppingToken);
                responseMessage.EnsureSuccessStatusCode();

                var html = await responseMessage.Content.ReadAsStringAsync();
                nextFormInputs = ExtractFormInputs(html, "mainForm");
                if (nextFormInputs.TryGetValue("dse_sessionId", out var sid) && !string.IsNullOrWhiteSpace(sid))
                {
                    _etaxSessionId = sid;
                }

                LogUtil.LogTrace($"PublicServiceCrawler.StepGNT7_LoginSSO.End, nextForm: {nextFormInputs.Count} inputs");
            }
            catch (Exception ex)
            {
                LogUtil.LogError(ex, responseMessage);
                throw;
            }

            return nextFormInputs ?? new Dictionary<string, string>();
        }

        /// <summary>
        /// Bước GNT8: POST tới trang chính của thuedientu (corporateHomeProc, startTTHC)
        /// </summary>
        private async Task StepGNT8_GotoMainPage(Dictionary<string, string> formInputs)
        {
            HttpResponseMessage responseMessage = null;
            try
            {
                LogUtil.LogTrace("PublicServiceCrawler.StepGNT8_GotoMainPage.Start");

                IEnumerable<KeyValuePair<string, string>> postData;
                if (formInputs != null && formInputs.Count > 0 && formInputs.ContainsKey("dse_operationName"))
                {
                    postData = formInputs;
                }
                else
                {
                    postData = new[]
                    {
                        new KeyValuePair<string, string>("dse_sessionId", _etaxSessionId),
                        new KeyValuePair<string, string>("dse_applicationId", "-1"),
                        new KeyValuePair<string, string>("dse_pageId", "3"),
                        new KeyValuePair<string, string>("dse_operationName", "corporateHomeProc"),
                        new KeyValuePair<string, string>("dse_errorPage", "error_page.jsp"),
                        new KeyValuePair<string, string>("dse_processorState", "initial"),
                        new KeyValuePair<string, string>("dse_nextEventName", "startTTHC")
                    };
                }

                var request = new HttpRequestMessage(HttpMethod.Post, "/etaxnnt/Request")
                {
                    Content = new FormUrlEncodedContent(postData)
                };
                request.Headers.Add("Accept", "text/html,application/xhtml+xml,application/xml;q=0.9,*/*;q=0.8");
                request.Headers.Add("Referer", $"{ETAX_URL}/etaxnnt/Request");
                request.Headers.Add("Origin", ETAX_URL);

                responseMessage = await _client.SendAsync(request, _stoppingToken);

                LogUtil.LogTrace("PublicServiceCrawler.StepGNT8_GotoMainPage.End");
            }
            catch (Exception ex)
            {
                LogUtil.LogError(ex, responseMessage);
                throw;
            }
        }

        /// <summary>
        /// Bước 9: Đi tới trang tra cứu giấy nộp tiền (corpQueryTaxProc, dse_pageId=4)
        /// POST /etaxnnt/Request trên thuedientu.gdt.gov.vn
        /// </summary>
        private async Task Step9_GotoTraCuuGiayNopTienPage()
        {
            HttpResponseMessage responseMessage = null;
            try
            {
                LogUtil.LogTrace("PublicServiceCrawler.Step9_GotoTraCuuGiayNopTienPage.Start");

                var formContent = new FormUrlEncodedContent(new[]
                {
                    new KeyValuePair<string, string>("dse_sessionId", _etaxSessionId),
                    new KeyValuePair<string, string>("dse_applicationId", "-1"),
                    new KeyValuePair<string, string>("dse_pageId", "4"),
                    new KeyValuePair<string, string>("dse_operationName", "corpQueryTaxProc"),
                    new KeyValuePair<string, string>("dse_errorPage", "error_page.jsp"),
                    new KeyValuePair<string, string>("dse_processorState", "initial"),
                    new KeyValuePair<string, string>("dse_nextEventName", "start")
                });

                var request = new HttpRequestMessage(HttpMethod.Post, "/etaxnnt/Request")
                {
                    Content = formContent
                };
                request.Headers.Add("Accept", "text/html,application/xhtml+xml,application/xml;q=0.9,*/*;q=0.8");
                request.Headers.Add("Referer", $"{ETAX_URL}/etaxnnt/Request");
                request.Headers.Add("Origin", ETAX_URL);

                responseMessage = await _client.SendAsync(request, _stoppingToken);

                var html = await responseMessage.Content.ReadAsStringAsync();

                var doc = new HtmlAgilityPack.HtmlDocument();
                doc.LoadHtml(html);

                _processorID = doc.DocumentNode
                    .SelectSingleNode("//input[@name='dse_processorId']")
                    ?.GetAttributeValue("value", null);

                // Lấy dse_pageId từ form tra cứu nếu có để truyền chính xác vào Step 10
                var pageIdNode = doc.DocumentNode.SelectSingleNode("//input[@name='dse_pageId']");
                _queryPageId = pageIdNode?.GetAttributeValue("value", null);

                // Cập nhật lại dse_sessionId nếu trang trả về session mới
                var sidNode = doc.DocumentNode.SelectSingleNode("//input[@name='dse_sessionId']");
                var sidVal = sidNode?.GetAttributeValue("value", null);
                if (!string.IsNullOrWhiteSpace(sidVal))
                {
                    _etaxSessionId = sidVal;
                }

                if (String.IsNullOrEmpty(_processorID))
                {
                    throw new NotFoundProcessIdException();
                }

                LogUtil.LogTrace($"PublicServiceCrawler.Step9_GotoTraCuuGiayNopTienPage.End, processorId: {_processorID}, queryPageId: {_queryPageId}");
            }
            catch (Exception ex)
            {
                LogUtil.LogError(ex, responseMessage);
                throw;
            }
        }

        private async Task<HtmlDocument> Step10_ClickButtonTraCuuGiayNopTien(string fromDate, string toDate)
        {
            HtmlDocument doc = new HtmlDocument();
            HttpResponseMessage responseMessage = null;
            try
            {
                LogUtil.LogTrace("Step10_ClickButtonTraCuuGiayNopTien.Start");
                var pageIdToUse = !string.IsNullOrWhiteSpace(_queryPageId) ? _queryPageId : "10";
                var content = new FormUrlEncodedContent(new[]
                {
                    new KeyValuePair<string, string>("dse_sessionId", _etaxSessionId),
                    new KeyValuePair<string, string>("dse_applicationId", "-1"),
                    new KeyValuePair<string, string>("dse_operationName", "corpQueryTaxProc"),
                    new KeyValuePair<string, string>("dse_pageId", pageIdToUse),
                    new KeyValuePair<string, string>("dse_processorState", "viewQueryPage"),
                    new KeyValuePair<string, string>("dse_processorId", _processorID),
                    new KeyValuePair<string, string>("dse_errorPage", "/etax/query_tax_information.jsp"),
                    new KeyValuePair<string, string>("dse_nextEventName", "query"),
                    new KeyValuePair<string, string>("pn", "1"),
                    new KeyValuePair<string, string>("sct", ""),
                    new KeyValuePair<string, string>("ctuId", ""),
                    new KeyValuePair<string, string>("soGnt", ""),
                    new KeyValuePair<string, string>("idBke", ""),
                    new KeyValuePair<string, string>("type_tax", "01"),
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
                    new KeyValuePair<string, string>("hthuc_nop", ""),
                    new KeyValuePair<string, string>("tong_tien_nt_tu", ""),
                    new KeyValuePair<string, string>("tong_tien_nt_den", ""),
                    new KeyValuePair<string, string>("trang_thai", ""),
                });

                var request = new HttpRequestMessage(HttpMethod.Post, "/etaxnnt/Request")
                {
                    Content = content
                };
                request.Headers.Add("Accept", "text/html,application/xhtml+xml,application/xml;q=0.9,*/*;q=0.8");
                request.Headers.Add("Referer", $"{ETAX_URL}/etaxnnt/Request");
                request.Headers.Add("Origin", ETAX_URL);

                responseMessage = await _client.SendAsync(request, _stoppingToken);
                responseMessage.EnsureSuccessStatusCode();
                var html = await responseMessage.Content.ReadAsStringAsync();
                doc.LoadHtml(html);
                LogUtil.LogTrace("Step10_ClickButtonTraCuuGiayNopTien.End");
            }
            catch (Exception ex)
            {
                LogUtil.LogError(ex, responseMessage);
                throw ex;
            }
            return doc;
        }

        #endregion
    }
}
