using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using System;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;

namespace DurableAzTwitterSar
{
    class SlackClient
    {
        /// <summary>
        /// Post the message msg to the slack channel implied by the webhook.
        /// </summary>
        /// <param name="log">Logger instance.</param>
        /// <param name="msg"> Message to be posted.</param>
        /// <returns>Status code: 0 = success.</returns>
        public static async Task<int> PostSlackMessageAsync(ILogger log, string msg)
        {
            log.LogInformation("PostSlackMessageAsync: enter.");

            var slackWebHook = await KeyVaultAccessor.GetInstance().GetSecretAsync("AzTwitterSarSlackHook");

            HttpClient httpClient = new HttpClient();
            httpClient.Timeout = TimeSpan.FromSeconds(30);
            Uri mlFuncUri = new Uri(slackWebHook);

            /* Setting the property link_names is required for the channel
             * alert to work. Alternatively (not tried), see
             * https://discuss.newrelic.com/t/sending-alerts-to-slack-with-channel-notification/35921/3 */
            var payload = JsonConvert.SerializeObject(new { text = $"{msg}", link_names = "1" });
            var httpContent = new StringContent(payload, Encoding.UTF8, "application/json");

            HttpResponseMessage httpResponseMsg = await httpClient.PostAsync(mlFuncUri, httpContent);

            if (httpResponseMsg.StatusCode == HttpStatusCode.OK
                && httpResponseMsg.Content != null)
            {
                var result = await httpResponseMsg.Content.ReadAsStringAsync();
                log.LogInformation("PostSlackMessageAsync: response: " + result);
            }
            else
            {
                log.LogInformation($"PostSlackMessageAsync: posting to slack failed, response code: {httpResponseMsg.StatusCode}.");
            }
            log.LogInformation("PostSlackMessageAsync: exit.");
            return 0;
        }
    }
}
