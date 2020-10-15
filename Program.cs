using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Reflection.Metadata;
using System.Text;
using System.Threading;
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
			var libs = new List<string>();
			var files = new List<string>();
			OutputKind kind = OutputKind.ConsoleApplication;

			for (int i = 0; i < args.Length; i++) {
				string fn = args [i];
				if (fn.StartsWith ("-l:")) {
					libs.Add (fn.Substring (3));
				} else if (fn == "-target:library") {
					kind = OutputKind.DynamicallyLinkedLibrary;
				} else if (!File.Exists (fn)) {
					Console.WriteLine ($"File {fn} doesn't exist");
					return 2;
				} else {
					files.Add (fn);
				}
			}

			if (files.Count <= 1) {
				Console.WriteLine("roslynildiff.exe originalfile.cs patch1.cs [patch2.cs patch3.cs ...]");
			}

			var sourcePath = files[0];
			var filename = Path.GetFileName(sourcePath);
			var filenameNoExt = Path.GetFileNameWithoutExtension(filename);
			var outputAsm = filenameNoExt + ".dll";

			AdhocWorkspace workspace = new AdhocWorkspace();
			Project project = workspace.AddProject ("CalcKit", LanguageNames.CSharp);
			project = project.AddMetadataReference (MetadataReference.CreateFromFile (typeof(object).Assembly.Location));
			project = project.AddMetadataReference (MetadataReference.CreateFromFile (typeof(Enumerable).Assembly.Location));
			project = project.AddMetadataReference (MetadataReference.CreateFromFile (typeof(Semaphore).Assembly.Location));
			foreach (string lib in libs) {
				project = project.AddMetadataReference (MetadataReference.CreateFromFile (lib));
			}
			project = project.WithCompilationOptions (new CSharpCompilationOptions (kind));
			var document = project.AddDocument (name: filename, text: SourceText.From (File.ReadAllText (sourcePath), Encoding.Unicode), folders: null, filePath: filename);
			project = document.Project;
			Console.WriteLine ("Building baseline...");
			Compilation baseCompilation = project.GetCompilationAsync ().Result;

			bool failed = false;
			foreach (var diag in baseCompilation.GetDiagnostics ().Where (d => d.Severity == DiagnosticSeverity.Error)) {
				Console.WriteLine (diag);
				failed = true;
			}

			if (failed)
				return 3;

			var baselineImage = new MemoryStream();
			var baselinePdb = new MemoryStream();
			EmitResult result = baseCompilation.Emit (baselineImage, baselinePdb);
			if (!result.Success) {
				foreach (var diag in result.Diagnostics.Where (d => d.Severity == DiagnosticSeverity.Error))
					Console.WriteLine (diag);
				
				return 4;
			}

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

			for (int i = 1; i < files.Count; i++) {
				var changedSymbolsPerDoc = new Dictionary<DocumentId, HashSet<ISymbol>> ();
				Console.WriteLine ($"parsing patch {files [i]} and creating delta");

				string contents = File.ReadAllText (files [i]);
				Document updatedDocument = document.WithText (SourceText.From (contents, Encoding.UTF8));
				project = updatedDocument.Project;

				var changes = updatedDocument.GetTextChangesAsync (document).Result.ToArray();
				if (changes.Length == 0) {
					Console.WriteLine ("no changes found");
					return 5;//continue
				}

				Console.WriteLine ($"Found changes in {document.Name}");

				SemanticModel baselineModel = document.GetSemanticModelAsync ().Result;

				HashSet<ISymbol> changedSymbols = null;

				foreach (TextChange change in changes) {
					var symbol = baselineModel.GetEnclosingSymbol (change.Span.Start);
					Console.WriteLine ($"Found changes for symbol: {symbol}");
					if (changedSymbols == null)
						changedSymbolsPerDoc[document.Id] = changedSymbols = new HashSet<ISymbol> ();

					changedSymbols.Add (symbol);
				}

				var updatedCompilation = project.GetCompilationAsync ();

				foreach (var kvp in changedSymbolsPerDoc) {
					Document doc = project.GetDocument (kvp.Key);
					SemanticModel model = doc.GetSemanticModelAsync ().Result;

					foreach (ISymbol symbol in kvp.Value) {
						try {
							ISymbol updatedSymbol = model.Compilation.GetSymbolsWithName (symbol.Name, SymbolFilter.Member).Single();
							edits.Add (new SemanticEdit (SemanticEditKind.Update, symbol, updatedSymbol));
						} catch (System.InvalidOperationException e) {
							// fixme
							continue;
						}
					}
				}

				foreach (var diag in updatedCompilation.Result.GetDiagnostics ().Where (d => d.Severity == DiagnosticSeverity.Error)) {
					Console.WriteLine (diag);
					failed = true;
				}

				if (failed)
					return 6;

				using (var metaStream = File.Create (outputAsm + "." + i + ".dmeta"))
				using (var ilStream   = File.Create (outputAsm + "." + i + ".dil"))
				using (var pdbStream  = File.Create (outputAsm + "." + i + ".dpdb")) {
					var updatedMethods = new List<MethodDefinitionHandle> ();
					EmitDifferenceResult emitResult = updatedCompilation.Result.EmitDifference (baseline, edits, metaStream, ilStream, pdbStream, updatedMethods);
					if (!emitResult.Success) {
						Console.WriteLine ("Emit failed");
						foreach (var diag in result.Diagnostics.Where (d => d.Severity == DiagnosticSeverity.Error))
							Console.WriteLine (diag);
					}

					metaStream.Flush ();
					ilStream.Flush();
					pdbStream.Flush();
				}
			}
			return 0;
		}
	}
}
