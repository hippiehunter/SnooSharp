using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading.Tasks;

namespace SnooSharp
{
    public class Reddit
    {
        Dictionary<string, string> _linkToOpMap = new Dictionary<string, string>();
        IListingFilter _listingFilter;
        UserState _userState; 
        IActionDeferralSink _deferalSink;
        ICaptchaProvider _captchaProvider;
        HttpClient _httpClient;
        CookieContainer _cookieContainer;
		string _appId;
		string _appSecret;
		string _redirectUrl;
		public Reddit(IListingFilter listingFilter, UserState userState, IActionDeferralSink deferalSink, ICaptchaProvider captchaProvider, string appId = null, string appSecret = null, string redirectUrl = null)
        {
            _listingFilter = listingFilter;
            _userState = userState;
            _deferalSink = deferalSink;
            _captchaProvider = captchaProvider;
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
			_httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("SnooStream/1.0");
        }

		private string RedditBaseUrl
		{
			get
			{
				if (_userState != null && _userState.OAuth != null)
					return "https://oauth.reddit.com";
				else
					return "http://www.reddit.com";
			}
		}

		public async Task<RedditOAuth> RequestGrantCode(string code)
		{
			//we're messing with the headers here so use a different client
			var httpClient = new HttpClient();
			httpClient.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Basic", Convert.ToBase64String(System.Text.UTF8Encoding.UTF8.GetBytes(string.Format("{0}:{1}", _appId, _appSecret))));
			var result = await httpClient.PostAsync(new Uri("https://ssl.reddit.com/api/v1/access_token"), new FormUrlEncodedContent(new Dictionary<string, string>
				{
					{"grant_type", "authorization_code"},
					{"code", code},
					{"redirect_uri", _redirectUrl}, //this is basically just a magic string that needs to match with reddit's app registry
				}));
			var jsonResult = await result.Content.ReadAsStringAsync();
			var oAuth = JsonConvert.DeserializeObject<RedditOAuth>(jsonResult);
			oAuth.Created = DateTime.UtcNow;
			return oAuth;
		}

		public async Task<RedditOAuth> RefreshToken(string refreshToken)
		{
			//we're messing with the headers here so use a different client
			var httpClient = new HttpClient();
			httpClient.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Basic", Convert.ToBase64String(System.Text.UTF8Encoding.UTF8.GetBytes(string.Format("{0}:{1}", _appId, _appSecret))));
			var result = await httpClient.PostAsync(new Uri("https://ssl.reddit.com/api/v1/access_token"), new FormUrlEncodedContent(new Dictionary<string, string>
				{
					{"grant_type", "refresh_token"},
					{"refresh_token", refreshToken},
				}));
			var jsonResult = await result.Content.ReadAsStringAsync();
			var oAuth = JsonConvert.DeserializeObject<RedditOAuth>(jsonResult);
			oAuth.Created = DateTime.UtcNow;
			oAuth.RefreshToken = refreshToken; //this is to make life a bit easier, since you would need to keep track of this thing anyway
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

        public string CurrentUserName
        {
            get
            {
                return _userState.Username;
            }
        }

        public async Task ProcessDeferralSink()
        {
            var deferral = _deferalSink.DequeDeferral();
            if (deferral != null)
            {
                await BasicPost(deferral.Item1, deferral.Item2);
            }
        }

        private async Task EnsureRedditCookie()
        {
            if (!string.IsNullOrWhiteSpace(_userState.LoginCookie))
            {
				var redditUri = new Uri(RedditBaseUrl);
                _cookieContainer.Add(redditUri, new Cookie("reddit_session", _userState.LoginCookie));
            }
			else if (_userState.OAuth != null)
			{
				//see if we need to refresh the token
				if (_userState.OAuth.Created.AddSeconds(_userState.OAuth.ExpiresIn) < DateTime.UtcNow)
				{
					_userState.OAuth = await RefreshToken(_userState.OAuth.RefreshToken);
				}

				_httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _userState.OAuth.AccessToken);
			}
        }

        public async Task<Account> GetMe()
        {
            return await GetMe(_userState.LoginCookie);
        }

        //this one is seperated out so we can use it interally on initial user login
        public async Task<Account> GetMe(string loginCookie)
        {
            await ThrottleRequests();
            await EnsureRedditCookie();
            var meString = await _httpClient.GetStringAsync(RedditBaseUrl + "/api/me.json");
            if (!string.IsNullOrWhiteSpace(meString) && meString != "{}")
            {
                var thing = JsonConvert.DeserializeObject<Thing>(meString);
                return (new TypedThing<Account>(thing)).Data;
            }
            else
                return null;
        }

