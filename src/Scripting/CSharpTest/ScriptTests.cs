// Copyright (c) Microsoft.  All Rights Reserved.  Licensed under the Apache License, Version 2.0.  See License.txt in the project root for license information.

using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis.CSharp;
using Roslyn.Test.Utilities;
using Roslyn.Utilities;
using Xunit;

#pragma warning disable RS0003 // Do not directly await a Task

namespace Microsoft.CodeAnalysis.Scripting.CSharp.Test
{
    public class ScriptTests : TestBase
    {
        [Fact]
        public void CreateScript()
        {
            var script = CSharpScript.Create("1 + 2");
            Assert.Equal("1 + 2", script.Code);
        }

        [Fact]
        public void NoCode()
        {
            var s = CSharpScript.Create("using System;");
            s.GetCompilation().GetDiagnostics().Verify();
            s.RunAsync().Wait();
        }

        [Fact]
        public void CompilationErrors_Parse()
        {
            var s = CSharpScript.Create("{");

            s.Build().Verify(
                // (1,2): error CS1513: } expected
                Diagnostic(ErrorCode.ERR_RbraceExpected, ""));

            Assert.Throws<CompilationErrorException>(() => s.RunAsync().Wait());
        }

        [Fact]
        public void CompilationErrors_Semantic()
        {
            var s = CSharpScript.Create("Foo()");

            s.Build().Verify(
                // (1,1): error CS0103: The name 'Foo' does not exist in the current context
                Diagnostic(ErrorCode.ERR_NameNotInContext, "Foo").WithArguments("Foo"));

            Assert.Throws<CompilationErrorException>(() => s.RunAsync().Wait());
        }

        [Fact]
        public void CompilationErrors_Emit()
        {
            var identifier = new string('X', 4000);

            var s = CSharpScript.Create($"int {identifier} = 1;");

            s.GetCompilation().GetDiagnostics().Verify();
            s.Build().Verify(
                Diagnostic(ErrorCode.ERR_MetadataNameTooLong, identifier).WithArguments(identifier));

            Assert.Throws<CompilationErrorException>(() => s.RunAsync().Wait());
        }

        [Fact]
        public void CompilationWarnings()
        {
            var s = CSharpScript.Create<Task<int>>("System.Threading.Tasks.Task.FromResult(1)");

            s.GetCompilation().GetDiagnostics().Verify(
                // (1,1): warning CS4014: Because this call is not awaited, execution of the current method continues before the call is completed. Consider applying the 'await' operator to the result of the call.
                Diagnostic(ErrorCode.WRN_UnobservedAwaitableExpression, "System.Threading.Tasks.Task.FromResult(1)"));

            s.Build().Verify(
                // (1,1): warning CS4014: Because this call is not awaited, execution of the current method continues before the call is completed. Consider applying the 'await' operator to the result of the call.
                Diagnostic(ErrorCode.WRN_UnobservedAwaitableExpression, "System.Threading.Tasks.Task.FromResult(1)"));

            Assert.Equal(1, s.RunAsync().Result.ReturnValue.Result);
        }

        [Fact]
        public void PreviousErrorsAndWarnings()
        {
            var s = CSharpScript.
                Create("Foo()").
                ContinueWith("System.Threading.Tasks.Task.FromResult(1)").
                ContinueWith("Bar()").
                ContinueWith("System.Threading.Tasks.Task.FromResult(2)");

            var expected = new[]
            {
                // (1,1): error CS0103: The name 'Foo' does not exist in the current context
                Diagnostic(ErrorCode.ERR_NameNotInContext, "Foo").WithArguments("Foo"),
                // (1,1): warning CS4014: Because this call is not awaited, execution of the current method continues before the call is completed. Consider applying the 'await' operator to the result of the call.
                Diagnostic(ErrorCode.WRN_UnobservedAwaitableExpression, "System.Threading.Tasks.Task.FromResult(1)"),
                // (1,1): error CS0103: The name 'Bar' does not exist in the current context
                Diagnostic(ErrorCode.ERR_NameNotInContext, "Bar").WithArguments("Bar"),
                // (1,1): warning CS4014: Because this call is not awaited, execution of the current method continues before the call is completed. Consider applying the 'await' operator to the result of the call.
                Diagnostic(ErrorCode.WRN_UnobservedAwaitableExpression, "System.Threading.Tasks.Task.FromResult(2)")
            };

            s.Build().Verify(expected);

            CompilationErrorException exception = null;
            try
            {
                s.RunAsync().Wait();
            }
            catch (CompilationErrorException e)
            {
                exception = e;
            }

            exception.Diagnostics.Verify(expected);
        }

