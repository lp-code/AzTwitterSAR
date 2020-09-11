using System;
using System.Collections.Generic;
using System.Text;

namespace DurableAzTwitterSar
{
    public static class AzTwitterSarVersion
    {
        public static string get()
        {
            Version version = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version;
            //DateTime buildDate = new DateTime(2000, 1, 1)
            //                        .AddDays(version.Build).AddSeconds(version.Revision * 2);
            return $"{version}";// ({buildDate})";
        }
    }
}
