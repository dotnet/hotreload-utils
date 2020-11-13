using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;

namespace Diffy
{

    /// Are we using a .csproj file or is this an ad-hoc 'csc' compilation?
    public enum ProjectType {
        Msbuild,
        Adhoc
    }
    /// Only for ProjectType.Adhoc - are we targeting dotnet/runtime or mono/mono
    public enum TfmType {
        Netcore,
        MonoMono
    }

    public class Config
    {

        public static ConfigBuilder Builder () => new ConfigBuilder ();

        public class ConfigBuilder {
            internal ConfigBuilder () {}

            public ProjectType ProjectType {get; set;} = ProjectType.Adhoc;

            public List<string> Files {get; set; } = new List<string> ();
            public List<string> Libs {get; set; } = new List<String> ();

            public bool Barebones {get ; set; } = false;

            public List<KeyValuePair<string,string>> Properties {get; set;} = new List<KeyValuePair<string,string>> ();
            public TfmType TfmType {get; set; } = TfmType.Netcore;

            public string ProjectPath {get; set; } = "";
            public string? BclBase {get; set; } = default;

            public string OutputDir {get; set;} = ".";

            public Microsoft.CodeAnalysis.OutputKind OutputKind {get; set; } = Microsoft.CodeAnalysis.OutputKind.ConsoleApplication;
            public Config Bake () {
                switch (ProjectType) {
                    case ProjectType.Adhoc:
                        return new AdhocConfig(this);
                    case ProjectType.Msbuild:
                        return new MsbuildConfig(this);
                    default:
                        throw new Exception ("Expected ProjectType Adhoc or Msbuild");
                }
            }
        }

        protected Config (ConfigBuilder builder) {
            Files = builder.Files;
            Libs = builder.Libs;
            Properties = builder.Properties;
            ProjectType = builder.ProjectType;
            TfmType = builder.TfmType;
            OutputKind = builder.OutputKind;
            BclBase = builder.BclBase;
            OutputDir = builder.OutputDir;
            ProjectPath = builder.ProjectPath;
            Barebones = builder.Barebones;
        }

        /// The libraries added to the project
        public IReadOnlyList<string> Libs { get; }
        /// The source file and its sequence of changes
        public IReadOnlyList<string> Files { get; }

        public bool Barebones {get; }

        public IReadOnlyList<KeyValuePair<string,string>> Properties { get; }

        public ProjectType ProjectType { get; }
        public TfmType TfmType { get; }

        public string? BclBase { get; }

        /// If TfmType is Msbuild, this is the csproj for this project
        public string ProjectPath { get; }

        public Microsoft.CodeAnalysis.OutputKind OutputKind { get; }

        /// Destination path for baseline and deltas
        public string OutputDir { get; }

        /// The full path of the baseline source file
        internal string SourcePath { get => Files[0]; }
        /// Just the base file name of the baseline source file
        public string Filename { get => Path.GetFileName(SourcePath);}


        /// The baseline assembly name (in the sense of AsssemblyName.Name)
        public string ProjectName { get => Path.GetFileNameWithoutExtension(Filename); }
        /// Just the files containing sequence of changes
        public IReadOnlyList<string> DeltaFiles { get => Files.Skip(1).ToList(); }


    }

    internal class AdhocConfig : Config {
        internal AdhocConfig (ConfigBuilder builder) : base (builder) {}
    }

    internal class MsbuildConfig : Config {
        internal MsbuildConfig (ConfigBuilder builder) : base (builder) {}
    }
}
