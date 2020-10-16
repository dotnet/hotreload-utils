using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Reflection.Metadata;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Emit;
using Microsoft.CodeAnalysis.Text;
using Document = Microsoft.CodeAnalysis.Document;

namespace RoslynILDiff
{
	/*
	 * Limitations:
	 *	- Doesn't handle overloads
	 *  - Doesn't support preserve locals
	 */
	class Program
	{

		
		static int Main(string[] args)
		{
			if (!ParseArgs (args, out var config))
				return 2;
			
			var filename = config.Filename;
			var filenameNoExt = Path.GetFileNameWithoutExtension(filename);
			var outputAsm = filenameNoExt + ".dll";

			var (workspace, project, document) = PrepareProject (config, filenameNoExt);
			Console.WriteLine ("Building baseline...");

			if (!CheckCompilationDiagnostics (project.GetCompilationAsync(), "base", out var baseCompilation))
				return 3;

			var baselineImage = new MemoryStream();
			var baselinePdb = new MemoryStream();
			EmitResult result = baseCompilation.Emit (baselineImage, baselinePdb);
			if (!CheckEmitResult(result))	
				return 4;
			

			using (var baseLineFile = File.Create(outputAsm))
            {
				baselineImage.Seek(0, SeekOrigin.Begin);
				baselineImage.CopyTo(baseLineFile);
				baseLineFile.Flush();
            }
            using (var baseLinePdbFile = File.Create(filenameNoExt + ".pdb"))
            {
				baselinePdb.Seek(0, SeekOrigin.Begin);
				baselinePdb.CopyTo(baseLinePdbFile);
				baseLinePdbFile.Flush();
            }

			baselineImage.Seek(0, SeekOrigin.Begin);
			baselinePdb.Seek (0, SeekOrigin.Begin);
			var baselineMetadata = ModuleMetadata.CreateFromStream (baselineImage);
			EmitBaseline baseline = EmitBaseline.CreateInitialBaseline (baselineMetadata, (handle) => default);


			List<SemanticEdit> edits = new List<SemanticEdit> ();

			foreach (var (file, i) in config.DeltaFiles.Select ((value, i)=> (value, i))) {
				var changedSymbolsPerDoc = new Dictionary<DocumentId, HashSet<ISymbol>> ();
				Console.WriteLine ($"parsing patch #{i} from {file} and creating delta");

				string contents = File.ReadAllText (file);
				Document updatedDocument = document.WithText (SourceText.From (contents, Encoding.UTF8));
				project = updatedDocument.Project;

				var changes = updatedDocument.GetTextChangesAsync (document).Result;
				if (!changes.Any()) {
					Console.WriteLine ("no changes found");
					return 5;//continue
				}

				Console.WriteLine ($"Found changes in {document.Name}");

				SemanticModel baselineModel = document.GetSemanticModelAsync ().Result!;

				HashSet<ISymbol>? changedSymbols = null;

				foreach (TextChange change in changes) {
					var symbol = baselineModel.GetEnclosingSymbol (change.Span.Start);
					if (symbol == null) {
						Console.WriteLine ($"Change {change} doesn't have an enclosing symbol");
						continue;
					}
					Console.WriteLine ($"Found changes for symbol: {symbol}");
					if (changedSymbols == null)
						changedSymbolsPerDoc[document.Id] = changedSymbols = new HashSet<ISymbol> ();

					changedSymbols.Add (symbol);
				}

				var updatedCompilation = project.GetCompilationAsync ();

				foreach (var kvp in changedSymbolsPerDoc) {
					Document doc = project.GetDocument (kvp.Key)!;
					SemanticModel model = doc.GetSemanticModelAsync ().Result!;

					foreach (ISymbol symbol in kvp.Value) {
						try {
							ISymbol updatedSymbol = model.Compilation.GetSymbolsWithName (symbol.Name, SymbolFilter.Member).Single();
							edits.Add (new SemanticEdit (SemanticEditKind.Update, symbol, updatedSymbol));
						} catch (System.InvalidOperationException) {
							// fixme
							continue;
						}
					}
				}

				if (!CheckCompilationDiagnostics(updatedCompilation, $"delta {i}", out var updatedCompilationResult))
					return 6;


				using (var metaStream = File.Create (outputAsm + "." + i + ".dmeta"))
				using (var ilStream   = File.Create (outputAsm + "." + i + ".dil"))
				using (var pdbStream  = File.Create (outputAsm + "." + i + ".dpdb")) {
					var updatedMethods = new List<MethodDefinitionHandle> ();
					EmitDifferenceResult emitResult = updatedCompilationResult.EmitDifference (baseline, edits, metaStream, ilStream, pdbStream, updatedMethods);
					CheckEmitResult(emitResult);

					metaStream.Flush ();
					ilStream.Flush();
					pdbStream.Flush();

					// FIXME: don't we need this? the updated compilation should be the next baseline.
					// baseline = emitResult.Baseline;
				}
			}
			return 0;
		}


