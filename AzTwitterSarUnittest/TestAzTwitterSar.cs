using System;
using System.Collections.Generic;
using DurableAzTwitterSar;
using Newtonsoft.Json;
using Xunit;

namespace AzTwitterSarTests
{
    public class UnitTestAzTwitterSar
    {
        [Fact]
        public void Test_CountWordsInString1()
        {
            int res = TweetAnalysis.CountWordsInString(
                "#Bergen, Danmarksplass: Politiet har gjennomført farts"
                + "kontroll. 9 forenklede forelegg, 2 førerkortbeslag, høyeste"
                + " fart var 112 km/t i 50-sonen.");

            Assert.Equal(18, res);
        }

        [Fact]
        public void Test_CountWordsInString2()
        {
            int res = TweetAnalysis.CountWordsInString(
                "#Espeland Bergen: Mann i 40 årene er savnet fra bopel det er"
                + " iverksatt leteaksjon. Mannskap fra Røde Kors Norske "
                + "Redningshunder samt Norsk Luftambulanse deltar foreløpig i"
                + " søket.");
            Assert.Equal(27, res);
        }

        [Fact]
        public void Test_CountWordsInString3()
        {
            int res = TweetAnalysis.CountWordsInString(
                "Saknet person i skred, politiet undersøker. Forsøk forsøkt.");
            Assert.Equal(8, res);
        }

        [Fact]
        public void Test_ScoreTweet1()
        {
            float res = TweetAnalysis.ScoreTweet(
                "#Bergen, Danmarksplass: Politiet har gjennomført farts"
                + "kontroll. 9 forenklede forelegg, 2 førerkortbeslag, høyeste"
                + " fart var 112 km/t i 50-sonen.", out string _);

            Assert.Equal(0, res);
        }

        [Fact]
        public void Test_ScoreTweet2()
        {
            //string highlightedText = "";
            float res = TweetAnalysis.ScoreTweet(
                "#Espeland Bergen: Mann i 40 årene er savnet fra bopel det er"
                + " iverksatt leteaksjon. Mannskap fra Røde Kors Norske "
                + "Redningshunder samt Norsk Luftambulanse deltar foreløpig i"
                + " søket.", out string highlightedText);

            // The tweet has 27 words, but here the minimum in the denominator
            // in the score function selects the number of trigger words.
            float expectedScore = (float)7 / 27;
            Assert.Equal(expectedScore, res, 3);

            Assert.Equal(
                "#Espeland Bergen: Mann i 40 årene er *savnet* fra bopel det er"
                + " iverksatt *leteaksjon.* Mannskap fra *Røde* *Kors* Norske "
                + "*Redningshunder* samt Norsk *Luftambulanse* deltar foreløpig i"
                + " *søket.*", highlightedText);
        }

        [Fact]
        public void Test_ScoreTweet3()
        {
            string txt =
                "#Kaupanger Politiet har leitet etter en mann i 30-årene siden"
                + " kl 04 i natt etter melding om beruset person som framsto "
                + "ute av stand til å ivareta seg selv. Lokalt politi fått "
                + "bistand fra Røde Kors og Norske redningshunder. Vedkommende "
                + "funnet ca kl 0930 i god behold.";
            float res = TweetAnalysis.ScoreTweet(txt, out string highlightedText);

            float expectedScore = (float)6 / Math.Min(
                TweetAnalysis.relevantStrings.Length,
                txt.Split().Length);
            Assert.Equal(expectedScore, res, 3);

            Assert.Equal(
                "#Kaupanger Politiet har *leitet* etter en mann i 30-årene siden"
                + " kl 04 i natt etter melding om beruset person som framsto "
                + "ute av stand til å ivareta seg selv. Lokalt politi fått "
                + "bistand fra *Røde* *Kors* og Norske *redningshunder.* Vedkommende"
                + " *funnet* ca kl 0930 i god *behold.*", highlightedText);
        }

        [Fact]
        public void Test_ScoreTweet4()
        {
            string originalText =
                "#Bergen, Nordre Toppe: Ordensforstyrrelse, ruset og aggressiv"
                + " mann, i forbindelse med innbringelsen, forsøkte han å "
                + "skalle til en politibetjent, samt en politibetjent ble "
                + "spyttet i øyet, mannen innsatt i Arresten, anmeldt for "
                + "vold mot off. tjenestemann.";
            float res = TweetAnalysis.ScoreTweet(originalText,
                out string highlightedText);

            Assert.Equal((float)0, res, 3);

            Assert.Equal(originalText, highlightedText);
        }

        [Fact]
        public void Test_ScoreTweet5()
        {
            float res = TweetAnalysis.ScoreTweet(
                "Saknet person i skred, politiet undersøker. Forsøk forsøkt.",
                out string highlightedText);

            float expectedScore = (float)2 / 8; // only eight words in text
            Assert.Equal(expectedScore, res, 3);

            Assert.Equal(
                "*Saknet* person i *skred,* politiet undersøker. Forsøk forsøkt.",
                highlightedText);
        }

        [Fact]
        public void Test_Blacklisting1()
        {
            float res = TweetAnalysis.ScoreTweet(
                "#Bergen: E16 v/Takvam. Patr stanset bil, mistanke om kjøring" +
                " i ruspåvirket tilstand. Funn av narkotika i bilen. Fører " +
                "innsettes i fengsling forvaring. Sak opprettes.",
                out string highlightedText);

            float expectedScore = 0; // blacklist "narkotika"
            Assert.Equal(expectedScore, res, 3);
        }

        [Fact]
        public void Test_Blacklisting2()
        {
            float res = TweetAnalysis.ScoreTweet(
                "Har *gjennomsøkt* boligen. Ingen brann på stedet. " +
                "Brannvesenet avslutter på stedet.",
                out string highlightedText);

            float expectedScore = 0; // blacklist "brann"
            Assert.Equal(expectedScore, res, 3);
        }

        [Fact]
        public void Test_Blacklisting3()
        {
            float res = TweetAnalysis.ScoreTweet(
                "Rettelse: kun en bil som er involvert.",
                out string highlightedText);

            float expectedScore = 0; // blacklist "rettelse"
            Assert.Equal(expectedScore, res, 3);
        }

        [Fact]
        public void Test_ErroneousTweet1()
        {
            // It turned out that it wasn't the line breaks in the tweet that
            // caused it to be rated at zero, it was "Brannvesenet", which
            // contains "brann". Fixed.
            float res = TweetAnalysis.ScoreTweet(
@"#Misje #Øygarden

Leteaksjon i området Misje.Siste observasjon er kl 0530.

Beskrivelse: Mann 22 år, ca 180 cm, tynn, mørkt brunt hår.Iført sort jakke, blå t-shirt, sort bukse.

Politi, Brannvesenet, Norske redningshunder, Røde Kors og Norsk folkehjelp bistår.",
            out string highlightedText);

            float expectedScore = 0.162F; // blacklist "rettelse"
            Assert.Equal(expectedScore, res, 3);
        }
    }

    public class UnitTestResponseData
    {
        [Fact]
        public void Test_Decode1()
        {

            var responseContent = @"{'tags': ['placeA', 'placeB'], 'label': 1, 'text': 'hello', 'original': 'bla'}";
            ResponseData ml_result = JsonConvert.DeserializeObject<ResponseData>(responseContent);

            Assert.Equal(new List<string> { "placeA", "placeB" }, ml_result.Tags);
            Assert.Equal("hello", ml_result.Text);
            Assert.Equal(1, ml_result.Label);
            Assert.Equal("bla", ml_result.Original);
        }
    }
}
