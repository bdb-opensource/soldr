using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Soldr.ProjectFileParser
{
    public struct ProjectConfiguration
    {
        public readonly string Configuration;
        public readonly string Platform;
        public readonly string OutputPath;

        public ProjectConfiguration(string Configuration, string Platform, string OutputPath)
        {
            this.Configuration = Configuration;
            this.Platform = Platform;
            this.OutputPath = OutputPath;
        }

        public override string ToString()
        {
            return String.Format("{{ ProjectConfiguration: Configuration='{0}', Platform='{1}', OutputPath='{2}' }}",
                                 this.Configuration, this.Platform, this.OutputPath);
        }
    }
}
