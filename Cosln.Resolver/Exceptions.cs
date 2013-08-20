using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace BuildDependencyReader.BuildDependencyResolver
{
    public class Exceptions
    {
        public class ProcessEndedWithFailExitCodeException : Exception
        {
            public ProcessEndedWithFailExitCodeException(string message) : base(message) { }
        }
    }
}