		//this one is seperated out so we can use it interally on initial user login
		public async Task<Account> GetIdentity()
		{
			await ThrottleRequests();
			await EnsureRedditCookie();
			var meString = await _httpClient.GetStringAsync(RedditBaseUrl + "/api/v1/me");
			if (!string.IsNullOrWhiteSpace(meString) && meString != "{}")
			{
				var thing = JsonConvert.DeserializeObject<Account>(meString);
				return thing;
			}
			else
				return null;
		}

        public async Task<User> Login(string username, string password)
        {
            var loginUri = "https://ssl.reddit.com/api/login";
            var content = new FormUrlEncodedContent(new[] 
            {
                new KeyValuePair<string, string>("api_type", "json"),
                new KeyValuePair<string, string>("user", username),
                new KeyValuePair<string, string>("passwd", password)
            });

            await ThrottleRequests();
            var loginResult = await _httpClient.PostAsync(loginUri, content);


            if (loginResult.IsSuccessStatusCode)
            {
                var jsonResult = await loginResult.Content.ReadAsStringAsync();
                var loginResultThing = JsonConvert.DeserializeObject<LoginJsonThing>(jsonResult);
                if (loginResultThing == null || loginResultThing.Json == null ||
                    (loginResultThing.Json.Errors != null && loginResultThing.Json.Errors.Length != 0))
                {
                    throw new RedditException(string.Format("failed to login as {0}", username));
                }
                else
                {
                    var user = new User { Authenticated = true, LoginCookie = Uri.EscapeDataString(loginResultThing.Json.Data.Cookie), Username = username, NeedsCaptcha = false };
                    user.Me = await GetMe(loginResultThing.Json.Data.Cookie);
                    return user;
                }
            }
            else
                throw new RedditException(loginResult.StatusCode.ToString());
        }

        public async Task<Tuple<string, Listing>> Search(string query, int? limit, bool reddits, string restrictedToSubreddit)
        {
            var maxLimit = _userState.IsGold ? 1500 : 100;
            var guardedLimit = Math.Min(maxLimit, limit ?? maxLimit);

            string targetUri = null;
			string afterUri = null;
            if (reddits)
            {
                targetUri = string.Format("{2}/subreddits/search.json?limit={0}&q={1}", guardedLimit, query, RedditBaseUrl);
				afterUri = string.Format("/subreddits/search.json?q={0}", query);
            }
            else if (string.IsNullOrWhiteSpace(restrictedToSubreddit))
            {
                targetUri = string.Format("{2}/search.json?limit={0}&q={1}", guardedLimit, query, RedditBaseUrl);
				afterUri = string.Format("/search.json&q={0}", query);
            }
            else
            {
                targetUri = string.Format("{3}/r/{2}/search.json?limit={0}&q={1}&restrict_sr=on", guardedLimit, query, restrictedToSubreddit, RedditBaseUrl);
				afterUri = string.Format("/subreddits/r/{1}/search.json?q={0}&restrict_sr=on", query, restrictedToSubreddit);
            }
            await ThrottleRequests();
            await EnsureRedditCookie();
            var listing = await _httpClient.GetStringAsync(targetUri);
            var newListing = JsonConvert.DeserializeObject<Listing>(listing);
            return Tuple.Create(afterUri, await _listingFilter.Filter(newListing));
        }

        public async Task<Thing> GetThingById(string id)
        {
            var targetUri = string.Format("{1}/by_id/{0}.json", id, RedditBaseUrl);
            await ThrottleRequests();
            await EnsureRedditCookie();
            var thingStr = await _httpClient.GetStringAsync(targetUri);
            if(thingStr.StartsWith("{\"kind\": \"Listing\""))
            {
                var listing = JsonConvert.DeserializeObject<Listing>(thingStr);
                return listing.Data.Children.First();
            }
            else
                return JsonConvert.DeserializeObject<Thing>(thingStr);
        }

        public virtual async Task<Listing> GetSubreddits(int? limit)
        {
            var maxLimit = _userState.IsGold ? 1500 : 100;
            var guardedLimit = Math.Min(maxLimit, limit ?? maxLimit);

            var targetUri = string.Format("{1}/reddits/.json?limit={0}", guardedLimit, RedditBaseUrl);
            await ThrottleRequests();
			await EnsureRedditCookie();
            var subreddits = await _httpClient.GetStringAsync(targetUri);
            var newListing = JsonConvert.DeserializeObject<Listing>(subreddits);

            return await _listingFilter.Filter(newListing);
        }

