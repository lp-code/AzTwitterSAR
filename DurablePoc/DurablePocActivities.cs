# nullable enable
using AzTwitterSar.ProcessTweets;
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
using Tweetinvi.Models.Entities;
using System.Net.Http;
using Newtonsoft.Json;
using System.Net;
using System.Globalization;

namespace DurablePoc
{
    public class EnvVars
    {
        public float MinScoreBL { get; set; }
        public float MinScoreBLAlert { get; set; }
        public string MlUriString { get; set; }
        public string SlackWebHook { get; set; }
    }


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

        [FunctionName("A_GetBusinessLogicScore")]
        public static Tuple<float, string> GetBusinessLogicScore([ActivityTrigger] string textWithoutTags, ILogger log)
        {
            log.LogInformation($"Getting BusinessLogicScore.");
            string highlightedText;
            float score = AzTwitterSarFunc.ScoreTweet(textWithoutTags, out highlightedText);
            return new Tuple<float, string>(score, highlightedText);
        }

        [FunctionName("A_GetEnvVars")]
        public static EnvVars GetEnvVars([ActivityTrigger] string nothing, ILogger log)
        {
            log.LogInformation($"Getting environment variable values.");
            return new EnvVars
            {
                MinScoreBL = AzTwitterSarFunc.GetScoreFromEnv("AZTWITTERSAR_MINSCORE", log, 0.01f),
                MinScoreBLAlert = AzTwitterSarFunc.GetScoreFromEnv("AZTWITTERSAR_MINSCORE_ALERT", log, 0.1f),
                
                SlackWebHook = Environment.GetEnvironmentVariable("AZTWITTERSAR_SLACKHOOK")
            };
        }


        [FunctionName("A_GetMlScore")]
        public static async Task<Tuple<float, int, string>> GetMlScore([ActivityTrigger] string tweet, ILogger log)
        {
            log.LogInformation($"Getting ML score.");
            string mlUriString = Environment.GetEnvironmentVariable("AZTWITTERSAR_AI_URI");
            
            var payload = JsonConvert.SerializeObject(new { tweet = tweet });
            var httpContent = new StringContent(payload, Encoding.UTF8, "application/json");

            HttpClient httpClient = new HttpClient();
            httpClient.Timeout = TimeSpan.FromSeconds(30);
            Uri mlFuncUri = new Uri(mlUriString);

            log.LogInformation($"Calling ML-inference at {mlFuncUri.ToString()}.");
            HttpResponseMessage httpResponseMsg = await httpClient.PostAsync(mlFuncUri, httpContent);

            if (!(mlUriString is null)
                && httpResponseMsg.StatusCode == HttpStatusCode.OK
                && httpResponseMsg.Content != null)
            {
                var responseContent = await httpResponseMsg.Content.ReadAsStringAsync();
                ResponseData ml_result = JsonConvert.DeserializeObject<ResponseData>(responseContent);

                return new Tuple<float, int, string>(ml_result.Score, ml_result.Label, ml_result.Version);
            }
            return new Tuple<float, int, string>(0, 0, null);
        }

        [FunctionName("A_PublishTweets")]
        public static async Task<int> PublishTweets([ActivityTrigger] List<TweetProcessingData> tpds, ILogger log)
        {
            log.LogInformation($"Publishing {tpds.Count} tweets.");

            float minScoreBLAlert = AzTwitterSarFunc.GetScoreFromEnv("AZTWITTERSAR_MINSCORE_ALERT", log, 0.1f);
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
                int sendResult = AzTwitterSarFunc.PostSlackMessage(log, slackMsg);
                log.LogInformation($"Message posted to slack, result: {sendResult}");
            }
            log.LogInformation($"Finished publishing tweets.");
            return 0;
        }

        [FunctionName("A_GetDelaySeconds")]
        public static int GetDelaySeconds([ActivityTrigger] DateTime now, ILogger log)
        {
            bool active = Int32.Parse(Environment.GetEnvironmentVariable("AZTWITTERSAR_ACTIVE")) == 1;
            if (active)
                return 60;
            else
                return 0;
        }

        [FunctionName("A_LogTweets")]
        public static async Task<int> LogTweets([ActivityTrigger] List<TweetProcessingData> tpds, ILogger log)
        {
            log.LogInformation($"Logging tweets to table storage.");
            // The data structure and content is quite different from the previous
            // version's, so the storing is reimplemented here.
            string storageAccountConnectionString = Environment.GetEnvironmentVariable("AzureWebJobsStorage");

            CloudTable? cloudTable = null;

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
            return 0;
        }
    }
}
