// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Microsoft.CodeAnalysis;

namespace Microsoft.DotNet.HotReload.Utils.Generator;

/// What we know about the base compilation
///
/// baselineSolution: the solution we're working on
/// baselineProjectId: the project we're working on; FIXME: need to be more clever when there are project references
/// baselineOutputAsmPath: absolute path of the baseline assembly
/// docResolver: a map from document ids to documents
/// changeMakerService: A stateful encapsulatio of the series of changes that have been made to the baseline
public record struct BaselineArtifacts (Solution baselineSolution, ProjectId baselineProjectId, string baselineOutputAsmPath, DocResolver docResolver, EnC.ChangeMakerService changeMakerService);
