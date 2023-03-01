using Xunit;

// MSBuildWorkspace uses MS Build which can't have more than one build going at a time.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
