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
    public class More : IThingData
    {
		[JsonProperty("parent_id")]
		public string ParentId { get; set; }

		[JsonProperty("count")]
		public int Count { get; set; }

        [JsonProperty("children")]
        public List<string> Children { get; set; }
    }
}
