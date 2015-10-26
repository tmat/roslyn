// Copyright (c) Microsoft.  All Rights Reserved.  Licensed under the Apache License, Version 2.0.  See License.txt in the project root for license information.

using System;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Reflection;
using Microsoft.CodeAnalysis;
using Roslyn.Utilities;
using Microsoft.CodeAnalysis.Scripting.Hosting;
using System.Runtime.CompilerServices;

namespace Microsoft.CodeAnalysis.Scripting
{
    /// <summary>
    /// A class that represents a script that you can run.
    /// 
    /// Create a script using a language specific script class such as CSharpScript or VisualBasicScript.
    /// </summary>
    public abstract class Script
    {
        internal readonly ScriptCompiler Compiler;
        internal readonly ScriptBuilder Builder;

        private Compilation _lazyCompilation;

        internal Script(ScriptCompiler compiler, ScriptBuilder builder, string code, ScriptOptions options, Type globalsTypeOpt, Script previousOpt)
        {
            Debug.Assert(code != null);
            Debug.Assert(options != null);
            Debug.Assert(compiler != null);
            Debug.Assert(builder != null);

            Compiler = compiler;
            Builder = builder;
            Previous = previousOpt;
            Code = code;
            Options = options;
            GlobalsType = globalsTypeOpt;
        }

        internal static Script<T> CreateInitialScript<T>(ScriptCompiler compiler, string codeOpt, ScriptOptions optionsOpt, Type globalsTypeOpt, InteractiveAssemblyLoader assemblyLoaderOpt)
        {
            return new Script<T>(compiler, new ScriptBuilder(assemblyLoaderOpt ?? new InteractiveAssemblyLoader()), codeOpt ?? "", optionsOpt ?? ScriptOptions.Default, globalsTypeOpt, previousOpt: null);
        }

        /// <summary>
        /// A script that will run first when this script is run. 
        /// Any declarations made in the previous script can be referenced in this script.
        /// The end state from running this script includes all declarations made by both scripts.
        /// </summary>
        public Script Previous { get; }

        /// <summary>
        /// The options used by this script.
        /// </summary>
        public ScriptOptions Options { get; }

        /// <summary>
        /// The source code of the script.
        /// </summary>
        public string Code { get; }

        /// <summary>
        /// The type of an object whose members can be accessed by the script as global variables.
        /// </summary>
        public Type GlobalsType { get; }

        /// <summary>
        /// The expected return type of the script.
        /// </summary>
        public abstract Type ReturnType { get; }

        /// <summary>
        /// Creates a new version of this script with the specified options.
        /// </summary>
        public Script WithOptions(ScriptOptions options) => WithOptionsInternal(options);
        internal abstract Script WithOptionsInternal(ScriptOptions options);

        /// <summary>
        /// Creates a new version of this script with the source code specified.
        /// </summary>
        /// <param name="code">The source code of the script.</param>
        public Script WithCode(string code) => WithCodeInternal(code);
        internal abstract Script WithCodeInternal(string code);

        /// <summary>
        /// Creates a new version of this script with the specified globals type. 
        /// The members of this type can be accessed by the script as global variables.
        /// </summary>
        /// <param name="globalsType">The type that defines members that can be accessed by the script.</param>
        public Script WithGlobalsType(Type globalsType) => WithGlobalsTypeInternal(globalsType);
        internal abstract Script WithGlobalsTypeInternal(Type globalsType);

        /// <summary>
        /// Continues the script with given code snippet.
        /// </summary>
        public Script<object> ContinueWith(string code, ScriptOptions options = null) =>
            ContinueWith<object>(code, options);

        /// <summary>
        /// Continues the script with given code snippet.
        /// </summary>
        public Script<TResult> ContinueWith<TResult>(string code, ScriptOptions options = null) =>
            new Script<TResult>(Compiler, Builder, code ?? "", options ?? Options, GlobalsType, this);

        /// <summary>
        /// Get's the <see cref="Compilation"/> that represents the semantics of the script.
        /// </summary>
        public Compilation GetCompilation()
        {
            if (_lazyCompilation == null)
            {
                var compilation = Compiler.CreateSubmission(this);
                Interlocked.CompareExchange(ref _lazyCompilation, compilation, null);
            }

            return _lazyCompilation;
        }

