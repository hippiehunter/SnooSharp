using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace SnooSharp
{
    public interface INetworkLayer : IDisposable
    {
        Task<string> Get(string url, CancellationToken token, IProgress<float> progress, Dictionary<string, string> body = null);
        Task Send(string url, string method, CancellationToken token, Dictionary<string, string> arguments);
        Task<string> Post(string url, CancellationToken token, Dictionary<string, string> arguments, IProgress<float> progress);
        Task<RedditOAuth> RequestGrantCode(string code, CancellationToken token);
        Task DestroyToken(string refreshToken);
        string RedditBaseUrl { get; }
        INetworkLayer Clone(RedditOAuth credential);
    }

    class NetworkLayer : INetworkLayer
    {
        HttpClient _httpClient;
        CookieContainer _cookieContainer;
        UserState _userState;
        private int _failedRequestCount = 0;
        static DateTime _priorRequestSet = new DateTime();
        static int _requestSetCount = 0;
        static DateTime _lastRequestMade = new DateTime();
        string _appId;
        string _appSecret;
        string _redirectUrl;

        public NetworkLayer(UserState userState, string appId, string appSecret, string redirectUrl)
        {
            _userState = userState;
            _appId = appId;
            _appSecret = appSecret;
            _redirectUrl = redirectUrl;

            _cookieContainer = new CookieContainer();
            var handler = new HttpClientHandler { CookieContainer = _cookieContainer };
            if (handler.SupportsAutomaticDecompression)
            {
                handler.AutomaticDecompression = DecompressionMethods.GZip |
                                                 DecompressionMethods.Deflate;
            }
            _httpClient = new HttpClient(handler);
            _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("SnooSharp/1.0");
        }

        public async Task<string> Get(string url, CancellationToken token, IProgress<float> progress, Dictionary<string, string> body)
        {
            await ThrottleRequests(token);
            await EnsureRedditCookie(token);

            HttpRequestMessage sendMessage = new HttpRequestMessage(HttpMethod.Get, RedditBaseUrl + url);

            if (body != null)
            {
                sendMessage.Content = new FormUrlEncodedContent(body);
            }

            var responseMessage = await _httpClient.SendAsync(sendMessage, HttpCompletionOption.ResponseContentRead, token);
            var bodyString = ProcessJsonErrors(await responseMessage.Content.ReadAsStringAsync());
            if (bodyString.StartsWith("<!doctype html><html><title>") && bodyString.EndsWith("try again and hopefully we will be fast enough this time."))
                return await Get(url, token, progress, body);
            else if (responseMessage.IsSuccessStatusCode)
            {
                if (string.IsNullOrWhiteSpace(bodyString) || bodyString == "{}" || bodyString == "\"{}\"")
                    throw new RedditException("body string was empty but no error code was present");
                else
                {
                    _failedRequestCount = 0;
                    return bodyString;
                }
            }
            else
            {
                _failedRequestCount++;
                switch (responseMessage.StatusCode)
                {
                    case HttpStatusCode.GatewayTimeout:
                    case HttpStatusCode.RequestTimeout:
                    case HttpStatusCode.BadGateway:
                    case HttpStatusCode.BadRequest:
                    case HttpStatusCode.InternalServerError:
                    case HttpStatusCode.ServiceUnavailable:
                        {
                            if (_failedRequestCount < 5)
                                return await Get(url, token, progress, body);
                            else
                                break;
                        }
                    case HttpStatusCode.NotFound:
                        //reddit likes to return 404 for no apparent reason
                        if (_failedRequestCount < 2)
                            return await Get(url, token, progress, body);
                        else
                            throw new RedditNotFoundException(url);
                    case HttpStatusCode.Forbidden:
                        throw new RedditException(url + " forbidden");
                }
                responseMessage.EnsureSuccessStatusCode();
                return null;
            }
        }

        public async Task Send(string url, string method, CancellationToken token, Dictionary<string, string> arguments)
        {
            await ThrottleRequests(token);
            await EnsureRedditCookie(token);
            HttpResponseMessage responseMessage = null;
            var request = new HttpRequestMessage(new HttpMethod(method), url);
            request.Content = new FormUrlEncodedContent(arguments);
            responseMessage = await _httpClient.SendAsync(request);
            await ProcessJsonErrors(responseMessage);
        }

        public async Task<string> Post(string url, CancellationToken token, Dictionary<string, string> arguments, IProgress<float> progress)
        {
            await ThrottleRequests(token);
            await EnsureRedditCookie(token);
            HttpResponseMessage responseMessage = null;
            var request = new HttpRequestMessage(HttpMethod.Post, url);
            request.Content = new FormUrlEncodedContent(arguments);
            responseMessage = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseContentRead, token);
            return ProcessJsonErrors(await responseMessage.Content.ReadAsStringAsync());
        }

        public string RedditBaseUrl
        {
            get
            {
                if (_userState != null && _userState.OAuth != null)
                    return "https://oauth.reddit.com";
                else
                    return "http://www.reddit.com";
            }
        }

        private string ProcessJsonErrors(string response)
        {
            string realErrorString = "";
            try
            {
                if (response.Contains("errors"))
                {
                    var jsonErrors = JsonConvert.DeserializeObject<JsonErrorsData>(response);
                    if (jsonErrors.Errors != null && jsonErrors.Errors.Length > 0)
                    {
                        realErrorString = jsonErrors.Errors[0].ToString();
                    }
                }

            }
            catch
            {
            }
            if (!string.IsNullOrWhiteSpace(realErrorString))
                throw new RedditException(realErrorString);

            return response;
        }

        private async Task ProcessJsonErrors(HttpResponseMessage httpResponse)
        {
            var response = await httpResponse.Content.ReadAsStringAsync();
            string realErrorString = "";
            try
            {
                if (response.Contains("errors"))
                {
                    var jsonErrors = JsonConvert.DeserializeObject<JsonErrorsData>(response);
                    if (jsonErrors.Errors != null && jsonErrors.Errors.Length > 0)
                    {
                        realErrorString = jsonErrors.Errors[0].ToString();
                    }
                }
            }
            catch
            {
            }
            if (!string.IsNullOrWhiteSpace(realErrorString))
                throw new RedditException(realErrorString);
        }

        private async Task EnsureRedditCookie(CancellationToken token)
        {
            if (_userState.OAuth != null)
            {
                //see if we need to refresh the token
                if (_userState.OAuth.Created.AddSeconds(_userState.OAuth.ExpiresIn) < DateTime.UtcNow)
                {
                    _userState.OAuth = await RefreshToken(_userState.OAuth.RefreshToken, token);
                }

                _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _userState.OAuth.AccessToken);
            }
            else
            {
                _httpClient.DefaultRequestHeaders.Authorization = null;
            }
        }

        public async Task<RedditOAuth> RequestGrantCode(string code, CancellationToken token)
        {
            //we're messing with the headers here so use a different client
            var httpClient = new HttpClient();
            httpClient.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Basic", Convert.ToBase64String(System.Text.UTF8Encoding.UTF8.GetBytes(string.Format("{0}:{1}", _appId, _appSecret))));
            var result = await httpClient.PostAsync(new Uri("https://ssl.reddit.com/api/v1/access_token"), new FormUrlEncodedContent(new Dictionary<string, string>
                {
                    {"grant_type", "authorization_code"},
                    {"code", code},
                    {"redirect_uri", _redirectUrl}, //this is basically just a magic string that needs to match with reddit's app registry
				}), token);
            var jsonResult = await result.Content.ReadAsStringAsync();
            var oAuth = JsonConvert.DeserializeObject<RedditOAuth>(jsonResult);
            oAuth.Created = DateTime.UtcNow;
            return oAuth;
        }

        public async Task DestroyToken(string refreshToken)
        {
            //we're messing with the headers here so use a different client
            var httpClient = new HttpClient();
            httpClient.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Basic", Convert.ToBase64String(System.Text.UTF8Encoding.UTF8.GetBytes(string.Format("{0}:{1}", _appId, _appSecret))));
            var result = await httpClient.PostAsync(new Uri("https://ssl.reddit.com/api/v1/revoke_token"), new FormUrlEncodedContent(new Dictionary<string, string>
                {
                    {"token", refreshToken},
                    {"token_type_hint", "refresh_token"},
                }));
        }

        public async Task<RedditOAuth> RefreshToken(string refreshToken, CancellationToken token)
        {
            //we're messing with the headers here so use a different client
            var httpClient = new HttpClient();
            httpClient.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Basic", Convert.ToBase64String(System.Text.UTF8Encoding.UTF8.GetBytes(string.Format("{0}:{1}", _appId, _appSecret))));
            var result = await httpClient.PostAsync(new Uri("https://ssl.reddit.com/api/v1/access_token"), new FormUrlEncodedContent(new Dictionary<string, string>
                {
                    {"grant_type", "refresh_token"},
                    {"refresh_token", refreshToken},
                }), token);
            var jsonResult = await result.Content.ReadAsStringAsync();
            var oAuth = JsonConvert.DeserializeObject<RedditOAuth>(jsonResult);
            oAuth.Created = DateTime.UtcNow;
            oAuth.RefreshToken = refreshToken; //this is to make life a bit easier, since you would need to keep track of this thing anyway
            return oAuth;
        }

        //dont hammer reddit!
        //Make no more than thirty requests per minute. This allows some burstiness to your requests, 
        //but keep it sane. On average, we should see no more than one request every two seconds from you.
        //the above statement is from the reddit api docs, but its not quite true, there are some api's that have logging 
        //set for 15 requests in 30 seconds, so we can allow some burstiness but it must fit in the 15 requests/30 seconds rule
        public static async Task ThrottleRequests(CancellationToken token)
        {
            var offset = DateTime.Now - _lastRequestMade;
            if (offset.TotalMilliseconds < 1000)
            {
                await Task.Delay(1000 - (int)offset.TotalMilliseconds);
            }

            if (_requestSetCount > 15)
            {
                var overallOffset = DateTime.Now - _priorRequestSet;

                if (overallOffset.TotalSeconds < 30)
                {
                    var delay = (30 - (int)overallOffset.TotalSeconds) * 1000;
                    if (delay > 2)
                    {
                        for (int i = 0; i < delay; i++)
                        {
                            await Task.Delay(1000);
                        }
                    }
                    else
                        await Task.Delay(delay);
                }
                _requestSetCount = 0;
                _priorRequestSet = DateTime.Now;
            }
            _requestSetCount++;

            _lastRequestMade = DateTime.Now;
        }

        public INetworkLayer Clone(RedditOAuth credential)
        {
            return new NetworkLayer(new UserState { OAuth = credential }, _appId, _appSecret, _redirectUrl);
        }

        public void Dispose()
        {
            if(_httpClient != null)
                _httpClient.Dispose();

            _httpClient = null;
        }
    }
}
