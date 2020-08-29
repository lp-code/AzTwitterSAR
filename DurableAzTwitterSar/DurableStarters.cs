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
            //string orchestrationId = await starter.StartNewAsync("O_MainOrchestrator", lastTweetId);
            string orchestrationId = await starter.StartNewAsync<string>("O_MainOrchestrator", lastTweetId);


            return starter.CreateCheckStatusResponse(req, orchestrationId);
        }
    }
}
