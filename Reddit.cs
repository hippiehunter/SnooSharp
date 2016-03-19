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
        INetworkLayer _networkLayer;
        ICachingProvider _cacheProvider;
        UserState _userState; 
        IActionDeferralSink _deferalSink;
        ICaptchaProvider _captchaProvider;
		string _appId;
		string _appSecret;
		string _redirectUrl;
		public Reddit(IListingFilter listingFilter, UserState userState, IActionDeferralSink deferalSink, ICaptchaProvider captchaProvider, string appId = null, string appSecret = null, string redirectUrl = null, ICachingProvider cacheProvider = null, INetworkLayer networkLayer = null)
        {
            _cacheProvider = cacheProvider;
            _networkLayer = networkLayer ?? new NetworkLayer(userState, appId, appSecret, redirectUrl);
            _listingFilter = listingFilter;
            _userState = userState ?? new UserState();
            _deferalSink = deferalSink;
            _captchaProvider = captchaProvider;
			_appId = appId;
			_appSecret = appSecret;
			_redirectUrl = redirectUrl;
        }

        public string CurrentUserName
        {
            get
            {
                return _userState?.Username;
            }
        }
        public RedditOAuth CurrentOAuth
        {
            get
            {
                return _userState?.OAuth;
            }
        }

        public bool CurrentUserIsMod
        {
            get
            {
                return _userState?.IsMod ?? false;
            }
        }

        public void SetUserState(UserState state)
        {
            _userState = state;
        }

        public async Task ProcessDeferralSink()
        {
            var deferral = _deferalSink.DequeDeferral();
            if (deferral != null)
            {
                await BasicPost(deferral.Item1, deferral.Item2);
            }
        }

        public async Task<RedditOAuth> RequestGrantCode(string code, CancellationToken token)
        {
            return await _networkLayer.RequestGrantCode(code, token);
        }

        private Task<string> GetAuthedString(string url, CancellationToken token, IProgress<float> progress, Dictionary<string, string> body = null)
        {
            return Task.Run(async () => await _networkLayer.Get(url, token, progress, body), token);
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

        private async Task<T> GetAuthedJson<T>(string url, CancellationToken token, IProgress<float> progress, Dictionary<string, string> body = null)
        {
            return await Task.Run(async () => JsonConvert.DeserializeObject<T>(await _networkLayer.Get(url, token, progress, body)));
        }

        private async Task<Listing> GetListing(string url, CancellationToken token, IProgress<float> progress, bool ignoreCache, Dictionary<string, string> body)
        {
            return await Task.Run(async () =>
            {
                if (!ignoreCache && _cacheProvider != null)
                {
                    var cacheResult = await _cacheProvider.GetListing(url);
                    if (cacheResult != null)
                        return cacheResult;
                }
                var resultString = await _networkLayer.Get(url, token, progress, body);
                Listing resultListing = null;
                if (resultString.StartsWith("["))
                {
                    var listings = JsonConvert.DeserializeObject<Listing[]>(resultString);
                    resultListing = new Listing { Data = new ListingData { Children = new List<Thing>() } };
                    foreach (var combinableListing in listings)
                    {
                        resultListing.Data.Children.AddRange(combinableListing.Data.Children);
                        resultListing.Kind = combinableListing.Kind;
                        resultListing.Data.After = combinableListing.Data.After;
                        resultListing.Data.Before = combinableListing.Data.Before;
                    }
                }
                else if (resultString == "\"{}\"")
                {
                    return new Listing { Kind = "Listing", Data = new ListingData { Children = new List<Thing>() } };
                }
                else
                {
                    resultListing = JsonConvert.DeserializeObject<Listing>(resultString);
                }
                var filteredResult =  await _listingFilter.Filter(resultListing);
                if(_cacheProvider != null)
                    await _cacheProvider.SetListing(url, filteredResult);
                return filteredResult;
            });
            
        }

		//this one is seperated out so we can use it interally on initial user login
		public async Task<Account> GetIdentity(CancellationToken token, IProgress<float> progress)
		{
            return await GetAuthedJson<Account>("/api/v1/me", token, progress);
		}

        public async Task<Tuple<string, Listing>> Search(string query, int? limit, bool reddits, string restrictedToSubreddit, CancellationToken token, IProgress<float> progress, bool ignoreCache)
        {
            var maxLimit = _userState?.IsGold ?? false ? 1500 : 100;
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

            return Tuple.Create(afterUri, await GetListing(targetUri, token, progress, ignoreCache, null));
        }

        public async Task<Thing> GetLinkByUrl(string url, CancellationToken token, IProgress<float> progress, bool ignoreCache)
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

            var json = await GetAuthedString(url, token, progress);
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

        public async Task<Thing> GetThingById(string id, CancellationToken token, IProgress<float> progress, bool ignoreCache)
        {
            if (!ignoreCache && _cacheProvider != null)
            {
                var cachedThing = await _cacheProvider.GetThingById(id);
                if (cachedThing != null)
                    return cachedThing;
            }

            var targetUri = string.Format("/by_id/{0}.json", id);
            var thingStr = await GetAuthedString(targetUri, token, progress);
            Thing result = null;
            if (thingStr.StartsWith("{\"kind\": \"Listing\""))
            {
                var listing = JsonConvert.DeserializeObject<Listing>(thingStr);
                result = listing.Data.Children.First();
            }
            else
                result = JsonConvert.DeserializeObject<Thing>(thingStr);

            if (_cacheProvider != null)
            {
                await _cacheProvider.SetThing(result);
            }

            return result;
        }

        public async Task CopyMulti(string from, string to, string displayName)
        {
            await BasicPut(new Dictionary<string, string>
            {
                { "from", from },
                { "to", to },
                { "display_name", displayName },
                { "uh", _userState.ModHash },
            }, "/api/multi/copy");
        }

        public async Task CreateOrUpdateMulti(string multiName, string description, string displayName, string iconName, string keyColor, string visibility, string weightingScheme, IEnumerable<string> subreddits)
        {
            var targetUrl = "/api/multi/" + multiName;
            await BasicPut(new Dictionary<string, string>
            {
                { "multipath", multiName },
                { "uh", _userState.ModHash },
                { "model", JsonConvert.SerializeObject(new { description_md = description, display_name = displayName, icon_name = iconName, key_color = keyColor, visibility = visibility, weighting_scheme = weightingScheme, subreddits = subreddits }) }
            }, targetUrl);
        }

        public async Task ChangeMulti(string multiName, string subredditName, bool add)
        {
            var targetUrl = string.Format("{0}/api/multi/{1}/r/{2}", _networkLayer.RedditBaseUrl, multiName, subredditName);
            if(!add)
                await BasicDelete(new Dictionary<string, string> { { "uh", _userState.ModHash } }, targetUrl);
            else
                await BasicPut(new Dictionary<string, string> { { "uh", _userState.ModHash } }, targetUrl);
        }

        public async Task DeleteMultireddit(string url)
        {
            await BasicDelete(new Dictionary<string, string> { { "uh", _userState.ModHash } }, _networkLayer.RedditBaseUrl + "/api/multi/" + url);
        }

        //known options at creation are where={new,popular}
        public virtual async Task<Listing> GetSubreddits(int? limit, CancellationToken token, IProgress<float> progress, bool ignoreCache, string where = null)
        {
            var guardedLimit = Math.Min(100, limit ?? 100);
            if(string.IsNullOrWhiteSpace(where))
                return await GetListing("/reddits/.json?limit=" + guardedLimit, token, progress, ignoreCache, null);
            else
                return await GetListing(string.Format("/subreddits/{0}.json?limit={1}", where, guardedLimit), token, progress, ignoreCache, null);
        }

        public async Task<Thing> GetSubredditAbout(string name, CancellationToken token, IProgress<float> progress, string where = null)
        {
            return JsonConvert.DeserializeObject<Thing>(await GetSubredditImpl(name, where, token, progress));
        }

        private async Task<string> GetSubredditImpl(string name, string where, CancellationToken token, IProgress<float> progress)
        {
			name = name.ToLower();
            //no info for the front page
            if (name == "/")
                return JsonConvert.SerializeObject(new TypedThing<Subreddit>(new Thing { Kind = "t5", Data = new Subreddit { Headertitle = name } }));
			else if (name == "all" || name == "/r/all")
                return JsonConvert.SerializeObject(new TypedThing<Subreddit>(new Thing { Kind = "t5", Data = new Subreddit { Headertitle = "all", Url = "/r/all", Name = "all", DisplayName="all", Title="all", Id="t5_fakeid" } }));

            if (!name.Contains("/m/"))
            {
                var subreddit = await GetAuthedString(string.Format("/r/{0}/about{1}.json", MakePlainSubredditName(name), where != null ? "/" + where : ""), token, progress);
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

                var description = await GetAuthedString(string.Format("/api/multi/{0}/description", name), token, progress);
                var labeledMulti = await GetAuthedString(string.Format("/api/multi/{0}/", name), token, progress);
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
        public async Task<Listing> GetPostsByUser(string username, int? limit, CancellationToken token, IProgress<float> progress, bool ignoreCache)
        {
            if (string.IsNullOrWhiteSpace(username))
                throw new ArgumentNullException("username");

            var maxLimit = _userState.IsGold ? 1500 : 100;
            var guardedLimit = Math.Min(maxLimit, limit ?? maxLimit);

            var targetUri = string.Format("/user/{0}/.json?limit={1}", username, guardedLimit);
            return await GetListing(targetUri, token, progress, ignoreCache, null);
        }

        public async Task<Listing> GetPostsBySubreddit(string subreddit, CancellationToken token, IProgress<float> progress, bool ignoreCache, string sort = "hot", int? limit = null)
        { 
            var guardedLimit = Math.Min(100, limit ?? 100);

            if (subreddit == null)
            {
                throw new RedditNotFoundException("(null)");
            }

			var targetUri = string.Format("{0}{2}.json?limit={1}", subreddit, guardedLimit, subreddit.EndsWith("/") ? sort : "/" + sort);

            return await GetListing(targetUri, token, progress, ignoreCache, null);
        }

		private static int getMoreCount = 0;
        public async Task<Listing> GetMoreOnListing(More more, string contentId, string subreddit, CancellationToken token, IProgress<float> progress)
        {
			var targetUri = _networkLayer.RedditBaseUrl + "/api/morechildren.json";

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
            var resultString = await _networkLayer.Post(targetUri, token, arguments, progress);
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

        public async Task<Listing> GetCommentsOnPost(string subreddit, string permalink, CancellationToken token, IProgress<float> progress, bool ignoreCache, int? limit = null, string context = null, string sort = null)
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

            var listing = await GetListing(targetUri, token, progress, ignoreCache, null);

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

        public void AddFlairInfo(string linkId, string opName)
        {
            if (!_linkToOpMap.ContainsKey(linkId))
            {
                _linkToOpMap.Add(linkId, opName);
            }
        }

        public async Task<Listing> GetAdditionalFromListing(string baseUrl, string after, CancellationToken token, IProgress<float> progress, bool ignoreCache, int? limit = null)
        {
            var maxLimit = _userState.IsGold ? 1500 : 500;
            var guardedLimit = Math.Min(maxLimit, limit ?? maxLimit);

            string targetUri = null;
            //if this base url already has arguments (like search) just append the count and the after
            if (baseUrl.Contains(".json?"))
                targetUri = string.Format("{0}&limit={1}&after={2}", baseUrl, guardedLimit, after);
            else
				targetUri = string.Format("{0}.json?limit={1}&after={2}", baseUrl, guardedLimit, after);

            return await GetListing(targetUri, token, progress, ignoreCache, null);

        }

        public async Task<TypedThing<Account>> GetAccountInfo(string accountName, CancellationToken token, IProgress<float> progress)
        {
            var targetUri = string.Format("/user/{0}/about.json", accountName);
            return new TypedThing<Account>(await GetAuthedJson<Thing>(targetUri, token, progress));

        }

        //this one is seperated out so we can use it interally on initial user login
        public async Task<Account> ChangeIdentity(RedditOAuth oAuth)
        {
            var authedLayer = _networkLayer.Clone(oAuth);
            var authedAccount = JsonConvert.DeserializeObject<Account>(await authedLayer.Get("/api/v1/me", CancellationToken.None, new Progress<float>(), null));
            _networkLayer = authedLayer;
            _userState = new UserState { OAuth = oAuth, Username = authedAccount.Name, IsGold = authedAccount.IsGold, IsMod = authedAccount.IsMod, ModHash = authedAccount.ModHash };
            return authedAccount;
        }

        public virtual async Task AddVote(string thingId, int direction)
        {
            var arguments = new Dictionary<string, string>
            {
                {"id", thingId},
                {"dir", direction.ToString()},
                {"uh", _userState.ModHash}
            };
			await BasicPost(arguments, _networkLayer.RedditBaseUrl + "/api/vote");
        }

        public virtual async Task AddSubredditSubscription(string subredditId, bool unsub)
        {
            var content = new Dictionary<string, string>
            {
                { "sr", subredditId},
                { "uh", _userState.ModHash},
                { "action", unsub ? "unsub" : "sub"}
            };
			await BasicPost(content, _networkLayer.RedditBaseUrl + "/api/subscribe");
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

			await PostCaptchable(arguments, _networkLayer.RedditBaseUrl + "/api/submit", CancellationToken.None);
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

			await BasicPost(arguments, _networkLayer.RedditBaseUrl + "/api/editusertext");
        }

        private string CaptchaIden { get; set; }
        private string Captcha { get; set; }
        private async Task<string> PostCaptchable(Dictionary<string, string> urlEncodedData, string uri, CancellationToken token)
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

            string responseString = null;
            do
            {
                try
                {
                    responseString = await _networkLayer.Post(uri, token, urlEncodedData, null);
                }
                catch(WebException ex)
                {
                    //connection problems during a post, so put it into the deferalSink
                    if (ex.Status == WebExceptionStatus.ConnectFailure || ex.Status == WebExceptionStatus.SendFailure)
                    {
                        _deferalSink.Defer(urlEncodedData, uri, "Post");
                        return null;
                    }
                    else
                        throw;
                
                }
            }while(await HandleCaptchaError(responseString));
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
            
            try
            {
                await _networkLayer.Send(url, "Post", CancellationToken.None, arguments);
            }
            catch(WebException ex)
            {
                //connection problems during a post, so put it into the deferalSink
                if (ex.Status == WebExceptionStatus.ConnectFailure || ex.Status == WebExceptionStatus.SendFailure)
                {
                    _deferalSink.Defer(arguments, url, "Post");
                    return;
                }
                else
                    throw;
            }
        }

        private async Task BasicDelete(Dictionary<string, string> arguments, string url)
        {
            try
            {
                await _networkLayer.Send(url, "Delete", CancellationToken.None, arguments);
            }
            catch (WebException ex)
            {
                //connection problems during a post, so put it into the deferalSink
                if (ex.Status == WebExceptionStatus.ConnectFailure || ex.Status == WebExceptionStatus.SendFailure)
                {
                    //TODO this isnt persisting the http verb
                    _deferalSink.Defer(arguments, url, "Delete");
                    return;
                }
                else
                    throw;
            }
        }

        private async Task BasicPut(Dictionary<string, string> arguments, string url)
        {
            try
            {
                await _networkLayer.Send(url, "Put", CancellationToken.None, arguments);
            }
            catch (WebException ex)
            {
                //connection problems during a post, so put it into the deferalSink
                if (ex.Status == WebExceptionStatus.ConnectFailure || ex.Status == WebExceptionStatus.SendFailure)
                {
                    //TODO this isnt persisting the http verb
                    _deferalSink.Defer(arguments, url, "Put");
                    return;
                }
                else
                    throw;
            }
        }

        public virtual async Task ReadMessage(string id)
        {
            var arguments = new Dictionary<string, string>
            {
                {"id", id},
                {"uh", _userState.ModHash}
            };

			await BasicPost(arguments, _networkLayer.RedditBaseUrl + "/api/read_message");
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

			await PostCaptchable(arguments, _networkLayer.RedditBaseUrl + "/api/compose", CancellationToken.None);
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

			await PostCaptchable(arguments, _networkLayer.RedditBaseUrl + "/api/compose", CancellationToken.None);
        }

        public virtual async Task<String> AddComment(string parentId, string content)
        {
            var arguments = new Dictionary<string, string>
            {
                {"thing_id", parentId},
                {"text", content.Replace("\r\n", "\n")},
                {"uh", _userState.ModHash}
            };

			var responseString = await PostCaptchable(arguments, _networkLayer.RedditBaseUrl + "/api/comment", CancellationToken.None);
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

			await BasicPost(arguments, _networkLayer.RedditBaseUrl + "/api/editusertext");
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

        public async Task<TypedThing<LabeledMulti>[]> GetUserMultis(CancellationToken token, IProgress<float> progress)
        {
            var targetUri = "/api/multi/mine.json";
            var subreddits = await GetAuthedJson< TypedThing < LabeledMulti >[]>(targetUri, token, progress, new Dictionary<string, string> { { "expand_srs", "True" } });
            if (subreddits.Length == 0)
                return subreddits;
            else
            {
                foreach (var labeledMulti in subreddits)
                {
                    labeledMulti.Data.Path = labeledMulti.Data.Path.Replace("/user/" + _userState.Username, "/me");
                }
            }
            
            return subreddits;
        }

        public Task<Listing> GetSubscribedSubredditListing(CancellationToken token, IProgress<float> progress, bool ignoreCache)
        {
			return GetSubredditListing("subscriber", 100, token, progress, ignoreCache);
        }

		public Task<Listing> GetContributorSubredditListing(CancellationToken token, IProgress<float> progress, bool ignoreCache)
		{
			return GetSubredditListing("contributor", 100, token, progress, ignoreCache);
		}

		public Task<Listing> GetModeratorSubredditListing(CancellationToken token, IProgress<float> progress, bool ignoreCache)
		{
			return GetSubredditListing("moderator", 20, token, progress, ignoreCache);
		}

		public async Task<Listing> GetSubredditListing(string where, int limit, CancellationToken token, IProgress<float> progress, bool ignoreCache)
		{
			var targetUri = limit == 20 ? 
				string.Format("/subreddits/mine/{0}.json", where) :
				string.Format("/subreddits/mine/{0}.json?limit={1}", where, limit);

            return await GetListing(targetUri, token, progress, ignoreCache , null);
		}

        public async Task<List<Recommendation>> GetRecomendedSubreddits(IEnumerable<string> inputSubreddits, CancellationToken token, IProgress<float> progress)
        {
            var targetUri = "/api/recommend/sr/" + string.Join(",", inputSubreddits.Select(MakePlainSubredditName));
            var subreddits = await _networkLayer.Get(targetUri, token, progress);
            return await Task.Run(() => JsonConvert.DeserializeObject<List<Recommendation>>(subreddits), token);
        }

        public async Task<Listing> GetSaved(int? limit, CancellationToken token, IProgress<float> progress, bool ignoreCache)
        {
            return await GetUserInfoListing(_userState.Username, "saved", limit, token, progress, ignoreCache);
        }

        public async Task<Listing> GetLiked(int? limit, CancellationToken token, IProgress<float> progress, bool ignoreCache)
        {
            return await GetUserInfoListing(_userState.Username, "liked", limit, token, progress, ignoreCache);
        }

        private async Task<Listing> GetUserInfoListing(string username, string kind, int? limit, CancellationToken token, IProgress<float> progress, bool ignoreCache)
        {
            var guardedLimit = Math.Min(100, limit ?? 100);
            var targetUri = string.Format("/user/{0}/{2}/.json?limit={1}", username, guardedLimit, kind);
            return await _listingFilter.Filter(await GetListing(targetUri, token, progress, ignoreCache, null));
        }

        public async Task<Listing> GetUserTrophies(string username, int? limit, CancellationToken token, IProgress<float> progress, bool ignoreCache)
        {
            var targetUri = string.Format("/api/v1/user/{0}/trophies?limit={1}", username, Math.Min(100, limit ?? 100));
            return await GetListing(targetUri, token, progress, ignoreCache, null);
        }

        public async Task<Thing> GetUserInfo(string username, string kind, int? limit, CancellationToken token, IProgress<float> progress)
        {
            var guardedLimit = Math.Min(100, limit ?? 100);
            var targetUri = string.Format("/user/{0}/{1}/.json", username, kind);
            return await GetAuthedJson<Thing>(targetUri, token, progress);
        }

        public async Task<Listing> GetDisliked(int? limit, CancellationToken token, IProgress<float> progress, bool ignoreCache)
        {
            return await GetUserInfoListing(_userState.Username, "disliked", limit, token, progress, ignoreCache);
        }

        public virtual Task AddSavedThing(string thingId)
        {
            return ThingAction("save", thingId);
        }

        public virtual Task AddReportOnThing(string thingId)
        {
            return ThingAction("report", thingId);
        }

        public virtual Task AddReportOnThing(string thingId, string reason)
        {
            return ThingAction("report", thingId, new Dictionary<string, string> { { "reason", reason } });
        }

        public virtual Task HideThing(string thingId)
        {
            return ThingAction("hide", thingId);
        }

        public virtual Task UnHideThing(string thingId)
        {
            return ThingAction("unhide", thingId);
        }

        public Task UnSaveThing(string thingId)
        {
            return ThingAction("unsave", thingId);
        }

        public Task ApproveThing(string thingId)
        {
            return ThingAction("approve", thingId);
        }

        public Task IgnoreReportsOnThing(string thingId)
        {
            return ThingAction("ignore_reports", thingId);
        }

        public async Task ThingAction(string action, string thingId, Dictionary<string, string> additionalParams)
        {
            var targetUri = _networkLayer.RedditBaseUrl + "/api/" + action;

            var content = new Dictionary<string, string>
            {
                { "id", thingId},
                { "uh", _userState.ModHash}
            };

            foreach (var par in additionalParams)
            {
                content.Add(par.Key, par.Value);
            }

            await BasicPost(content, targetUri);
        }

        public async Task ThingAction(string action, string thingId)
        {
			var targetUri = _networkLayer.RedditBaseUrl + "/api/" + action;

            var content = new Dictionary<string, string>
            {
                { "id", thingId},
                { "uh", _userState.ModHash}
            };

            await BasicPost(content, targetUri);
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

				await BasicPost(arguments, _networkLayer.RedditBaseUrl + "/api/store_visits");
            }
        }

        public Task<Listing> GetModActions(string subreddit, int? limit, CancellationToken token, IProgress<float> progress, bool ignoreCache)
        {
			return GetSubredditAbout(subreddit, "log", limit, token, progress, ignoreCache);
        }

		public Task<Listing> GetModQueue(string subreddit, int? limit, CancellationToken token, IProgress<float> progress, bool ignoreCache)
		{
			return GetSubredditAbout(subreddit, "modqueue", limit, token, progress, ignoreCache);
		}

		public static readonly string SubredditAboutBaseUrlFormat = "/r/{0}/about/{1}";
		public async Task<Listing> GetSubredditAbout(string subreddit, string where, int? limit, CancellationToken token, IProgress<float> progress, bool ignoreCache)
		{
			var guardedLimit = Math.Min(20, limit ?? 100);

			var targetUri = string.Format(SubredditAboutBaseUrlFormat + ".json?limit={2}", subreddit, where, guardedLimit);

			return await GetListing(targetUri, token, progress, ignoreCache, null);
		}

        public Task<Listing> GetMessages(int? limit, CancellationToken token, IProgress<float> progress)
        {
            return GetMail("inbox", limit, token, progress);
        }

        public Task<Listing> GetSentMessages(int? limit, CancellationToken token, IProgress<float> progress)
        {
            return GetMail("sent", limit, token, progress);
        }

        public static readonly string MailBaseUrlFormat = "/message/{0}/";

        public Task<Listing> GetModMail(int? limit, CancellationToken token, IProgress<float> progress)
        {
            return GetMail("moderator", limit, token, progress);
        }

        private async Task<Listing> GetMail(string kind, int? limit, CancellationToken token, IProgress<float> progress)
        {
            var guardedLimit = Math.Min(100, limit ?? 100);

			var targetUri = limit != null ? 
				string.Format(_networkLayer.RedditBaseUrl + MailBaseUrlFormat + ".json?limit={1}", kind, guardedLimit) :
				string.Format(_networkLayer.RedditBaseUrl + MailBaseUrlFormat + ".json", kind);

            var messages = await GetAuthedString(targetUri, token, progress);
            if (messages == "\"{}\"")
            {
                return new Listing { Kind = "Listing", Data = new ListingData { Children = new List<Thing>() } };
            }
            // Hacky hack mcHackerson
            messages = messages.Replace("\"kind\": \"t1\"", "\"kind\": \"t4\"");
            return await _listingFilter.Filter(JsonConvert.DeserializeObject<Listing>(messages));
        }

		public async Task DeleteLinkOrComment(string thingId)
		{
			var targetUri = _networkLayer.RedditBaseUrl + "/api/del";

			var content = new Dictionary<string, string>
            {
                { "id", thingId},
                { "uh", _userState.ModHash}
            };

			await BasicPost(content, targetUri);
		}

        public async Task RemoveThing(string thingId, bool spam)
        {
			var targetUri = _networkLayer.RedditBaseUrl + "/api/remove";

            var content = new Dictionary<string, string>
            {
                { "id", thingId},
                { "uh", _userState.ModHash},
                { "spam", spam ? "true" : "false"}
            };

            await BasicPost(content, targetUri);
        }

        public async Task Friend(string name, string container, string note, string type)
        {
			var targetUri = _networkLayer.RedditBaseUrl + "/api/friend";

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
			var targetUri = _networkLayer.RedditBaseUrl + "/api/unfriend";

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
    }
}
