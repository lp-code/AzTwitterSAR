using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.DurableTask;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
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
            var parallelScoringTasks = new List<Task<TweetProcessingData>>();
            foreach (var tpd in tweetData) // processes in order
            {
                if (!context.IsReplaying)
                    log.LogDebug("Call sub-orchestration: P_ProcessTweet for tweet: ");
                Task<TweetProcessingData> processTask = context.CallSubOrchestratorAsync<TweetProcessingData>(
                    "P_ProcessTweet", tpd);
                parallelScoringTasks.Add(processTask);
            }
            await Task.WhenAll(parallelScoringTasks);

            // Sort the list of analyzed tweets by ascending id (chronologically).
            List<TweetProcessingData> logList = (from pt in parallelScoringTasks
                                                 orderby Int64.Parse(pt.Result.IdStr)
                                                 select pt.Result).ToList();

            // Find the tweets that shall be published, chronological order.
            List<TweetProcessingData> publishList = (
                from tpd in logList
                where (tpd.Label == 1 || (tpd.Label == 2 && tpd.VersionML is null))
                orderby Int64.Parse(tpd.IdStr)
                select tpd).ToList();
            {
                List<Task<int>> parallelPostprocessingTasks = new List<Task<int>>();
                if (publishList.Count > 0)
                {
                    parallelPostprocessingTasks.Add(context.CallActivityAsync<int>("A_PublishTweets", publishList));
                }
                if (logList.Count > 0)
                {
                    parallelPostprocessingTasks.Add(context.CallActivityAsync<int>("A_LogTweets", logList));
                }
                await Task.WhenAll(parallelPostprocessingTasks);
            }

            // If there have been any tweets then we update the last seen id, which is passed to the next call.
            if (logList.Count > 0)
                lastTweetId = logList[logList.Count - 1].IdStr;

            int delaySeconds = await context.CallActivityAsync<int>("A_GetDelaySeconds", context.CurrentUtcDateTime);
            if (delaySeconds > 0)
            {
                DateTime nextTime = context.CurrentUtcDateTime.AddSeconds(delaySeconds);
                await context.CreateTimer(nextTime, CancellationToken.None);
                context.ContinueAsNew(lastTweetId);
            }

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
            (tpd.Score, tpd.TextWithoutTagsHighlighted) = await
                context.CallActivityAsync<Tuple<float, string>>("A_GetBusinessLogicScore", tpd.TextWithoutTags);

            if (tpd.Score > 0)
            {
                // Getting environment variables is not permitted in an orchestrator.
                EnvVars envVars = await context.CallActivityAsync<EnvVars>("A_GetEnvVars", null);

                if (tpd.Score > envVars.MinScoreBL)
                {
                    log.LogInformation("Minimum score exceeded, query ML filter.");
                    tpd.Label = 2; // Since the main orchestrator does not have the minScore we have to indicate
                                   // whether the BL has indicated posting (in case ML is not working).
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
                    }
                    else
                    {
                        // Did not get a reply from the ML function, therefore
                        // we fall back and continue according to the traditional
                        // logic's result; indicated by tpd.VersionML is null.
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