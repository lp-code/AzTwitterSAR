using Microsoft.Azure.Cosmos.Table;
using Microsoft.Azure.WebJobs;
using Microsoft.Extensions.Logging;
using Microsoft.Azure.WebJobs.Extensions.DurableTask;
using System;
using System.Linq;
using Tweetinvi;
using Tweetinvi.Parameters;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Text;
using System.Net.Http;
using Newtonsoft.Json;
using System.Net;
using System.Globalization;

namespace DurablePoc
{
    public static class DurablePocActivities
    {
        [FunctionName("A_GetTweets")]
        public static async Task<List<TweetProcessingData>> GetTweets([ActivityTrigger] string lastTweetId, ILogger log)
        {
            log.LogInformation($"A_GetTweets: Getting new tweets after {lastTweetId}.");
            string apiKey = Environment.GetEnvironmentVariable("TwitterApiKey"); // aka consumer key
            string apiSecretKey = Environment.GetEnvironmentVariable("TwitterApiSecretKey"); // aka consumer secret
            string accessToken = Environment.GetEnvironmentVariable("TwitterAccessToken");
            string accessTokenSecret = Environment.GetEnvironmentVariable("TwitterAccessTokenSecret");
            string monitoredTwitterAccount = Environment.GetEnvironmentVariable("MonitoredTwitterAccount");

            var userCredentials = Auth.SetUserCredentials(apiKey, apiSecretKey, accessToken, accessTokenSecret);
            var authenticatedUser = User.GetAuthenticatedUser(userCredentials);

            // Note: The following does NOT get MaximumNumberOfResults tweets
            //       from after lastTweetId!!! Rather it gets the most recent
            //       tweets with the early limit defined by lastTweetId OR the
            //       defined maximum, whichever is more limiting!
            //       (Therefore, in order to test on past tweets, one may need
            //       to increase MaximumNumberOfResults considerably to get ALL
            //       tweets from the one targeted to the current one.
            SearchTweetsParameters searchParameter = new SearchTweetsParameters($"from:{monitoredTwitterAccount}")
            {
                MaximumNumberOfResults = 10,
                SinceId = long.Parse(lastTweetId)
            };

            // Since the further processing can scramble the order again, we don't need to sort here.
            var tweets = await SearchAsync.SearchTweets(searchParameter);

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

                tpd.TextWithoutTags = TweetAnalysis.removeHashtagsFromText(tweet.FullText, tweet.Hashtags);

                tpds.Add(tpd);
            }
            log.LogInformation($"A_GetTweets: Done, got {tweets.Count()} new tweets.");

            return tpds;
        }

        [FunctionName("A_GetBusinessLogicScore")]
        public static Tuple<float, PublishLabel, string> GetBusinessLogicScore([ActivityTrigger] string textWithoutTags, ILogger log)
        {
            log.LogInformation("A_GetBusinessLogicScore: Start.");
            string highlightedText;
            float score = TweetAnalysis.ScoreTweet(textWithoutTags, out highlightedText);
            float minScoreBL = TweetAnalysis.GetScoreFromEnv("AZTWITTERSAR_MINSCORE", log, 0.01f);
            
            PublishLabel label = PublishLabel.Negative;
            if (score > minScoreBL)
                label = PublishLabel.Positive;

            log.LogInformation("A_GetBusinessLogicScore: Done.");

            return new Tuple<float, PublishLabel, string>(score, label, highlightedText);
        }

        [FunctionName("A_GetMlScore")]
        public static async Task<MlResult> GetMlScore([ActivityTrigger] string tweet, ILogger log)
        {
            log.LogInformation("A_GetMlScore: Start.");
            string mlUriString = Environment.GetEnvironmentVariable("AZTWITTERSAR_AI_URI");

            MlResult result = new MlResult
            {
                Score = 0,
                Label = PublishLabel.NotAssigned,
                MlVersion = ""
            };

            if (mlUriString is null)
            {
                log.LogError($"ML-inference link not configured.");
            }
            else
            {
                var payload = JsonConvert.SerializeObject(new { tweet = tweet });
                var httpContent = new StringContent(payload, Encoding.UTF8, "application/json");

                HttpClient httpClient = new HttpClient();
                httpClient.Timeout = TimeSpan.FromSeconds(30);
                Uri mlFuncUri = new Uri(mlUriString);

                log.LogInformation($"Calling ML-inference.");
                HttpResponseMessage httpResponseMsg = await httpClient.PostAsync(mlFuncUri, httpContent);

                if (httpResponseMsg.StatusCode == HttpStatusCode.OK
                    && httpResponseMsg.Content != null)
                {
                    var responseContent = await httpResponseMsg.Content.ReadAsStringAsync();
                    ResponseData ml_result = JsonConvert.DeserializeObject<ResponseData>(responseContent);

                    result.Score = ml_result.Score;
                    result.Label = ml_result.Label == 1 ? PublishLabel.Positive : PublishLabel.Negative;
                    result.MlVersion = ml_result.Version;
                }
            }
            log.LogInformation("A_GetMlScore: Done.");

            return result;
        }

