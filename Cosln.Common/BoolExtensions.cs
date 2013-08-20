using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace BuildDependencyReader.Common
{
    public class BoolExtensions
    {
        /// <summary>
        /// Returns not <paramref name="value"/> (!value) if <paramref name="toFlip"/> is True, otherwise returns <paramref name="value"/>
        /// <para>(Equivalent to XOR)</para>
        /// </summary>
        public static bool Flip(bool value, bool toFlip)
        {
            if (toFlip)
            {
                return false == value;
            }
            return value;
        }
    }
}
