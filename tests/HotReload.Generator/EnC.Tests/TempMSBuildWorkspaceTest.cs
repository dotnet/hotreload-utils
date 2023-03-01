using System;
using System.IO;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Emit;

using CancellationToken = System.Threading.CancellationToken;
using SourceText = Microsoft.CodeAnalysis.Text.SourceText;

using Xunit;

#nullable enable

namespace EnC.Tests;

/// <summary> Each test gets its own temporary directory and MSBuildWorkspace </summary>
/// <remarks>
///   The temporary directory gets a global.json and NuGet.config like the root of this git repo.
///   Each test gets its own MSBuildWorkspace, which is disposed after the test.
///   <para>
///   The temp directory is normally deleted after the test is done.
///    Set <c>TempDir.Keep = true</c> if you want to keep the temp directory.
///   </para>
/// </remarks>
public class TempMSBuildWorkspaceTest : IClassFixture<MSBuildLocatorFixture>, IClassFixture<GlobalFilesFixture>, IDisposable
{
    public Microsoft.CodeAnalysis.MSBuild.MSBuildWorkspace Workspace { get; }
    public GlobalFilesFixture GlobalFiles { get; }
    public TempDirectory TempDir { get; }
    public TempMSBuildWorkspaceTest(MSBuildLocatorFixture _1, GlobalFilesFixture globalFiles)
    {
        GlobalFiles = globalFiles;
        Workspace = Microsoft.CodeAnalysis.MSBuild.MSBuildWorkspace.Create();
        Workspace.WorkspaceFailed += OnWorkspaceFailed;
        TempDir = new TempDirectory();
    }

    public virtual void OnWorkspaceFailed(object? sender, WorkspaceDiagnosticEventArgs e)
    {
        if (e.Diagnostic.Kind == WorkspaceDiagnosticKind.Warning)
            return;
        throw new Exception($"MSBuildWorkspace {e.Diagnostic.Kind}: {e.Diagnostic.Message}");
    }

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    protected virtual void Dispose(bool disposing)
    {
        if (disposing)
        {
            Workspace?.Dispose();
            TempDir?.Dispose();
        }
    }

    private void PrepareGlobalFiles()
    {
        string globalJsonPath = Path.Combine(TempDir.Path, "global.json");
        using (var globalJson = new StreamWriter(globalJsonPath))
        {
            globalJson.Write(GlobalFiles.GlobalJsonContents);
        }
        string nugetConfigPath = Path.Combine(TempDir.Path, "NuGet.config");
        using (var nugetConfig = new StreamWriter(nugetConfigPath))
        {
            nugetConfig.Write(GlobalFiles.NugetConfigContents);
        }
    }

    private async Task<(Solution, ProjectId)> CreateProject(string projectContent, CancellationToken cancellationToken = default)
    {
        PrepareGlobalFiles();
        string projectPath = Path.Combine(TempDir.Path, "project.csproj");
        using (var projectFile = new StreamWriter(projectPath))
        {
            projectFile.Write(projectContent);
            projectFile.Flush();
        }
        var project = await Workspace.OpenProjectAsync(projectPath, cancellationToken: cancellationToken);
        return (Workspace.CurrentSolution, project.Id);
    }

    protected SourceText MakeText(string text) => SourceText.From(text, System.Text.Encoding.UTF8);

    protected async Task<Project> PrepareProject(CancellationToken cancellationToken = default)
    {
        (var solution, var projectId) = await CreateProject("""
            <Project Sdk="Microsoft.NET.Sdk">
                <PropertyGroup>
                    <OutputType>Library</OutputType>
                    <TargetFramework>net8.0</TargetFramework>
                    <EnableDefaultItems>false</EnableDefaultItems>
                </PropertyGroup>
                <ItemGroup>
                    <!-- Compile Include="Program.cs" -->
                </ItemGroup>
            </Project>
            """, cancellationToken);
        Assert.NotNull(solution);
        var project = solution.GetProject(projectId);
        Assert.NotNull(project);
        return project;
    }

    protected void WithBaselineSource(ref Project project, string csFileName, SourceText src, out DocumentId docId)
    {
        var d = project.AddDocument(csFileName, src);
        project = d.Project;
        docId = d.Id;
    }

    protected static void ValidateEmitResult(EmitResult emitResult, [CallerMemberName] string? caller = null, [CallerLineNumber] int? line = null, [CallerFilePath] string? file = null)
    {
        if (!emitResult.Success)
        {
            foreach (var diag in emitResult.Diagnostics)
            {
                Console.WriteLine(diag);
            }
            Assert.True(false, $"{file}:{line} EmitResult failed in {caller}");
        }
    }

}
