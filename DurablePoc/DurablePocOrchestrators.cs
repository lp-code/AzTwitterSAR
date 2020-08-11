using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.DurableTask;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;

namespace DurablePoc
{


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
                    "P_ProcessTweet", tpd);
                parallelTasks.Add(processTask);
            }
            await Task.WhenAll(parallelTasks);

            // Find the tweets that shall be published and order them chronologically.
            List<TweetProcessingData> publishList = (from pt in parallelTasks
                                                     where pt.Result.Label == 1
                                                     orderby Int64.Parse(pt.Result.IdStr)
                                                     select pt.Result).ToList();

            if (publishList.Count > 0)
            {
                int res = await context.CallActivityAsync<int>("A_PublishTweets", publishList);
            }
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
            TweetProcessingData tpd = context.GetInput<TweetProcessingData>();

            if (!context.IsReplaying)
                log.LogDebug("Call A_GetBusinessLogicScore");

            // The following function is not async, so it could be called directly rather than through the activity model.
            (tpd.ScoreBL, tpd.TextWithoutTagsHighlighted) = await
                context.CallActivityAsync<Tuple<float, string>>("A_GetBusinessLogicScore", tpd.TextWithoutTags);

            if (tpd.ScoreBL > 0)
            {
                // Getting environment variables is not permitted in an orchestrator.
                EnvVars envVars = await context.CallActivityAsync<EnvVars>("A_GetEnvVars", null);
                float ml_score = -1.0F;
                if (tpd.ScoreBL > envVars.MinScoreBL)
                {
                    log.LogInformation("Minimum score exceeded, query ML filter.");

                    if (!(envVars.MlUriString is null))
                    {
                        (tpd.ScoreML, tpd.Label, tpd.VersionML) = await context.CallActivityAsync<Tuple<float, int, string>>("A_GetMlScore", tpd.TextWithoutTags);
                    }
                    else
                    {
                        log.LogInformation($"ML-inference link not configured.");
                    }

                    if (!(tpd.VersionML is null))
                    { 
                        log.LogInformation(
                            $"ML-inference OK, label: {tpd.Label}, "
                            + $"score: {tpd.ScoreML.ToString("F", CultureInfo.InvariantCulture)}, "
                            + $"version: {tpd.VersionML}");
                        if (tpd.Label == 0)
                        {
                            // When the ML filter says "no" then we return without posting to Slack.
                            // To mark this type of result, we return the negative of the "manual" score.
                            tpd.ScoreBL = -tpd.ScoreBL;
                        }
                    }
                    else
                    {
                        // We did not get a reply from the ML function, therefore
                        // we fall back and continue according to the traditional
                        // logic's result.
                        log.LogInformation("ML inference failed or did not reply, rely on conventional logic.");
                    }
                } // if (tpd.ScoreBL > envVars.MinScoreBL)
            } // if (tpd.ScoreBL > 0)

            if (!context.IsReplaying)
                log.LogDebug("Call A_GetBusinessLogicScore");

            return tpd;
        } // func
    }
}