		static (Workspace, Project, Document) PrepareProject (Diffy.Config config, string projectName)
		{

			AdhocWorkspace workspace = new AdhocWorkspace();
			Project project = workspace.AddProject (projectName, LanguageNames.CSharp);
			switch (config.TfmType) {
				case Diffy.TfmType.Netcore:
					project = project.AddMetadataReference (MetadataReference.CreateFromFile (typeof(object).Assembly.Location));
					project = project.AddMetadataReference (MetadataReference.CreateFromFile (typeof(Enumerable).Assembly.Location));
					project = project.AddMetadataReference (MetadataReference.CreateFromFile (typeof(Semaphore).Assembly.Location));
					break;
				case Diffy.TfmType.MonoMono:
					// FIXME: hack
					if (config.BclBase == null)
						throw new Exception ("bcl base not specified for MonoMono compilation");
					project = project.AddMetadataReference (MetadataReference.CreateFromFile (Path.Combine(config.BclBase, "mscorlib.dll")));
					project = project.AddMetadataReference (MetadataReference.CreateFromFile (Path.Combine(config.BclBase, "System.Core.dll")));
					project = project.AddMetadataReference (MetadataReference.CreateFromFile (Path.Combine(config.BclBase, "System.dll")));
					break;
				default:
					throw new Exception($"unexpected TfmType {config.TfmType}");
			}

			foreach (string lib in config.Libs) {
				project = project.AddMetadataReference (MetadataReference.CreateFromFile (lib));
			}
			project = project.WithCompilationOptions (new CSharpCompilationOptions (config.OutputKind));
			var document = project.AddDocument (name: config.Filename, text: SourceText.From (File.ReadAllText (config.SourcePath), Encoding.Unicode), folders: null, filePath: config.SourcePath);
			project = document.Project;

			return (workspace, project, document);
		}

		/// <summary>Waits for compilation to finish, and writes the errors, if any.</summary>
		/// <returns>true if compilation succeeded, or false if there were errors</returns>
		static bool CheckCompilationDiagnostics (Task<Compilation?> compilation, string diagnosticPrefix, [NotNullWhen(true)] out Compilation? result)
		{
			result = compilation.Result;
			if (result == null) {
				Console.WriteLine ($"{diagnosticPrefix} compilation was null");
				return false;
			} else {
				bool failed = false;
				foreach (var diag in result.GetDiagnostics ().Where (d => d.Severity == DiagnosticSeverity.Error)) {
					Console.WriteLine ($"{diagnosticPrefix} --- {diag}");
					failed = true;
				}

				return !failed;
			}

		}

		/// <summary>Check <see cref="EmitResult"/> or <see cref="EmitDifferenceResult"/> for failures</summary>
		static bool CheckEmitResult (EmitResult emitResult)
		{
			if (!emitResult.Success) {
				Console.WriteLine ("Emit failed");
				foreach (var diag in emitResult.Diagnostics.Where (d => d.Severity == DiagnosticSeverity.Error))
					Console.WriteLine (diag);
			}
			return emitResult.Success;	
		}

		static bool ParseArgs (string[] args, [NotNullWhen(true)] out Diffy.Config? config)
		{
			var builder = Diffy.Config.Builder();
			
			for (int i = 0; i < args.Length; i++) {
				string fn = args [i];
				if (fn == "-mono") {
					builder.TfmType = Diffy.TfmType.MonoMono;
				} else if (fn.StartsWith("-bcl:")) {
					builder.BclBase = fn.Substring(5);
				} else if (fn.StartsWith ("-l:")) {
					builder.Libs.Add (fn.Substring (3));
				} else if (fn == "-target:library") {
					builder.OutputKind = OutputKind.DynamicallyLinkedLibrary;
				} else if (!File.Exists (fn)) {
					Console.WriteLine ($"File {fn} doesn't exist");
					config = null;
					return false;
				} else {
					builder.Files.Add (fn);
				}
			}

			if (builder.Files.Count <= 1) {
				Console.WriteLine("roslynildiff.exe originalfile.cs patch1.cs [patch2.cs patch3.cs ...]");
				config = null;
				return false;
			}

			config = builder.Bake();
			return true;
		}
	}
}
