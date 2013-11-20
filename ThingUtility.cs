using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SnooSharp
{
    public class ThingUtility
    {
        public static HashSet<string> HashifyListing(IEnumerable<Thing> listing)
        {
            if (listing == null)
                return null;

            var hashifyListing = new Func<Thing, string>((thing) =>
            {
                if (thing.Data is Subreddit)
                {
                    return ((Subreddit)thing.Data).Name;
                }
                else
                    return null;
            });

            return new HashSet<string>(listing.Select(hashifyListing)
                    .Where(str => str != null));
        }

        public static Thing GetFrontPageThing()
        {
            Thing frontPage = new Thing();
            frontPage.Data = new Subreddit
            {
                DisplayName = "front page",
                Url = "/",
                Name = "/",
                Id = "/",
                Subscribers = 5678123,
                HeaderImage = "/Assets/reddit.png",
                PublicDescription = "The front page of this device."
            };
            frontPage.Kind = "t5";
            return frontPage;
        }

        public static Thing GetOfflinePageThing()
        {
            Thing frontPage = new Thing();
            frontPage.Data = new Subreddit
            {
                DisplayName = "offline content",
                Url = "offline content",
                Name = "offline content",
                Id = "offline content",
                Subscribers = 1,
                HeaderImage = "/Assets/reddit.png",
                PublicDescription = "Offline content stored on this device."
            };
            frontPage.Kind = "t5";
            return frontPage;
        }
    }
}
