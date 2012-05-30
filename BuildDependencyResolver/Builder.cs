using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using BuildDependencyReader.ProjectFileParser;
using System.Diagnostics;
using Common;

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

                System.IO.File.Copy(source, target, true);
            }
        }

        public static void BuildSolution(IProjectFinder projectFinder, string solutionFileName)
        {
            _logger.InfoFormat("Building Solution: '{0}'", solutionFileName);
            UpdateComponentsFromBuiltProjects(projectFinder, solutionFileName);
            _logger.InfoFormat("\tCleaning...");
            MSBuild(solutionFileName, "/t:clean");
            _logger.InfoFormat("\tBuilding...");
            MSBuild(solutionFileName);
            _logger.InfoFormat("\tDone: '{0}'", solutionFileName);
        }

        public static void UpdateComponentsFromBuiltProjects(IProjectFinder projectFinder, string solutionFileName)
        {
            _logger.InfoFormat("\tCopying dependencies...");
            Builder.CopyAssemblyReferencesFromBuiltProjects(projectFinder,
                                                            projectFinder.GetProjectsOfSLN(solutionFileName)
                                                                         .SelectMany(x => x.AssemblyReferences)
                                                                         .Distinct());
        }


        protected static void MSBuild(string solutionFileName, string args = "")
        {
            var process = new Process();
            process.StartInfo.CreateNoWindow = true;
            process.StartInfo.UseShellExecute = false;
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
