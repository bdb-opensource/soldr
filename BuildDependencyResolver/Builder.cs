using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using BuildDependencyReader.ProjectFileParser;

namespace BuildDependencyReader.BuildDependencyResolver
{
    public class Builder
    {
        private IDictionary<AssemblyReference, string> CalculateAssemblyBuildPaths(IProjectFinder projectFinder, IEnumerable<Project> projects)
        {
            var assemblyBuildPaths = new Dictionary<AssemblyReference, string>();

            foreach (var assemblyReference in projects.SelectMany(x => x.AssemblyReferences).Distinct())
            {
                assemblyBuildPaths.Add(assemblyReference, projectFinder.GetBuildPathForAssemblyReference(assemblyReference));
            }
            return assemblyBuildPaths;
        }


    }
}
