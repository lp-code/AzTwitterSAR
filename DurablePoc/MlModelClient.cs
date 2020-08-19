using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Text;

namespace DurableAzTwitterSar
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

    class MlModelClient
    {
    }
}
