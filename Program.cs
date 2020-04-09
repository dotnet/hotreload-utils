using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Reflection.Metadata;
using System.Text;
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
		static void Main(string[] args)
		{
			OutputKind kind = OutputKind.DynamicallyLinkedLibrary;
			if (args.Length == 0) {
				Console.WriteLine("roslynildiff.exe originalfile.cs patch1.cs [patch2.cs patch3.cs ...]");
				return;
			}

			var sourcePath = args[0];
			var filename = Path.GetFileName(sourcePath);
			var filenameNoExt = Path.GetFileNameWithoutExtension(filename);
			var outputAsm = filenameNoExt + ".dll";

			AdhocWorkspace workspace = new AdhocWorkspace();
			Project project = workspace.AddProject ("Project", LanguageNames.CSharp);
			project = project.AddMetadataReference (MetadataReference.CreateFromFile (typeof(object).Assembly.Location));
			project = project.AddMetadataReference (MetadataReference.CreateFromFile (typeof(Enumerable).Assembly.Location));
			project = project.WithCompilationOptions (new CSharpCompilationOptions (kind));
			var document = project.AddDocument (filename, SourceText.From (File.ReadAllText (sourcePath), Encoding.Unicode));
			project = document.Project;
			Console.WriteLine ("Building baseline...");
			Compilation baseCompilation = project.GetCompilationAsync ().Result;

			bool failed = false;
			foreach (var diag in baseCompilation.GetDiagnostics ().Where (d => d.Severity == DiagnosticSeverity.Error)) {
				Console.WriteLine (diag);
				failed = true;
			}

			if (failed)
				return;

			var baselineImage = new MemoryStream();
			var baselinePdb = new MemoryStream();
			EmitResult result = baseCompilation.Emit (baselineImage, baselinePdb);
			if (!result.Success) {
				foreach (var diag in result.Diagnostics.Where (d => d.Severity == DiagnosticSeverity.Error))
					Console.WriteLine (diag);
				
				return;
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


			Console.WriteLine ("Make changes and press enter");
			Console.ReadLine ();

			
			var changedSymbolsPerDoc = new Dictionary<DocumentId, HashSet<ISymbol>> ();
			/*Project currentProject = project;
			for (int i = 0; i < currentProject.DocumentIds.Count; i++) {
				Document document = currentProject.GetDocument (project.DocumentIds[i]);*/
				string contents = File.ReadAllText (sourcePath);
				Document updatedDocument = document.WithText (SourceText.From (contents, Encoding.UTF8));
				project = updatedDocument.Project;

				var changes = updatedDocument.GetTextChangesAsync (document).Result.ToArray();
				if (changes.Length == 0)
					return;//continue

				Console.WriteLine ($"Found changes in {document.FilePath}");

				SemanticModel baselineModel = document.GetSemanticModelAsync ().Result;

				HashSet<ISymbol> changedSymbols = null;

				foreach (TextChange change in changes) {
					var symbol = baselineModel.GetEnclosingSymbol (change.Span.Start);
					Console.WriteLine ($"Found changes for symbol: {symbol}");
					if (changedSymbols == null)
						changedSymbolsPerDoc[document.Id] = changedSymbols = new HashSet<ISymbol> ();

					changedSymbols.Add (symbol);
				}
			//}

			var updatedCompilation = project.GetCompilationAsync ();

			List<SemanticEdit> edits = new List<SemanticEdit> ();

			foreach (var kvp in changedSymbolsPerDoc) {
				Document doc = project.GetDocument (kvp.Key);
				SemanticModel model = doc.GetSemanticModelAsync ().Result;

				foreach (ISymbol symbol in kvp.Value) {
					ISymbol updatedSymbol = model.Compilation.GetSymbolsWithName (symbol.Name, SymbolFilter.Member).Single();
					edits.Add (new SemanticEdit (SemanticEditKind.Update, symbol, updatedSymbol));
				}
			}

			foreach (var diag in updatedCompilation.Result.GetDiagnostics ().Where (d => d.Severity == DiagnosticSeverity.Error)) {
				Console.WriteLine (diag);
				failed = true;
			}

			if (failed)
				return;

			var changeCount = ".1";

			using (var metaStream = File.Create (outputAsm + changeCount + ".dmeta"))
			using (var ilStream = File.Create (outputAsm + changeCount + ".dil")) 
			using (var pdbStream = File.Create (outputAsm + changeCount + ".dpdb")) {
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
	}
}
