using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Runtime.Serialization;

namespace BuildDependencyReader.ProjectFileParser
{
    [Serializable]
    public class AssemblyReferenceHintPathDoesNotExist : Exception
    {
        protected AssemblyReferenceHintPathDoesNotExist() : base() { }
        public AssemblyReferenceHintPathDoesNotExist(SerializationInfo info, StreamingContext context) : base(info, context) { }

        public AssemblyReferenceHintPathDoesNotExist(string assemblyName, string hintPath, string containingProjectFile)
            : base(String.Format("Assembly Reference '{0}' has a bad HintPath - the file does not exist: '{1}' (in project {2})",
                                 assemblyName,
                                 hintPath,
                                 containingProjectFile))
        {
        }
    }

    [Serializable]
    public class AssemblyReferenceMultipleHintPaths : Exception
    {
        protected AssemblyReferenceMultipleHintPaths() : base() { }
        public AssemblyReferenceMultipleHintPaths(SerializationInfo info, StreamingContext context) : base(info, context) { }

        public AssemblyReferenceMultipleHintPaths(string assemblyName, string hintPath, string containingProjectFile)
            : base(String.Format("Assembly Reference '{0}' has a multiple HintPaths: '{1}' (in project {2})",
                                 assemblyName,
                                 hintPath,
                                 containingProjectFile))
        {
        }
    }

}
