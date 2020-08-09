using Microsoft.Azure.WebJobs;
using Microsoft.Extensions.Logging;
using Microsoft.Azure.WebJobs.Extensions.DurableTask;
using System;
using System.Linq;
using Tweetinvi;
using Tweetinvi.Models;
using Tweetinvi.Parameters;
using Dynamitey.DynamicObjects;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Text;
using Tweetinvi.Models.Entities;

namespace DurablePoc
{
    public static class DurablePocActivities
    {
        private static string removeHashtagsFromText(string FullText, List<IHashtagEntity> Hashtags)
        {
            StringBuilder sb = new StringBuilder(FullText);
            // Remove right to left.
            for (int i = Hashtags.Count-1; i >= 0; i--)
            {
                sb.Remove(Hashtags[i].Indices[0], Hashtags[i].Indices[1] - Hashtags[i].Indices[0]);
            }
            return sb.ToString();
        }

        [FunctionName("A_GetTweets")]
        public static async Task<List<TweetProcessingData>> GetTweets([ActivityTrigger] string lastTweetId, ILogger log)
        {
            log.LogInformation($"Getting new tweets after {lastTweetId}.");
            string apiKey = Environment.GetEnvironmentVariable("TwitterApiKey"); // aka consumer key
            string apiSecretKey = Environment.GetEnvironmentVariable("TwitterApiSecretKey"); // aka consumer secret
            string accessToken = Environment.GetEnvironmentVariable("TwitterAccessToken");
            string accessTokenSecret = Environment.GetEnvironmentVariable("TwitterAccessTokenSecret");

            var userCredentials = Auth.SetUserCredentials(apiKey, apiSecretKey, accessToken, accessTokenSecret);
            var authenticatedUser = User.GetAuthenticatedUser(userCredentials);

            // Note: The following does NOT get MaximumNumberOfResults tweets
            //       from after lastTweetId!!! Rather it gets the most recent
            //       tweets with the early limit defined by lastTweetId OR the
            //       defined maximum, whichever is more limiting!
            //       (Therefore, in order to test on past tweets, one may need
            //       to increase MaximumNumberOfResults considerably to get ALL
            //       tweets from the one targeted to the current one.
            SearchTweetsParameters searchParameter = new SearchTweetsParameters("from:politivest")
            {
                MaximumNumberOfResults = 10,
                SinceId = long.Parse(lastTweetId)
            };

            // Since the further processing can scramble the order again, we don't need to sort here.
            var tweets = Search.SearchTweets(searchParameter);
            log.LogInformation($"Got {tweets.Count()} new tweets. Convert to TweetProcessingData.");

            List<TweetProcessingData> tpds = new List<TweetProcessingData>();
            foreach (var tweet in tweets)
            {
                // Copy the data that we need over to a serializable struct.
                TweetProcessingData tpd = new TweetProcessingData();
                tpd.IdStr = tweet.IdStr;
                tpd.CreatedAt = tweet.CreatedAt;
                tpd.FullText = tweet.FullText;
                
                tpd.Hashtags = new List<string>();
                tweet.Hashtags.ForEach(t => tpd.Hashtags.Add(t.Text));

                tpd.InReplyToStatusIdStr = tweet.InReplyToStatusIdStr;
                tpd.Url = tweet.Url;

                tpd.TextWithoutTags = removeHashtagsFromText(tweet.FullText, tweet.Hashtags);

                tpds.Add(tpd);
            }

            return tpds;
        }

        //[FunctionName("A_GetBusinessLogicScore")]
        //public static string GetBusinessLogicScore([ActivityTrigger] string name, ILogger log)
        //{
        //    log.LogInformation($"Saying hello to {name}.");
        //    return $"Hello {name}!";
        //}

        //[FunctionName("A_GetAiScore")]
        //public static string GetAiScore([ActivityTrigger] string name, ILogger log)
        //{
        //    log.LogInformation($"Saying hello to {name}.");
        //    return $"Hello {name}!";
        //}

        //[FunctionName("A_GetGeoLocation")]
        //public static string GetGeoLocation([ActivityTrigger] string name, ILogger log)
        //{
        //    log.LogInformation($"Saying hello to {name}.");
        //    return $"Hello {name}!";
        //}

        //[FunctionName("A_LogToTable")]
        //public static string LogToTable([ActivityTrigger] string name, ILogger log)
        //{
        //    log.LogInformation($"Saying hello to {name}.");
        //    return $"Hello {name}!";
        //}

        //[FunctionName("A_PostAlert")]
        //public static string PostAlert([ActivityTrigger] string name, ILogger log)
        //{
        //    log.LogInformation($"Saying hello to {name}.");
        //    return $"Hello {name}!";
        //}

    }
}
