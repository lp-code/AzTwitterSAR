using Microsoft.Azure.Cosmos.Table;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using Tweetinvi.Models;

namespace AzTwitterSar.CheckTwitter
{
    public class AnalyzedTweetEntity : TableEntity
    {
        public AnalyzedTweetEntity(ITweet tweet, Tuple<float, float> scores)
        {
            PartitionKey = tweet.TweetLocalCreationDate.Year.ToString();
            RowKey = tweet.IdStr;
            this.FullText = tweet.FullText;
            this.CreatedAt = tweet.CreatedAt;
            this.Url = tweet.Url;
            this.Score = scores.Item1;
            this.ScoreML = scores.Item2;
        }

        public AnalyzedTweetEntity()
        { }

        public string FullText { get; set; }
        public DateTime CreatedAt { get; set; }
        public string Url { get; set; }
        public double Score { get; set; }
        public double ScoreML { get; set; }
    }

    class TweetLogger
    {
        private readonly CloudTable _cloudTable;
        private readonly ILogger _logger;

        public TweetLogger(ILogger logger)
        {
            _logger = logger;
            string storageAccountConnectionString = Environment.GetEnvironmentVariable("AzureWebJobsStorage");

            if (CloudStorageAccount.TryParse(storageAccountConnectionString, out CloudStorageAccount storageAccount))
            {
                // If the connection string is valid, proceed with operations against table
                // storage here.
                CloudTableClient cloudTableClient = storageAccount.CreateCloudTableClient(new TableClientConfiguration());

                _cloudTable = cloudTableClient.GetTableReference("TweetTable");
                _cloudTable.CreateIfNotExistsAsync().Wait(); // cannot use await in ctor
            }
            else
            {
                _logger.LogError("No valid connection string for the Tweet Logger " +
                    "in the environment variables.");
            }
        }

        public async Task LogTweet(ITweet tweet, Tuple<float, float> scores)
        {
            try
            {
                AnalyzedTweetEntity entity = new AnalyzedTweetEntity(tweet, scores);

                // Create the InsertOrReplace table operation
                TableOperation insertOrMergeOperation = TableOperation.InsertOrMerge(entity);

                // Execute the operation.
                TableResult result = await _cloudTable.ExecuteAsync(insertOrMergeOperation);
                _logger.LogInformation($"Saved tweet to table, return code: {result.HttpStatusCode.ToString()}");
            }
            catch (StorageException e)
            {
                _logger.LogError(e.Message);
                throw;
            }
        }
    }
}
