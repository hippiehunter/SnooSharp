using SnooSharp.Converters;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Serialization;
using System.Text;
using System.Threading.Tasks;

namespace SnooSharp
{
    [DataContract]
    public class Subreddit : ThingData, ICreated
    {
        public Subreddit() { }
        public Subreddit(string target)
        {
			var displayName = target.Split(new char[] { '/' }, StringSplitOptions.RemoveEmptyEntries).LastOrDefault();
            Description = "not loaded yet";
			DisplayName = string.IsNullOrWhiteSpace(displayName) ? "front page" : displayName;
            Over18 = false;
            Subscribers = 1;
            Title = DisplayName;
            Url = target;
            HeaderImage = "http://www.redditstatic.com/icon.png";
            PublicDescription = Description;
            Headertitle = DisplayName;
			if (target == "/")
				Id = "";
        }

        [JsonProperty("description")]
        public string Description { get; set; }
        [JsonProperty("display_name")]
        public string DisplayName { get; set; }
        [JsonProperty("over18")]
        public bool? Over18 { get; set; }
        [JsonProperty("subscribers", NullValueHandling=NullValueHandling.Ignore)]
        public long Subscribers { get; set; }
        [JsonProperty("user_is_subscriber", NullValueHandling = NullValueHandling.Ignore)]
        public bool Subscribed { get; set; }
        [JsonProperty("user_is_moderator", NullValueHandling = NullValueHandling.Ignore)]
        public bool Moderator { get; set; }
        [JsonProperty("title")]
        public string Title { get; set; }
        [JsonProperty("url")]
        public string Url { get; set; }
        [JsonConverter(typeof(UnixTimeConverter))]
        [JsonProperty("created")]
        public DateTime Created { get; set; }
        [JsonConverter(typeof(UnixUTCTimeConverter))]
        [JsonProperty("created_utc")]
        public DateTime CreatedUTC { get; set; }
        [JsonProperty("header_img")]
        public string HeaderImage { get; set; }
        [JsonProperty("header_size")]
        public int[] HeaderSize { get; set; }
        [JsonProperty("public_description")]
        public string PublicDescription { get; set; }
        [JsonProperty("header_title")]
        public string Headertitle { get; set; }
    }

    [DataContract]
    public class LabeledMulti : ThingData
    {
        [JsonProperty("path")]
        public string Path { get; set; }

        [JsonProperty("visibility")]
        public string Visibility { get; set; }

        [JsonProperty("can_edit")]
        public string CanEdit { get; set; }

        [JsonConverter(typeof(UnixUTCTimeConverter))]
        [JsonProperty("created")]
        public DateTime Created { get; set; }

        [JsonProperty("subreddits")]
        public List<LabeledMultiSubredditItem> Subreddits { get; set; }
    }

    [DataContract]
    public class LabeledMultiSubredditItem
    {
        [JsonProperty("name")]
        public string Name { get; set; }
    }

    [DataContract]
    public class LabeledMultiDescription : ThingData
    {
        [JsonProperty("body_md")]
        public string BodyMD { get; set; }
    }
}
