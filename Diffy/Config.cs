using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;

namespace Diffy
{
    public class Config 
    {
        public enum TfmType {
			Netcore,
			MonoMono
		}

        public static ConfigBuilder Builder () => new ConfigBuilder ();

        public class ConfigBuilder {
            internal ConfigBuilder () {}

            public List<string> Files {get; set;} = new List<string> ();
            public List<string> Libs {get; set; } = new List<String> ();

            public Config Bake() => new Config(this);
        }

        Config (ConfigBuilder builder) {
            Files = builder.Files;
            Libs = builder.Libs;

        }

        /// The libraries added to the project
        public IReadOnlyList<string> Libs { get; }
        /// The source file and its sequence of changes
        public IReadOnlyList<string> Files { get; }

        internal string SourcePath { get => Files[0]; }
        public string Filename { get => Path.GetFileName(SourcePath);}

        public IReadOnlyList<string> DeltaFiles { get => Files.Skip(1).ToList(); }


    }
}
