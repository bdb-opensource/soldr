using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace BuildDependencyReader.ProjectFileParser
{
    public class AssemblyReference
    {
        public string HintPath { get; protected set; }
        public string Name { get; protected set; }

        public AssemblyReference(string name, string hintPath)
        {
            this.HintPath = hintPath;
            this.Name = name;
        }

        public override string ToString()
        {
            return String.Format("{{ AssemblyReference: Name = '{0}', HintPath = '{1}' }}", this.Name, this.HintPath);
        }
    }

}
