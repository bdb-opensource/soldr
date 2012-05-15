using System;
using System.Collections.Generic;
using BuildDependencyReader.ProjectFileParser;
using System.IO;
namespace BuildDependencyReader.BuildDependencyResolver
{
    public interface IProjectFinder
    {
        IEnumerable<Project> FindProjectForAssemblyReference(AssemblyReference assemblyReference);
        FileInfo GetSLNFileForProject(Project project);
    }
}