        public async Task<TypedThing<Subreddit>> GetSubreddit(string name)
        {
            //no info for the front page
            if (name == "/")
                return new TypedThing<Subreddit>(new Thing { Kind = "t5", Data = new Subreddit { Headertitle = name } });
            else if (name == "all")
                return new TypedThing<Subreddit>(new Thing { Kind = "t5", Data = new Subreddit { Headertitle = "all", Url = "/r/all", Name = "all", DisplayName="all", Title="all", Id="t5_fakeid" } });

            string targetUri;
            if (!name.Contains("/m/"))
            {
                targetUri = string.Format("{1}/r/{0}/about.json", name, RedditBaseUrl);
                await ThrottleRequests();
				await EnsureRedditCookie();
                var subreddit = await _httpClient.GetStringAsync(targetUri);
                //error page
                if (subreddit.ToLower().StartsWith("<!doctype html>"))
                {
                    return new TypedThing<Subreddit>(new Thing { Kind = "t5", Data = new Subreddit { Headertitle = name, Title = name, Url = string.Format("r/{0}", name), Created = DateTime.Now, CreatedUTC = DateTime.UtcNow, DisplayName = name, Description = "there doesnt seem to be anything here", Name = name, Over18 = false, PublicDescription = "there doesnt seem to be anything here", Subscribers = 0 } });
                }
                else
                {
                    return new TypedThing<Subreddit>(JsonConvert.DeserializeObject<Thing>(subreddit));
                }
            }
            else
            {
                if (name.StartsWith("/"))
                    name = name.TrimStart('/');

                if (name.StartsWith("me/"))
                {
                   name = name.Replace("me/", "user/" + _userState.Username + "/");
                }

                targetUri = string.Format("{1}/api/multi/{0}.json", name, RedditBaseUrl);
                await ThrottleRequests();
				await EnsureRedditCookie();
                var subreddit = await _httpClient.GetStringAsync(targetUri);
                //error page
                if (subreddit.ToLower().StartsWith("<!doctype html>"))
                {
                    throw new RedditNotFoundException(name);
                }
                else
                {
                    var labeledMulti = new TypedThing<LabeledMulti>(JsonConvert.DeserializeObject<Thing>(subreddit));
                    var multiPath = labeledMulti.Data.Path;

                    if (!string.IsNullOrWhiteSpace(_userState.Username))
                        multiPath = multiPath.Replace("/user/" + _userState.Username, "/me");

                    return new TypedThing<Subreddit>(new Thing { Kind = "t5", Data = new Subreddit { DisplayName = labeledMulti.Data.Name, Title = labeledMulti.Data.Name, Url = multiPath, Headertitle = labeledMulti.Data.Name, Over18 = false } });
                }
            }
        }

        public static readonly string PostByUserBaseFormat = "/user/{0}/";

        public async Task<Listing> GetPostsByUser(string username, int? limit)
        {
            if (string.IsNullOrWhiteSpace(username))
                throw new ArgumentNullException("username");

            var maxLimit = _userState.IsGold ? 1500 : 100;
            var guardedLimit = Math.Min(maxLimit, limit ?? maxLimit);

            var targetUri = string.Format("{2}/user/{0}/.json?limit={1}", username, guardedLimit, RedditBaseUrl);
            await ThrottleRequests();
			await EnsureRedditCookie();
            var listing = await _httpClient.GetStringAsync(targetUri);
            var newListing = JsonConvert.DeserializeObject<Listing>(listing);

            return await _listingFilter.Filter(newListing);
        }

