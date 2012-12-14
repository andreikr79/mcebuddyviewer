using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace MCEBuddy.Util
{
    public static class MathLib
    {
        public static int RoundOff(int number, int toNearest)
        {
            return (int)(Math.Round((double)number/(double)toNearest, MidpointRounding.ToEven) * (double)toNearest);
        }
    }
}
