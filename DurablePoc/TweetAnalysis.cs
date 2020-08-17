using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Text;
using Tweetinvi.Models.Entities;

namespace DurablePoc
{
    public static class TweetAnalysis
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
            // "evakuer", rarely relevant, and then there are always other trigger words
            "turgåer",
            "frivillige",
            "forsv", // forsvunnet, forsvant
            "funn",
            "behold", "behald",
            "rette", // "komme(t) til rette"
            "iført",
            // "observ", // try this when the AI negative filter is active
            "hår", // in many descriptions
            "skårfast",
            "turfølge",
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
            "paradisleitet",
            "korsrygg",
            "kyskskreda",
            "skredestranda",
            "kveldsfesteskredo",
            "skredhaugen",
        };
        // discard completely: "Trolltunga"

        public static string[] blacklistStrings = new string[]
        {
            "narkoti", "hasj",
            "røyk", "tørrkoking", "brann ", "brenn", // brannvesen er OK!!!
            "innbrudd", "gjernings", "tyve", "pålegg",
            "håra", // place name in Hardanger
            "trafikkulykke", "trafikkuhell",
            "bevæpnet",
            "kanin",
            "stjål",
        };

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

        public static string removeHashtagsFromText(string FullText, List<IHashtagEntity> Hashtags)
        {
            StringBuilder sb = new StringBuilder(FullText);
            // Remove right to left.
            for (int i = Hashtags.Count - 1; i >= 0; i--)
            {
                sb.Remove(Hashtags[i].Indices[0], Hashtags[i].Indices[1] - Hashtags[i].Indices[0]);
            }
            return sb.ToString();
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

        public static float GetScoreFromEnv(string envVarName, ILogger log, float defaultScore)
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
    }
}
