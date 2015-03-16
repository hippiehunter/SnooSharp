using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SnooSharp
{
    public class UserState
    {
        public string Username { get; set; }
        public string LoginCookie {get; set;}
		public RedditOAuth OAuth { get; set; }
        public string ModHash { get; set; }
        public bool IsGold { get; set; }
		public bool IsMod { get; set; }
        public bool NeedsCaptcha { get; set; }
		public bool IsDefault { get; set; }
    }
}
