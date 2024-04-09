using System;

namespace EnC.Tests;

public class GlobalFilesFixture : IDisposable
{
    public string GlobalJsonContents { get; }
    public string NugetConfigContents { get; }

    public GlobalFilesFixture()
    {
        using (var s = typeof(GlobalFilesFixture).Assembly.GetManifestResourceStream("projectData/global.json"))
        {
            if (s == null)
                throw new Exception("Couldn't get global.json");
            using var sr = new System.IO.StreamReader(s);
            GlobalJsonContents = sr.ReadToEnd();
        }
        using (var s = typeof(GlobalFilesFixture).Assembly.GetManifestResourceStream("projectData/NuGet.config"))
        {
            if (s == null)
                throw new Exception("Couldn't get NuGet.config");
            using var sr = new System.IO.StreamReader(s);
            NugetConfigContents = sr.ReadToEnd();
        }
    }

    public void Dispose()
    {
    }

}
