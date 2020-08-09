using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.DurableTask;
//using Microsoft.Build.Framework;
using Microsoft.Extensions.Logging;
using Tweetinvi;
using Tweetinvi.Models;

namespace DurablePoc
{
    public class TweetAndLogger
    {
        public TweetProcessingData tpd { get; set; }

        public ILogger log { get; set; }
    }

    public static class DurablePocOrchestrators
    {
        [FunctionName("O_MainOrchestrator")]
        public static async Task<string> MainOrchestrator(
            [OrchestrationTrigger] IDurableOrchestrationContext context,
            ILogger log)
        {
            string lastTweetId = context.GetInput<string>();

            if (!context.IsReplaying)
                log.LogDebug("Call activity: GetTweets");

            // 1) Get tweets from Twitter API
            List<TweetProcessingData> tweetData = await context.CallActivityAsync<List<TweetProcessingData>>("A_GetTweets", lastTweetId);

            // 2) Process to find interesting ones.
            var parallelTasks = new List<Task<TweetProcessingData>>();
            foreach (var tpd in tweetData) // processes in order
            {
                if (!context.IsReplaying)
                    log.LogDebug("Call sub-orchestration: P_ProcessTweet for tweet: ");
                Task<TweetProcessingData> processTask = context.CallSubOrchestratorAsync<TweetProcessingData>(
                    "P_ProcessTweet", new TweetAndLogger { tpd = tpd, log = log });
                parallelTasks.Add(processTask);
            }
            await Task.WhenAll(parallelTasks);

            //.OrderBy(tweet => tweet.Id);

            // 3) Log tweets to table storage and send to output those that were selected.

            // need to order the output from the loop above according to tweet id

            //await Task.WhenAll(parallelTasks);

            lastTweetId = "xxx";
            return lastTweetId;
        }

        [FunctionName("P_ProcessTweet")]
        public static async Task<TweetProcessingData> ProcessTweet(
            [OrchestrationTrigger] IDurableOrchestrationContext context,
            ILogger log)
        {
            ITweet tweet = context.GetInput<ITweet>();

            if (!context.IsReplaying)
                log.LogDebug("About to call A_SplitGeoAndMessage");

            Tuple<List<string>, string> geoAndText = await
                context.CallActivityAsync<Tuple<List<string>, string>>("A_SplitTagsAndMessage", tweet);

            return new TweetProcessingData();
        }
    }
}