// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Xunit;

// MSBuildWorkspace uses MS Build which can't have more than one build going at a time.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
