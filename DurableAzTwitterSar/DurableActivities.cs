using Microsoft.Azure.Cosmos.Table;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.DurableTask;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using Tweetinvi;
using Tweetinvi.Models;
using Tweetinvi.Parameters;

namespace DurableAzTwitterSar
{
    public static class DurableActivities
    {
        [FunctionName("A_GetTweets")]
        public static async Task<List<TweetProcessingData>> GetTweets([ActivityTrigger] string lastTweetId, ILogger log)
        {
            log.LogInformation($"A_GetTweets: Getting new tweets after {lastTweetId}.");
            KeyVaultAccessor kva = KeyVaultAccessor.GetInstance();
            string apiKey = await kva.GetSecretAsync("TwitterApiKey"); // aka consumer key
            string apiSecretKey = await kva.GetSecretAsync("TwitterApiSecretKey"); // aka consumer secret
            string accessToken = await kva.GetSecretAsync("TwitterAccessToken");
            string accessTokenSecret = await kva.GetSecretAsync("TwitterAccessTokenSecret");

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
                MaximumNumberOfResults = 15,
                SinceId = long.Parse(lastTweetId)
            };

            IEnumerable<ITweet> tweets = null;
            try
            {
                tweets = await SearchAsync.SearchTweets(searchParameter);
            }
            catch (Exception e)
            {
                // Inserted try-catch after a seemingly intermittent exception that lead to
                // the service stopping completely, 20201213, ca. 7 am.
                log.LogWarning($"A_GetTweets: SearchTweets failed with exception: {e.Message}");
            }
            if (tweets is null)
            {
                log.LogWarning($"A_GetTweets: Twitter connection failure. Return no tweets and retry in next cycle.");
                tweets = new List<ITweet>();
            }
            // Since the further processing can scramble the order again, we don't need to sort here.

            List<TweetProcessingData> tpds = new List<TweetProcessingData>();
            foreach (var tweet in tweets)
            {
                // Copy the data that we need over to a serializable struct.
                TweetProcessingData tpd = new TweetProcessingData();
                tpd.IdStr = tweet.IdStr;
                tpd.CreatedAt = tweet.CreatedAt;
                tpd.FullText = tweet.FullText;
                
                tpd.Hashtags = String.Join("|", tweet.Hashtags.Select(t => t.Text));

                tpd.InReplyToStatusIdStr = tweet.InReplyToStatusIdStr;
                tpd.Url = tweet.Url;

                tpd.TextWithoutTags = TweetAnalysis.removeHashtagsFromText(tweet.FullText, tweet.Hashtags);

                tpds.Add(tpd);
            }
            log.LogInformation($"A_GetTweets: Done, got {tweets.Count()} new tweets.");

            return tpds;
        }

        [FunctionName("A_GetBusinessLogicScore")]
        public static Tuple<double, PublishLabel, string> GetBusinessLogicScore([ActivityTrigger] string textWithoutTags, ILogger log)
        {
            log.LogInformation("A_GetBusinessLogicScore: Start.");
            string highlightedText;
            double score = TweetAnalysis.ScoreTweet(textWithoutTags,
                                                    out highlightedText);
            double minScoreBL = TweetAnalysis.GetScoreFromEnv("AZTWITTERSAR_MINSCORE", log, 0.01f);
            
            PublishLabel label = PublishLabel.Negative;
            if (score > minScoreBL)
                label = PublishLabel.Positive;

            log.LogInformation("A_GetBusinessLogicScore: Done.");

            return new Tuple<double, PublishLabel, string>(score, label, highlightedText);
        }

        [FunctionName("A_GetMlScore")]
        public static async Task<MlResult> GetMlScore([ActivityTrigger] string tweet, ILogger log)
        {
            log.LogInformation("A_GetMlScore: Start.");
            string mlUriString = await KeyVaultAccessor.GetInstance().GetSecretAsync("AzTwitterSarAiUri");

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

            double minScoreBLAlert = TweetAnalysis.GetScoreFromEnv("AZTWITTERSAR_MINSCORE_ALERT", log, 0.1f);
            foreach (var tpd in tpds)
            {
                string slackMsg = "";
                if (tpd.Score > minScoreBLAlert)
                    slackMsg += $"@channel\n";
                slackMsg +=
                    $"{tpd.FullText}\n"
                    + $"Score (v{AzTwitterSarVersion.get()}): {tpd.Score.ToString("F", CultureInfo.InvariantCulture)}, "
                    + $"ML ({tpd.VersionML}): {tpd.ScoreML.ToString("F", CultureInfo.InvariantCulture)}\n"
                    + $"Link: http://twitter.com/politivest/status/{tpd.IdStr}";

                log.LogInformation($"Message: {slackMsg}");
                int sendResult = await SlackClient.PostSlackMessageAsync(log, slackMsg);
                log.LogInformation($"Message posted to slack, result: {sendResult}");
            }

            log.LogInformation($"A_PublishTweets: Done.");

            return 0;
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
