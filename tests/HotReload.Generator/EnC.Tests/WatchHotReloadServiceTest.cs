// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using CancellationToken = System.Threading.CancellationToken;
using System.Linq;

using System.Reflection.Metadata;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Emit;

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
        var src = MakeText("""
            using System;
            public class C1 {
                public static void M1() {
                    Console.WriteLine("Hello");
                }
            }
            """);
        WithBaselineSource(ref project, "Class1.cs", src, out var d);
        var comp = await project.GetCompilationAsync(cancellationToken);
        Assert.NotNull(comp);
        using var peStream = new System.IO.MemoryStream();
        using var pdbStream = new System.IO.MemoryStream();
        var emitResult = comp.Emit(peStream, pdbStream);
        ValidateEmitResult(emitResult);

        var hostWorkspaceServices = project.Solution.Workspace.Services;
        var changeMakerService = Microsoft.DotNet.HotReload.Utils.Generator.EnC.ChangeMakerService.Make(hostWorkspaceServices, default);

        await changeMakerService.StartSessionAsync(project.Solution, cancellationToken);

        changeMakerService.EndSession();
        // var editSession = changeMakerService.GetEditSession();
        //Assert.NotNull(editSession);
    }
}