        /// <summary>
        /// Runs the script from the beginning and returns the result of the last code snippet.
        /// </summary>
        /// <param name="globals">
        /// An instance of <see cref="Script.GlobalsType"/> holding on values of global variables accessible from the script.
        /// Must be specified if and only if the script was created with a <see cref="Script.GlobalsType"/>.
        /// </param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>The result of the last code snippet.</returns>
        public Task<object> EvaluateAsync(object globals = null, CancellationToken cancellationToken = default(CancellationToken)) =>
            CommonEvaluateAsync(globals, cancellationToken);

        internal abstract Task<object> CommonEvaluateAsync(object globals, CancellationToken cancellationToken);

        /// <summary>
        /// Runs the script from the beginning.
        /// </summary>
        /// <param name="globals">
        /// An instance of <see cref="Script.GlobalsType"/> holding on values for global variables accessible from the script.
        /// Must be specified if and only if the script was created with <see cref="Script.GlobalsType"/>.
        /// </param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>A <see cref="ScriptState"/> that represents the state after running the script, including all declared variables and return value.</returns>
        public Task<ScriptState> RunAsync(object globals = null, CancellationToken cancellationToken = default(CancellationToken)) =>
            CommonRunAsync(globals, cancellationToken);

        internal abstract Task<ScriptState> CommonRunAsync(object globals, CancellationToken cancellationToken);

        /// <summary>
        /// Continue script execution from the specified state.
        /// </summary>
        /// <param name="previousState">
        /// Previous state of the script execution.
        /// </param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>A <see cref="ScriptState"/> that represents the state after running the script, including all declared variables and return value.</returns>
        public Task<ScriptState> ContinueAsync(ScriptState previousState, CancellationToken cancellationToken = default(CancellationToken)) =>
            CommonContinueAsync(previousState, cancellationToken);

        internal abstract Task<ScriptState> CommonContinueAsync(ScriptState previousState, CancellationToken cancellationToken);

        /// <summary>
        /// Forces the script through the build step.
        /// If not called directly, the build step will occur on the first call to Run.
        /// </summary>
        /// <returns>All diagnostics (errors, warnigns, etc.) produced during compilation of the script.</returns>
        /// <remarks>
        /// If the script has multiple parts (chained thru <see cref="ContinueWith(string, ScriptOptions)"/>) returns diagnostics for all the parts.
        /// </remarks>
        public ImmutableArray<Diagnostic> Build(CancellationToken cancellationToken = default(CancellationToken)) =>
            CommonBuild(cancellationToken);

        internal abstract ImmutableArray<Diagnostic> CommonBuild(CancellationToken cancellationToken);
        internal abstract Func<object[], Task> CommonGetExecutorAndDiagnostics(out ImmutableArray<Diagnostic> diagnostics, CancellationToken cancellationToken);

        /// <summary>
        /// Gets the references that need to be assigned to the compilation.
        /// This can be different than the list of references defined by the <see cref="ScriptOptions"/> instance.
        /// </summary>
        internal ImmutableArray<MetadataReference> GetReferencesForCompilation(
            CommonMessageProvider messageProvider,
            DiagnosticBag diagnostics,
            MetadataReference languageRuntimeReferenceOpt = null)
        {
            var resolver = Options.MetadataResolver;
            var references = ArrayBuilder<MetadataReference>.GetInstance();
            try
            {
                var previous = Previous;
                if (previous != null)
                {
                    // TODO: this should be done in reference manager
                    references.AddRange(previous.GetCompilation().References);
                }
                else
                {
                    var corLib = MetadataReference.CreateFromAssemblyInternal(typeof(object).GetTypeInfo().Assembly);
                    references.Add(corLib);

                    if (GlobalsType != null)
                    {
                        var globalsTypeAssembly = MetadataReference.CreateFromAssemblyInternal(GlobalsType.GetTypeInfo().Assembly);
                        references.Add(globalsTypeAssembly);
                    }

                    if (languageRuntimeReferenceOpt != null)
                    {
                        references.Add(languageRuntimeReferenceOpt);
                    }
                }

                foreach (var reference in Options.MetadataReferences)
                {
                    var unresolved = reference as UnresolvedMetadataReference;
                    if (unresolved != null)
                    {
                        var resolved = resolver.ResolveReference(unresolved.Reference, null, unresolved.Properties);
                        if (resolved.IsDefault)
                        {
                            diagnostics.Add(messageProvider.CreateDiagnostic(messageProvider.ERR_MetadataFileNotFound, Location.None, unresolved.Reference));
                        }
                        else
                        {
                            references.AddRange(resolved);
                        }
                    }
                    else
                    {
                        references.Add(reference);
                    }
                }

                return references.ToImmutable();
            }
            finally
            {
                references.Free();

            }
        }
    }

