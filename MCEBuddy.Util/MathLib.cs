using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace MCEBuddy.Util
{
    public static class MathLib
    {
        /// <summary>
        /// Rounds off to the nearest multiple (towards zero if midway through)
        /// </summary>
        /// <param name="number">Number to round off</param>
        /// <param name="toNearest">Nearest multiple to round off to</param>
        /// <returns></returns>
        public static int RoundOff(int number, int toNearest)
        {
            return ((int)((double)number / (double)toNearest)) * toNearest;
        }
    }
}
