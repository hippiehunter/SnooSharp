using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading;
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

        private async Task EnsureRedditCookie(CancellationToken token)
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
					_userState.OAuth = await RefreshToken(_userState.OAuth.RefreshToken, token);
				}

				_httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _userState.OAuth.AccessToken);
			}
			else
			{
				_httpClient.DefaultRequestHeaders.Authorization = null;
			}
        }

        public async Task<Account> GetMe()
        {
            return await GetMe(_userState.LoginCookie);
        }

        private int _failedRequestCount = 0;

        private Task<string> GetAuthedString(string url, CancellationToken token)
        {
            return Task.Run(() => GetAuthedStringInner(url, token), token);
        }

        private async Task<string> GetAuthedStringInner(string url, CancellationToken token)
        {
            await ThrottleRequests(token);
            await EnsureRedditCookie(token);
            var responseMessage = await _httpClient.GetAsync(RedditBaseUrl + url, HttpCompletionOption.ResponseContentRead, token);
            var bodyString = ProcessJsonErrors(await responseMessage.Content.ReadAsStringAsync());
            if (bodyString.StartsWith("<!doctype html><html><title>") && bodyString.EndsWith("try again and hopefully we will be fast enough this time."))
                return await GetAuthedString(url, token);
            else if (responseMessage.IsSuccessStatusCode)
            {
                if (string.IsNullOrWhiteSpace(bodyString) || bodyString == "{}" || bodyString == "\"{}\"")
                    throw new RedditException("body string was empty but no error code was present");
                else
                    return bodyString;
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
                            {
                                return await GetAuthedString(url, token);
                            }
                            break;
                        }
                    case HttpStatusCode.NotFound:
                        throw new RedditNotFoundException(url);
                    case HttpStatusCode.Forbidden:
                        throw new RedditException(url + " forbidden");
                }
                responseMessage.EnsureSuccessStatusCode();
                return null;
            }
        }

        public static string MakePlainSubredditName(string subreddit)
        {
            var cleanSubreddit = subreddit;
            if (cleanSubreddit.Contains("/r/"))
            {
                var startOfSubredditName = cleanSubreddit.IndexOf("/r/") + 3;
                var endOfSubredditName = cleanSubreddit.IndexOf('/', startOfSubredditName);
                if (endOfSubredditName != -1)
                    cleanSubreddit = cleanSubreddit.Substring(startOfSubredditName, endOfSubredditName - startOfSubredditName);
                else
                    cleanSubreddit = cleanSubreddit.Substring(startOfSubredditName);
            }
            return cleanSubreddit;
            
        }

        private async Task<T> GetAuthedJson<T>(string url, CancellationToken token)
        {
            return await Task.Run(async () => JsonConvert.DeserializeObject<T>(await GetAuthedString(url, token)));
        }

        //this one is seperated out so we can use it interally on initial user login
        public async Task<Account> GetMe(string loginCookie)
        {
            var thing = await GetAuthedJson<Thing>("/api/me.json", CancellationToken.None);
            return thing == null ? null : (new TypedThing<Account>(thing)).Data;
        }

		//this one is seperated out so we can use it interally on initial user login
		public async Task<Account> GetIdentity()
		{
            return await GetAuthedJson<Account>("/api/v1/me", CancellationToken.None);
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

            await ThrottleRequests(CancellationToken.None);
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

        public Task<Tuple<string, Listing>> Search(string query, int? limit, bool reddits, string restrictedToSubreddit)
        {
            return Search(query, limit, reddits, restrictedToSubreddit, CancellationToken.None);
        }

        public async Task<Tuple<string, Listing>> Search(string query, int? limit, bool reddits, string restrictedToSubreddit, CancellationToken token)
        {
            var maxLimit = _userState.IsGold ? 1500 : 100;
            var guardedLimit = Math.Min(maxLimit, limit ?? maxLimit);

            string targetUri = null;
			string afterUri = null;
            if (reddits)
            {
                targetUri = string.Format("/subreddits/search.json?limit={0}&q={1}", guardedLimit, query);
				afterUri = string.Format("/subreddits/search.json?q={0}", query);
            }
            else if (string.IsNullOrWhiteSpace(restrictedToSubreddit))
            {
                targetUri = string.Format("/search.json?limit={0}&q={1}", guardedLimit, query);
				afterUri = string.Format("/search.json&q={0}", query);
            }
            else
            {
                targetUri = string.Format("/r/{2}/search.json?limit={0}&q={1}&restrict_sr=on", guardedLimit, query, MakePlainSubredditName(restrictedToSubreddit));
				afterUri = string.Format("/subreddits/r/{1}/search.json?q={0}&restrict_sr=on", query, MakePlainSubredditName(restrictedToSubreddit));
            }
            var newListing = await GetAuthedJson<Listing>(targetUri,token);
            return Tuple.Create(afterUri, await _listingFilter.Filter(newListing));
        }

        public Task<Thing> GetThingById(string id)
        {
            return GetThingById(id, CancellationToken.None);
        }

        public async Task<Thing> GetThingById(string id, CancellationToken token)
        {
            var targetUri = string.Format("/by_id/{0}.json", id);
            await ThrottleRequests(token);
            await EnsureRedditCookie(token);
            var thingStr = await GetAuthedString(targetUri, token);
            if(thingStr.StartsWith("{\"kind\": \"Listing\""))
            {
                var listing = JsonConvert.DeserializeObject<Listing>(thingStr);
                return listing.Data.Children.First();
            }
            else
                return JsonConvert.DeserializeObject<Thing>(thingStr);
        }

        public async Task DeleteMultireddit(string url)
        {
            await BasicDelete(new Dictionary<string, string> { { "uh", _userState.ModHash } }, RedditBaseUrl + "/api/multi/" + url);
        }

        public virtual Task<Listing> GetSubreddits(int? limit, string where = null)
        {
            return GetSubreddits(limit, CancellationToken.None, where);
        }

        //known options at creation are where={new,popular}
        public virtual async Task<Listing> GetSubreddits(int? limit, CancellationToken token, string where = null)
        {
            var guardedLimit = Math.Min(100, limit ?? 100);
            if(string.IsNullOrWhiteSpace(where))
                return await _listingFilter.Filter(await GetAuthedJson<Listing>("/reddits/.json?limit=" + guardedLimit, token));
            else
                return await _listingFilter.Filter(await GetAuthedJson<Listing>(string.Format("/subreddits/{0}.json?limit={1}", where, guardedLimit), token));
        }

        public async Task<TypedThing<Subreddit>> GetSubredditAbout(string name)
        {
            return JsonConvert.DeserializeObject<TypedThing<Subreddit>>(await GetSubredditImpl(name, null, CancellationToken.None));
        }

        public async Task<UserListing> GetSubredditAbout(string name, string where)
        {
            return JsonConvert.DeserializeObject<UserListing>(await GetSubredditImpl(name, where, CancellationToken.None));
        }

        private async Task<string> GetSubredditImpl(string name, string where, CancellationToken token)
        {
            //no info for the front page
            if (name == "/")
                return JsonConvert.SerializeObject(new TypedThing<Subreddit>(new Thing { Kind = "t5", Data = new Subreddit { Headertitle = name } }));
            else if (name == "all")
                return JsonConvert.SerializeObject(new TypedThing<Subreddit>(new Thing { Kind = "t5", Data = new Subreddit { Headertitle = "all", Url = "/r/all", Name = "all", DisplayName="all", Title="all", Id="t5_fakeid" } }));

            if (!name.Contains("/m/"))
            {
                var subreddit = await GetAuthedString(string.Format("/r/{0}/about{1}.json", MakePlainSubredditName(name), where != null ? "/" + where : ""), token);
                //error page
                if (subreddit.ToLower().StartsWith("<!doctype html>"))
                {
                    throw new RedditNotFoundException(name);
                }
                else
                {
                    return subreddit;
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

                await ThrottleRequests(token);
				await EnsureRedditCookie(token);
                var description = await GetAuthedString(string.Format("/api/multi/{0}/description", name), token);
                await ThrottleRequests(token);
                var labeledMulti = await GetAuthedString(string.Format("/api/multi/{0}/", name), token);
                //error page
                if (description.ToLower().Contains("\"reason\": \"MULTI_NOT_FOUND\"") ||
                    labeledMulti.ToLower().Contains("\"reason\": \"MULTI_NOT_FOUND\""))
                {
                    throw new RedditNotFoundException(name);
                }
                else
                {
                    var typedMulti = new TypedThing<LabeledMulti>(JsonConvert.DeserializeObject<Thing>(labeledMulti));
                    var typedMultiDescription = new TypedThing<LabeledMultiDescription>(JsonConvert.DeserializeObject<Thing>(labeledMulti));
                    var multiPath = typedMulti.Data.Path;

                    if (!string.IsNullOrWhiteSpace(_userState.Username))
                        multiPath = multiPath.Replace("/user/" + _userState.Username, "/me");

                    return JsonConvert.SerializeObject(new TypedThing<Subreddit>(new Thing { Kind = "t5", Data = new Subreddit { Description = typedMultiDescription.TypedData.BodyMD, DisplayName = typedMulti.Data.Name, Title = typedMulti.Data.Name, Url = multiPath, Headertitle = typedMulti.Data.Name, Over18 = false } }));
                }
            }
        }

        public static readonly string PostByUserBaseFormat = "/user/{0}/";


        public Task<Listing> GetPostsByUser(string username, int? limit)
        {
            return GetPostsByUser(username, limit, CancellationToken.None);
        }

        public async Task<Listing> GetPostsByUser(string username, int? limit, CancellationToken token)
        {
            if (string.IsNullOrWhiteSpace(username))
                throw new ArgumentNullException("username");

            var maxLimit = _userState.IsGold ? 1500 : 100;
            var guardedLimit = Math.Min(maxLimit, limit ?? maxLimit);

            var targetUri = string.Format("/user/{0}/.json?limit={1}", username, guardedLimit);
            return await _listingFilter.Filter(await GetAuthedJson<Listing>(targetUri, token));
        }

        public Task<Listing> GetPostsBySubreddit(string subreddit, string sort = "hot", int? limit = null)
        {
            return GetPostsBySubreddit(subreddit, CancellationToken.None, sort, limit);
        }

        public async Task<Listing> GetPostsBySubreddit(string subreddit, CancellationToken token, string sort = "hot", int? limit = null)
        { 
            var guardedLimit = Math.Min(100, limit ?? 100);

            if (subreddit == null)
            {
                throw new RedditNotFoundException("(null)");
            }

			var targetUri = string.Format("{0}{2}.json?limit={1}", subreddit, guardedLimit, subreddit.EndsWith("/") ? sort : "/" + sort);

            return await _listingFilter.Filter(await GetAuthedJson<Listing>(targetUri, token));
        }
		private static int getMoreCount = 0;


        public Task<Listing> GetMoreOnListing(More more, string contentId, string subreddit)
        {
            return GetMoreOnListing(more, contentId, subreddit, CancellationToken.None);
        }


        public async Task<Listing> GetMoreOnListing(More more, string contentId, string subreddit, CancellationToken token)
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
            await ThrottleRequests(token);
			await EnsureRedditCookie(token);
            var result = await _httpClient.PostAsync(targetUri, new FormUrlEncodedContent(arguments), token);
            var resultString = await result.Content.ReadAsStringAsync();
            var newListing = new Listing
            {
                Kind = "Listing",
                Data = new ListingData { Children = await Task.Run(() => JsonConvert.DeserializeObject<JsonThing>(resultString).Json.Data.Things) }
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
        public Task<Thing> GetLinkByUrl(string url)
        {
            return GetLinkByUrl(url, CancellationToken.None);
        }

        public async Task<Thing> GetLinkByUrl(string url, CancellationToken token)
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
			if (url.StartsWith("http://") && url.Contains("reddit.com"))
			{
				url = url.Substring(url.IndexOf("reddit.com") + "reddit.com".Length);
			}

            var json = await GetAuthedString(url, token);
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

        public Task<Listing> GetCommentsOnPost(string subreddit, string permalink, int? limit, string context = null, string sort = null)
        {
            return GetCommentsOnPost(subreddit, permalink, CancellationToken.None, limit, context, sort);
        }

        public async Task<Listing> GetCommentsOnPost(string subreddit, string permalink, CancellationToken token, int? limit = null, string context = null, string sort = null)
        {
            return await Task.Run(async () => 
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
				    targetUri = permalink;
                }
                else if (permalink.Contains("?"))
                {
                    var queryPos = permalink.IndexOf("?");
                    targetUri = string.Format("{0}.json{1}", permalink.Remove(queryPos), permalink.Substring(queryPos));
                }
                else
                {
                    targetUri = limit == -1 ?
                                string.Format("{0}.json", permalink) :
							    string.Format("{0}.json?limit={1}", permalink, guardedLimit);
                }

                if (context != null || sort != null)
                {
                    var parameters = new List<string>();
                    if (context != null)
                        parameters.Add("context=" + context);
                    if (sort != null)
                        parameters.Add("sort=" + sort);

                    if (targetUri.Contains("?"))
                    {
                        targetUri += "&" + string.Join("&", parameters);
                    }
                    else
                    {
                        targetUri += "?" + string.Join("&", parameters);
                    }
                }
                

                Listing listing = null;
                var comments = await GetAuthedString(targetUri, token);
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
            }, token);
        }
        public Task<Listing> GetMessages(int? limit)
        {
            return GetMail("inbox", limit, CancellationToken.None);
        }
        public Task<Listing> GetMessages(int? limit, CancellationToken token)
        {
            return GetMail("inbox", limit, token);
        }

        public void AddFlairInfo(string linkId, string opName)
        {
            if (!_linkToOpMap.ContainsKey(linkId))
            {
                _linkToOpMap.Add(linkId, opName);
            }
        }


        public Task<Listing> GetAdditionalFromListing(string baseUrl, string after, int? limit = null)
        {
            return GetAdditionalFromListing(baseUrl, after, CancellationToken.None, limit);
        }

        public async Task<Listing> GetAdditionalFromListing(string baseUrl, string after, CancellationToken token, int? limit = null)
        {
            var maxLimit = _userState.IsGold ? 1500 : 500;
            var guardedLimit = Math.Min(maxLimit, limit ?? maxLimit);

            string targetUri = null;
            //if this base url already has arguments (like search) just append the count and the after
            if (baseUrl.Contains(".json?"))
                targetUri = string.Format("{0}&limit={1}&after={2}", baseUrl, guardedLimit, after);
            else
				targetUri = string.Format("{0}.json?limit={1}&after={2}", baseUrl, guardedLimit, after);

            return await _listingFilter.Filter(await GetAuthedJson<Listing>(targetUri, token));

        }

        public Task<TypedThing<Account>> GetAccountInfo(string accountName)
        {
            return GetAccountInfo(accountName, CancellationToken.None);
        }

        public async Task<TypedThing<Account>> GetAccountInfo(string accountName, CancellationToken token)
        {
            var targetUri = string.Format("/user/{0}/about.json", accountName);
            return new TypedThing<Account>(await GetAuthedJson<Thing>(targetUri, token));

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

        public virtual async Task AddSubredditSubscription(string subredditId, bool unsub)
        {
            var content = new Dictionary<string, string>
            {
                { "sr", subredditId},
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

        public virtual Task HideThing(string thingId)
        {
            return ThingAction("hide", thingId);
        }

        public virtual Task UnHideThing(string thingId)
        {
            return ThingAction("unhide", thingId);
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
                {"sr", MakePlainSubredditName(subreddit)},
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
        private async Task<string> PostCaptchable(Dictionary<string, string> urlEncodedData, string uri)
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


			await EnsureRedditCookie(CancellationToken.None);
            HttpResponseMessage response = null;
            string responseString = null;
            do
            {
                try
                {
                    await ThrottleRequests(CancellationToken.None);
                    response = await _httpClient.PostAsync(uri, new FormUrlEncodedContent(urlEncodedData));
                    responseString = await response.Content.ReadAsStringAsync();
                }
                catch(WebException ex)
                {
                    //connection problems during a post, so put it into the deferalSink
                    if (ex.Status == WebExceptionStatus.ConnectFailure || ex.Status == WebExceptionStatus.SendFailure)
                    {
                        _deferalSink.Defer(urlEncodedData, uri);
                        return null;
                    }
                    else
                        throw;
                
                }
            }while(response != null && await HandleCaptchaError(responseString));
            return responseString;
        }

        private async Task<bool> HandleCaptchaError(string json)
        {
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
            await ThrottleRequests(CancellationToken.None);
            await EnsureRedditCookie(CancellationToken.None);
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

        private async Task BasicDelete(Dictionary<string, string> arguments, string url)
        {
            await ThrottleRequests(CancellationToken.None);
            await EnsureRedditCookie(CancellationToken.None);
            HttpResponseMessage responseMessage = null;
            try
            {
                var request = new HttpRequestMessage(HttpMethod.Delete, url);
                request.Content = new FormUrlEncodedContent(arguments);
                responseMessage = await _httpClient.SendAsync(request);
            }
            catch (WebException ex)
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

        public virtual async Task<String> AddComment(string parentId, string content)
        {
            var arguments = new Dictionary<string, string>
            {
                {"thing_id", parentId},
                {"text", content.Replace("\r\n", "\n")},
                {"uh", _userState.ModHash}
            };

			var responseString = await PostCaptchable(arguments, RedditBaseUrl + "/api/comment");
            if (responseString != null)
            {
                var responseObject = JsonConvert.DeserializeAnonymousType(responseString, new { json = new { data = new { things = new List<Thing>() } } });
                return ((Comment)responseObject.json.data.things.First().Data).Id;
            }
            else
                return null;
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

        private async Task<Listing> GetUserMultis(Listing listing, CancellationToken token)
        {
            var targetUri = "/api/multi/mine.json";
            var subreddits = await GetAuthedString(targetUri, token);
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

        public Task<Listing> GetSubscribedSubredditListing()
        {
            return GetSubscribedSubredditListing(CancellationToken.None);
        }

        public Task<Listing> GetSubscribedSubredditListing(CancellationToken token)
        {
			return GetSubredditListing("subscriber", 100, token);
        }

		public Task<Listing> GetContributorSubredditListing(CancellationToken token)
		{
			return GetSubredditListing("contributor", 100, token);
		}

		public Task<Listing> GetModeratorSubredditListing(CancellationToken token)
		{
			return GetSubredditListing("moderator", 100, token);
		}

		public async Task<Listing> GetSubredditListing(string where, int limit, CancellationToken token)
		{
			var targetUri = string.Format("/subreddits/mine/{0}.json?limit={1}", where, limit);
			try
			{
				return await GetAuthedJson<Listing>(targetUri, token);
			}
			catch
			{
				return new Listing { Data = new ListingData { Children = new List<Thing>() } };
			}
		}


        public Task<List<Recommendation>> GetRecomendedSubreddits(IEnumerable<string> inputSubreddits)
        {
            return GetRecomendedSubreddits(inputSubreddits, CancellationToken.None);
        }

        public async Task<List<Recommendation>> GetRecomendedSubreddits(IEnumerable<string> inputSubreddits, CancellationToken token)
        {
            var targetUri = "/api/recommend/sr/" + string.Join(",", inputSubreddits.Select(MakePlainSubredditName));
            var subreddits = await GetAuthedString(targetUri, token);
            return await Task.Run(() => JsonConvert.DeserializeObject<List<Recommendation>>(subreddits), token);
        }


        public async Task<bool> CheckLogin(string loginToken)
        {
            await ThrottleRequests(CancellationToken.None);
			await EnsureRedditCookie(CancellationToken.None);
			var meString = await _httpClient.GetStringAsync(RedditBaseUrl + "/api/me.json");
            return (!string.IsNullOrWhiteSpace(meString) && meString != "{}");
        }

        public Task<Listing> GetSaved(int? limit)
        {
            return GetSaved(limit);
        }

        public async Task<Listing> GetSaved(int? limit, CancellationToken token)
        {
            return await GetUserInfoListing(_userState.Username, "saved", limit, token);
        }

        public Task<Listing> GetLiked(int? limit)
        {
            return GetLiked(limit, CancellationToken.None);
        }

        public async Task<Listing> GetLiked(int? limit, CancellationToken token)
        {
            return await GetUserInfoListing(_userState.Username, "liked", limit, token);
        }

        private async Task<Listing> GetUserInfoListing(string username, string kind, int? limit, CancellationToken token)
        {
            var guardedLimit = Math.Min(100, limit ?? 100);
            var targetUri = string.Format("/user/{0}/{2}/.json?limit={1}", username, guardedLimit, kind);
            return await _listingFilter.Filter(await GetAuthedJson<Listing>(targetUri, token));
        }

        public Task<Listing> GetUserTrophies(string username, int? limit)
        {
            return GetUserTrophies(username, limit);
        }

        public async Task<Listing> GetUserTrophies(string username, int? limit, CancellationToken token)
        {
            var targetUri = string.Format("/api/v1/user/{0}/trophies?limit={1}", username, Math.Min(100, limit ?? 100));
            return await GetAuthedJson<Listing>(targetUri, token);
        }

        public Task<Thing> GetUserInfo(string username, string kind, int? limit)
        {
            return GetUserInfo(username, kind, limit, CancellationToken.None);
        }

        public async Task<Thing> GetUserInfo(string username, string kind, int? limit, CancellationToken token)
        {
            var guardedLimit = Math.Min(100, limit ?? 100);
            var targetUri = string.Format("/user/{0}/{1}/.json", username, kind);
            return await GetAuthedJson<Thing>(targetUri, token);
        }

        public Task<Listing> GetDisliked(int? limit)
        {
            return GetDisliked(limit, CancellationToken.None);
        }

        public async Task<Listing> GetDisliked(int? limit, CancellationToken token)
        {
            return await GetUserInfoListing(_userState.Username, "disliked", limit, token);
        }

        public Task<Listing> GetSentMessages(int? limit)
        {
            return GetMail("sent", limit, CancellationToken.None);
        }

        public Task<Listing> GetSentMessages(int? limit, CancellationToken token)
        {
            return GetMail("sent", limit, token);
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
            return ThingAction("unsave", thingId);
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

        public Task<Listing> GetModActions(string subreddit, int? limit)
        {
            return GetModActions(subreddit, limit, CancellationToken.None);
        }

        public Task<Listing> GetModActions(string subreddit, int? limit, CancellationToken token)
        {
			return GetSubredditAbout(subreddit, "log", limit, token);
        }

		

		public Task<Listing> GetModQueue(string subreddit, int? limit)
		{
			return GetModQueue(subreddit, limit, CancellationToken.None);
		}

		public Task<Listing> GetModQueue(string subreddit, int? limit, CancellationToken token)
		{
			return GetSubredditAbout(subreddit, "modqueue", limit, token);
		}

		public static readonly string SubredditAboutBaseUrlFormat = "/r/{0}/about/{1}";
		public async Task<Listing> GetSubredditAbout(string subreddit, string where, int? limit, CancellationToken token)
		{
			var guardedLimit = Math.Min(20, limit ?? 100);

			var targetUri = string.Format(SubredditAboutBaseUrlFormat + ".json?limit={2}", subreddit, where, guardedLimit);

			var messages = await GetAuthedString(targetUri, token);
			if (messages == "\"{}\"")
			{
				return new Listing { Kind = "Listing", Data = new ListingData { Children = new List<Thing>() } };
			}
			return JsonConvert.DeserializeObject<Listing>(messages);
		}

        private async Task<Listing> GetMail(string kind, int? limit, CancellationToken token)
        {
            var guardedLimit = Math.Min(100, limit ?? 100);

			var targetUri = string.Format(RedditBaseUrl + MailBaseUrlFormat + ".json?limit={1}", kind, guardedLimit);

            await ThrottleRequests(token);
			await EnsureRedditCookie(token);
            var messages = await GetAuthedString(targetUri, token);
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
            return GetMail("moderator", limit, CancellationToken.None);
        }

        public Task<Listing> GetModMail(int? limit, CancellationToken token)
        {
            return GetMail("moderator", limit, token);
        }

        public Task ApproveThing(string thingId)
        {
            return ThingAction("approve", thingId);
        }

		public async Task DeleteLinkOrComment(string thingId)
		{
			var targetUri = RedditBaseUrl + "/api/del";

			var content = new Dictionary<string, string>
            {
                { "id", thingId},
                { "uh", _userState.ModHash}
            };

			await BasicPost(content, targetUri);
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
    }
}
