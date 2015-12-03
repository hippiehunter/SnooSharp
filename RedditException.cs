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

    public class RedditDisallowedException : Exception
    {
        public RedditDisallowedException(string what)
            : base(string.Format("{0} was not disallowed", what))
        {

        }
    }

    public class RedditUnauthorizedException : Exception
    {
        public RedditUnauthorizedException(string what)
            : base(string.Format("access to {0} was not unauthorized", what))
        {

        }
    }

    public class RedditEmptyException : Exception
    {
        public RedditEmptyException(string what)
            : base(string.Format("{0} was empty", what))
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
