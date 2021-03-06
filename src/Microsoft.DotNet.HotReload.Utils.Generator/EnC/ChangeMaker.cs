// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

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
        private const string activeStatementsMapTypeName = "Microsoft.CodeAnalysis.EditAndContinue.ActiveStatementsMap";

        private const string capabilitiesTypeName = "Microsoft.CodeAnalysis.EditAndContinue.EditAndContinueCapabilities";

        private const string editSessionTypeName = "Microsoft.CodeAnalysis.EditAndContinue.EditSession";

        struct Reflected {
            internal Type _codeAnalyzer {get; init;}
            internal readonly Type _activeStatement {get; init;}

            internal readonly Type _activeStatementsMap {get; init;}

            internal readonly Type _capabilities {get; init;}

            internal readonly Type _editSession {get; init;}

        }

        private readonly Reflected _reflected;

        public Type EditAncContinueCapabilitiesType => _reflected._capabilities;

        public ChangeMaker () {
            _reflected = ReflectionInit();
        }
        // Get all the Roslyn stuff we need
        private static Reflected ReflectionInit ()
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

            var actSM = assm.GetType(activeStatementsMapTypeName);

            if (actSM == null) {
                throw new Exception ("Couldn't find ActiveStatementsMap");
            }

            var caps = assm.GetType (capabilitiesTypeName);

            if (caps == null) {
                throw new Exception ("Couldn't find EditAndContinueCapabilities type");
            }

            var editSess = assm.GetType (editSessionTypeName);

            if (editSess == null) {
                throw new Exception ("Couldn't find EditSession type");
            }

            return new Reflected() { _codeAnalyzer =  ca,
                                    _activeStatement =  actS,
                                    _activeStatementsMap = actSM,
                                    _capabilities =  caps,
                                    _editSession = editSess
                                    };
        }

        /// Convert my EditAndContinueCapabilities enum value to
        ///  [Microsoft.CodeAnalysis.Features]Microsoft.CodeAnalysis.EditAndContinue.EditAndContinueCapabilities
        private object ConvertCapabilities (EditAndContinueCapabilities myCaps)
        {
            int i = (int)myCaps;
            object theirCaps = Enum.ToObject(_reflected._capabilities, i);
            return theirCaps;
        }

        private object? makeEmptyActiveStatementsMap()
        {
            // return ActiveStatementsMap.Empty;
            var fi = _reflected._activeStatementsMap.GetField ("Empty", BindingFlags.Static | BindingFlags.Public)!;
            return fi.GetValue(null);
        }

                public Task<(DocumentAnalysisResultsWrapper, ImmutableArray<RudeEditDiagnosticWrapper>)> GetChanges(EditAndContinueCapabilities capabilities, Project oldProject, Document newDocument, CancellationToken cancellationToken = default)
        {
            // Effectively
            //
            // async Task<IEnumerable<SemanticEdit>> GetChanges (...) {
            //      var analyzer = new CSharpEditAndContinueAnalyzer (null);
            //      //var activeStatements = ImmutableArray.Create<ActiveStatement>();
            //      var activeStatementsMap = new ActiveStatementsMap (); // XXX
            //      var newActiveStatementSpans = ImmutableArray.Create<LinePositionSpans>();
            //      var capabilities = DefaultEditAndContinueCapabilities ();
            //      var result = await analyzer.AnalyzeDocumentAsync (oldDocument, activeStatements, newDocument, textSpans, capabilities, cancellationToken);
            //      var edits = result.SemanticEdits;
            //      var rudeEdits = result.RudeEditErrors;
            //      return (edits, rudeEdits);
            // }
            //
            var analyzer = Activator.CreateInstance(_reflected._codeAnalyzer, new object?[]{null});

            var makeEmptyImmutableArray = typeof(ImmutableArray).GetMethod("Create", 1, Array.Empty<Type>())!.MakeGenericMethod(new Type[] {_reflected._activeStatement});
            var activeStatementsMap = makeEmptyActiveStatementsMap();
            var mi = _reflected._codeAnalyzer.GetMethod("AnalyzeDocumentAsync")!;

            var newActiveStatementSpans = ImmutableArray.Create<LinePositionSpan>();

            var roslynCapabilities = ConvertCapabilities (capabilities);

            var taskResult = mi.Invoke (analyzer, new object?[] {oldProject, activeStatementsMap, newDocument, newActiveStatementSpans, capabilities, cancellationToken});

            if (taskResult == null) {
                throw new Exception("taskResult was null");
            }

            // We just want
            //   taskResult.ContinueWith ((t) => (t.Result, t.Result.RudeEdits);
            // but then we'd need to make a Func<DocumentAnalysisResults, IEnumerable<RudeEditErrors>>
            // and that's really annoying to do if you can't say the type name DocumentAnalysisResults
            //
            // So instead we do:
            //
            //  var tcs = new TaskCompletionSource<...>();
            //
            //  var awaiter = taskResult.GetAwaiter();
            //  awaiter.OnCompleted(delegate {
            //     tcs.SetResult (awaiter.GetResult().RudeEdits); // and exn handling
            //   });
            //  return tcs.Task;
            //
            //  because OnCompleted only needs an Action.

            var awaiter = taskResult.GetType().GetMethod("GetAwaiter")!.Invoke(taskResult, Array.Empty<object>())!;

            TaskCompletionSource<(DocumentAnalysisResultsWrapper, ImmutableArray<RudeEditDiagnosticWrapper>)> tcs = new ();

            Action onCompleted = delegate {
                try {
                    var result = awaiter.GetType().GetMethod("GetResult")!.Invoke(awaiter, Array.Empty<object>())!;
                    var wrappedResult = new DocumentAnalysisResultsWrapper(result);

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
                    tcs.TrySetResult((wrappedResult, rudeEdits.ToImmutableArray()));
                } catch (TaskCanceledException e) {
                    tcs.TrySetCanceled(e.CancellationToken);
                } catch (Exception e) {
                    tcs.TrySetException (e);
                }
            };

            awaiter.GetType().GetMethod("OnCompleted")!.Invoke (awaiter, new object[] {onCompleted});

            return tcs.Task;
        }


        private static MethodInfo GetImmutableArrayCreateSingletonMethod () {
            foreach (var mi in typeof(ImmutableArray).GetMethods(BindingFlags.Static|BindingFlags.Public)) {
                if (mi.Name != "Create")
                    continue;
                if (mi.GetGenericArguments().Length != 1)
                    continue;
                var pars = mi.GetParameters();
                if (pars.Length != 1)
                    continue;
                if (pars[0].ParameterType.IsArray)
                    continue;
                if (!pars[0].ParameterType.IsGenericParameter)
                    continue;
                return mi;
            }
            throw new Exception ("Could not find ImmutableArray.Create<T>(T arg)");
        }

        public object MakeSingleImmutableArrayDocumentAnalysisResults (DocumentAnalysisResultsWrapper w)
        {
            // ImmutableArray.Create<DocumentAnalsysisResult> (w.Underlying)
            MethodInfo mi = GetImmutableArrayCreateSingletonMethod ();

            var dar = w.Underlying;

            MethodInfo inst = mi.MakeGenericMethod(dar.GetType());

            var res = inst.Invoke(null, new object[] {dar});
            if (res == null)
                throw new NullReferenceException ("ImmutableArray.Create<DocumentAnalysisResults>(DocumentAnalysisResults single) returned null");
            return res;
        }

        public Task<ImmutableArray<SemanticEdit>> GetProjectChangesAsync (Compilation oldCompilation, Compilation newCompilation, Project oldProject, Project newProject, DocumentAnalysisResultsWrapper documentAnalysisResultsWrapper, CancellationToken ct = default)
        {
                // var projectChanges = EditSession.GetProjectChangesAsync (baseActiveStatementsMap, oldCompilation, newCompilation, oldProject, newProject)
                var getProjectChangesAsyncMethod = _reflected._editSession.GetMethod("GetProjectChangesAsync", BindingFlags.NonPublic | BindingFlags.Static);
                if (getProjectChangesAsyncMethod == null)
                    throw new Exception ("EditSession.GetProjectChangesAsync method not found in Roslyn.");

                var baseActiveStatementsMap = makeEmptyActiveStatementsMap();

                object results = MakeSingleImmutableArrayDocumentAnalysisResults (documentAnalysisResultsWrapper);

                var projectChangesValueTask = getProjectChangesAsyncMethod.Invoke (null, new object?[] {baseActiveStatementsMap, oldCompilation, newCompilation, oldProject, newProject, results, ct});

                if (projectChangesValueTask == null)
                    throw new Exception ("Did not expect GetProjectChangesAsync boxed ValueTask to be null");

                var t = projectChangesValueTask.GetType().GetMethod("AsTask")!.Invoke(projectChangesValueTask, Array.Empty<object>())!;

                var tcs = new TaskCompletionSource<ImmutableArray<SemanticEdit>>();
                object awaiter = t.GetType().GetMethod("GetAwaiter")!.Invoke(t, Array.Empty<object>())!;

                Action onCompleted = delegate {
                    try {
                        object? projectChanges = awaiter.GetType().GetMethod("GetResult")!.Invoke(awaiter, Array.Empty<object>());
                        ImmutableArray<SemanticEdit> edits;
                        if (projectChanges == null)
                            edits = ImmutableArray.Create<SemanticEdit>();
                        else {
                            edits = (ImmutableArray<SemanticEdit>) projectChanges.GetType().GetField("SemanticEdits")!.GetValue(projectChanges)!;
                        }
                        tcs.TrySetResult (edits);
                    } catch (TaskCanceledException e) {
                        tcs.TrySetCanceled(e.CancellationToken);
                    } catch (Exception e) {
                        tcs.TrySetException(e);
                    }
                };
                awaiter.GetType().GetMethod("OnCompleted")!.Invoke (awaiter, new object[] {onCompleted});
                return tcs.Task;
        }
    }
}
