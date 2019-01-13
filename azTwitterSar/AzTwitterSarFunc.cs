using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.Azure.WebJobs.Host;
using Newtonsoft.Json;

namespace AzTwitterSar
{
    public static class AzTwitterSarFunc
    {
        /// <summary>
        /// This variable contains all word (fragments) that we look for
        /// in the Twitter messages. Note that we are searching case-
        /// insensitively, and everything in this variable should be
        /// lowercase.
        /// </summary>
        public static string[] relevantStrings = new string[] {
                "savn", // -a, -et, -ede
                "sakn", // -a, -et
                "teaksjon", // le-, lei-
                "leite", "leting", "leter", "s�k",
                "redningsaksjon", "redning", "redningsoppdrag",
                "bekymringsmelding", "borte", "sist sett", // "sist sett" vil ikke matche enkeltord!
                "v�rfast",
                "helikopter", "chc", "luftamb",
                "scooter", "firehjul", "4-hjul",
                "hundepatrulje", "redningshund", "ekvipasje",
                "dement",
                "beskrivelse", "signalement", "kledd",
                "skred",
                "r�de",  "kors", "hjelpekorps", "hjelpemannskap",
                "alpin", "redningsgruppe",
                //"fjell", too many false positives, 20181223 
                "byfjell",
                "evakuer",
                "turg�er",
                "frivillige",
                "forsv", // forsvunnet, forsvant
                "funn",
                "behold", "behald",
                "rette", // "komme(t) til rette"
            };

        public static string[] irrelevantStrings = new string[] {
            "fors�k", "unders�k", "ans�k", "asyls�k", "bes�k", "s�knad",
            "opps�k", "s�ksm�l", "saks�k",   // s�k
            "borttatt",
            "spredning", "redningsarbeid", // redning
            "forsvar", // forsv
            "opprette", // rette
        };
        // discard completely: "Trolltunga"


        [FunctionName("ReceiveTweet")]
        public static async Task<HttpResponseMessage> Run(
            [HttpTrigger(AuthorizationLevel.Function, "get", "post", Route = null)]HttpRequestMessage req, 
            TraceWriter log)
        {
            log.Info("AzTwitterSarFunc.Run: enter.");

            // Get request body
            dynamic data = await req.Content.ReadAsAsync<object>();
            string TweetId = data?.tweetid;
            string CreatedAt = data?.createdat;
            string TweetText = data?.tweettext;

            if (TweetId == null || CreatedAt == null || TweetText == null)
                return req.CreateResponse(HttpStatusCode.BadRequest,
                    "Please pass all required parameters in the request body!");

            float minimumScore = GetScoreFromEnv("AZTWITTERSAR_MINSCORE", log, 0.01f);
            float minimumScoreAlert = GetScoreFromEnv("AZTWITTERSAR_MINSCORE_ALERT", log, 0.1f);

            float score = ScoreTweet(TweetText, out string highlightedText);
            int sendResult = 0;
            if (score > minimumScore)
            {
                log.Info("Minimum score exceeded, send message to Slack!");
                string CreatedAtLocalTime = ConvertUtcToLocal(CreatedAt);
                string slackMsg = "";
                if (score > minimumScoreAlert)
                    slackMsg += $"@channel\n";
                slackMsg +=
                    $"{highlightedText}\n"
                    + $"Score (v03): {score.ToString("F", CultureInfo.InvariantCulture)}\n"
                    + $"Link: http://twitter.com/politivest/status/{TweetId}";

                log.Info($"Message: {slackMsg}");
                sendResult = PostSlackMessage(log, slackMsg);
            }
            log.Info("AzTwitterSarFunc.Run: exit.");
            return (sendResult != 0)
                ? req.CreateResponse(HttpStatusCode.BadRequest,
                                     "Error sending message to slack.")
                : req.CreateResponse(HttpStatusCode.OK,
                                     "Message sent to slack: OK.");
        }

