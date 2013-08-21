using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Runtime.Serialization;

namespace Soldr.ProjectFileParser
{
    [Serializable]
    public class AssemblyReferenceHintPathDoesNotExistException : Exception
    {
        protected AssemblyReferenceHintPathDoesNotExistException() : base() { }
        public AssemblyReferenceHintPathDoesNotExistException(SerializationInfo info, StreamingContext context) : base(info, context) { }

        public AssemblyReferenceHintPathDoesNotExistException(string assemblyName, string hintPath, string containingProjectFile)
            : base(String.Format("Assembly Reference '{0}' has a bad HintPath - the file does not exist: '{1}' (in project {2})",
                                 assemblyName,
                                 hintPath,
                                 containingProjectFile))
        {
        }
    }

    [Serializable]
    public class AssemblyReferenceMultipleHintPathsException : Exception
    {
        protected AssemblyReferenceMultipleHintPathsException() : base() { }
        public AssemblyReferenceMultipleHintPathsException(SerializationInfo info, StreamingContext context) : base(info, context) { }

        public AssemblyReferenceMultipleHintPathsException(string assemblyName, string hintPath, string containingProjectFile)
            : base(String.Format("Assembly Reference '{0}' has a multiple HintPaths: '{1}' (in project {2})",
                                 assemblyName,
                                 hintPath,
                                 containingProjectFile))
        {
        }
    }

    [Serializable]
    public class InvalidDefaultConfigurationException : Exception
    {
        protected InvalidDefaultConfigurationException() : base() { }
        public InvalidDefaultConfigurationException(string message) : base(message) { }
        public InvalidDefaultConfigurationException(SerializationInfo info, StreamingContext context) : base(info, context) { }
    }
}
