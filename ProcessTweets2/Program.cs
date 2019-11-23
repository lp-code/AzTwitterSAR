using OfficeOpenXml;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;


namespace ProcessTweets
{
    class Program
    {
        static void Main(string[] args)
        {
            const string fileName = @"C:\Users\lpesch\Private\RKH\TwitterSAR\Tweets\1_ProcessedScript\vicinitas_user_tweets_vest_scoring_layout.xlsx";
            var fi = new FileInfo(fileName);
            using (var package = new ExcelPackage(fi))
            {
                ExcelWorksheet worksheet = package.Workbook.Worksheets.First();
                var rowCount = worksheet.Dimension.Rows;

                int nonzeroScores = 0;
                for (int row = 2; row <= rowCount; row++)
                {
                    if (worksheet.Cells[row, 4] != null && (worksheet.Cells[row, 4]).Value != null)
                    {
                        // run the scoring function, output if nonzero
                        float score = AzTwitterSar.ProcessTweets.AzTwitterSarFunc.ScoreTweet((worksheet.Cells[row, 4]).Value.ToString(), out string _);
                        worksheet.Cells[row, 2].Value = score;
                        if (score > 0)
                        {
                            Console.WriteLine(worksheet.Cells[row, 4].Value.ToString());
                            Console.WriteLine($"Score: {score}");
                            nonzeroScores++;
                        }
                    }
                }
                Console.WriteLine($"Found {nonzeroScores} tweets with nonzero score.");
                package.Save();
            }

            Console.WriteLine("Processing finished!");
            Console.ReadKey();
        }
    }
}