        private static float GetScoreFromEnv(string envVarName,
            TraceWriter log, float defaultScore)
        {
            float score = defaultScore;
            try
            {
                string min_score_string = Environment.GetEnvironmentVariable(envVarName);
                score = float.Parse(min_score_string);
                log.Info($"Got score from environment variable {envVarName}: "
                    + "{score}.");
            }
            catch
            {
                log.Info($"Getting score from environment variable {envVarName}"
                    + " failed, using default: {score}.");
            }
            return score;
        }

        /// <summary>
        /// Post the message msg to the slack channel implied by the webhook.
        /// </summary>
        /// <param name="log">Logger instance.</param>
        /// <param name="msg"> Message to be posted.</param>
        /// <returns>Status code: 0 = success.</returns>
        public static int PostSlackMessage(TraceWriter log, string msg)
        {
            log.Info("PostSlackMessage: enter.");

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
            
            log.Info("PostSlackMessage: response: " + result);
            log.Info("PostSlackMessage: exit.");
            return 0;
        }

        /// <summary>
        /// Return a score value in the interval [0;1] for the given text, and
        /// the input string with trigger words in Slack-highlighting.
        /// </summary>
        /// <param name="text">String to be scored.</param>
        /// <param name="highlightedText">Copy of the input with scored words
        ///                               highlighted.</param>
        /// <returns>Score value</returns>
        public static float ScoreTweet(string text, out string highlightedText)
        {
            string[] words = text.Split(null);
            highlightedText = "";

            int found = 0;
            foreach (string word in words)
            {
                string wordLower = word.ToLower();
                bool highlight = false;
                foreach (string relevantWord in relevantStrings)
                {
                    if (wordLower.Contains(relevantWord))
                    {
                        // When a word contains a desired word, we now
                        // check whether it is not one of the list of to-be-
                        // disregarded words.
                        bool matchIrrelevant = false;
                        foreach (string irrelevantWord in irrelevantStrings)
                            matchIrrelevant |= wordLower.Contains(irrelevantWord);
                        if (!matchIrrelevant)
                        {
                            highlight = true;
                            break;
                        }
                    }
                }
                if (highlight)
                {
                    found++;
                    highlightedText += " *" + word + "*";
                }
                else
                    highlightedText += " " + word;
            }
            highlightedText = highlightedText.Trim();
            return ((float)found) / Math.Min(relevantStrings.Length, 
                                             CountWordsInString(text));
        }

        /// <summary>
        /// Count the words in a given string. Very simple algorithm, but
        /// takes into account multiple whitespace caracters.
        /// </summary>
        /// <param name="text"></param>
        /// <returns></returns>
        public static int CountWordsInString(string text)
        {
            int wordCount = 0, index = 0;

            while (index < text.Length)
            {
                // check if current char is part of a word
                while (index < text.Length && !char.IsWhiteSpace(text[index]))
                    index++;

                wordCount++;

                // skip whitespace until next word
                while (index < text.Length && char.IsWhiteSpace(text[index]))
                    index++;
            }

            return wordCount;
        }

        /// <summary>
        /// Convert from a Twitter UTC time string to CE(S)T.
        /// </summary>
        /// <param name="dateIn">Tweet CreatedAt property.</param>
        /// <returns>Datetime string in local time.</returns>
        public static string ConvertUtcToLocal(string dateIn)
        {
            // Twitter's datetime format is "Fri Dec 14 23:47:57 + 0000 2018",
            // but the Azure Logic app's JSON output actually contains a 
            // different format, namely "2018-12-14T23:47:57.000Z".
            string dateNoMillisec = dateIn.Substring(0, 19);
            string res = "";
            try
            {
                DateTime dtUtc = DateTime.ParseExact(dateNoMillisec, "s",
                new System.Globalization.CultureInfo("en-US"));
                DateTime dtLoc = TimeZoneInfo.ConvertTimeFromUtc(dtUtc,
                    TimeZoneInfo.FindSystemTimeZoneById("Central Europe Standard Time"));
                res = dtLoc.ToString("yyyy-MM-dd HH:mm:ss");
            }
            catch
            {
                res = "Time conversion failed: " + dateIn;
            }
            return res;
        }
    }
}
