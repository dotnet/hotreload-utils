using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Runtime.Loader;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Emit;
using Microsoft.CodeAnalysis.Text;

namespace Microsoft.DotNet.HotReload.Utils.Generator.EnC
{
    //
    // Inspired by https://github.com/dotnet/roslyn/issues/8962
    public class ChangeMaker {

        private const string csharpCodeAnalysisAssemblyName = "Microsoft.CodeAnalysis.CSharp.Features";
        private const string codeAnalysisFeaturesAssemblyName = "Microsoft.CodeAnalysis.Features";
        private const string csharpCodeAnalyzerTypeName = "Microsoft.CodeAnalysis.CSharp.EditAndContinue.CSharpEditAndContinueAnalyzer";
        private const string activeStatementTypeName = "Microsoft.CodeAnalysis.EditAndContinue.ActiveStatement";

        private readonly Type _codeAnalyzer;
        private readonly Type _activeStatement;

        public ChangeMaker () {
            var (codeAnalyzer, activeStatement) = ReflectionInit();
            _codeAnalyzer = codeAnalyzer;
            _activeStatement = activeStatement;
        }
        // Get all the Roslyn stuff we need
        private static (Type codeAnalyzer, Type activeStatement) ReflectionInit ()
        {
            var an = new AssemblyName (csharpCodeAnalysisAssemblyName);
            var assm = AssemblyLoadContext.Default.LoadFromAssemblyName(an)!;
            var ca = assm.GetType(csharpCodeAnalyzerTypeName);
            if (ca == null) {
                throw new Exception ("Couldn't find CSharpCodeAnalyzer type");
            }

            an = new AssemblyName(codeAnalysisFeaturesAssemblyName);
            assm = AssemblyLoadContext.Default.LoadFromAssemblyName(an);
            var actS = assm.GetType (activeStatementTypeName);

            if (actS == null) {
                throw new Exception ("Coudln't find ActiveStatement type");
            }
            return (codeAnalyzer: ca, activeStatement: actS);
        }

        public Task<(ImmutableArray<SemanticEdit>, ImmutableArray<RudeEditDiagnosticWrapper>)> GetChanges(Document oldDocument, Document newDocument, CancellationToken cancellationToken = default)
        {
            // Effectively
            //
            // async Task<IEnumerable<SemanticEdit>> GetChanges (...) {
            //      var analyzer = new CSharpEditAndContinueAnalyzer (null);
            //      var activeStatements = ImmutableArray.Create<ActiveStatement>();
            //      var textSpans = ImmutableArray.Create<TextSpan>();
            //      var result = await analyzer.AnalyzeDocumentAsync (oldDocument, activeStatements, newDocument, textSpans, cancellationToken);
            //      var edits = result.SemanticEdits;
            //      var rudeEdits = result.RudeEditErrorss;
            //      return (edits, rudeEdits);
            // }
            //
            var analyzer = Activator.CreateInstance(_codeAnalyzer, new object?[]{null});

            var makeEmptyImmutableArray = typeof(ImmutableArray).GetMethod("Create", 1, Array.Empty<Type>())!.MakeGenericMethod(new Type[] {_activeStatement});
            var activeStatements = makeEmptyImmutableArray.Invoke(null, Array.Empty<object>())!;
            var mi = _codeAnalyzer.GetMethod("AnalyzeDocumentAsync")!;

            var textSpans = ImmutableArray.Create<TextSpan>();

            var taskResult = mi.Invoke (analyzer, new object[] {oldDocument, activeStatements, newDocument, textSpans, cancellationToken});

            if (taskResult == null) {
                throw new Exception("taskResult was null");
            }

            // We just want
            //   taskResult.ContinueWith ((t) => t.Result.SemanticEdits);
            // but then we'd need to make a Func<DocumentAnalysisResults, IEnumerable<SemanticEdit>>
            // and that's really annoying to do if you can't say the type name DocumentAnalysisResults
            //
            // So instead we do:
            //
            //  var tcs = new TaskCompletionSource<...>();
            //
            //  var awaiter = taskResult.GetAwaiter();
            //  awaiter.OnCompleted(delegate {
            //     tcs.SetResult (awaiter.GetResult().SemanticEdits); // and exn handling
            //   });
            //  return tcs.Task;
            //
            //  because OnCompleted only needs an Action.

            var awaiter = taskResult.GetType().GetMethod("GetAwaiter")!.Invoke(taskResult, Array.Empty<object>())!;

            TaskCompletionSource<(ImmutableArray<SemanticEdit>, ImmutableArray<RudeEditDiagnosticWrapper>)> tcs = new ();

            Action onCompleted = delegate {
                try {
                    var result = awaiter.GetType().GetMethod("GetResult")!.Invoke(awaiter, Array.Empty<object>())!;
                    var edits = (ImmutableArray<SemanticEdit>)result.GetType().GetProperty("SemanticEdits")!.GetValue(result)!;

                    // type is ImmutableArray<RudeEditDiagnostic>
                    var rudeEditErrors = (System.Collections.IEnumerable)result.GetType().GetProperty("RudeEditErrors")!.GetValue(result)!;
                    // Type is RudeEditKind (enum)
                    FieldInfo? kindFieldInfo = null;
                    // Type is TextSpan
                    FieldInfo? spanFieldInfo = null;
                    var rudeEdits = new List<RudeEditDiagnosticWrapper> ();
                    foreach (var rudeEditError in rudeEditErrors)
                    {
                        if (kindFieldInfo == null) {
                            kindFieldInfo = rudeEditError.GetType().GetField("Kind")!;
                        }
                        if (spanFieldInfo == null) {
                            spanFieldInfo = rudeEditError.GetType().GetField("Span")!;
                        }
                        var kind = kindFieldInfo.GetValue(rudeEditError)!.ToString()!;
                        var span = (TextSpan) spanFieldInfo.GetValue(rudeEditError)!;
                        rudeEdits.Add(new RudeEditDiagnosticWrapper(kind, span));
                    }
                    tcs.TrySetResult((edits, rudeEdits.ToImmutableArray()));
                } catch (TaskCanceledException e) {
                    tcs.TrySetCanceled(e.CancellationToken);
                } catch (Exception e) {
                    tcs.TrySetException (e);
                }
            };

            awaiter.GetType().GetMethod("OnCompleted")!.Invoke (awaiter, new object[] {onCompleted});

            return tcs.Task;
        }
    }
}
