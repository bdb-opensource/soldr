using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Soldr.Common
{
    public class PathExtensions
    {
        protected static int MAX_PATH = 260;

        /// <summary>
        /// <para>Same as System.IO.Path.GetFullPath but without a bug that exists in .NET 4.0 (and will be fixed in 4.5).</para>
        /// <see cref="https://connect.microsoft.com/VisualStudio/feedback/details/729120/why-does-system-io-path-getfullpath-throws-exception-by-traversal-paths-with-exact-260-characters"/>
        /// </summary>
        public static string GetFullPath(string combinedPath)
        {
            if (MAX_PATH == combinedPath.Length)
            {
                combinedPath = " " + combinedPath;
            }
            return System.IO.Path.GetFullPath(combinedPath);
        }
    }
}
