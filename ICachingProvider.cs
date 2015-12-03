using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SnooSharp
{
    public interface ICachingProvider
    {
        Task<Listing> GetListing(string url);
        Task<Thing> GetThingById(string id);

        Task SetListing(string url, Listing listing);
        Task SetThing(Thing thing);
    }
}
