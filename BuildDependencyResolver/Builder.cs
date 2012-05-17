using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using BuildDependencyReader.ProjectFileParser;

namespace BuildDependencyReader.BuildDependencyResolver
{
    public class Builder
    {
        public static void CopyAssemblyReferencesFromBuiltProjects(IProjectFinder projectFinder, IEnumerable<AssemblyReference> assemblyReferences)
        {
            foreach (var assemblyReference in assemblyReferences)
            {
                var buildingProject = projectFinder.FindProjectForAssemblyReference(assemblyReference).SingleOrDefault();
                if (null == buildingProject)
                {
                    continue;
                }
                var targetPath = System.IO.Path.GetDirectoryName(assemblyReference.HintPath);
                CopyProjectOutputsToDirectory(buildingProject, targetPath);
            }
        }

        public static void CopyProjectOutputsToDirectory(Project project, string targetPath)
        {
            foreach (var projectOutput in project.GetBuiltProjectOutputs())
            {
                var source = projectOutput.FullName;
                var target = System.IO.Path.Combine(targetPath, projectOutput.Name);

                Console.Error.Write(String.Format("copying {0} -> {1}...", source, target));

                System.IO.File.Copy(source, target, true);

                Console.Error.WriteLine("Done.");
            }
        }
    }
}
