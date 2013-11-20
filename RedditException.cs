using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace SnooSharp
{
    public class RedditException : Exception
    {
        public RedditException(string message) : base(message)
        {

        }
    }

    public class RedditNotFoundException : Exception
    {
        public RedditNotFoundException(string what)
            : base(string.Format("{0} was not found", what))
        {

        }
    }
}
