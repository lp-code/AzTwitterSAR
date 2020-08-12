using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Text;

namespace DurablePoc
{
    public class TweetProcessingData
    {
        public TweetProcessingData()
        {

        }
        // Tweetinvi ITweet data members.
        [JsonProperty("idStr")]
        public string IdStr { get; set; }

        [JsonProperty("createdAt")]
        public DateTime CreatedAt { get; set; }

        [JsonProperty("fullText")]
        public string FullText { get; set; }

        [JsonProperty("hashtags")]
        public List<string> Hashtags { get; set; }

        [JsonProperty("inReplyToStatusIdStr")]
        public string InReplyToStatusIdStr  { get; set; }

        [JsonProperty("url")]
        public string Url{ get; set; }


        // Derived data members.
        [JsonProperty("textWithoutTags")]
        public string TextWithoutTags { get; set; }

        [JsonProperty("textWithoutTagsHighlighted")]
        public string TextWithoutTagsHighlighted { get; set; }

        [JsonProperty("label")]
        public int? Label { get; set; }

        [JsonProperty("score")]
        public float Score { get; set; }

        [JsonProperty("scoreML")]
        public float ScoreML { get; set; }

        [JsonProperty("versionBL")]
        public string VersionBL { get; set; }

        [JsonProperty("versionML")]
        public string VersionML { get; set; }
    }

}
