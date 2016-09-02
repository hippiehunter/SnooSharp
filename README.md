SnooSharp
=========

C# Portable Class Library for interacting with the Reddit API

# License
Copyright (c) 2012 Synergex International Corporation

Permission is hereby granted, free of charge, to any person obtaining a copy of this software and associated documentation files (the "Software"), to deal in  the Software without restriction, including without limitation the rights to use, copy, modify, merge, publish, distribute, sublicense, and/or sell copies of the Software, and to permit persons to whom the Software is furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE SOFTWARE.

Example
=========

    using SnooSharp;
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Text;
    using System.Threading;
    using System.Threading.Tasks;
    
    namespace Example
    {
    	class Program
    	{
    		static void Main(string[] args)
    		{
    			//normally not needed just a way to get async stuff working in a simple console app
    			Task.Run((Func<Task>)DoStuff).Wait();
    		}
    		static async Task DoStuff()
    		{
    			var reddit = new SnooSharp.Reddit(new ListingFilter(), new UserState(),
    				new DefferalSink(), new CaptchaProvider());
    
    			var listing = await reddit.GetPostsBySubreddit("/r/wpdev");
    			foreach (var post in listing.Data.Children)
    			{
    				if (post.Data is Link)
    				{
    					Console.WriteLine(((Link)post.Data).Title);
    				}
    			}
    		}
    	}
    
    	//this is where you implement things like filtering out nsfw items
    	class ListingFilter : IListingFilter
    	{
    		public Task<Listing> Filter(Listing listing)
    		{
    			return Task.FromResult(listing);
    		}
    	}
    
    	//implement this to pop up a UI and solve the captcha
    	class CaptchaProvider : ICaptchaProvider
    	{
    		public Task<string> GetCaptchaResponse(string captchaIden)
    		{
    			throw new NotImplementedException();
    		}
    	}
    
    	//implement this so you can store off requests to be completed later if you have bad internet
    	class DefferalSink : IActionDeferralSink
    	{
    		public void Defer(Dictionary<string, string> arguments, string action)
    		{
    			throw new NotImplementedException();
    		}
    
    		public Tuple<Dictionary<string, string>, string> DequeDeferral()
    		{
    			throw new NotImplementedException();
    		}
    	}
    }
