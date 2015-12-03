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
    public class Listing
    {
        [JsonProperty("kind")]
        public string Kind { get; set; }
        [JsonProperty("data")]
        public ListingData Data { get; set; }
        [JsonIgnore]
        public DateTime? DataAge { get; set; }
    }

    [DataContract]
    public class UserListing
    {
        [JsonProperty("kind")]
        public string Kind { get; set; }
        [JsonProperty("data")]
        public UserListingData Data { get; set; }
    }

    [DataContract]
    public class UserListingData
    {
        [JsonProperty("modhash")]
        public string ModHash { get; set; }

        [JsonProperty("children")]
        public List<UserListingItem> Children { get; set; }

        [JsonProperty("after")]
        public string After { get; set; }
        [JsonProperty("before")]
        public string Before { get; set; }
    }

    [DataContract]
    public class UserListingItem
    {
        [JsonProperty("name")]
        public string Name { get; set; }
    }

    [DataContract]
    public class ListingData
    {
        [JsonProperty("modhash")]
        public string ModHash { get; set; }

        [JsonProperty("children")]
        public List<Thing> Children { get; set; }

        [JsonProperty("after")]
        public string After { get; set; }
        [JsonProperty("before")]
        public string Before { get; set; }
    }
}
