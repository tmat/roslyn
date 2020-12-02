// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

extern alias InteractiveHost;
using Microsoft.CodeAnalysis.CSharp.Scripting.Hosting;
using System;
using System.IO;
using System.Threading.Tasks;

namespace Microsoft.CodeAnalysis.UnitTests.Interactive
{
    using System.Globalization;
    using InteractiveHost::Microsoft.CodeAnalysis.Interactive;

    class Program
    {
        static async Task Main(string[] args)
        {
            var host = new InteractiveHost(typeof(CSharpReplServiceProvider), ".", millisecondsTimeout: -1, joinOutputWritingThreadsOnDisposal: true);
            host.SetOutputs(Console.Out, Console.Error);
            var root = Path.Combine(Path.GetDirectoryName(typeof(Program).Assembly.Location)!, "Host");

            await host.ResetAsync(InteractiveHostOptions.CreateFromDirectory(root, null, CultureInfo.InvariantCulture, InteractiveHostPlatform.Desktop64)).ConfigureAwait(false);

            await host.ExecuteAsync(@"System.Console.OutputEncoding = System.Text.Encoding.UTF8").ConfigureAwait(false);
        }
    }
}
