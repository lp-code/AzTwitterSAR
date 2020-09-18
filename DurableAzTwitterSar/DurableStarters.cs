using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.DurableTask;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.Extensions.Logging;


// Important links:
// getInput gives null error: https://github.com/Azure/azure-functions-durable-extension/issues/1199


namespace DurableAzTwitterSar
{
    public static class DurableStarters
    {
        [FunctionName("S_HttpStart")]
        public static async Task<HttpResponseMessage> HttpStart(
            [HttpTrigger(AuthorizationLevel.Function, "get")] HttpRequestMessage req,
            [DurableClient] IDurableOrchestrationClient starter,
            ILogger log)
        {
            string lastTweetId = req.RequestUri.ParseQueryString()["lastTweetId"];

            if (lastTweetId == null)
            {
                return req.CreateResponse(HttpStatusCode.BadRequest, "Please pass lastTweetId in the query string.");
            }

            log.LogDebug($"Starting orchestration for lastTweetId: {lastTweetId}");

            // Function input comes from the request content.
            string orchestrationId = await starter.StartNewAsync<string>("O_MainOrchestrator", lastTweetId);

            return starter.CreateCheckStatusResponse(req, orchestrationId);
        }

        [FunctionName("S_HttpStartSingle")]
        public static async Task<HttpResponseMessage> HttpStartSingle(
            [HttpTrigger(AuthorizationLevel.Function, methods: "post", Route = "orchestrators/{functionName}/{instanceId}")] HttpRequestMessage req,
            [DurableClient] IDurableOrchestrationClient starter,
            string functionName,
            string instanceId,
            ILogger log)
        {
            // Check if an instance with the specified ID already exists.
            var existingInstance = await starter.GetStatusAsync(instanceId);
            if (existingInstance == null)
            {
                // An instance with the specified ID doesn't exist, create one.

                // Get the id of the last tweet treated previously, as specified in the http request.
                string lastTweetId = req.RequestUri.ParseQueryString()["lastTweetId"];
                if (lastTweetId == null)
                {
                    return req.CreateResponse(HttpStatusCode.BadRequest, "Please pass lastTweetId in the query string.");
                }
                
                await starter.StartNewAsync(functionName, instanceId, lastTweetId);
                log.LogInformation($"Started orchestration with ID = '{instanceId}'.");
                return starter.CreateCheckStatusResponse(req, instanceId);
            }
            else
            {
                // An instance with the specified ID exists, don't create one.
                return new HttpResponseMessage(HttpStatusCode.Conflict)
                {
                    Content = new StringContent($"An instance with ID '{instanceId}' already exists."),
                };
            }
        }
    }
}
