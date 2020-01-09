using System;
using AzTwitterSar.ProcessTweets;
using Xunit;

namespace AzTwitterSarUnitTest
{
    public class UnitTestAzTwitterSar
    {
        [Fact]
        public void Test_CountWordsInString1()
        {
            int res = AzTwitterSarFunc.CountWordsInString(
                "#Bergen, Danmarksplass: Politiet har gjennomf�rt farts"
                + "kontroll. 9 forenklede forelegg, 2 f�rerkortbeslag, h�yeste"
                + " fart var 112 km/t i 50-sonen.");

            Assert.Equal(18, res);
        }

        [Fact]
        public void Test_CountWordsInString2()
        {
            int res = AzTwitterSarFunc.CountWordsInString(
                "#Espeland Bergen: Mann i 40 �rene er savnet fra bopel det er"
                + " iverksatt leteaksjon. Mannskap fra R�de Kors Norske "
                + "Redningshunder samt Norsk Luftambulanse deltar forel�pig i"
                + " s�ket.");
            Assert.Equal(27, res);
        }

        [Fact]
        public void Test_CountWordsInString3()
        {
            int res = AzTwitterSarFunc.CountWordsInString(
                "Saknet person i skred, politiet unders�ker. Fors�k fors�kt.");
            Assert.Equal(8, res);
        }

        [Fact]
        public void Test_ConvertUtcToLocal1()
        {
            string res = AzTwitterSarFunc.ConvertUtcToLocal("2018-12-17T01:42:34.000Z");

            Assert.Equal("2018-12-17 02:42:34", res);
        }

        [Fact]
        public void Test_ConvertUtcToLocal2()
        {
            string res = AzTwitterSarFunc.ConvertUtcToLocal("2018-12-16T22:27:19.000Z");

            Assert.Equal("2018-12-16 23:27:19", res);
        }

        [Fact]
        public void Test_ConvertUtcToLocal3failure()
        {
            // wrong input format (missing T in the middle)
            string res = AzTwitterSarFunc.ConvertUtcToLocal("2018-12-16 22:27:19.000Z");

            Assert.Equal("Time conversion failed: 2018-12-16 22:27:19.000Z", res);
        }

        [Fact]
        public void Test_ScoreTweet1()
        {
            float res = AzTwitterSarFunc.ScoreTweet(
                "#Bergen, Danmarksplass: Politiet har gjennomf�rt farts"
                + "kontroll. 9 forenklede forelegg, 2 f�rerkortbeslag, h�yeste"
                + " fart var 112 km/t i 50-sonen.", out string _);

            Assert.Equal(0, res);
        }

        [Fact]
        public void Test_ScoreTweet2()
        {
            //string highlightedText = "";
            float res = AzTwitterSarFunc.ScoreTweet(
                "#Espeland Bergen: Mann i 40 �rene er savnet fra bopel det er"
                + " iverksatt leteaksjon. Mannskap fra R�de Kors Norske "
                + "Redningshunder samt Norsk Luftambulanse deltar forel�pig i"
                + " s�ket.", out string highlightedText);

            // The tweet has 27 words, but here the minimum in the denominator
            // in the score function selects the number of trigger words.
            float expectedScore = (float)7 / 27;
            Assert.Equal(expectedScore, res, 3);

            Assert.Equal(
                "#Espeland Bergen: Mann i 40 �rene er *savnet* fra bopel det er"
                + " iverksatt *leteaksjon.* Mannskap fra *R�de* *Kors* Norske "
                + "*Redningshunder* samt Norsk *Luftambulanse* deltar forel�pig i"
                + " *s�ket.*", highlightedText);
        }

        [Fact]
        public void Test_ScoreTweet3()
        {
            string txt =
                "#Kaupanger Politiet har leitet etter en mann i 30-�rene siden"
                + " kl 04 i natt etter melding om beruset person som framsto "
                + "ute av stand til � ivareta seg selv. Lokalt politi f�tt "
                + "bistand fra R�de Kors og Norske redningshunder. Vedkommende "
                + "funnet ca kl 0930 i god behold.";
            float res = AzTwitterSarFunc.ScoreTweet(txt, out string highlightedText);

            float expectedScore = (float)6 / Math.Min(
                AzTwitterSarFunc.relevantStrings.Length,
                txt.Split().Length);
            Assert.Equal(expectedScore, res, 3);

            Assert.Equal(
                "#Kaupanger Politiet har *leitet* etter en mann i 30-�rene siden"
                + " kl 04 i natt etter melding om beruset person som framsto "
                + "ute av stand til � ivareta seg selv. Lokalt politi f�tt "
                + "bistand fra *R�de* *Kors* og Norske *redningshunder.* Vedkommende"
                + " *funnet* ca kl 0930 i god *behold.*", highlightedText);
        }

        [Fact]
        public void Test_ScoreTweet4()
        {
            string originalText =
                "#Bergen, Nordre Toppe: Ordensforstyrrelse, ruset og aggressiv"
                + " mann, i forbindelse med innbringelsen, fors�kte han � "
                + "skalle til en politibetjent, samt en politibetjent ble "
                + "spyttet i �yet, mannen innsatt i Arresten, anmeldt for "
                + "vold mot off. tjenestemann.";
            float res = AzTwitterSarFunc.ScoreTweet(originalText,
                out string highlightedText);

            Assert.Equal((float)0, res, 3);

            Assert.Equal(originalText, highlightedText);
        }

        [Fact]
        public void Test_ScoreTweet5()
        {
            float res = AzTwitterSarFunc.ScoreTweet(
                "Saknet person i skred, politiet unders�ker. Fors�k fors�kt.",
                out string highlightedText);

            float expectedScore = (float)2 / 8; // only eight words in text
            Assert.Equal(expectedScore, res, 3);

            Assert.Equal(
                "*Saknet* person i *skred,* politiet unders�ker. Fors�k fors�kt.",
                highlightedText);
        }

        [Fact]
        public void Test_Blacklisting1()
        {
            float res = AzTwitterSarFunc.ScoreTweet(
                "#Bergen: E16 v/Takvam. Patr stanset bil, mistanke om kj�ring" +
                " i rusp�virket tilstand. Funn av narkotika i bilen. F�rer " +
                "innsettes i fengsling forvaring. Sak opprettes.",
                out string highlightedText);

            float expectedScore = 0; // blacklist "narkotika"
            Assert.Equal(expectedScore, res, 3);
        }

        [Fact]
        public void Test_Blacklisting2()
        {
            float res = AzTwitterSarFunc.ScoreTweet(
                "Har *gjennoms�kt* boligen. Ingen brann p� stedet. " +
                "Brannvesenet avslutter p� stedet.",
                out string highlightedText);

            float expectedScore = 0; // blacklist "brann"
            Assert.Equal(expectedScore, res, 3);
        }

        [Fact]
        public void Test_Blacklisting3()
        {
            float res = AzTwitterSarFunc.ScoreTweet(
                "Rettelse: kun en bil som er involvert.",
                out string highlightedText);

            float expectedScore = 0; // blacklist "rettelse"
            Assert.Equal(expectedScore, res, 3);
        }
    }
}
