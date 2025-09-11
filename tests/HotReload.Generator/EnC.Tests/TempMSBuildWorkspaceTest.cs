// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.IO;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Emit;
using Microsoft.CodeAnalysis.MSBuild;

using CancellationToken = System.Threading.CancellationToken;
using SourceText = Microsoft.CodeAnalysis.Text.SourceText;
using TempDirectory = Microsoft.DotNet.HotReload.Utils.Common.TempDirectory;

using Xunit;

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
public class TempMSBuildWorkspaceTest : IClassFixture<GlobalFilesFixture>, IDisposable
{
    public MSBuildWorkspace Workspace { get; }
    public GlobalFilesFixture GlobalFiles { get; }
    private protected TempDirectory TempDir { get; }
    public TempMSBuildWorkspaceTest(GlobalFilesFixture globalFiles)
    {
        GlobalFiles = globalFiles;
        Workspace = MSBuildWorkspace.Create();
        _ = Workspace.RegisterWorkspaceFailedHandler(OnWorkspaceFailed);
        TempDir = new TempDirectory();
    }

    public virtual void OnWorkspaceFailed(WorkspaceDiagnosticEventArgs e)
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
        string targetFramework = $"net{Environment.Version.Major}.{Environment.Version.Minor}";
        (var solution, var projectId) = await CreateProject($"""
            <Project Sdk="Microsoft.NET.Sdk">
                <PropertyGroup>
                    <OutputType>Library</OutputType>
                    <TargetFramework>{targetFramework}</TargetFramework>
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
        var d = project.AddDocument(csFileName, src, filePath: Path.Combine(project.FilePath!, csFileName));
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
            Assert.Fail($"{file}:{line} EmitResult failed in {caller}");
        }
    }

}
