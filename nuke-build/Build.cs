using System;
using System.IO;
using Nuke.Common;
using Nuke.Common.IO;
using Nuke.Common.ProjectModel;
using Nuke.Common.Tooling;
using Nuke.Common.Tools.DotNet;
using Nuke.Common.Tools.MSBuild;
using Nuke.Common.Tools.NuGet;
using Nuke.Common.Utilities.Collections;
using static Nuke.Common.IO.FileSystemTasks;
using static Nuke.Common.IO.PathConstruction;
using static Nuke.Common.Tools.DotNet.DotNetTasks;
using static Nuke.Common.Tools.NuGet.NuGetTasks;
using static Nuke.Common.Tools.Git.GitTasks;
using System.Collections.Concurrent;
using Semver;

class Build : NukeBuild {

    public static int Main() => Execute<Build>(x => x.Finalize);

    SemVersion CurrentPackageVersion { get; set; }
    SemVersion NextPackageVersion { get; set; }

    static AbsolutePath OutputDirectory => RootDirectory / "nuke-build" / "output";
    static AbsolutePath NuspecFilename => RootDirectory / "RepositorySettings" / "RepositorySettings.nuspec";

    Target Clean => _ => _
        .Executes(() => {
            EnsureCleanDirectory(OutputDirectory);
        });

    Target GetVersionInfo => _ => _
        .DependsOn(Clean)
        .Executes(() => {
            // Extracting current version. For preview releases, this will save some typing, as the version does not change.
            var currentVersionString = ExtractVersionString(NuspecFilename, "<version>", "</version>");
            CurrentPackageVersion = SemVersion.Parse(currentVersionString, SemVersionStyles.Strict);

            Console.WriteLine($"Current version is {CurrentPackageVersion}.");

            // Figuring out the branch repository is currently on. For non-release branches, like Development or Feature/*, we'll automatically add "preview" suffixes.
            var commitsAheadOfRelease = 0;
            var currentBranch = GitCurrentBranch();
            if ((currentBranch == "Development") || currentBranch.StartsWith("Features/")) {
                var gitCommandResult = Git($"rev-list --count {GitCurrentBranch()} ^Release");
                // The following line is based on pure faith that nobody changes their underlying implementations...
                commitsAheadOfRelease = int.Parse(((BlockingCollection<Output>)gitCommandResult).Take().Text);

                NextPackageVersion = CurrentPackageVersion.WithPrerelease("preview", commitsAheadOfRelease.ToString());
                Serilog.Log.Information($"Branch is {currentBranch}, so that's a preview release. Version will be updated to {NextPackageVersion}");
            }
            else if ((currentBranch == "Release") || currentBranch.StartsWith("Releases/")) {
                NextPackageVersion = CurrentPackageVersion.WithoutPrerelease();
                Serilog.Log.Information($"Branch is {currentBranch}, so that's NOT a preview release. Version will be updated to {NextPackageVersion}");
            }
            else {
                Assert.Fail("You're on some illegal branch!");
            }

            ReportSummary(_ => _.AddPair("Version", NextPackageVersion));
        });

    Target UpdateVersions => _ => _
        .DependsOn(GetVersionInfo)
        .Executes(() => {
            var assemblyInfoContent = File.ReadAllText(NuspecFilename);
            ReplaceVersion(ref assemblyInfoContent, NextPackageVersion.ToString(), "<version>", "</version>");
            File.WriteAllText(NuspecFilename, assemblyInfoContent);
        });


    Target Pack => _ => _
        .DependsOn(UpdateVersions)
        .Produces(OutputDirectory / "*.nupkg")
        .Executes(() => {
            NuGetPack(p => p.SetOutputDirectory(OutputDirectory).SetExclude("bin/**/*.*").SetTargetPath(NuspecFilename).EnableNoDefaultExcludes());
            ReportSummary(_ => _.AddPair("Packages", OutputDirectory.GlobFiles("*.nupkg").Count.ToString()));
        });

    Target Push => _ => _
       .DependsOn(Pack)
       .Executes(() => {
           var apiKey = Environment.GetEnvironmentVariable("GithubTevuxPackages");
           DotNetNuGetPush(_ => _
               .SetSource("https://nuget.pkg.github.com/tevux-tech/index.json")
               .SetApiKey(apiKey)
               .CombineWith(OutputDirectory.GlobFiles("*.nupkg").NotEmpty(), (_, v) => _.SetTargetPath(v)));

       });

    Target Tag => _ => _
       .DependsOn(Pack)
       .Executes(() => {
           Git($"tag Versions/{NextPackageVersion}");
       });

    Target Commit => _ => _
       .DependsOn(Tag)
       .Executes(() => {
           Git($"add .", workingDirectory: RootDirectory);
           Git($"commit -m \"Releasing {NextPackageVersion}.\"", workingDirectory: RootDirectory);
       });

    Target Finalize => _ => _
       .DependsOn(Commit)
       .Executes(() => {

       });


    string ExtractVersionString(string file, string startTag, string endTag) {
        var content = File.ReadAllText(file);

        var startPosition = content.IndexOf(startTag) + startTag.Length;
        var endPosition = content.IndexOf(endTag, startPosition);

        var returnVersion = content[startPosition..endPosition];

        return returnVersion;
    }
    static void ReplaceVersion(ref string content, string versionToInject, string startTag, string endTag) {
        var startPosition = content.IndexOf(startTag) + startTag.Length;
        var endPosition = content.IndexOf(endTag, startPosition);
        content = content.Remove(startPosition, endPosition - startPosition);
        content = content.Insert(startPosition, versionToInject);
    }
}
