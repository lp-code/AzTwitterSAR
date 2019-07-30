using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using AzTwitterSar;

namespace UnitTestProjectAzTwitterSar
{
    [TestClass]
    public class UnitTestAzTwitterSar
    {
        [TestMethod]
        public void Test_CountWordsInString1()
        {
            int res = AzTwitterSarFunc.CountWordsInString(
                "#Bergen, Danmarksplass: Politiet har gjennomført farts"
                + "kontroll. 9 forenklede forelegg, 2 førerkortbeslag, høyeste"
                + " fart var 112 km/t i 50-sonen.");

            Assert.AreEqual(18, res);
        }

        [TestMethod]
        public void Test_CountWordsInString2()
        {
            int res = AzTwitterSarFunc.CountWordsInString(
                "#Espeland Bergen: Mann i 40 årene er savnet fra bopel det er"
                + " iverksatt leteaksjon. Mannskap fra Røde Kors Norske "
                + "Redningshunder samt Norsk Luftambulanse deltar foreløpig i"
                + " søket.");
            Assert.AreEqual(27, res);
        }

        [TestMethod]
        public void Test_CountWordsInString3()
        {
            int res = AzTwitterSarFunc.CountWordsInString(
                "Saknet person i skred, politiet undersøker. Forsøk forsøkt.");
            Assert.AreEqual(8, res);
        }

        [TestMethod]
        public void Test_ConvertUtcToLocal1()
        {
            string res = AzTwitterSarFunc.ConvertUtcToLocal("2018-12-17T01:42:34.000Z");

            Assert.AreEqual("2018-12-17 02:42:34", res);
        }

        [TestMethod]
        public void Test_ConvertUtcToLocal2()
        {
            string res = AzTwitterSarFunc.ConvertUtcToLocal("2018-12-16T22:27:19.000Z");

            Assert.AreEqual("2018-12-16 23:27:19", res);
        }

        [TestMethod]
        public void Test_ConvertUtcToLocal3failure()
        {
            // wrong input format (missing T in the middle)
            string res = AzTwitterSarFunc.ConvertUtcToLocal("2018-12-16 22:27:19.000Z");

            Assert.AreEqual("Time conversion failed: 2018-12-16 22:27:19.000Z", res);
        }

        [TestMethod]
        public void Test_ScoreTweet1()
        {
            float res = AzTwitterSarFunc.ScoreTweet(
                "#Bergen, Danmarksplass: Politiet har gjennomført farts"
                + "kontroll. 9 forenklede forelegg, 2 førerkortbeslag, høyeste"
                + " fart var 112 km/t i 50-sonen.", out string _);

            Assert.AreEqual(0, res);
        }

        [TestMethod]
        public void Test_ScoreTweet2()
        {
            //string highlightedText = "";
            float res = AzTwitterSarFunc.ScoreTweet(
                "#Espeland Bergen: Mann i 40 årene er savnet fra bopel det er"
                + " iverksatt leteaksjon. Mannskap fra Røde Kors Norske "
                + "Redningshunder samt Norsk Luftambulanse deltar foreløpig i"
                + " søket.", out string highlightedText);

            // The tweet has 27 words, but here the minimum in the denominator
            // in the score function selects the number of trigger words.
            float expectedScore = (float)7 / 27;
            Assert.AreEqual(expectedScore, res, 0.001f);

            Assert.AreEqual(
                "#Espeland Bergen: Mann i 40 årene er *savnet* fra bopel det er"
                + " iverksatt *leteaksjon.* Mannskap fra *Røde* *Kors* Norske "
                + "*Redningshunder* samt Norsk *Luftambulanse* deltar foreløpig i"
                + " *søket.*", highlightedText);
        }

        [TestMethod]
        public void Test_ScoreTweet3()
        {
            string txt =
                "#Kaupanger Politiet har leitet etter en mann i 30-årene siden"
                + " kl 04 i natt etter melding om beruset person som framsto "
                + "ute av stand til å ivareta seg selv. Lokalt politi fått "
                + "bistand fra Røde Kors og Norske redningshunder. Vedkommende "
                + "funnet ca kl 0930 i god behold.";
            float res = AzTwitterSarFunc.ScoreTweet(txt, out string highlightedText);

            float expectedScore = (float)6 / Math.Min(
                AzTwitterSarFunc.relevantStrings.Length,
                txt.Split().Length);
            Assert.AreEqual(expectedScore, res, 0.001f);

            Assert.AreEqual(
                "#Kaupanger Politiet har *leitet* etter en mann i 30-årene siden"
                + " kl 04 i natt etter melding om beruset person som framsto "
                + "ute av stand til å ivareta seg selv. Lokalt politi fått "
                + "bistand fra *Røde* *Kors* og Norske *redningshunder.* Vedkommende"
                + " *funnet* ca kl 0930 i god *behold.*", highlightedText);
        }

        [TestMethod]
        public void Test_ScoreTweet4()
        {
            string originalText =
                "#Bergen, Nordre Toppe: Ordensforstyrrelse, ruset og aggressiv"
                + " mann, i forbindelse med innbringelsen, forsøkte han å "
                + "skalle til en politibetjent, samt en politibetjent ble "
                + "spyttet i øyet, mannen innsatt i Arresten, anmeldt for "
                + "vold mot off. tjenestemann.";
            float res = AzTwitterSarFunc.ScoreTweet(originalText, 
                out string highlightedText);

            Assert.AreEqual((float)0, res, 0.001f);

            Assert.AreEqual(originalText, highlightedText);
        }

        [TestMethod]
        public void Test_ScoreTweet5()
        {
            float res = AzTwitterSarFunc.ScoreTweet(
                "Saknet person i skred, politiet undersøker. Forsøk forsøkt.",
                out string highlightedText);

            float expectedScore = (float)2 / 8; // only eight words in text
            Assert.AreEqual(expectedScore, res, 0.001f);

            Assert.AreEqual(
                "*Saknet* person i *skred,* politiet undersøker. Forsøk forsøkt.",
                highlightedText);
        }

        [TestMethod]
        public void Test_Blacklisting1()
        {
            float res = AzTwitterSarFunc.ScoreTweet(
                "#Bergen: E16 v/Takvam. Patr stanset bil, mistanke om kjøring" +
                " i ruspåvirket tilstand. Funn av narkotika i bilen. Fører " +
                "innsettes i fengsling forvaring. Sak opprettes.",
                out string highlightedText);

            float expectedScore = 0; // blacklist "narkotika"
            Assert.AreEqual(expectedScore, res, 0.001f);
        }

        [TestMethod]
        public void Test_Blacklisting2()
        {
            float res = AzTwitterSarFunc.ScoreTweet(
                "Har *gjennomsøkt* boligen. Ingen brann på stedet. " +
                "Brannvesenet avslutter på stedet.",
                out string highlightedText);

            float expectedScore = 0; // blacklist "brann"
            Assert.AreEqual(expectedScore, res, 0.001f);
        }

        [TestMethod]
        public void Test_Blacklisting3()
        {
            float res = AzTwitterSarFunc.ScoreTweet(
                "Rettelse: kun en bil som er involvert.",
                out string highlightedText);

            float expectedScore = 0; // blacklist "rettelse"
            Assert.AreEqual(expectedScore, res, 0.001f);
        }
    }
}
