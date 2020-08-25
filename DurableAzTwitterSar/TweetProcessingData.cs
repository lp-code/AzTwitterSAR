using Microsoft.Azure.Cosmos.Table;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Text;

namespace DurableAzTwitterSar
{
    public enum PublishLabel
    {
        NotAssigned,
        Negative,
        Positive
    }

    // This class can both be JSON-serialized for passing into/out of activities,
    // and it is a TableEntity, so it can be written to Azure Table storage.
    public class TweetProcessingData : TableEntity
    {
        private string _idStr;
        private DateTime _createdAt;

        public TweetProcessingData()
        {}

        public bool ShallBePublished
        {
            get
            {
                return (this.LabelML == (int) PublishLabel.Positive ||
                    (this.LabelML == (int) PublishLabel.NotAssigned && this.LabelBL == (int) PublishLabel.Positive));
            }
        }

        // Tweetinvi ITweet data members.
        [JsonProperty("idStr")]
        public string IdStr
        {
            get => _idStr;
            set
            {
                _idStr = value;
                base.RowKey = _idStr;
            }
        }

        [JsonProperty("createdAt")]
        public DateTime CreatedAt
        {
            get => _createdAt;
            set
            {
                _createdAt = value;
                base.PartitionKey = CreatedAt.Year.ToString();
            }
        }

        [JsonProperty("fullText")]
        public string FullText { get; set; }

        [JsonProperty("hashtags")]
        public string Hashtags { get; set; }

        [JsonProperty("inReplyToStatusIdStr")]
        public string InReplyToStatusIdStr  { get; set; }

        [JsonProperty("url")]
        public string Url{ get; set; }


        // Derived data members.
        [JsonProperty("textWithoutTags")]
        public string TextWithoutTags { get; set; }

        [JsonProperty("textWithoutTagsHighlighted")]
        public string TextWithoutTagsHighlighted { get; set; }

        // The labels have to be int here as enums do not autoconvert when writing to Azure table.
        [JsonProperty("labelBL")]
        public int LabelBL { get; set; } = (int) PublishLabel.NotAssigned;

        [JsonProperty("labelML")]
        public int LabelML { get; set; } = (int) PublishLabel.NotAssigned;

        [JsonProperty("score")]
        public double Score { get; set; }

        [JsonProperty("scoreML")]
        public double ScoreML { get; set; }

        [JsonProperty("versionBL")]
        public string VersionBL { get; set; }

        [JsonProperty("versionML")]
        public string VersionML { get; set; }
    }

    public class MlResult
    {
        public double Score;
        public PublishLabel Label;
        public string MlVersion;
    }
}
