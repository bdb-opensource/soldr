using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using BuildDependencyReader.Common;
using BuildDependencyReader.ProjectFileParser;
using Mono.Cecil;
using System.IO;

namespace BuildDependencyReader.BuildDependencyResolver
{
    public class Builder
    {
        protected static readonly log4net.ILog _logger = log4net.LogManager.GetLogger(
            System.Reflection.MethodBase.GetCurrentMethod().DeclaringType);


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
        public static void BuildSolution(IProjectFinder projectFinder, string solutionFileName, Regex[] ignoredDependencyAssemblies, bool ignoreOnlyMatching, bool ignoreMissing)
        {
            _logger.InfoFormat("Building Solution: '{0}'", solutionFileName);
            UpdateComponentsFromBuiltProjects(projectFinder, solutionFileName, ignoredDependencyAssemblies, ignoreOnlyMatching, ignoreMissing);
            ValidateSolutionReadyForBuild(projectFinder, solutionFileName, ignoredDependencyAssemblies, ignoreOnlyMatching);
            _logger.InfoFormat("\tCleaning...");
            MSBuild(solutionFileName, "/t:clean");
            _logger.InfoFormat("\tBuilding...");
            MSBuild(solutionFileName);
            _logger.InfoFormat("\tDone: '{0}'", solutionFileName);
        }

        protected static void ValidateSolutionReadyForBuild(IProjectFinder projectFinder, string solutionFileName, Regex[] ignoredDependencyAssemblies, bool ignoreOnlyMatching)
        {
            foreach (var project in projectFinder.GetProjectsOfSLN(solutionFileName))
            {
                project.ValidateHintPaths(ignoredDependencyAssemblies, ignoreOnlyMatching);
                project.ValidateAssemblyReferencesAreAvailable();
            }
        }

        public static void UpdateComponentsFromBuiltProjects(IProjectFinder projectFinder, string solutionFileName, Regex[] assemblyNamePatterns, bool ignoreOnlyMatching, bool ignoreMissing)
        {
            _logger.InfoFormat("\tCopying dependencies...");
            var assemblyReferences = projectFinder.GetProjectsOfSLN(solutionFileName)
                                                  .SelectMany(x => x.AssemblyReferences)
                                                  .Distinct();
            Builder.CopyAssemblyReferencesFromBuiltProjects(projectFinder, assemblyNamePatterns, ignoreOnlyMatching, assemblyReferences, ignoreMissing);
        }

        protected static bool IncludeAssemblyWhenCopyingDeps(AssemblyReference assemblyReference, Regex[] assemblyNamePatterns, bool ignoreOnlyMatching)
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

        public static void CopyAssemblyReferencesFromBuiltProjects(
            IProjectFinder projectFinder, Regex[] assemblyNamePatterns, bool ignoreOnlyMatching, IEnumerable<AssemblyReference> assemblyReferences, bool ignoreMissing)
        {
            // TODO: Refactor this mess, and save for each indirect reference the dependency path that caused it to be included in a unified way

            var originalAssemblyReferenceNames = new HashSet<string>(assemblyReferences.Select(ComparableAssemblyName));

            var remainingReferences = new Queue<AssemblyReference>(assemblyReferences);
            var indirectReferencesOutsideSolution = new List<IndirectReferenceInfo>();

            while (remainingReferences.Any())
            {
                var assemblyReference = remainingReferences.Dequeue();
                if (false == IncludeAssemblyWhenCopyingDeps(assemblyReference, assemblyNamePatterns, ignoreOnlyMatching))
                {
                    _logger.InfoFormat("Not copying ignored assembly: '{0}'", assemblyReference.ToString());
                    continue;
                }

                if (String.IsNullOrWhiteSpace(assemblyReference.HintPath))
                {
                    _logger.WarnFormat("Can't copy dependency (no target path): Missing HintPath for assembly reference: '{0}', used by projects:\n{1}",
                        assemblyReference, ProjectsUsingAssemblyReference(projectFinder, assemblyReference));
                    continue;
                }
                var targetPath = System.IO.Path.GetDirectoryName(assemblyReference.HintPath);

                var buildingProject = projectFinder.FindProjectForAssemblyReference(assemblyReference).SingleOrDefault();
                if (null == buildingProject)
                {
                    _logger.WarnFormat("Can't find dependency (no building project): No project builds assembly reference: '{0}', used by projects:\n{1}",
                        assemblyReference,
                        ProjectsUsingAssemblyReference(projectFinder, assemblyReference));
                    continue;
                }

                if (ignoreMissing && (false == System.IO.Directory.Exists(buildingProject.GetAbsoluteOutputPath())))
                {
                    _logger.InfoFormat("Ignoring (not copying) all components from not-built project: {0}", buildingProject.ToString());
                    continue;
                }
                var projectOutputs = buildingProject.GetBuiltProjectOutputs().ToArray();

                CopyFilesToDirectory(projectOutputs, targetPath, ignoreMissing);

                // Add sub-references - the indirectly referenced assemblies, the ones used by the current assemblyReference
                var explicitTargetPath = System.IO.Path.GetDirectoryName(assemblyReference.ExplicitHintPath);
                // TODO: Refactor
                AddIndirectReferences(projectFinder, assemblyNamePatterns, ignoreOnlyMatching, originalAssemblyReferenceNames,
                    remainingReferences, targetPath, buildingProject, projectOutputs, explicitTargetPath, assemblyReference, indirectReferencesOutsideSolution);
            }

            WarnAboutRemainingIndirectReferences(projectFinder, originalAssemblyReferenceNames, indirectReferencesOutsideSolution);
        }