    public sealed class Script<T> : Script
    {
        private ImmutableArray<Func<object[], Task>> _lazyPrecedingExecutors;
        private Func<object[], Task<T>> _lazyExecutor;

        internal Script(ScriptCompiler compiler, ScriptBuilder builder, string code, ScriptOptions options, Type globalsTypeOpt, Script previousOpt)
            : base(compiler, builder, code, options, globalsTypeOpt, previousOpt)
        {
        }

        public override Type ReturnType => typeof(T);

        public new Script<T> WithOptions(ScriptOptions options)
        {
            return (options == Options) ? this : new Script<T>(Compiler, Builder, Code, options, GlobalsType, Previous);
        }

        public new Script<T> WithCode(string code)
        {
            code = code ?? "";
            return (code == Code) ? this : new Script<T>(Compiler, Builder, code, Options, GlobalsType, Previous);
        }

        public new Script<T> WithGlobalsType(Type globalsType)
        {
            return (globalsType == GlobalsType) ? this : new Script<T>(Compiler, Builder, Code, Options, globalsType, Previous);
        }

        internal override Script WithOptionsInternal(ScriptOptions options) => WithOptions(options);
        internal override Script WithCodeInternal(string code) => WithCode(code);
        internal override Script WithGlobalsTypeInternal(Type globalsType) => WithGlobalsType(globalsType);

        internal override Func<object[], Task> CommonGetExecutorAndDiagnostics(out ImmutableArray<Diagnostic> diagnostics, CancellationToken cancellationToken)
            => GetExecutorAndDiagnostics(out diagnostics, cancellationToken);

        internal override Task<object> CommonEvaluateAsync(object globals, CancellationToken cancellationToken) =>
            EvaluateAsync(globals, cancellationToken).CastAsync<T, object>();

        internal override Task<ScriptState> CommonRunAsync(object globals, CancellationToken cancellationToken) =>
            RunAsync(globals, cancellationToken).CastAsync<ScriptState<T>, ScriptState>();

        internal override Task<ScriptState> CommonContinueAsync(ScriptState previousState, CancellationToken cancellationToken) =>
            ContinueAsync(previousState, cancellationToken).CastAsync<ScriptState<T>, ScriptState>();

        internal override ImmutableArray<Diagnostic> CommonBuild(CancellationToken cancellationToken)
        {
            ImmutableArray<ImmutableArray<Diagnostic>> precedingDiagnostics;
            GetPrecedingExecutors(out precedingDiagnostics, cancellationToken);

            ImmutableArray<Diagnostic> currentDiagnostics;
            GetExecutorAndDiagnostics(out currentDiagnostics, cancellationToken);

            return MergeDiagnostics(precedingDiagnostics, currentDiagnostics);
        }

        /// <exception cref="CompilationErrorException">Compilation has errors.</exception>
        private void GetExecutorsThrowing(out ImmutableArray<Func<object[], Task>> preceding, out Func<object[], Task<T>> current, CancellationToken cancellationToken)
        {
            ImmutableArray<Diagnostic> currentDiagnostics;
            ImmutableArray<ImmutableArray<Diagnostic>> precedingDiagnostics;

            // We must build executors in order, preceding first then current:
            preceding = GetPrecedingExecutors(out precedingDiagnostics, cancellationToken);
            current = GetExecutorAndDiagnostics(out currentDiagnostics, cancellationToken);

            ThrowOnError(precedingDiagnostics, currentDiagnostics);
        }

        private void ThrowOnError(ImmutableArray<ImmutableArray<Diagnostic>> preceding, ImmutableArray<Diagnostic> current)
        {
            Diagnostic firstError = TryGetFirstError(preceding, current);
            if (firstError != null)
            {
                throw new CompilationErrorException(
                    Compiler.DiagnosticFormatter.Format(firstError, CultureInfo.CurrentCulture),
                    MergeDiagnostics(preceding, current));
            }
        }

        private ImmutableArray<Diagnostic> MergeDiagnostics(ImmutableArray<ImmutableArray<Diagnostic>> preceding, ImmutableArray<Diagnostic> current)
        {
            var allDiagnostics = ArrayBuilder<Diagnostic>.GetInstance();

            foreach (var diagnostics in preceding)
            {
                allDiagnostics.AddRange(diagnostics);
            }

            allDiagnostics.AddRange(current);

            return allDiagnostics.ToImmutableAndFree();
        }

