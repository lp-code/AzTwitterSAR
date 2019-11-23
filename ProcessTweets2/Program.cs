using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Excel = Microsoft.Office.Interop.Excel;

namespace ProcessTweets
{
    class Program
    {
        static void Main(string[] args)
        {
            const string fileName = "C:\\User\\lpesch\\test.xlsx";
            Console.WriteLine(fileName);
            Console.ReadLine();
            //Create COM Objects. Create a COM object for everything that is referenced
            Excel.Application xlApp = new Excel.Application();
            Excel.Workbooks xlWorkbooks = xlApp.Workbooks;
            Excel.Workbook xlWorkbook = xlWorkbooks.Open(fileName); // @"C:\Users\lpesch\Private\RKH\TwitterSAR\Tweets\1_ProcessedScript\vicinitas_user_tweets_vest_scoring_layout.xlsx");
            Excel._Worksheet xlWorksheet = (Excel.Worksheet)xlWorkbook.Sheets[1];
            Excel.Range xlRange = xlWorksheet.UsedRange;

            int i = 2;
            int nonzeroScores = 0;
            while (xlRange.Cells[i, 4] != null && ((Excel.Range)xlRange.Cells[i, 4]).Value2 != null)
            {
                // run the scoring function, output if nonzero
                float score = AzTwitterSar.ProcessTweets.AzTwitterSarFunc.ScoreTweet(((Excel.Range)xlRange.Cells[i, 4]).Value2.ToString(), out string _);
                ((Excel.Range)xlRange.Cells[i, 2]).Value2 = score;
                if (score > 0)
                {
                    Console.WriteLine(((Excel.Range)xlRange.Cells[i, 4]).Value2.ToString());
                    Console.WriteLine($"Score: {score}");
                    nonzeroScores++;
                }
                i++;
            }
            Console.WriteLine($"Found {nonzeroScores} tweets with nonzero score.");

            xlWorkbook.Save();

            //cleanup
            GC.Collect();
            GC.WaitForPendingFinalizers();

            //rule of thumb for releasing com objects:
            //  never use two dots, all COM objects must be referenced and released individually
            //  ex: [somthing].[something].[something] is bad

            //release com objects to fully kill excel process from running in the background
            Marshal.ReleaseComObject(xlRange);
            Marshal.ReleaseComObject(xlWorksheet);

            //close and release
            xlWorkbook.Close();
            Marshal.ReleaseComObject(xlWorkbook);
            Marshal.ReleaseComObject(xlWorkbooks);

            //quit and release
            xlApp.Quit();
            Marshal.ReleaseComObject(xlApp);


            // The code provided will print ‘Hello World’ to the console.
            // Press Ctrl+F5 (or go to Debug > Start Without Debugging) to run your app.
            Console.WriteLine("Hello World!");
            Console.ReadKey();

            // Go to http://aka.ms/dotnet-get-started-console to continue learning how to build a console app! 
        }
    }
}
