using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.DurableTask;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace DurableAzTwitterSar
{
    public static class DurableOrchestrators
    {
        [FunctionName("O_MainOrchestrator")]
        public static async Task<string> MainOrchestrator(
            [OrchestrationTrigger] IDurableOrchestrationContext context,
            ILogger log)
        {
            DateTime startTime = context.CurrentUtcDateTime;
            if (!context.IsReplaying)
                log.LogInformation($"Main orchestrator (v{AzTwitterSarVersion.get()}), start time {startTime}. Call activity: GetTweets");

            string lastTweetId = context.GetInput<string>();

            // 1) Get tweets from Twitter API
            List<TweetProcessingData> tweetData = await context.CallActivityAsync<List<TweetProcessingData>>(
                "A_GetTweets", lastTweetId);

            // The most likely case is that there are NO new tweets, so we provide
            // the short circuit which skips all the processing in this block, and
            // thus avoids a lot of replaying by the durable function mechanism.
            if (tweetData.Count > 0)
            {
                if (!context.IsReplaying)
                    log.LogInformation($"Got {tweetData.Count} new tweets; enter processing sub-orchestration.");

                // 2) Process tweets one by one to find interesting ones.
                var parallelScoringTasks = new List<Task<TweetProcessingData>>();
                foreach (var tpd in tweetData) // processes in order
                {
                    if (!context.IsReplaying)
                        log.LogInformation($"Call sub-orchestration: P_ProcessTweet for tweet: {tpd.IdStr}");
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
                    where tpd.ShallBePublished
                    orderby Int64.Parse(tpd.IdStr)
                    select tpd).ToList();

                // Parallel section for postprocessing tasks.
                {
                    if (!context.IsReplaying)
                        log.LogInformation($"Publishing {publishList.Count} tweets; logging {logList.Count} tweets.");

                    List<Task<int>> parallelPostprocessingTasks = new List<Task<int>>();
                    // We know there is something in the log list, but publishing we
                    // trigger only if there is something to do for this activity.
                    if (publishList.Count > 0)
                    {
                        parallelPostprocessingTasks.Add(context.CallActivityAsync<int>("A_PublishTweets", publishList));
                    }
                    parallelPostprocessingTasks.Add(context.CallActivityAsync<int>("A_LogTweets", logList));
                    await Task.WhenAll(parallelPostprocessingTasks);
                }

                // We know there has been >= 1 tweet, so we update the last seen id,
                // which is passed to the next call.
                lastTweetId = logList[logList.Count - 1].IdStr;
            }
            else
            {
                if (!context.IsReplaying)
                    log.LogInformation($"Got no new tweets.");
            }

            DateTime currentTime = context.CurrentUtcDateTime;
            int delaySeconds = GetDelaySeconds(context, log, startTime, currentTime);

            if (!context.IsReplaying)
                log.LogInformation($"Determined delay: {delaySeconds} seconds after current time {currentTime}.");

            if (delaySeconds > 0)
            {
                DateTime nextTime = currentTime.AddSeconds(delaySeconds);
                if (!context.IsReplaying)
                    log.LogInformation($"Setting wakeup time: {nextTime}.");
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
                log.LogInformation("P_ProcessTweet call A_GetBusinessLogicScore");

            // The following function is not async, so it could be called directly rather than through the activity model.
            PublishLabel tmpLabelBL = PublishLabel.NotAssigned;
            (tpd.Score, tmpLabelBL, tpd.TextWithoutTagsHighlighted) = await
                context.CallActivityAsync<Tuple<double, PublishLabel, string>>("A_GetBusinessLogicScore", tpd.TextWithoutTags);
            tpd.LabelBL = (int)tmpLabelBL;

            if (tpd.LabelBL != (int) PublishLabel.Negative)
            {
                log.LogInformation("Minimum BL score exceeded, query ML filter.");

                var mlResult = await context.CallActivityAsync<MlResult>("A_GetMlScore", tpd.TextWithoutTags);
                tpd.ScoreML = mlResult.Score;
                tpd.LabelML = (int)mlResult.Label;
                tpd.VersionML = mlResult.MlVersion;

                if (!(tpd.VersionML is null))
                { 
                    log.LogInformation(
                        $"ML-inference OK, label: {tpd.LabelML}, "
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
            }

            if (!context.IsReplaying)
                log.LogInformation("Completed  O_ProcessTweet.");

            return tpd;
        } // func

        private static int GetDelaySeconds(
            IDurableOrchestrationContext context, ILogger log,
            DateTime startTime, DateTime currentTime)
        {
            if (!context.IsReplaying)
                log.LogInformation("GetDelaySeconds: Start.");

            int runtimeSeconds = (int) (currentTime - startTime).TotalSeconds;

            // There is a bug in the durable function sleep/schedule routine so that it
            // restarts later than scheduled:
            // https://github.com/Azure/azure-functions-durable-extension/issues/1395
            int targetSecondsBetweenRuns = 45;  // This results in ca. one minute.
            int hr = currentTime.ToLocalTime().Hour;
            if (hr >= 1 && hr <= 6)
                targetSecondsBetweenRuns = 180;
            const int minimumSecondsBetweenRuns = 30;

            bool envVarSet = Int32.TryParse(Environment.GetEnvironmentVariable("AZTWITTERSAR_ACTIVE"), out int envVarValue);
            bool active = envVarSet && (envVarValue == 1);

            int delaySeconds = 0;
            if (active)
            {
                delaySeconds = Math.Max(
                    targetSecondsBetweenRuns - runtimeSeconds,
                    minimumSecondsBetweenRuns);
            }
            if (!context.IsReplaying)
                log.LogInformation($"GetDelaySeconds: Done, determined delay is {delaySeconds} seconds.");

            return delaySeconds;
        }


    }
}