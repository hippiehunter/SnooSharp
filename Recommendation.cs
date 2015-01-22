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
    public class Recommendation
    {
        [JsonProperty("sr_name")]
        public string Subreddit {get; set;}
    }
}