        public async Task<Listing> GetPostsBySubreddit(string subreddit, string sort = "hot", int? limit = null)
        {
            var maxLimit = _userState.IsGold ? 1500 : 100;
            var guardedLimit = Math.Min(maxLimit, limit ?? maxLimit);

            if (subreddit == null)
            {
                throw new RedditNotFoundException("(null)");
            }

			var targetUri = string.Format("{3}{0}{2}.json?limit={1}", subreddit, guardedLimit, subreddit.EndsWith("/") ? sort : "/" + sort, RedditBaseUrl);

            await ThrottleRequests();
			await EnsureRedditCookie();
            var listing = await _httpClient.GetStringAsync(targetUri);
            var newListing = JsonConvert.DeserializeObject<Listing>(listing);
            return await _listingFilter.Filter(newListing);
        }
		private static int getMoreCount = 0;
        public async Task<Listing> GetMoreOnListing(More more, string contentId, string subreddit)
        {
			var targetUri = RedditBaseUrl + "/api/morechildren.json";

            if (more.Children.Count == 0)
                return new Listing
                {
                    Kind = "Listing",
                    Data = new ListingData()
                };

			var viableChildren = more.Children.Take(20);
			var leftovers = more.Children.Skip(20);

            var arguments = new Dictionary<string, string>
            {
                {"children", string.Join(",", viableChildren) },
                {"link_id", contentId.Contains("_") ? contentId : "t3_" + contentId },
                {"pv_hex", ""},
                {"api_type", "json" }
            };

            if (subreddit != null)
            {
                arguments.Add("r", subreddit);
            }
			getMoreCount++;
            await ThrottleRequests();
			await EnsureRedditCookie();
            var result = await _httpClient.PostAsync(targetUri, new FormUrlEncodedContent(arguments));
            var resultString = await result.Content.ReadAsStringAsync();
            var newListing = new Listing
            {
                Kind = "Listing",
                Data = new ListingData { Children = JsonConvert.DeserializeObject<JsonThing>(resultString).Json.Data.Things }
            };

			if (leftovers.Count() > 0)
			{
				newListing.Data.Children.Add(new Thing { Kind = "more", Data = new More { Children = new List<string>(leftovers), ParentId = more.ParentId, Count = (more.Count - viableChildren.Count()) } });
			}
			if(getMoreCount != 0)

            return newListing;
			else
				return newListing;
        }

        public async Task<Thing> GetLinkByUrl(string url)
        {
            var originalUrl = url;
            if (originalUrl.Contains(".json"))
            {
            }
            else if (originalUrl.Contains("?"))
            {
                var queryPos = url.IndexOf("?");
                url = string.Format("{0}.json{1}", originalUrl.Remove(queryPos), originalUrl.Substring(queryPos));
            }
            else 
            {
                url = originalUrl + ".json";
            }

            Listing listing = null;
            await ThrottleRequests();
			await EnsureRedditCookie();
            var json = await _httpClient.GetStringAsync(url);
            if (json.StartsWith("["))
            {
                var listings = JsonConvert.DeserializeObject<Listing[]>(json);
                listing = new Listing { Data = new ListingData { Children = new List<Thing>() } };
                foreach (var combinableListing in listings)
                {
                    listing.Data.Children.AddRange(combinableListing.Data.Children);
                    listing.Kind = combinableListing.Kind;
                    listing.Data.After = combinableListing.Data.After;
                    listing.Data.Before = combinableListing.Data.Before;
                }
            }
            else
                listing = JsonConvert.DeserializeObject<Listing>(json);

            var requestedLinkInfo = listing.Data.Children.FirstOrDefault(thing => thing.Data is Link);
            if (requestedLinkInfo != null)
            {
                return requestedLinkInfo;
            }
            else
                return null;
        }

        public async Task<Listing> GetCommentsOnPost(string subreddit, string permalink, int? limit)
        {
            var maxLimit = _userState.IsGold ? 1500 : 500;
            var guardedLimit = Math.Min(maxLimit, limit ?? maxLimit);

            string targetUri = null;

            if (permalink.Contains("reddit.com"))
            {
                permalink = permalink.Substring(permalink.IndexOf("reddit.com") + "reddit.com".Length);
            }

            if (permalink.Contains(".json?"))
            {
                targetUri = "http://www.reddit.com" + permalink;
            }
            else if (permalink.Contains("?"))
            {
                var queryPos = permalink.IndexOf("?");
                targetUri = string.Format("{2}{0}.json{1}", permalink.Remove(queryPos), permalink.Substring(queryPos), RedditBaseUrl);
            }
            else
            {
                targetUri = limit == -1 ?
                            string.Format("{1}{0}.json", permalink, RedditBaseUrl) :
                            string.Format("{2}{0}.json?limit={1}", permalink, limit, RedditBaseUrl);
            }

            Listing listing = null;
            await ThrottleRequests();
			await EnsureRedditCookie();
            var comments = await _httpClient.GetStringAsync(targetUri);
            if (comments.StartsWith("["))
            {
                var listings = JsonConvert.DeserializeObject<Listing[]>(comments);
                listing = new Listing { Data = new ListingData { Children = new List<Thing>() } };
                foreach (var combinableListing in listings)
                {
                    listing.Data.Children.AddRange(combinableListing.Data.Children);
                    listing.Kind = combinableListing.Kind;
                    listing.Data.After = combinableListing.Data.After;
                    listing.Data.Before = combinableListing.Data.Before;
                }
            }
            else
                listing = JsonConvert.DeserializeObject<Listing>(comments);

            var requestedLinkInfo = listing.Data.Children.FirstOrDefault(thing => thing.Data is Link);
            if (requestedLinkInfo != null)
            {
                if (!_linkToOpMap.ContainsKey(((Link)requestedLinkInfo.Data).Name))
                {
                    _linkToOpMap.Add(((Link)requestedLinkInfo.Data).Name, ((Link)requestedLinkInfo.Data).Author);
                }
            }
            return listing;
        }