        protected static void WarnAboutRemainingIndirectReferences(IProjectFinder projectFinder, HashSet<string> originalAssemblyReferenceNames,
            List<IndirectReferenceInfo> indirectReferencesOutsideSolution)
        {
            foreach (var indirectReferenceInfo in
                indirectReferencesOutsideSolution
                    .Where(x => false == originalAssemblyReferenceNames.Contains(ComparableAssemblyName(x.IndirectReference))))
            {
                _logger.WarnFormat(@"Skipped indirect reference from solution other than the direct reference that caused it:
    Indirect reference:             {0}
    Indirect reference built by:    {1}
    Required by project:            {2}
    Which builds reference:         {3}
    Which is used directly by projects:
    {4}
",
                    indirectReferenceInfo.IndirectReference.ToString(),
                    indirectReferenceInfo.IndirectReferenceProject.ToString(),
                    indirectReferenceInfo.DirectReferenceProject.ToString(),
                    indirectReferenceInfo.DirectReference.ToString(),
                    StringExtensions.Tabify(ProjectsUsingAssemblyReference(projectFinder, indirectReferenceInfo.DirectReference)));
            }
        }

        protected static void AddIndirectReferences(IProjectFinder projectFinder, Regex[] assemblyNamePatterns, bool ignoreOnlyMatching,
            HashSet<string> originalAssemblyReferenceNames, Queue<AssemblyReference> remainingReferences, string targetPath,
            Project buildingProject, FileInfo[] projectOutputs, string explicitTargetPath, AssemblyReference assemblyReference,
            List<IndirectReferenceInfo> indirectReferencesOutsideSolution)
        {
            var indirectReferences = GetIndirectReferences(originalAssemblyReferenceNames, targetPath, projectOutputs, explicitTargetPath)
                                            .Where(x => IncludeAssemblyWhenCopyingDeps(x, assemblyNamePatterns, ignoreOnlyMatching))
                                            .ToArray();

            if (false == indirectReferences.Any())
            {
                return;
            }
            var buildingSolution = projectFinder.GetSLNFileForProject(buildingProject);

            _logger.InfoFormat("Adding indirect references due to reference {0} built by project: '{1}'\n{2}",
                assemblyReference, buildingProject, StringExtensions.Tabify(indirectReferences.Select(x => x.ToString())));
            foreach (var indirectReference in indirectReferences)
            {
                var indirectReferenceBuildingProject = projectFinder.FindProjectForAssemblyReference(indirectReference).SingleOrDefault();
                var indirectReferenceInfo = new IndirectReferenceInfo(assemblyReference, buildingProject, indirectReference, indirectReferenceBuildingProject);
                if (null != indirectReferenceBuildingProject)
                {
                    if (projectFinder.GetSLNFileForProject(indirectReferenceBuildingProject).FullName.Equals(buildingSolution.FullName, StringComparison.InvariantCultureIgnoreCase))
                    {
                        originalAssemblyReferenceNames.Add(ComparableAssemblyName(indirectReference));
                        remainingReferences.Enqueue(indirectReference);
                        continue;
                    }
                }
                indirectReferencesOutsideSolution.Add(indirectReferenceInfo);
            }
        }

        protected static AssemblyReference[] GetIndirectReferences(HashSet<string> originalAssemblyReferenceNames, string targetPath, FileInfo[] projectOutputs, string explicitTargetPath)
        {
            return projectOutputs.Select(x => x.FullName) // Get the file name
                                 .Where(IsAssemblyFileName) // filter only assembly files
                                 .Select(AssemblyDefinition.ReadAssembly) // read the assembly
                                 .SelectMany(x => x.Modules.SelectMany(module => module.AssemblyReferences)) // get the references
                // create an AssemblyReference object
                // TODO: a reference doesn't neccesarily have to be a dll file
                                 .Select(subRef => new AssemblyReference(subRef.FullName,
                                                                         System.IO.Path.Combine(targetPath, subRef.Name + ".dll"),
                                                                         System.IO.Path.Combine(explicitTargetPath, subRef.Name + ".dll")))
                                 .Where(x => false == originalAssemblyReferenceNames.Contains(ComparableAssemblyName(x))) // filter out the ones we already have
                                 .ToArray();
        }

        protected static bool IsAssemblyFileName(string fileName)
        {
            var trimmed = fileName.Trim();
            return trimmed.EndsWith(".dll", StringComparison.InvariantCultureIgnoreCase)
                || trimmed.EndsWith(".exe", StringComparison.InvariantCultureIgnoreCase);
        }

        protected static string ComparableAssemblyName(AssemblyReference assemblyReference)
        {
            return assemblyReference.AssemblyNameFromFullName().Trim().ToLowerInvariant();
        }

        protected static string ProjectsUsingAssemblyReference(IProjectFinder projectFinder, AssemblyReference assemblyReference)
        {
            return StringExtensions.Tabify(projectFinder.AllProjectsInPath()
                                                        .Where(x => x.AssemblyReferences
                                                                    .Contains(assemblyReference))
                                                        .Select(x => x.ToString()));
        }

        protected static void CopyFilesToDirectory(IEnumerable<FileInfo> files, string targetPath, bool ignoreMissing)
        {
            foreach (var file in files)
            {
                var source = file.FullName;
                var target = System.IO.Path.Combine(targetPath, file.Name);

                if (ignoreMissing && (false == System.IO.File.Exists(source)))
                {
                    _logger.InfoFormat("copy: ignoring missing file, not copying: {0} -> {1}...", source, target);
                }
                _logger.InfoFormat("copy: {0} -> {1}...", source, target);
                System.IO.Directory.CreateDirectory(targetPath);
                System.IO.File.Copy(source, target, true);
            }
        }

    }
}
