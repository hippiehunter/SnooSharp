using SnooSharp.Converters;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SnooSharp
{
    class ModAction : ThingData
    {
        [JsonProperty("description")]
        public string Description { get; set; }

        [JsonProperty("mod_id36")]
        public string ModId36 { get; set; }

        [JsonConverter(typeof(UnixUTCTimeConverter))]
        [JsonProperty("created_utc")]
        public DateTime CreatedUTC { get; set; }

        [JsonProperty("subreddit")]
        public string Subreddit { get; set; }

        [JsonProperty("sr_id36")]
        public string SRId36 { get; set; }

        [JsonProperty("details")]
        public string Details { get; set; }

        [JsonProperty("action")]
        public string Action { get; set; }

        [JsonProperty("target_fullname")]
        public string TargetFullname { get; set; }

        [JsonProperty("mod")]
        public string Mod { get; set; }
    }
}
