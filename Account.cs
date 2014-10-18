using SnooSharp.Converters;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SnooSharp
{
    public class Account : ThingData
    {
		[JsonProperty("gold_credits")]
		public int GoldCredits { get; set; }
        [JsonProperty("comment_karma")]
        public int CommentKarma { get; set; }
        [JsonConverter(typeof(UnixTimeConverter))]
        [JsonProperty("created")]
        public DateTime Created { get; set; }
        [JsonConverter(typeof(UnixUTCTimeConverter))]
        [JsonProperty("created_utc")]
        public DateTime CreatedUTC { get; set; }

		[JsonProperty("over_18", NullValueHandling = NullValueHandling.Ignore, DefaultValueHandling = DefaultValueHandling.Populate)]
		public bool Over18 { get; set; }

        [JsonProperty("has_mail", NullValueHandling = NullValueHandling.Ignore, DefaultValueHandling = DefaultValueHandling.Populate)]
        public bool HasMail { get; set; }
        [JsonProperty("has_mod_mail", NullValueHandling = NullValueHandling.Ignore, DefaultValueHandling = DefaultValueHandling.Populate)]
        public bool HasModMail { get; set; }
        [JsonProperty("is_gold", NullValueHandling = NullValueHandling.Ignore, DefaultValueHandling = DefaultValueHandling.Populate)]
        public bool IsGold { get; set; }
        [JsonProperty("is_mod", NullValueHandling = NullValueHandling.Ignore, DefaultValueHandling = DefaultValueHandling.Populate)]
        public bool IsMod { get; set; }
        [JsonProperty("link_karma", NullValueHandling = NullValueHandling.Ignore, DefaultValueHandling = DefaultValueHandling.Populate)]
        public int LinkKarma { get; set; }
        [JsonProperty("modhash")]
        public string ModHash { get; set; }
    }
}