        [FunctionName("A_PublishTweets")]
        public static async Task<int> PublishTweets([ActivityTrigger] List<TweetProcessingData> tpds, ILogger log)
        {
            log.LogInformation($"A_PublishTweets: Publishing {tpds.Count} tweets.");

            float minScoreBLAlert = TweetAnalysis.GetScoreFromEnv("AZTWITTERSAR_MINSCORE_ALERT", log, 0.1f);
            foreach (var tpd in tpds)
            {
                string slackMsg = "";
                if (tpd.Score > minScoreBLAlert)
                    slackMsg += $"@channel\n";
                slackMsg +=
                    $"{tpd.FullText}\n"
                    + $"Score (v3.0): {tpd.Score.ToString("F", CultureInfo.InvariantCulture)}, "
                    + $"ML ({tpd.VersionML}): {tpd.ScoreML.ToString("F", CultureInfo.InvariantCulture)}\n"
                    + $"Link: http://twitter.com/politivest/status/{tpd.IdStr}";

                log.LogInformation($"Message: {slackMsg}");
                int sendResult = SlackClient.PostSlackMessage(log, slackMsg);
                log.LogInformation($"Message posted to slack, result: {sendResult}");
            }

            log.LogInformation($"A_PublishTweets: Done.");

            return 0;
        }

        [FunctionName("A_GetDelaySeconds")]
        public static int GetDelaySeconds([ActivityTrigger] DateTime startTime, ILogger log)
        {
            log.LogInformation("A_GetDelaySeconds: Start.");
            DateTime currentTime = DateTime.UtcNow;

            const int targetSecondsBetweenRuns = 60;
            const int minimumSecondsBetweenRuns = 30;


            bool envVarSet = Int32.TryParse(Environment.GetEnvironmentVariable("AZTWITTERSAR_ACTIVE"), out int envVarValue);
            bool active = envVarSet && (envVarValue == 1);

            int delaySeconds = 0;
            if (active)
            {
                delaySeconds = Math.Max(
                    targetSecondsBetweenRuns - (int)(currentTime - startTime).TotalSeconds,
                    minimumSecondsBetweenRuns);
            }
            log.LogInformation($"A_GetDelaySeconds: Done, determined delay is {delaySeconds} seconds.");

            return delaySeconds;
        }

        [FunctionName("A_LogTweets")]
        public static async Task<int> LogTweets([ActivityTrigger] List<TweetProcessingData> tpds, ILogger log)
        {
            log.LogInformation($"A_LogTweets: Start logging tweets to table storage.");

            // The data structure and content is quite different from the previous
            // version's, so the storing is reimplemented here.
            string storageAccountConnectionString = Environment.GetEnvironmentVariable("AzureWebJobsStorage");

            CloudTable cloudTable = null;

            if (CloudStorageAccount.TryParse(storageAccountConnectionString, out CloudStorageAccount storageAccount))
            {
                // If the connection string is valid, proceed with operations against table
                // storage here.
                CloudTableClient cloudTableClient = storageAccount.CreateCloudTableClient(new TableClientConfiguration());

                cloudTable = cloudTableClient.GetTableReference("TweetTable2");
                await cloudTable.CreateIfNotExistsAsync();
            }
            else
            {
                log.LogError("No valid connection string for the LogTweets activity " +
                    "in the environment variables.");
            }

            if (cloudTable != null)
            {
                try
                {
                    foreach (TweetProcessingData tpd in tpds)
                    {
                        // Create the InsertOrReplace table operation
                        TableOperation insertOrMergeOperation = TableOperation.InsertOrMerge(tpd);

                        // Execute the operation.
                        TableResult result = await cloudTable.ExecuteAsync(insertOrMergeOperation);
                        log.LogInformation($"Saved tweet to table, return code: {result.HttpStatusCode.ToString()}");
                    }
                }
                catch (Exception e)
                {
                    log.LogError($"Saving tweet to table failed: {e.Message}");
                    throw;
                }
            }

            log.LogInformation($"A_LogTweets: Done.");
            
            return 0;
        }
    }
}
