using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace RoslynILDiff
{
    class TestClass
    {
        public int DoStuff (int x, int y)
        {
            return x * x + y * y;
        }

#if true
        public class Nesty {
            public static void P () { }
        }

#endif
    }
}
