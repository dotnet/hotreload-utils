using System;

namespace ImportExplicitly.Tests
{
    public static class TargetClass
    {
        public static string TargetMethod ()
        {
            Func<string,string> fn = static (s) => s;
            return fn ("OLD!");
        }
    }
}
