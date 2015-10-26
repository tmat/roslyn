// Copyright (c) Microsoft.  All Rights Reserved.  Licensed under the Apache License, Version 2.0.  See License.txt in the project root for license information.

using Roslyn.Test.Utilities;
using Xunit;

namespace Microsoft.CodeAnalysis.Scripting.CSharp.UnitTests
{
    partial class CommandLineRunnerTests 
    {
        [Fact]
        public void Script_Warnings()
        {
            var dir = Temp.CreateDirectory();
            var script = dir.CreateFile("script.csx").WriteAllText(@"
System.Threading.Tasks.Task.FromResult(1);
System.Threading.Tasks.Task.FromResult(1);
System.Threading.Tasks.Task.FromResult(1);
System.Threading.Tasks.Task.FromResult(1);
System.Threading.Tasks.Task.FromResult(1);
System.Threading.Tasks.Task.FromResult(1);
System.Threading.Tasks.Task.FromResult(1);
System.Threading.Tasks.Task.FromResult(1);
System.Threading.Tasks.Task.FromResult(1);
");
            var runner = CreateRunner(new[] { script.Path }, baseDirectory: dir.Path);

            Assert.Equal(0, runner.RunInteractive());

            AssertEx.AssertEqualToleratingWhitespaceDifferences(@"
script.csx(2,1): warning CS4014: Because this call is not awaited, execution of the current method continues before the call is completed. Consider applying the 'await' operator to the result of the call.
script.csx(3,1): warning CS4014: Because this call is not awaited, execution of the current method continues before the call is completed. Consider applying the 'await' operator to the result of the call.
script.csx(4,1): warning CS4014: Because this call is not awaited, execution of the current method continues before the call is completed. Consider applying the 'await' operator to the result of the call.
script.csx(5,1): warning CS4014: Because this call is not awaited, execution of the current method continues before the call is completed. Consider applying the 'await' operator to the result of the call.
script.csx(6,1): warning CS4014: Because this call is not awaited, execution of the current method continues before the call is completed. Consider applying the 'await' operator to the result of the call.
script.csx(7,1): warning CS4014: Because this call is not awaited, execution of the current method continues before the call is completed. Consider applying the 'await' operator to the result of the call.
script.csx(8,1): warning CS4014: Because this call is not awaited, execution of the current method continues before the call is completed. Consider applying the 'await' operator to the result of the call.
script.csx(9,1): warning CS4014: Because this call is not awaited, execution of the current method continues before the call is completed. Consider applying the 'await' operator to the result of the call.
script.csx(10,1): warning CS4014: Because this call is not awaited, execution of the current method continues before the call is completed. Consider applying the 'await' operator to the result of the call.
", runner.Console.Out.ToString());
        }

        [Fact]
        public void Interactive_LimitErrors()
        {
            var dir = Temp.CreateDirectory();
            var runner = CreateRunner(baseDirectory: dir.Path, input: @"
{ 
    foo();
    foo();
    foo();
    foo();
    foo();
}
");
            Assert.Equal(0, runner.RunInteractive());

            AssertEx.AssertEqualToleratingWhitespaceDifferences(
$@"Microsoft (R) Visual C# Interactive Compiler version {CompilerVersion}
Copyright (C) Microsoft Corporation. All rights reserved.

> 
> {{ 
.     foo();
.     foo();
.     foo();
.     foo();
.     foo();
. }}
«Red»
(2,5): error CS0103: The name 'foo' does not exist in the current context
(3,5): error CS0103: The name 'foo' does not exist in the current context
(4,5): error CS0103: The name 'foo' does not exist in the current context
(5,5): error CS0103: The name 'foo' does not exist in the current context
(6,5): error CS0103: The name 'foo' does not exist in the current context
«Gray»
> ", runner.Console.Out.ToString());
        }

        [Fact]
        public void Interactive_AdditionalErrors()
        {
            var dir = Temp.CreateDirectory();
            var runner = CreateRunner(baseDirectory: dir.Path, input: @"
{ 
    foo();
    foo();
    foo();
    foo();
    foo();
    foo();
}
");
            Assert.Equal(0, runner.RunInteractive());

            AssertEx.AssertEqualToleratingWhitespaceDifferences(
$@"Microsoft (R) Visual C# Interactive Compiler version {CompilerVersion}
Copyright (C) Microsoft Corporation. All rights reserved.

> 
> {{ 
.     foo();
.     foo();
.     foo();
.     foo();
.     foo();
.     foo();
. }}
«Red»
(2,5): error CS0103: The name 'foo' does not exist in the current context
(3,5): error CS0103: The name 'foo' does not exist in the current context
(4,5): error CS0103: The name 'foo' does not exist in the current context
(5,5): error CS0103: The name 'foo' does not exist in the current context
(6,5): error CS0103: The name 'foo' does not exist in the current context
«DarkRed»
+ additional 1 error(s)
«Gray»
> ", runner.Console.Out.ToString());
        }

        [Fact]
        public void Interactive_LimitWarnings()
        {
            var dir = Temp.CreateDirectory();
            var runner = CreateRunner(baseDirectory: dir.Path, input: @"
{ 
    System.Threading.Tasks.Task.FromResult(1);
    System.Threading.Tasks.Task.FromResult(1);
    System.Threading.Tasks.Task.FromResult(1);
    System.Threading.Tasks.Task.FromResult(1);
    System.Threading.Tasks.Task.FromResult(1);
}
");
            Assert.Equal(0, runner.RunInteractive());

            Assert.Equal(
$@"Microsoft (R) Visual C# Interactive Compiler version {CompilerVersion}
Copyright (C) Microsoft Corporation. All rights reserved.

> 
> {{ 
.     System.Threading.Tasks.Task.FromResult(1);
.     System.Threading.Tasks.Task.FromResult(1);
.     System.Threading.Tasks.Task.FromResult(1);
.     System.Threading.Tasks.Task.FromResult(1);
.     System.Threading.Tasks.Task.FromResult(1);
. }}
«Yellow»
(2,5): warning CS4014: Because this call is not awaited, execution of the current method continues before the call is completed. Consider applying the 'await' operator to the result of the call.
(3,5): warning CS4014: Because this call is not awaited, execution of the current method continues before the call is completed. Consider applying the 'await' operator to the result of the call.
(4,5): warning CS4014: Because this call is not awaited, execution of the current method continues before the call is completed. Consider applying the 'await' operator to the result of the call.
(5,5): warning CS4014: Because this call is not awaited, execution of the current method continues before the call is completed. Consider applying the 'await' operator to the result of the call.
(6,5): warning CS4014: Because this call is not awaited, execution of the current method continues before the call is completed. Consider applying the 'await' operator to the result of the call.
«Gray»
> ", runner.Console.Out.ToString());
        }

        [Fact]
        public void Interactive_AdditionalWarnings()
        {
            var dir = Temp.CreateDirectory();
            var runner = CreateRunner(baseDirectory: dir.Path, input: @"
{ 
    System.Threading.Tasks.Task.FromResult(1);
    System.Threading.Tasks.Task.FromResult(1);
    System.Threading.Tasks.Task.FromResult(1);
    System.Threading.Tasks.Task.FromResult(1);
    System.Threading.Tasks.Task.FromResult(1);
    System.Threading.Tasks.Task.FromResult(1);
    System.Threading.Tasks.Task.FromResult(1);
    System.Threading.Tasks.Task.FromResult(1);
    System.Threading.Tasks.Task.FromResult(1);
}
");
            Assert.Equal(0, runner.RunInteractive());

            Assert.Equal(
$@"Microsoft (R) Visual C# Interactive Compiler version {CompilerVersion}
Copyright (C) Microsoft Corporation. All rights reserved.

> 
> {{ 
.     System.Threading.Tasks.Task.FromResult(1);
.     System.Threading.Tasks.Task.FromResult(1);
.     System.Threading.Tasks.Task.FromResult(1);
.     System.Threading.Tasks.Task.FromResult(1);
.     System.Threading.Tasks.Task.FromResult(1);
.     System.Threading.Tasks.Task.FromResult(1);
.     System.Threading.Tasks.Task.FromResult(1);
.     System.Threading.Tasks.Task.FromResult(1);
.     System.Threading.Tasks.Task.FromResult(1);
. }}
«Yellow»
(2,5): warning CS4014: Because this call is not awaited, execution of the current method continues before the call is completed. Consider applying the 'await' operator to the result of the call.
(3,5): warning CS4014: Because this call is not awaited, execution of the current method continues before the call is completed. Consider applying the 'await' operator to the result of the call.
(4,5): warning CS4014: Because this call is not awaited, execution of the current method continues before the call is completed. Consider applying the 'await' operator to the result of the call.
(5,5): warning CS4014: Because this call is not awaited, execution of the current method continues before the call is completed. Consider applying the 'await' operator to the result of the call.
(6,5): warning CS4014: Because this call is not awaited, execution of the current method continues before the call is completed. Consider applying the 'await' operator to the result of the call.
«DarkYellow»
+ additional 4 warning(s)
«Gray»
> ", runner.Console.Out.ToString());
        }

        [Fact]
        public void Interactive_AdditionalErrorsAndWarnings1()
        {
            var dir = Temp.CreateDirectory();
            var runner = CreateRunner(baseDirectory: dir.Path, input: @"
{ 
    foo();
    foo();
    foo();
    foo();
    foo();
    foo();
    System.Threading.Tasks.Task.FromResult(1);
    System.Threading.Tasks.Task.FromResult(1);
    System.Threading.Tasks.Task.FromResult(1);
    System.Threading.Tasks.Task.FromResult(1);
}
");
            Assert.Equal(0, runner.RunInteractive());

            AssertEx.AssertEqualToleratingWhitespaceDifferences(
$@"Microsoft (R) Visual C# Interactive Compiler version {CompilerVersion}
Copyright (C) Microsoft Corporation. All rights reserved.

> 
> {{ 
.     foo();
.     foo();
.     foo();
.     foo();
.     foo();
.     foo();
.     System.Threading.Tasks.Task.FromResult(1);
.     System.Threading.Tasks.Task.FromResult(1);
.     System.Threading.Tasks.Task.FromResult(1);
.     System.Threading.Tasks.Task.FromResult(1);
. }}
«Red»
(2,5): error CS0103: The name 'foo' does not exist in the current context
(3,5): error CS0103: The name 'foo' does not exist in the current context
(4,5): error CS0103: The name 'foo' does not exist in the current context
(5,5): error CS0103: The name 'foo' does not exist in the current context
(6,5): error CS0103: The name 'foo' does not exist in the current context
«DarkRed»
+ additional 1 error(s)
«Gray»
> ", runner.Console.Out.ToString());
        }

        [Fact]
        public void Interactive_AdditionalErrorsAndWarnings2()
        {
            var dir = Temp.CreateDirectory();
            var runner = CreateRunner(baseDirectory: dir.Path, input: @"
{ 
    foo();
    System.Threading.Tasks.Task.FromResult(1);
    System.Threading.Tasks.Task.FromResult(1);
    foo();
    System.Threading.Tasks.Task.FromResult(1);
    System.Threading.Tasks.Task.FromResult(1);
}
");
            Assert.Equal(0, runner.RunInteractive());

            AssertEx.AssertEqualToleratingWhitespaceDifferences(
$@"Microsoft (R) Visual C# Interactive Compiler version {CompilerVersion}
Copyright (C) Microsoft Corporation. All rights reserved.

> 
> {{
.     foo();
.     System.Threading.Tasks.Task.FromResult(1);
.     System.Threading.Tasks.Task.FromResult(1);
.     foo();
.     System.Threading.Tasks.Task.FromResult(1);
.     System.Threading.Tasks.Task.FromResult(1);
. }}
«Red»
(2,5): error CS0103: The name 'foo' does not exist in the current context
(5,5): error CS0103: The name 'foo' does not exist in the current context
«Yellow»
(3,5): warning CS4014: Because this call is not awaited, execution of the current method continues before the call is completed. Consider applying the 'await' operator to the result of the call.
(4,5): warning CS4014: Because this call is not awaited, execution of the current method continues before the call is completed. Consider applying the 'await' operator to the result of the call.
(6,5): warning CS4014: Because this call is not awaited, execution of the current method continues before the call is completed. Consider applying the 'await' operator to the result of the call.
«DarkYellow»
+ additional 1 warning(s)
«Gray»
> ", runner.Console.Out.ToString());
        }

        [Fact]
        public void Interactive_Chain1()
        {
            var dir = Temp.CreateDirectory();
            var runner = CreateRunner(baseDirectory: dir.Path, input: 
@"System.Threading.Tasks.Task.FromResult(1);
1
");
            Assert.Equal(0, runner.RunInteractive());

            AssertEx.AssertEqualToleratingWhitespaceDifferences(
$@"Microsoft (R) Visual C# Interactive Compiler version {CompilerVersion}
Copyright (C) Microsoft Corporation. All rights reserved.

> System.Threading.Tasks.Task.FromResult(1);
«Yellow»
(1,1): warning CS4014: Because this call is not awaited, execution of the current method continues before the call is completed. Consider applying the 'await' operator to the result of the call.
«Gray»
> 1
1
> ", runner.Console.Out.ToString());
        }

        [Fact]
        public void Interactive_Chain2()
        {
            var dir = Temp.CreateDirectory();
            var runner = CreateRunner(baseDirectory: dir.Path, input:
@"{ 
    foo();
    System.Threading.Tasks.Task.FromResult(1);
}
foo();
System.Threading.Tasks.Task.FromResult(1);
1
");
            Assert.Equal(0, runner.RunInteractive());

            AssertEx.AssertEqualToleratingWhitespaceDifferences(
$@"Microsoft (R) Visual C# Interactive Compiler version {CompilerVersion}
Copyright (C) Microsoft Corporation. All rights reserved.

> {{
.     foo();
.     System.Threading.Tasks.Task.FromResult(1);
. }}
«Red»
(2,5): error CS0103: The name 'foo' does not exist in the current context
«Yellow»
(3,5): warning CS4014: Because this call is not awaited, execution of the current method continues before the call is completed. Consider applying the 'await' operator to the result of the call.
«Gray»
> foo();
«Red»
(1,1): error CS0103: The name 'foo' does not exist in the current context
«Gray»
> System.Threading.Tasks.Task.FromResult(1);
«Yellow»
(1,1): warning CS4014: Because this call is not awaited, execution of the current method continues before the call is completed. Consider applying the 'await' operator to the result of the call.
«Gray»
> 1
1
> ", runner.Console.Out.ToString());
        }
    }
}
