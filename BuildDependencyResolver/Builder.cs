using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using BuildDependencyReader.ProjectFileParser;

namespace BuildDependencyReader.BuildDependencyResolver
{
    public class Builder
    {
        protected static readonly log4net.ILog _logger = log4net.LogManager.GetLogger(
            System.Reflection.MethodBase.GetCurrentMethod().DeclaringType);


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

                _logger.InfoFormat("copying {0} -> {1}...", source, target);

                if (false == projectOutput.Name.StartsWith(project.Name))
                {
                    _logger.WarnFormat("Project with name '{0}' has unexpected output '{1}' (project = {2})", project.Name, source, project);
                }
                System.IO.File.Copy(source, target, true);
            }
        }
    }
}
