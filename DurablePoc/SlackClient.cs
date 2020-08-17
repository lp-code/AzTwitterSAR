using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Text;

namespace DurablePoc
{
    class SlackClient
    {
        /// <summary>
        /// Post the message msg to the slack channel implied by the webhook.
        /// </summary>
        /// <param name="log">Logger instance.</param>
        /// <param name="msg"> Message to be posted.</param>
        /// <returns>Status code: 0 = success.</returns>
        public static int PostSlackMessage(ILogger log, string msg)
        {
            log.LogInformation("PostSlackMessage: enter.");

            var slackWebHook = Environment.GetEnvironmentVariable(
                "AZTWITTERSAR_SLACKHOOK");
            HttpWebRequest httpWebRequest =
                (HttpWebRequest)WebRequest.Create(slackWebHook);
            httpWebRequest.ContentType = "application/json";
            httpWebRequest.Method = "POST";

            using (var streamWriter = new StreamWriter(httpWebRequest.GetRequestStream()))
            {
                /* Setting the property link_names is required for the channel
                 * alert to work. Alternatively (not tried), see 
                 * https://discuss.newrelic.com/t/sending-alerts-to-slack-with-channel-notification/35921/3 */
                var values = new Dictionary<string, string>
                { { "text", $"{msg}" }, { "link_names", "1"} };
                string json = JsonConvert.SerializeObject(values);

                streamWriter.Write(json);
            }

            var httpResponse = (HttpWebResponse)httpWebRequest.GetResponse();
            string result;
            using (var streamReader = new StreamReader(httpResponse.GetResponseStream()))
            {
                result = streamReader.ReadToEnd();
            }

            log.LogInformation("PostSlackMessage: response: " + result);
            log.LogInformation("PostSlackMessage: exit.");
            return 0;
        }
    }
}
