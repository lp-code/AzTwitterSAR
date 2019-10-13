using System;
using System.Collections.Generic;
using System.Configuration;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Azure.Storage;
using Microsoft.Azure.Storage.Blob;
using Microsoft.Extensions.Logging;

namespace AzTwitterSar.CheckTwitter
{
    public class CheckpointManager
    {
        private const string containerName = "checkpointmanager";
        private const string filenameSuffix = ".checkpoint";
        private readonly CloudBlockBlob cloudBlockBlob;
        private readonly ILogger logger;
        private readonly bool hasBlobAccess;

        public CheckpointManager(string storageAccountConnectionString, string parameterName, ILogger logger)
        {
            this.logger = logger;
            // Check whether the connection string can be parsed.
            if (CloudStorageAccount.TryParse(storageAccountConnectionString, out CloudStorageAccount storageAccount))
            {
                // If the connection string is valid, proceed with operations against Blob
                // storage here.
                CloudBlobClient cloudBlobClient = storageAccount.CreateCloudBlobClient();

                // Create container. Name must be lower case.
                CloudBlobContainer cloudBlobContainer = cloudBlobClient.GetContainerReference(containerName.ToLower());
                cloudBlobContainer.CreateIfNotExistsAsync().Wait(); // cannot use await in ctor

                cloudBlockBlob = cloudBlobContainer.GetBlockBlobReference(parameterName + filenameSuffix);
                hasBlobAccess = true;
            }
            else
            {
                logger.LogError("No valid connection string for the checkpoint " +
                    "manager in the environment variables.");
                hasBlobAccess = false;
            }
        }

        public async Task<string> GetLastAsync()
        {
            string tweetId = "1"; // If there is no blob with the last seen tweet id then we return "1".
            if (hasBlobAccess)
            {
                try
                {
                    tweetId = await cloudBlockBlob.DownloadTextAsync();
                    logger.LogInformation($"Got last Tweet Id: {tweetId}.");
                }
                catch
                {
                    logger.LogInformation($"No blob with last Tweet Id found, using id: {tweetId}.");
                }
            }
            return tweetId;
        }

        public async Task UpdateAsync(string last)
        {
            if (hasBlobAccess)
            {
                logger.LogInformation($"Setting last Tweet Id to: {last}.");
                await cloudBlockBlob.UploadTextAsync(last);
            }
        }
    }
}