        private Diagnostic TryGetFirstError(ImmutableArray<ImmutableArray<Diagnostic>> preceding, ImmutableArray<Diagnostic> current)
        {
            foreach (var diagnostics in preceding)
            {
                Diagnostic firstError = diagnostics.FirstOrDefault(d => d.Severity == DiagnosticSeverity.Error);
                if (firstError != null)
                {
                    return firstError;
                }
            }

            return current.FirstOrDefault(d => d.Severity == DiagnosticSeverity.Error);
        }

        private Func<object[], Task<T>> GetExecutorAndDiagnostics(out ImmutableArray<Diagnostic> diagnostics, CancellationToken cancellationToken)
        {
            if (_lazyExecutor == null)
            {
                var executorAndDiagnostics = Builder.CreateExecutor<T>(GetCompilation(), cancellationToken);
                if (Interlocked.CompareExchange(ref _lazyExecutor, executorAndDiagnostics.Item1, null) == null)
                {
                    diagnostics = executorAndDiagnostics.Item2;
                }
            }

            return _lazyExecutor;
        }

        private ImmutableArray<Func<object[], Task>> GetPrecedingExecutors(CancellationToken cancellationToken)
        {
            if (_lazyPrecedingExecutors == null)
            {
                var preceding = TryGetPrecedingExecutors(null, cancellationToken);
                Debug.Assert(preceding != null);
                InterlockedOperations.Initialize(ref _lazyPrecedingExecutors, preceding);
            }

            return _lazyPrecedingExecutors;
        }

        private ImmutableArray<Func<object[], Task>> TryGetPrecedingExecutors(Script lastExecutedScriptInChainOpt, ArrayBuilder<Diagnostic> diagnostics, CancellationToken cancellationToken)
        {
            Script script = Previous;
            if (script == lastExecutedScriptInChainOpt)
            {
                return ImmutableArray<Func<object[], Task>>.Empty;
            }

            var scriptsReversed = ArrayBuilder<Script>.GetInstance();

            while (script != null && script != lastExecutedScriptInChainOpt)
            {
                scriptsReversed.Add(script);
                script = script.Previous;
            }

            if (lastExecutedScriptInChainOpt != null && script != lastExecutedScriptInChainOpt)
            {
                scriptsReversed.Free();
                return default(ImmutableArray<Func<object[], Task>>);
            }

            var executors = ArrayBuilder<Func<object[], Task>>.GetInstance(scriptsReversed.Count);

            // We need to build executors in the order in which they are chained,
            // so that assemblies created for the submissions are loaded in the correct order.
            for (int i = scriptsReversed.Count - 1; i >= 0; i--)
            {
                ImmutableArray<Diagnostic> scriptDiagnostics;
                executors.Add(scriptsReversed[i].CommonGetExecutorAndDiagnostics(out scriptDiagnostics, cancellationToken));
                diagnostics.AddRange();
            }

            return executors.ToImmutableAndFree();
        }

        /// <summary>
        /// Runs the script from the beginning and returns the result of the last code snippet.
        /// </summary>
        /// <param name="globals">
        /// An instance of <see cref="Script.GlobalsType"/> holding on values of global variables accessible from the script.
        /// Must be specified if and only if the script was created with a <see cref="Script.GlobalsType"/>.
        /// </param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>The result of the last code snippet.</returns>
        public new Task<T> EvaluateAsync(object globals = null, CancellationToken cancellationToken = default(CancellationToken)) =>
            RunAsync(globals, cancellationToken).GetEvaluationResultAsync();

        /// <summary>
        /// Runs the script from the beginning.
        /// </summary>
        /// <param name="globals">
        /// An instance of <see cref="Script.GlobalsType"/> holding on values for global variables accessible from the script.
        /// Must be specified if and only if the script was created with <see cref="Script.GlobalsType"/>.
        /// </param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>A <see cref="ScriptState"/> that represents the state after running the script, including all declared variables and return value.</returns>
        /// <exception cref="CompilationErrorException">Compilation has errors.</exception>
        /// <exception cref="ArgumentException">The type of <paramref name="globals"/> doesn't match <see cref="Script.GlobalsType"/>.</exception>
        public new Task<ScriptState<T>> RunAsync(object globals = null, CancellationToken cancellationToken = default(CancellationToken))
        {
            // The following validation and executor contruction may throw;
            // do so synchronously so that the exception is not wrapped in the task.

            ValidateGlobals(globals, GlobalsType);

            var executionState = ScriptExecutionState.Create(globals);

            Func<object[], Task<T>> currentExecutor;
            ImmutableArray<Func<object[], Task>> precedingExecutors;
            GetExecutorsThrowing(out precedingExecutors, out currentExecutor, cancellationToken);

            return RunSubmissionsAsync(executionState, precedingExecutors, currentExecutor, cancellationToken);
        }

