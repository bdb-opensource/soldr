using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Cosln.Common
{
    public class StringExtensions
    {
        /// <summary>
        /// Will add a tab ('\t') at the beginning of every line in each string
        /// </summary>
        public static string Tabify(params string[] strings)
        {
            return Tabify((IEnumerable<String>)strings);
        }

        /// <summary>
        /// Will add a tab ('\t') at the beginning of every line in each string
        /// </summary>
        public static string Tabify(IEnumerable<String> strings)
        {
            return "\t" + String.Join("\n\t", strings.SelectMany(x => x.Split('\n')));
        }
    }
}
