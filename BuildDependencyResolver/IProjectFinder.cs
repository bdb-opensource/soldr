using System;
using System.Collections.Generic;
using BuildDependencyReader.ProjectFileParser;
namespace BuildDependencyReader.BuildDependencyResolver
{
    public interface IProjectFinder
    {
        IEnumerable<Project> FindProjectForAssemblyReference(AssemblyReference assemblyReference);
    }
}