        /// <summary>
        /// Creates a delegate that will run this script from the beginning when invoked.
        /// </summary>
        /// <remarks>
        /// The delegate doesn't hold on this script, its compilation or diagnostics.
        /// </remarks>
        /// <exception cref="CompilationErrorException">Script has compilation errors.</exception>
        public ScriptRunner<T> CreateDelegate(CancellationToken cancellationToken = default(CancellationToken))
        {
            Func<object[], Task<T>> currentExecutor;
            ImmutableArray<Func<object[], Task>> precedingExecutors;
            GetExecutorsThrowing(out precedingExecutors, out currentExecutor, cancellationToken);

            var globalsType = GlobalsType;

            return (globals, token) =>
            {
                ValidateGlobals(globals, globalsType);
                return ScriptExecutionState.Create(globals).RunSubmissionsAsync<T>(precedingExecutors, currentExecutor, token);
            };
        }

        /// <summary>
        /// Continue script execution from the specified state.
        /// </summary>
        /// <param name="previousState">
        /// Previous state of the script execution.
        /// </param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>A <see cref="ScriptState"/> that represents the state after running the script, including all declared variables and return value.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="previousState"/> is null.</exception>
        /// <exception cref="ArgumentException"><paramref name="previousState"/> is not a previous execution state of this script.</exception>
        /// <exception cref="CompilationErrorException">Script has compilation errors.</exception>
        public new Task<ScriptState<T>> ContinueAsync(ScriptState previousState, CancellationToken cancellationToken = default(CancellationToken))
        {
            // The following validation and executor contruction may throw;
            // do so synchronously so that the exception is not wrapped in the task.

            if (previousState == null)
            {
                throw new ArgumentNullException(nameof(previousState));
            }

            if (previousState.Script == this)
            {
                // this state is already the output of running this script.
                return Task.FromResult((ScriptState<T>)previousState);
            }

            // We must build executors in order, preceding first then current:
            var precedingExecutorsAndDiagnostics = TryGetPrecedingExecutors(previousState.Script, cancellationToken);
            if (precedingExecutorsAndDiagnostics == null)
            {
                throw new ArgumentException(ScriptingResources.StartingStateIncompatible, nameof(previousState));
            }

            ImmutableArray<Diagnostic> currentDiagnostics;
            var currentExecutor = GetExecutorAndDiagnostics(out currentDiagnostics, cancellationToken);

            ThrowOnError(precedingExecutorsAndDiagnostics.Item2, currentDiagnostics);

            ScriptExecutionState newExecutionState = previousState.ExecutionState.FreezeAndClone();
            return RunSubmissionsAsync(newExecutionState, precedingExecutorsAndDiagnostics.Item1, currentExecutor, cancellationToken);
        }

        private async Task<ScriptState<T>> RunSubmissionsAsync(ScriptExecutionState executionState, ImmutableArray<Func<object[], Task>> precedingExecutors, Func<object[], Task> currentExecutor, CancellationToken cancellationToken)
        {
            var result = await executionState.RunSubmissionsAsync<T>(precedingExecutors, currentExecutor, cancellationToken).ConfigureAwait(continueOnCapturedContext: true);
            return new ScriptState<T>(executionState, result, this);
        }

        private static void ValidateGlobals(object globals, Type globalsType)
        {
            if (globalsType != null)
            {
                if (globals == null)
                {
                    throw new ArgumentException(ScriptingResources.ScriptRequiresGlobalVariables, nameof(globals));
                }

                var runtimeType = globals.GetType().GetTypeInfo();
                var globalsTypeInfo = globalsType.GetTypeInfo();

                if (!globalsTypeInfo.IsAssignableFrom(runtimeType))
                {
                    throw new ArgumentException(string.Format(ScriptingResources.GlobalsNotAssignable, runtimeType, globalsTypeInfo), nameof(globals));
                }
            }
            else if (globals != null)
            {
                throw new ArgumentException(ScriptingResources.GlobalVariablesWithoutGlobalType, nameof(globals));
            }
        }
    }
}
