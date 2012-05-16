using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace BuildDependencyReader.ProjectFileParser
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
    }
}
