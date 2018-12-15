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
        public void Test_ConvertUtcToLocal()
        {
            string res = AzTwitterSarFunc.ConvertUtcToLocal("Fri Dec 14 23:47:57 +0000 2018");

            Assert.AreEqual("2018-12-15T00:47:57", res);
        }


        [TestMethod]
        public void Test_ScoreTweet1()
        {
            float res = AzTwitterSarFunc.ScoreTweet(
                "#Bergen, Danmarksplass: Politiet har gjennomført farts"
                + "kontroll. 9 forenklede forelegg, 2 førerkortbeslag, høyeste"
                + " fart var 112 km/t i 50-sonen.");

            Assert.AreEqual(0, res);
        }

        [TestMethod]
        public void Test_ScoreTweet2()
        {
            float res = AzTwitterSarFunc.ScoreTweet(
                "#Espeland Bergen: Mann i 40 årene er savnet fra bopel det er"
                + " iverksatt leteaksjon. Mannskap fra Røde Kors Norske "
                + "Redningshunder samt Norsk Luftambulanse deltar foreløpig i"
                + " søket.");

            // The tweet has 27 words, but here the minimum in the denominator
            // in the score function selects the number of trigger words.
            float expectedScore = (float)6 /
                AzTwitterSarFunc.relevantStrings.Length;
            Assert.AreEqual(expectedScore, res, 0.001f);
        }

        [TestMethod]
        public void Test_ScoreTweet3()
        {
            float res = AzTwitterSarFunc.ScoreTweet(
                "#Kaupanger Politiet har leitet etter en mann i 30-årene siden"
                + " kl 04 i natt etter melding om beruset person som framsto "
                + "ute av stand til å ivareta seg selv. Lokalt politi fått "
                + "bistand fra Røde Kors og Norske redningshunder. Vedkommende"
                + "funnet ca kl 0930 i god behold.");

            float expectedScore = (float)3 /
                AzTwitterSarFunc.relevantStrings.Length;
            Assert.AreEqual(expectedScore, res, 0.001f);
        }

    }
}