        public Task<Listing> GetMessages(int? limit)
        {
            return GetMail("inbox", limit);
        }

        public void AddFlairInfo(string linkId, string opName)
        {
            if (!_linkToOpMap.ContainsKey(linkId))
            {
                _linkToOpMap.Add(linkId, opName);
            }
        }

        public async Task<Listing> GetAdditionalFromListing(string baseUrl, string after, int? limit = null)
        {
            var maxLimit = _userState.IsGold ? 1500 : 500;
            var guardedLimit = Math.Min(maxLimit, limit ?? maxLimit);

            string targetUri = null;
            //if this base url already has arguments (like search) just append the count and the after
            if (baseUrl.Contains(".json?"))
                targetUri = string.Format("{3}{0}&limit={1}&after={2}", baseUrl, guardedLimit, after, RedditBaseUrl);
            else
				targetUri = string.Format("{3}{0}.json?limit={1}&after={2}", baseUrl, guardedLimit, after, RedditBaseUrl);

            await ThrottleRequests();
			await EnsureRedditCookie();
            var listing = await _httpClient.GetStringAsync(targetUri);
            var newListing = JsonConvert.DeserializeObject<Listing>(listing);

            return await _listingFilter.Filter(newListing);

        }

        public async Task<TypedThing<Account>> GetAccountInfo(string accountName)
        {
            var targetUri = string.Format("{1}/user/{0}/about.json", accountName, RedditBaseUrl);

            await ThrottleRequests();
			await EnsureRedditCookie();
            var account = await _httpClient.GetStringAsync(targetUri);
            return new TypedThing<Account>(JsonConvert.DeserializeObject<Thing>(account));

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

        public virtual async Task AddVote(string thingId, int direction)
        {
            var arguments = new Dictionary<string, string>
            {
                {"id", thingId},
                {"dir", direction.ToString()},
                {"uh", _userState.ModHash}
            };
			await BasicPost(arguments, RedditBaseUrl + "/api/vote");
        }

        public virtual async Task AddSubredditSubscription(string subreddit, bool unsub)
        {
            var content = new Dictionary<string, string>
            {
                { "sr", subreddit},
                { "uh", _userState.ModHash},
                { "action", unsub ? "unsub" : "sub"}
            };
			await BasicPost(content, RedditBaseUrl + "/api/subscribe");
        }

        public virtual Task AddSavedThing(string thingId)
        {
            return ThingAction("save", thingId);
        }

        public virtual Task AddReportOnThing(string thingId)
        {
            return ThingAction("report", thingId);
        }

        public virtual async Task AddPost(string kind, string url, string text, string subreddit, string title)
        {
            var arguments = new Dictionary<string, string>
            {
                {"api_type", "json"},
                {"kind", kind},
                {"extension", "json"},
                {"url", url},
                {"text", text},
                {"title", title},
                {"sr", subreddit},
                {"renderstyle", "html" },
                {"uh", _userState.ModHash}
            };

			await PostCaptchable(arguments, RedditBaseUrl + "/api/submit");
        }

        public virtual async Task EditPost(string text, string name)
        {
            var arguments = new Dictionary<string, string>
            {
                {"api_type", "json"},
                {"text", text},
                {"thing_id", name},
                {"uh", _userState.ModHash}
            };

			await BasicPost(arguments, RedditBaseUrl + "/api/editusertext");
        }

        private string CaptchaIden { get; set; }
        private string Captcha { get; set; }
        private async Task PostCaptchable(Dictionary<string, string> urlEncodedData, string uri)
        {
            if (!urlEncodedData.ContainsKey("api_type"))
                urlEncodedData.Add("api_type", "json");

            if (!String.IsNullOrEmpty(CaptchaIden))
            {
                if (urlEncodedData.ContainsKey("iden"))
                    urlEncodedData["iden"] = CaptchaIden;
                else
                    urlEncodedData.Add("iden", CaptchaIden);
            }

            if (!String.IsNullOrEmpty(Captcha))
            {
                if (urlEncodedData.ContainsKey("captcha"))
                    urlEncodedData["captcha"] = Captcha;
                else
                    urlEncodedData.Add("captcha", Captcha);
            }


			await EnsureRedditCookie();
            HttpResponseMessage response = null;

            do
            {
                try
                {
                    await ThrottleRequests();
                    response = await _httpClient.PostAsync(uri, new FormUrlEncodedContent(urlEncodedData));
                }
                catch(WebException ex)
                {
                    //connection problems during a post, so put it into the deferalSink
                    if (ex.Status == WebExceptionStatus.ConnectFailure || ex.Status == WebExceptionStatus.SendFailure)
                    {
                        _deferalSink.Defer(urlEncodedData, uri);
                        return;
                    }
                    else
                        throw;
                
                }
            }while(response != null && await HandleCaptchaError(response));
        }

        private async Task<bool> HandleCaptchaError(HttpResponseMessage response)
        {
            var json = await response.Content.ReadAsStringAsync();
            var jsonObject = JsonConvert.DeserializeObject(json) as JObject;
            JToken captcha = null;
            JToken errors = null;
            JObject first = null;

            if (jsonObject.First != null)
                first = (jsonObject.First as JProperty).Value as JObject;

            if (first != null)
            {
                first.TryGetValue("captcha", out captcha);
                first.TryGetValue("errors", out errors);
                if (captcha != null)
                    CaptchaIden = captcha.Value<string>();

                if (captcha != null && errors != null)
                {
                    _userState.NeedsCaptcha = true;
                    Captcha = await _captchaProvider.GetCaptchaResponse(CaptchaIden);
                    if (Captcha == null)
                    {
                        throw new RedditException("captcha failed");
                    }
                    else
                        return true;
                }
                else
                    return false;
            }
            return false;
        }

        private async Task BasicPost(Dictionary<string, string> arguments, string url)
        {
            await ThrottleRequests();
            HttpResponseMessage responseMessage = null;
            try
            {
                responseMessage = await _httpClient.PostAsync(url, new FormUrlEncodedContent(arguments));
            }
            catch(WebException ex)
            {
                //connection problems during a post, so put it into the deferalSink
                if (ex.Status == WebExceptionStatus.ConnectFailure || ex.Status == WebExceptionStatus.SendFailure)
                {
                    _deferalSink.Defer(arguments, url);
                    return;
                }
                else
                    throw;
                
            }
            await ProcessJsonErrors(responseMessage);
        }

        public virtual async Task ReadMessage(string id)
        {
            var arguments = new Dictionary<string, string>
            {
                {"id", id},
                {"uh", _userState.ModHash}
            };

			await BasicPost(arguments, RedditBaseUrl + "/api/read_message");
        }

        public virtual async Task AddMessage(string recipient, string subject, string message)
        {
            var arguments = new Dictionary<string, string>
            {
                {"id", "#compose-message"},
                {"to", recipient},
                {"text", message},
                {"subject", subject},
                {"thing-id", ""},
                {"renderstyle", "html"},
                {"uh", _userState.ModHash}
            };

			await PostCaptchable(arguments, RedditBaseUrl + "/api/compose");
        }

        public virtual async Task AddReply(string recipient, string subject, string message, string thing_id)
        {
            var arguments = new Dictionary<string, string>
            {
                {"id", "#compose-message"},
                {"to", recipient},
                {"text", message},
                {"subject", subject},
                {"thing-id", ""},
                {"renderstyle", "html"},
                {"uh", _userState.ModHash}
            };

			await PostCaptchable(arguments, RedditBaseUrl + "/api/compose");
        }

        public virtual async Task AddComment(string parentId, string content)
        {
            var arguments = new Dictionary<string, string>
            {
                {"thing_id", parentId},
                {"text", content.Replace("\r\n", "\n")},
                {"uh", _userState.ModHash}
            };

			await PostCaptchable(arguments, RedditBaseUrl + "/api/comment");
        }

        public virtual async Task EditComment(string thingId, string text)
        {
            var arguments = new Dictionary<string, string>
            {
                {"thing_id", thingId},
                {"text", text.Replace("\r\n", "\n")},
                {"uh", _userState.ModHash}
            };

			await BasicPost(arguments, RedditBaseUrl + "/api/editusertext");
        }

        public AuthorFlairKind GetUsernameModifiers(string username, string linkid, string subreddit)
        {
            if (!string.IsNullOrEmpty(linkid))
            {
                string opName;
                if (_linkToOpMap.TryGetValue(linkid, out opName) && opName == username)
                {
                    return AuthorFlairKind.OriginalPoster;
                }
            }

            return AuthorFlairKind.None;
        }

        private async Task<Listing> GetUserMultis(Listing listing)
        {
            var targetUri = string.Format("{0}/api/multi/mine.json", RedditBaseUrl);
            await ThrottleRequests();
			await EnsureRedditCookie();
            var subreddits = await _httpClient.GetStringAsync(targetUri);
            if (subreddits == "[]")
                return listing;
            else
            {
                var userMultis = JsonConvert.DeserializeObject<Thing[]>(subreddits);
                foreach (var thing in userMultis)
                {
                    var labeledMulti = new TypedThing<LabeledMulti>(thing);
                    var multiPath = labeledMulti.Data.Path;

                    multiPath = multiPath.Replace("/user/" + _userState.Username, "/me");

                    listing.Data.Children.Insert(0, (new Thing { Kind = "t5", Data = new Subreddit { DisplayName = labeledMulti.Data.Name, HeaderImage = "/Assets/multireddit.png", Title = labeledMulti.Data.Name, Url = multiPath, Headertitle = labeledMulti.Data.Name, Over18 = false } }));
                }
            }
            
            return listing;
        }

        public async Task<Listing> GetSubscribedSubredditListing()
        {
            var maxLimit = _userState.IsGold ? 1500 : 100;

            var targetUri = string.Format("{1}/reddits/mine.json?limit={0}", maxLimit, RedditBaseUrl);
            await ThrottleRequests();
			await EnsureRedditCookie();
            var subreddits = await _httpClient.GetStringAsync(targetUri);

            if (subreddits == "\"{}\"")
                return await GetDefaultSubreddits();
            else
                return JsonConvert.DeserializeObject<Listing>(subreddits);
        }

        public async Task<Listing> GetDefaultSubreddits()
        {
			var maxLimit = 25;

			var targetUri = string.Format("{1}/subreddits/popular.json?limit={0}", maxLimit, RedditBaseUrl);
			await ThrottleRequests();
			await EnsureRedditCookie();
			var subreddits = await _httpClient.GetStringAsync(targetUri);

			if (subreddits == "\"{}\"")
				return new Listing { Data = new ListingData { Children = new List<Thing> { new Thing { Kind = "t3", Data = new Subreddit { Headertitle = "/", Name = "/" } } } } };
			else
				return JsonConvert.DeserializeObject<Listing>(subreddits);
        }


        public async Task<bool> CheckLogin(string loginToken)
        {
            await ThrottleRequests();
			await EnsureRedditCookie();
			var meString = await _httpClient.GetStringAsync(RedditBaseUrl + "/api/me.json");
            return (!string.IsNullOrWhiteSpace(meString) && meString != "{}");
        }

        public async Task<Listing> GetSaved(int? limit)
        {
            return await GetUserInfoListing("saved", limit);
        }

        public async Task<Listing> GetLiked(int? limit)
        {
            return await GetUserInfoListing("liked", limit);
        }

        private async Task<Listing> GetUserInfoListing(string kind, int? limit)
        {
            var maxLimit = _userState.IsGold ? 1500 : 100;
            var guardedLimit = Math.Min(maxLimit, limit ?? maxLimit);

            var targetUri = string.Format("{3}/user/{0}/{2}/.json?limit={1}", _userState.Username, guardedLimit, kind, RedditBaseUrl);
            await ThrottleRequests();
			await EnsureRedditCookie();
            var info = await _httpClient.GetStringAsync(targetUri);
            var newListing = JsonConvert.DeserializeObject<Listing>(info);

            return await _listingFilter.Filter(newListing);
        }

        public async Task<Listing> GetDisliked(int? limit)
        {
            return await GetUserInfoListing("disliked", limit);
        }

        public Task<Listing> GetSentMessages(int? limit)
        {
            return GetMail("sent", limit);
        }

        public async Task ThingAction(string action, string thingId)
        {
			var targetUri = RedditBaseUrl + "/api/" + action;

            var content = new Dictionary<string, string>
            {
                { "id", thingId},
                { "uh", _userState.ModHash}
            };

            await BasicPost(content, targetUri);
        }

        public Task UnSaveThing(string thingId)
        {
            return ThingAction("unsafe", thingId);
        }

        public async Task MarkVisited(IEnumerable<string> ids)
        {
            if(_userState.IsGold)
            {
                var arguments = new Dictionary<string, string>
                {
                    {"links", string.Join(",", ids)},
                    { "uh", _userState.ModHash}
                };

				await BasicPost(arguments, RedditBaseUrl + "/api/store_visits");
            }
        }


        public async Task<Listing> GetModActions(string subreddit, int? limit)
        {
            var maxLimit = _userState.IsGold ? 1500 : 100;
            var guardedLimit = Math.Min(maxLimit, limit ?? maxLimit);

            var targetUri = string.Format("{2}/r/{0}/about/log.json?limit={1}", subreddit, guardedLimit, RedditBaseUrl);

            await ThrottleRequests();
			await EnsureRedditCookie();
            var messages = await _httpClient.GetStringAsync(targetUri);
            if (messages == "\"{}\"")
            {
                return new Listing { Kind = "Listing", Data = new ListingData { Children = new List<Thing>() } };
            }
            return JsonConvert.DeserializeObject<Listing>(messages);
        }

        private async Task<Listing> GetMail(string kind, int? limit)
        {
            var maxLimit = _userState.IsGold ? 1500 : 100;
            var guardedLimit = Math.Min(maxLimit, limit ?? maxLimit);

			var targetUri = string.Format(RedditBaseUrl + MailBaseUrlFormat + ".json?limit={1}", kind, guardedLimit);

            await ThrottleRequests();
			await EnsureRedditCookie();
            var messages = await _httpClient.GetStringAsync(targetUri);
            if (messages == "\"{}\"")
            {
                return new Listing { Kind = "Listing", Data = new ListingData { Children = new List<Thing>() } };
            }
            // Hacky hack mcHackerson
            messages = messages.Replace("\"kind\": \"t1\"", "\"kind\": \"t4\"");
            return JsonConvert.DeserializeObject<Listing>(messages);
        }

		public static readonly string MailBaseUrlFormat = "/message/{0}/";

        public Task<Listing> GetModMail(int? limit)
        {
            return GetMail("moderator", limit);
        }

        public Task ApproveThing(string thingId)
        {
            return ThingAction("approve", thingId);
        }

        public async Task RemoveThing(string thingId, bool spam)
        {
			var targetUri = RedditBaseUrl + "/api/remove";

            var content = new Dictionary<string, string>
            {
                { "id", thingId},
                { "uh", _userState.ModHash},
                { "spam", spam ? "true" : "false"}
            };

            await BasicPost(content, targetUri);
        }

        public Task IgnoreReportsOnThing(string thingId)
        {
            return ThingAction("ignore_reports", thingId);
        }

        public async Task Friend(string name, string container, string note, string type)
        {
			var targetUri = RedditBaseUrl + "/api/friend";

            var content = new Dictionary<string, string>
            {
                { "api_type", "json"},
                { "uh", _userState.ModHash},
                { "container", container },
                { "name", name },
                { "note", note},
                { "type", type }
            };

            await BasicPost(content, targetUri);
        }

        public async Task Unfriend(string name, string container, string type)
        {
			var targetUri = RedditBaseUrl + "/api/unfriend";

            var content = new Dictionary<string, string>
            {
                { "uh", _userState.ModHash},
                { "container", container },
                { "name", name },
                { "type", type }
            };

            await BasicPost(content, targetUri);
        }

        public Task AddContributor(string name, string subreddit, string note)
        {
            return Friend(name, subreddit, note, "contributor");
        }

        public Task RemoveContributor(string subreddit, string name)
        {
            return Unfriend(name, subreddit, "contributor");
        }

        public Task AddModerator(string name, string subreddit, string note)
        {
            return Friend(name, subreddit, note, "moderator");
        }

        public Task RemoveModerator(string subreddit, string name)
        {
            return Unfriend(name, subreddit, "moderator");
        }

        public Task AddBan(string name, string subreddit, string note)
        {
            return Friend(name, subreddit, note, "banned");
        }

        public Task RemoveBan(string subreddit, string name)
        {
            return Unfriend(name, subreddit, "banned");
        }

        static DateTime _priorRequestSet = new DateTime();
        static int _requestSetCount = 0;
        static DateTime _lastRequestMade = new DateTime();

        //dont hammer reddit!
        //Make no more than thirty requests per minute. This allows some burstiness to your requests, 
        //but keep it sane. On average, we should see no more than one request every two seconds from you.
        //the above statement is from the reddit api docs, but its not quite true, there are some api's that have logging 
        //set for 15 requests in 30 seconds, so we can allow some burstiness but it must fit in the 15 requests/30 seconds rule
        public static async Task ThrottleRequests()
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
    }
}
