SnooSharp
=========

C# Portable Class Library for interacting with the Reddit API

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
