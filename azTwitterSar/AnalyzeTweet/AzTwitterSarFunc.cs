using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using Tweetinvi.Models;

namespace AzTwitterSar.ProcessTweets
{
    public class ResponseData
    {
        [JsonProperty("tags")]
        public List<string> Tags { get; set; }

        [JsonProperty("text")]
        public string Text { get; set; }

        [JsonProperty("label")]
        public int Label { get; set; }

        [JsonProperty("score")]
        public float Score { get; set; }

        [JsonProperty("original")]
        public string Original { get; set; }

        [JsonProperty("version")]
        public string Version { get; set; }
    }

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
                "leite", "leting", "leter", "søk",
                "redningsaksjon", "redning", "redningsoppdrag",
                "bekymringsmelding", "borte", "sist sett", // "sist sett" vil ikke matche enkeltord!
                "værfast",
                "helikopter", "chc", "luftamb",
                "snøscooter", "firehjul", "4-hjul", // "scooter" gives too many false positives
                "hundepatrulje", "redningshund", "ekvipasje",
                "dement",
                "beskrivelse", "signalement", "kledd",
                "skred",
                "røde",  "kors", "hjelpekorps", "hjelpemannskap",
                "alpin", "redningsgruppe",
                //"fjell", too many false positives, 20181223 
                "byfjell",
                "evakuer",
                "turgåer",
                "frivillige",
                "forsv", // forsvunnet, forsvant
                "funn",
                "behold", "behald",
                "rette", // "komme(t) til rette"
                "iført",
                // "observ", // try this when the AI negative filter is active
            };

        public static string[] irrelevantStrings = new string[] {
            "forsøk", "undersøk", "ansøk", "asylsøk", "besøk", "søknad",
            "oppsøk", "søksmål", "saksøk",   // søk
            "borttatt",
            "spredning", "redningsarbeid", // redning
            "forsvar", // forsv
            "opprette", // rette
            "deretter", // rette
            "rettelse", // rette
            "korskirke",
            "rossleitet", // leite
            "korsrygg",
        };
        // discard completely: "Trolltunga"

        public static string[] blacklistStrings = new string[]
        {
            "narkoti", "hasj", "røyk", "tørrkoking", "brann", "innbrudd",
            "gjernings", "tyve", "pålegg",
        };

        
        public static async Task<float> ScoreAndPostTweet(ITweet tweet, HttpClient httpClient, ILogger log)
        {
            log.LogInformation("AzTwitterSarFunc.Run: enter.");

            // Get request tweet info.
            string TweetId = tweet.IdStr;
            string TweetText = tweet.FullText;

            float minimumScore = GetScoreFromEnv("AZTWITTERSAR_MINSCORE", log, 0.01f);
            float minimumScoreAlert = GetScoreFromEnv("AZTWITTERSAR_MINSCORE_ALERT", log, 0.1f);

            float score = ScoreTweet(TweetText, out string highlightedText);
            
            if (score > minimumScore)
            {
                log.LogInformation("Minimum score exceeded, query ML filter.");

                Uri ml_func_uri = new Uri(Environment.GetEnvironmentVariable("AZTWITTERSAR_AI_URI"));
                var payload = JsonConvert.SerializeObject(new { tweet = TweetText });
                var httpContent = new StringContent(payload, Encoding.UTF8, "application/json");
                ResponseData ml_result;
                float ml_score = -1.0F;
                string ml_version = "";

                log.LogInformation("Calling ML-inference.");
                HttpResponseMessage httpResponseMsg = await httpClient.PostAsync(ml_func_uri, httpContent);

                if (httpResponseMsg.StatusCode == HttpStatusCode.OK && httpResponseMsg.Content != null)
                {
                    var responseContent = await httpResponseMsg.Content.ReadAsStringAsync();

                    ml_result = JsonConvert.DeserializeObject<ResponseData>(responseContent);
                    if (ml_result != null)
                    {
                        ml_score = ml_result.Score;
                        ml_version = ml_result.Version;

                        log.LogInformation(
                            $"ML-inference OK, label: {ml_result.Label}, "
                            + $"score: {ml_score.ToString("F", CultureInfo.InvariantCulture)}, "
                            + $"version: {ml_version}");
                        if (ml_result.Label == 0)
                        {
                            // When the ML filter says "no" then we return without posting to Slack.
                            // To mark this type of result, we return the negative of the "manual" score.
                            return -score;
                        }
                    }
                }
                else
                {
                    // We did not get a reply from the ML function, therefore
                    // we fall back and continue according to the traditional
                    // logic's result.
                    log.LogInformation("ML inference failed or did not reply, rely on conventional logic.");
                }
                // @todo The ML result has more than the label, should use e.g. the geographical tags, too.
                string slackMsg = "";
                if (score > minimumScoreAlert)
                    slackMsg += $"@channel\n";
                slackMsg +=
                    $"{highlightedText}\n"
                    + $"Score (v2.0): {score.ToString("F", CultureInfo.InvariantCulture)}, "
                    + $"ML ({ml_version}): {ml_score.ToString("F", CultureInfo.InvariantCulture)}\n"
                    + $"Link: http://twitter.com/politivest/status/{TweetId}";

                log.LogInformation($"Message: {slackMsg}");
                int sendResult = PostSlackMessage(log, slackMsg);
                log.LogInformation($"Message posted to slack, result: {sendResult}");
            }
            log.LogInformation("AzTwitterSarFunc.Run: exit.");
            return score;
        }

        private static float GetScoreFromEnv(string envVarName,
            ILogger log, float defaultScore)
        {
            float score = defaultScore;
            try
            {
                string min_score_string = Environment.GetEnvironmentVariable(envVarName);
                score = float.Parse(min_score_string);
                log.LogInformation($"Got score from environment variable {envVarName}: "
                    + "{score}.");
            }
            catch
            {
                log.LogInformation($"Getting score from environment variable {envVarName}"
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
            float score = ((float)found) / Math.Min(relevantStrings.Length,
                                             CountWordsInString(text));

            // Last step: blacklisting; if any of the words in blacklistStrings
            // occurs in the tweet then its score will be set to zero whatever
            // it was before.
            string textLower = text.ToLower();
            foreach (string blacklistWord in blacklistStrings)
            {
                if (textLower.Contains(blacklistWord))
                {
                    score = 0;
                }
            }

            return score;
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
            string res;
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
