using Newtonsoft.Json;
using SnooSharp.Converters;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SnooSharp
{
	public class RedditOAuth
	{
		[JsonProperty("access_token")]
		public string AccessToken {get; set;}
		[JsonProperty("token_type")]
		public string TokenType { get; set; }
		[JsonProperty("expires_in")]
		public int ExpiresIn { get; set; }
		[JsonConverter(typeof(UnixTimeConverter))]
		[JsonProperty("created")]
		public DateTime Created { get; set; }
		[JsonProperty("scope")]
		public string Scope { get; set; }
		[JsonProperty("refresh_token")]
		public string RefreshToken { get; set; }
	}
}
