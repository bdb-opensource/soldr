using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Cosln.ProjectFileParser
{
    public struct AssemblyReference
    {
        /// <summary>
        /// Absolute path of HintPath.
        /// </summary>
        public readonly string HintPath;

        /// <summary>
        /// Assembly name
        /// </summary>
        public readonly string Name;

        /// <summary>
        /// HintPath as given explicitly in the project file.
        /// </summary>
        public readonly string ExplicitHintPath;

        public AssemblyReference(string name, string hintPath, string explicitHintPath)
        {
            this.HintPath = hintPath;
            this.Name = name;
            this.ExplicitHintPath = explicitHintPath;
        }

        public override string ToString()
        {
            return String.Format("{{ AssemblyReference: Name='{0}', HintPath='{1}', ExplicitHintPath='{2}' }}", this.Name, this.HintPath, this.ExplicitHintPath);
        }

        public override bool Equals(object obj)
        {
            if (false == obj.GetType().Equals(this.GetType()))
            {
                return false;
            }
            var other = (AssemblyReference)obj;
            return Object.Equals(this.HintPath, other.HintPath) && Object.Equals(this.Name, other.Name);
        }

        public override int GetHashCode()
        {
            return (this.HintPath ?? String.Empty).GetHashCode() + 13 * (this.Name ?? String.Empty).GetHashCode();
        }

        public string AssemblyNameFromFullName()
        {
            return AssemblyReference.AssemblyNameFromFullName(this.Name);
        }

        // TODO: Find a system library function that does the same?
        public static string AssemblyNameFromFullName(string fullName)
        {
            return fullName.Split(',').First();
        }
    }

}