        [Fact]
        public async Task GetCompilation()
        {
            var state = await CSharpScript.RunAsync("1 + 2", globals: new ScriptTests());
            var compilation = state.Script.GetCompilation();
            Assert.Equal(state.Script.Code, compilation.SyntaxTrees.First().GetText().ToString());
        }

        [Fact]
        public void CreateScriptDelegate()
        {
            // create a delegate for the entire script
            var script = CSharpScript.Create("1 + 2");
            var fn = script.CreateDelegate();

            Assert.Equal(3, fn().Result);
            AssertEx.ThrowsArgumentException("globals", () => fn(new object()));
        }

        [Fact]
        public void CreateScriptDelegateWithGlobals()
        {
            // create a delegate for the entire script
            var script = CSharpScript.Create<int>("X + Y", globalsType: typeof(Globals));
            var fn = script.CreateDelegate();

            AssertEx.ThrowsArgumentException("globals", () => fn());
            AssertEx.ThrowsArgumentException("globals", () => fn(new object()));
            Assert.Equal(4, fn(new Globals { X = 1, Y = 3 }).Result);
        }

        [Fact]
        public async Task TestRunScript()
        {
            var state = await CSharpScript.RunAsync("1 + 2");
            Assert.Equal(3, state.ReturnValue);
        }

        [Fact]
        public async Task TestCreateAndRunScript()
        {
            var script = CSharpScript.Create("1 + 2");
            var state = await script.RunAsync();
            Assert.Same(script, state.Script);
            Assert.Equal(3, state.ReturnValue);
        }

        [Fact]
        public async Task TestEvalScript()
        {
            var value = await CSharpScript.EvaluateAsync("1 + 2");
            Assert.Equal(3, value);
        }

        [Fact]
        public async Task TestRunScriptWithSpecifiedReturnType()
        {
            var state = await CSharpScript.RunAsync("1 + 2");
            Assert.Equal(3, state.ReturnValue);
        }

        [Fact]
        public async Task TestRunVoidScript()
        {
            var state = await CSharpScript.RunAsync("System.Console.WriteLine(0);");
            Assert.Null(state.ReturnValue);
        }

