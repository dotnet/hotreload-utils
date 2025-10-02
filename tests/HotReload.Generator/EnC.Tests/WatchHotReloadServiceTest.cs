// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using CancellationToken = System.Threading.CancellationToken;
using System.Linq;

using System.Reflection.Metadata;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Emit;
using Microsoft.CodeAnalysis.Text;
using Microsoft.DotNet.HotReload.Utils.Generator.EnC;

using Xunit;

namespace EnC.Tests;

public class WatchHotReloadServiceTest : TempMSBuildWorkspaceTest
{
    public WatchHotReloadServiceTest(GlobalFilesFixture globalFiles) : base(globalFiles)
    {
    }

    [Fact]
    public async Task SanityCheckWatchService()
    {
        var cancellationToken = CancellationToken.None;
        var project = await PrepareProject(cancellationToken);
        var src1 = MakeText("""
            using System;
            public class C1 {
                public static void M1() {
                    Console.WriteLine("Hello");
                }
            }
            """);
        WithBaselineSource(ref project, "Class1.cs", src1, out var documentId);
        var comp = await project.GetCompilationAsync(cancellationToken);
        Assert.NotNull(comp);

        using (var peStream = File.OpenWrite(project.CompilationOutputInfo.AssemblyPath!))
        using (var pdbStream = File.OpenWrite(Path.ChangeExtension(project.CompilationOutputInfo.AssemblyPath!, ".pdb")))
        {
            var emitResult = comp.Emit(peStream, pdbStream, options: new EmitOptions(debugInformationFormat: DebugInformationFormat.PortablePdb), cancellationToken: TestContext.Current.CancellationToken);
            ValidateEmitResult(emitResult);
        }

        var hostWorkspaceServices = project.Solution.Workspace.Services;
        var changeMakerService = ChangeMakerService.Make(hostWorkspaceServices, EditAndContinueCapabilities.Baseline);

        await changeMakerService.StartSessionAsync(project.Solution, cancellationToken);

        var src2 = MakeText("""
            using System;
            public class C1 {
                public static void M1() {
                    Console.WriteLine("Updated");
                }
            }
            """);

        var newSolution = project.Solution.WithDocumentText(documentId, src2);

        var update = await changeMakerService.EmitSolutionUpdateAsync(newSolution, cancellationToken);
        Assert.NotEmpty(update.ProjectUpdates);

        changeMakerService.CommitUpdate();

        changeMakerService.EndSession();
    }
}
