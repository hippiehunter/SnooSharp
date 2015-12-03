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
    public class Link : ThingData, ICreated, IVotable
    {
        [JsonConverter(typeof(UnixTimeConverter))]
        [JsonProperty("created")]
        public DateTime Created { get; set; }
        [JsonConverter(typeof(UnixUTCTimeConverter))]
        [JsonProperty("created_utc")]

        public DateTime CreatedUTC { get; set; }
        [JsonProperty("author")]
        public string Author { get; set; }
        [JsonProperty("author_flair_css_class")]
        public string AuthorFlairCssClass { get; set; }
		[JsonProperty("link_flair_css_class")]
		public string LinkFlairCssClass { get; set; }
		[JsonProperty("link_flair_text")]
		public string LinkFlairText { get; set; }
        [JsonProperty("author_flair_text")]
        public string AuthorFlairText { get; set; }
        [JsonProperty("clicked")]
        public bool Clicked { get; set; }
        [JsonProperty("domain")]
        public string Domain { get; set; }
        [JsonProperty("hidden")]
        public bool Hidden { get; set; }
        [JsonProperty("is_self")]
        public bool IsSelf { get; set; }
        [JsonProperty("media")]
        public object Media { get; set; }
        [JsonProperty("media_embed")]
        public MediaEmbed MediaEmbed { get; set; }
        [JsonProperty("preview")]
        public Preview Preview { get; set; }
        [JsonProperty("num_comments")]
        public int CommentCount { get; set; }
        [JsonProperty("over_18")]
        public bool Over18 { get; set; }
        [JsonProperty("permalink")]
        public string Permalink { get; set; }
        [JsonProperty("saved")]
        public bool Saved { get; set; }
        [JsonProperty("score")]
        public int Score { get; set; }
        [JsonProperty("selftext")]
        public string Selftext { get; set; }
        [JsonProperty("selftext_html")]
        public string SelftextHtml { get; set; }
        [JsonProperty("subreddit")]
        public string Subreddit { get; set; }
        [JsonProperty("subreddit_id")]
        public string SubredditId { get; set; }
        [JsonProperty("thumbnail")]
        public string Thumbnail { get; set; }
        [JsonProperty("title")]
        public string Title { get; set; }
        [JsonProperty("url")]
        public string Url { get; set; }

        [JsonProperty("ups")]
        public int Ups { get; set; }
        [JsonProperty("downs")]
        public int Downs { get; set; }
        [JsonProperty("likes")]
        public bool? Likes { get; set; }
        [JsonProperty("visited")]
        public bool? Visited { get; set; }

		[JsonProperty("stickied")]
		public bool? Stickied { get; set; }
		[JsonProperty("edited")]
		public bool? Edited { get; set; }
		[JsonProperty("approved_by")]
		public string ApprovedBy { get; set; }
		[JsonProperty("score_hidden")]
		public bool? ScoreHidden { get; set; }
		[JsonProperty("user_reports")]
		public List<string[]> UserReports { get; set; }
		[JsonProperty("mod_reports")]
		public List<string[]> ModReports { get; set; }
		[JsonProperty("num_reports")]
		public int? NumberOfReports { get; set; }
    }

    public class MediaEmbed
    {
        [JsonProperty("content")]
        public string Content { get; set; }
        [JsonProperty("width")]
        public int Width { get; set; }
        [JsonProperty("height")]
        public int Height { get; set; }
    }

    public class PreviewSource
    {
        public string url { get; set; }
        public int width { get; set; }
        public int height { get; set; }
    }

    public class PreviewResolution
    {
        public string url { get; set; }
        public int width { get; set; }
        public int height { get; set; }
    }

    public class PreviewVariants
    {
    }

    public class PreviewImage
    {
        public PreviewSource source { get; set; }
        public List<PreviewResolution> resolutions { get; set; }
        public PreviewVariants variants { get; set; }
        public string id { get; set; }
    }

    public class Preview
    {
        public List<PreviewImage> images { get; set; }
    }
}
