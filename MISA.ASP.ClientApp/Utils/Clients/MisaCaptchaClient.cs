using MISA.ASP.ClientApp.Models;
using MISA.ASP.ClientApp.Models.Enums;
using MISA.ASP.ClientApp.Utils.Logging;
using Newtonsoft.Json;
using System;
using System.Net.Http;
using System.Threading.Tasks;

namespace MISA.ASP.ClientApp.Utils.Clients
{
    public class MisaCaptchaClient : IDisposable
    {
    
        private HttpClient _client { get; set; }

        public MisaCaptchaClient()
        {
            _client = new HttpClient() { BaseAddress = new Uri(CommonConst.ASP_API_URL) };
        }

        public async Task<string> Decaptcha(CaptchaRequest request, string domainName = "")
        {
            var captcha = String.Empty;

            try
            {
                using (var multipartFormContent = new MultipartFormDataContent())
                {
                    if (!string.IsNullOrEmpty(request.Base64Image))
                    {
                        multipartFormContent.Add(
                            new StringContent(request.Base64Image),
                            "image"
                        );
                    }
                    else if (request.Stream != null)
                    {
                        var fileStreamContent = new StreamContent(request.Stream);

                        multipartFormContent.Add(
                            fileStreamContent,
                            name: "image",
                            fileName: $"image_{DateTime.Now.Ticks}.jpg"
                        );
                    }

                    var urldecode =
                        $"api/ClientToolAction/CaptchaDecode?domainName={domainName}&source={request.Source}";

                    var message = await _client.PostAsync(
                        urldecode,
                        multipartFormContent
                    );

                    message.EnsureSuccessStatusCode();

                    var content = await message.Content.ReadAsStringAsync();

                    var apiResult = JsonConvert.DeserializeObject<ApiResult>(content);

                    if (apiResult.Code == 200 && apiResult.Data != null)
                    {
                        captcha = apiResult.Data.ToString();
                    }
                }
            }
            catch (Exception ex)
            {
                LogUtil.LogError(ex);
                throw ex;
            }

            return captcha;
        }
        public void Dispose()
        {
            _client?.Dispose();
        }
    }
}
