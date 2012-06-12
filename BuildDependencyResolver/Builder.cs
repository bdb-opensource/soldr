using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using BuildDependencyReader.Common;
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
                // TODO: Include more information in the warnings: which projects use the assembly, which project builds it, etc.
                if (null == buildingProject)
                {
                    _logger.WarnFormat("Can't find dependency (no building project): No project builds assembly reference: '{0}', used by projects:\n{1}", 
                        assemblyReference,
                        ProjectsUsingAssemblyReference(projectFinder, assemblyReference));
                    continue;
                }
                if (String.IsNullOrWhiteSpace(assemblyReference.HintPath))
                {
                    _logger.WarnFormat("Can't copy dependency (no target path): Missing HintPath for assembly reference: '{0}', used by projects:\n{1}",
                        assemblyReference, ProjectsUsingAssemblyReference(projectFinder, assemblyReference));
                    continue;
                }
                var targetPath = System.IO.Path.GetDirectoryName(assemblyReference.HintPath);
                CopyProjectOutputsToDirectory(buildingProject, targetPath);
            }
        }

        private static string ProjectsUsingAssemblyReference(IProjectFinder projectFinder, AssemblyReference assemblyReference)
        {
            return StringExtensions.Tabify(projectFinder.AllProjectsInPath()
                                                        .Where(x => x.AssemblyReferences
                                                                    .Contains(assemblyReference))
                                                        .Select(x => x.ToString()));
        }

        public static void CopyProjectOutputsToDirectory(Project project, string targetPath)
        {
            foreach (var projectOutput in project.GetBuiltProjectOutputs())
            {
                var source = projectOutput.FullName;
                var target = System.IO.Path.Combine(targetPath, projectOutput.Name);

                _logger.InfoFormat("copying {0} -> {1}...", source, target);
                System.IO.Directory.CreateDirectory(targetPath);
                System.IO.File.Copy(source, target, true);
            }
        }
        
        /// <summary>
        /// <para>Builds the given solution. Steps:</para>
        /// <para>1. Update dependencies (components) by copying all referenced assemblies from their building projects' output paths to the HintPath</para>
        /// <para>2. Clean the solution</para>
        /// <para>3. Build the solution</para>
        /// <para>Use <paramref name="ignoredDependencyAssemblies"/> to ignore system and/or third party assemblies that are not part of the build tree (those that will not be found when the <paramref name="projectFinder"/> will search for a project that builds them)</para>
        /// </summary>
        /// <param name="projectFinder"></param>
        /// <param name="solutionFileName"></param>
        /// <param name="ignoredDependencyAssemblies">Patterns for dependent assemblies to ignore when trying to find a building project to copy from.</param>
        /// <param name="ignoreOnlyMatching">Flips the meaning of the ignored assemblies so that ALL assemblies will be ignored, EXCEPT the ones matching the given patterns</param>
        public static void BuildSolution(IProjectFinder projectFinder, string solutionFileName, Regex[] ignoredDependencyAssemblies, bool ignoreOnlyMatching)
        {
            _logger.InfoFormat("Building Solution: '{0}'", solutionFileName);
            UpdateComponentsFromBuiltProjects(projectFinder, solutionFileName, ignoredDependencyAssemblies, ignoreOnlyMatching);
            _logger.InfoFormat("\tCleaning...");
            MSBuild(solutionFileName, "/t:clean");
            _logger.InfoFormat("\tBuilding...");
            MSBuild(solutionFileName);
            _logger.InfoFormat("\tDone: '{0}'", solutionFileName);
        }

        public static void UpdateComponentsFromBuiltProjects(IProjectFinder projectFinder, string solutionFileName, Regex[] assemblyNamePatterns, bool ignoreOnlyMatching)
        {
            _logger.InfoFormat("\tCopying dependencies...");
            var assemblyReferences = projectFinder.GetProjectsOfSLN(solutionFileName)
                                                  .SelectMany(x => x.AssemblyReferences)
                                                  .Distinct();
            var filtered = assemblyReferences.Split(x => IncludeAssemblyWhenCopyingDeps(x, assemblyNamePatterns, ignoreOnlyMatching));
            foreach (var ignored in filtered.Value)
            {
                _logger.InfoFormat("Not copying ignored assembly: '{0}'", ignored.ToString());
            }
            Builder.CopyAssemblyReferencesFromBuiltProjects(projectFinder, filtered.Key);
        }

        private static bool IncludeAssemblyWhenCopyingDeps(AssemblyReference assemblyReference, Regex[] assemblyNamePatterns, bool ignoreOnlyMatching)
        {
            return BoolExtensions.Flip(assemblyNamePatterns.Any(r => r.IsMatch(assemblyReference.Name)), ignoreOnlyMatching);
        }


        protected static void MSBuild(string solutionFileName, string args = "")
        {
            var process = new Process();
            process.StartInfo.CreateNoWindow = true;
            process.StartInfo.UseShellExecute = false;
            // TODO: Remove hard-coded path to msbuild
            process.StartInfo.FileName = @"C:\Windows\Microsoft.NET\Framework\v4.0.30319\MSBuild.exe";
            process.StartInfo.Arguments = String.Format("/nologo /v:quiet \"{0}\" {1}", solutionFileName, args);
            process.StartInfo.RedirectStandardError = true;
            process.StartInfo.RedirectStandardOutput = true;
            process.Start();

            // Must read process outputs before calling WaitForExit to prevent deadlocks
            LogWarnProcessOutputs(process);

            process.WaitForExit();
            if (0 != process.ExitCode)
            {
                throw new Exception("Build failed: " + solutionFileName);
            }
        }

        protected static void LogWarnProcessOutputs(Process process)
        {
            var processStdOut = process.StandardOutput.ReadToEnd().Trim();
            if (processStdOut.Any())
            {
                _logger.WarnFormat("'{0} {1}': stdout:\n{2}", process.StartInfo.FileName, process.StartInfo.Arguments, StringExtensions.Tabify(processStdOut));
            }
            var processStdErr = process.StandardError.ReadToEnd().Trim();
            if (processStdErr.Any())
            {
                _logger.WarnFormat("'{0} {1}': stderr:\n{2}", process.StartInfo.FileName, process.StartInfo.Arguments, StringExtensions.Tabify(processStdErr));
            }
        }
    }
}
