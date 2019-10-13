using AzTwitterSar.ProcessTweets;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs;
using Microsoft.Extensions.Logging;
using Tweetinvi;
using Tweetinvi.Models;
using Tweetinvi.Parameters;

namespace AzTwitterSar.CheckTwitter
{
    public static class GetNewTweets
    {
        // Helper function that gets a list of those tweets that are new since the last processed tweet.
        public static async Task<IOrderedEnumerable<ITweet>> GetTweetsSinceLastProcessed(ILogger log)
        {
            string apiKey = Environment.GetEnvironmentVariable("TwitterApiKey"); // aka consumer key
            string apiSecretKey = Environment.GetEnvironmentVariable("TwitterApiSecretKey"); // aka consumer secret
            string accessToken = Environment.GetEnvironmentVariable("TwitterAccessToken");
            string accessTokenSecret = Environment.GetEnvironmentVariable("TwitterAccessTokenSecret");

            var userCredentials = Auth.SetUserCredentials(apiKey, apiSecretKey, accessToken, accessTokenSecret);
            var authenticatedUser = User.GetAuthenticatedUser(userCredentials);

            // For keeping track of the last gotten Tweet, we use a CheckpointManager.
            string storageAccountConnectionString = Environment.GetEnvironmentVariable("AzureWebJobsStorage");
            CheckpointManager checkpointManager = new CheckpointManager(storageAccountConnectionString, "LastSeenTweet", log);

            string lastTweetId = await checkpointManager.GetLastAsync();

            var searchParameter = new SearchTweetsParameters("from:politivest")
            {
                MaximumNumberOfResults = 10,
                SinceId = long.Parse(lastTweetId)
            };

            var tweets = Search.SearchTweets(searchParameter).OrderBy(tweet => tweet.Id);
            var nrOfTweets = tweets.Count();
            log.LogInformation($"Got {nrOfTweets} new tweets.");
            if (nrOfTweets > 0)
            {
                await checkpointManager.UpdateAsync(tweets.Last().Id.ToString());
            }
            return tweets;
        }

        [FunctionName("GetNewTweets")]
        public static async Task Run([TimerTrigger("0 * * * * *")]TimerInfo myTimer, ILogger log)
        {
            log.LogInformation($"C# Timer trigger function executed at: {DateTime.Now}");

            var tweets = await GetNewTweets.GetTweetsSinceLastProcessed(log);

            if (tweets != null && tweets.Any())
            {
                // Prepare for logging all tweets to table storage.
                TweetLogger tweetLogger = new TweetLogger(log);

                foreach (var tweet in tweets)
                {
                    float score = await AzTwitterSarFunc.ScoreAndPostTweet(tweet, log);
                    await tweetLogger.LogTweet(tweet, score);
                }
            }
            else
            {
                log.LogInformation("No new tweets.");
            }
        }
    }
}
