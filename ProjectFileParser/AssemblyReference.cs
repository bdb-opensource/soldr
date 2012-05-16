using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace BuildDependencyReader.ProjectFileParser
{
    public struct AssemblyReference
    {
        public readonly string HintPath;
        public readonly string Name;

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