        [WorkItem(5279, "https://github.com/dotnet/roslyn/issues/5279")]
        [Fact]
        public async void TestRunExpressionStatement()
        {
            var state = await CSharpScript.RunAsync(
@"int F() { return 1; }
F();");
            Assert.Null(state.ReturnValue);
        }

        [Fact(Skip = "https://github.com/dotnet/roslyn/issues/170")]
        public void TestRunDynamicVoidScriptWithTerminatingSemicolon()
        {
            var result = CSharpScript.RunAsync(@"
class SomeClass
{
    public void Do()
    {
    }
}
dynamic d = new SomeClass();
d.Do();"
, ScriptOptions.Default.WithReferences(MscorlibRef, SystemRef, SystemCoreRef, CSharpRef));
        }

        [Fact(Skip = "https://github.com/dotnet/roslyn/issues/170")]
        public void TestRunDynamicVoidScriptWithoutTerminatingSemicolon()
        {
            var result = CSharpScript.RunAsync(@"
class SomeClass
{
    public void Do()
    {
    }
}
dynamic d = new SomeClass();
d.Do()"
, ScriptOptions.Default.WithReferences(MscorlibRef, SystemRef, SystemCoreRef, CSharpRef));
        }

        public class Globals
        {
            public int X;
            public int Y;
        }

        [Fact]
        public async Task TestRunScriptWithGlobals()
        {
            var state = await CSharpScript.RunAsync("X + Y", globals: new Globals { X = 1, Y = 2 });
            Assert.Equal(3, state.ReturnValue);
        }

        [Fact]
        public async Task TestRunCreatedScriptWithExpectedGlobals()
        {
            var script = CSharpScript.Create("X + Y", globalsType: typeof(Globals));
            var state = await script.RunAsync(new Globals { X = 1, Y = 2 });
            Assert.Equal(3, state.ReturnValue);
            Assert.Same(script, state.Script);
        }

        [Fact]
        public void TestRunCreatedScriptWithUnexpectedGlobals()
        {
            var script = CSharpScript.Create("X + Y");

            // Global variables passed to a script without a global type
            AssertEx.ThrowsArgumentException("globals", () => script.RunAsync(new Globals { X = 1, Y = 2 }));
        }

        [Fact]
        public void TestRunCreatedScriptWithoutGlobals()
        {
            var script = CSharpScript.Create("X + Y", globalsType: typeof(Globals));
           
            //  The script requires access to global variables but none were given
            AssertEx.ThrowsArgumentException("globals", () => script.RunAsync());
        }

        [Fact]
        public void TestRunCreatedScriptWithMismatchedGlobals()
        {
            var script = CSharpScript.Create("X + Y", globalsType: typeof(Globals));

            //  The globals of type 'System.Object' is not assignable to 'Microsoft.CodeAnalysis.Scripting.CSharp.Test.ScriptTests+Globals'
            AssertEx.ThrowsArgumentException("globals", () => script.RunAsync(new object()));
        }

        [Fact]
        public async Task ContinueAsync_Error1()
        {
            var state = await CSharpScript.RunAsync("X + Y", globals: new Globals());

            AssertEx.ThrowsArgumentNull("previousState", () => state.Script.ContinueAsync(null));
        }

        [Fact]
        public async Task ContinueAsync_Error2()
        {
            var state1 = await CSharpScript.RunAsync("X + Y + 1", globals: new Globals());
            var state2 = await CSharpScript.RunAsync("X + Y + 2", globals: new Globals());

            AssertEx.ThrowsArgumentException("previousState", () => state1.Script.ContinueAsync(state2));
        }

        [Fact]
        public async Task TestRunScriptWithScriptState()
        {
            // run a script using another scripts end state as the starting state (globals)
            var state = await CSharpScript.RunAsync("int X = 100;").ContinueWith("X + X");
            Assert.Equal(200, state.ReturnValue);
        }

        [Fact]
        public async Task TestRepl()
        {
            string[] submissions = new[]
            {
                "int x = 100;",
                "int y = x * x;",
                "x + y"
            };

            var state = await CSharpScript.RunAsync("");
            foreach (var submission in submissions)
            {
                state = await state.ContinueWithAsync(submission);
            }

            Assert.Equal(10100, state.ReturnValue);
        }

#if TODO // https://github.com/dotnet/roslyn/issues/3720
        [Fact]
        public void TestCreateMethodDelegate()
        {
            // create a delegate to a method declared in the script
            var state = CSharpScript.Run("int Times(int x) { return x * x; }");
            var fn = state.CreateDelegate<Func<int, int>>("Times");
            var result = fn(5);
            Assert.Equal(25, result);
        }
#endif

        [Fact]
        public async Task TestGetScriptVariableAfterRunningScript()
        {
            var state = await CSharpScript.RunAsync("int x = 100;");
            var globals = state.Variables.Names.ToList();
            Assert.Equal(1, globals.Count);
            Assert.Equal(true, globals.Contains("x"));
            Assert.Equal(true, state.Variables.ContainsVariable("x"));
            Assert.Equal(100, (int)state.Variables["x"].Value);
        }

        [Fact]
        public async Task TestBranchingSubscripts()
        {
            // run script to create declaration of M
            var state1 = await CSharpScript.RunAsync("int M(int x) { return x + x; }");

            // run second script starting from first script's end state
            // this script's new declaration should hide the old declaration
            var state2 = await state1.ContinueWithAsync("int M(int x) { return x * x; } M(5)");
            Assert.Equal(25, state2.ReturnValue);

            // run third script also starting from first script's end state
            // it should not see any declarations made by the second script.
            var state3 = await state1.ContinueWithAsync("M(5)");
            Assert.Equal(10, state3.ReturnValue);
        }
    }
}
